using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WireCAD.Controls;
using WireCAD.Models;
using Path = System.Windows.Shapes.Path;

namespace WireCAD;

// Parte della finestra dedicata alle guaine: strumento di disegno, ordine di sovrapposizione, fili contenuti.
public partial class MainWindow
{
    private readonly List<Point> _sheathPoints = new();
    private int _sheathWireCount = 2;

    private SolidColorBrush FillBrush(string hex, double opacity)
    {
        if (opacity >= 0.999)
        {
            return CachedBrush(hex);
        }
        int alpha = Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        return CachedBrush("#" + alpha.ToString("X2", CultureInfo.InvariantCulture) + hex.TrimStart('#'));
    }

    /// <summary>Le guaine stanno sopra ai fili che racchiudono; i loro fili interni sopra la guaina.</summary>
    private void RaiseSheaths()
    {
        foreach (SheathEntity sheath in _entities.OfType<SheathEntity>())
        {
            if (_paths.TryGetValue(sheath, out Path? path))
            {
                DrawCanvas.Children.Remove(path);
                DrawCanvas.Children.Add(path);
                RaiseTwistStrands(sheath);
            }
            if (_layers.TryGetValue(sheath, out Path? layer))
            {
                DrawCanvas.Children.Remove(layer);
                DrawCanvas.Children.Add(layer);
            }
            if (_inners.TryGetValue(sheath, out Path? inner))
            {
                DrawCanvas.Children.Remove(inner);
                DrawCanvas.Children.Add(inner);
            }
        }
    }

    private readonly Dictionary<EntityBase, Path> _layers = new();

    /// <summary>Strato interno scoperto (calza o tubo) con il proprio colore, nei tratti sguainati.</summary>
    private void UpdateSheathLayer(SheathEntity sheath)
    {
        Geometry? geometry = sheath.InnerLayerGeometry();
        if (geometry == null)
        {
            RemoveLayer(sheath);
            return;
        }
        if (!_layers.TryGetValue(sheath, out Path? layer))
        {
            layer = new Path
            {
                IsHitTestVisible = false,
                Stroke = CachedBrush("#374151"),
                StrokeThickness = 0.35
            };
            _layers[sheath] = layer;
            int index = _paths.TryGetValue(sheath, out Path? main) ? DrawCanvas.Children.IndexOf(main) : -1;
            DrawCanvas.Children.Insert(index >= 0 ? index + 1 : DrawCanvas.Children.Count, layer);
        }
        layer.Data = geometry;
        layer.Fill = CachedBrush(sheath.InnerFillHex);
    }

    private void RemoveLayer(EntityBase entity)
    {
        if (_layers.Remove(entity, out Path? layer))
        {
            DrawCanvas.Children.Remove(layer);
        }
    }

    /// <summary>Fili interni di una guaina con pin: uno per ingresso, dal pin d'ingresso a quello di uscita.</summary>
    private void UpdateSheathInner(SheathEntity sheath)
    {
        // i collegamenti ingresso→uscita sostituiti da fili verso i pin di una guaina annidata non si disegnano
        Geometry? lines = sheath.WireCount > 0 && !sheath.IsTwisted && sheath.Inside != SheathEntity.InsideHidden
            ? sheath.InnerGeometry(SheathAnalyzer.UsedLinks(sheath, _entities.OfType<WireEntity>(), _entities.OfType<SheathEntity>()))
            : null;
        if (lines == null)
        {
            RemoveInner(sheath);
            return;
        }
        if (!_inners.TryGetValue(sheath, out Path? inner))
        {
            inner = new Path
            {
                IsHitTestVisible = false,
                Stroke = CachedBrush("#374151"),
                StrokeThickness = 0.4
            };
            _inners[sheath] = inner;
            DrawCanvas.Children.Add(inner);
        }
        inner.Data = lines;
        inner.Opacity = sheath.Inside == SheathEntity.InsideVisible ? 1.0 : 0.35;
    }

    private string NextSheathTag(string prefix = "FA")
    {
        int max = 0;
        foreach (SheathEntity sheath in _entities.OfType<SheathEntity>())
        {
            if (sheath.Tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(sheath.Tag.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number > max)
            {
                max = number;
            }
        }
        return prefix + (max + 1).ToString(CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- guaina disposta lungo un'altra

    private SheathEntity? _attachParent;
    private double _attachFrom;

    /// <summary>Guaina sul cui corpo (non vicino ai suoi pin) cade il punto indicato, con l'ascissa lungo il suo asse.</summary>
    private SheathEntity? FindSheathBody(Point world, out double along)
    {
        along = 0;
        double pinReach = Math.Max(1.5, 8 / _viewTransform.Matrix.M11);
        SheathEntity? best = null;
        double bestDistance = double.MaxValue;
        foreach (SheathEntity sheath in _entities.OfType<SheathEntity>())
        {
            if (!sheath.TryProject(world, out double s, out double distance) || distance > sheath.DrawWidth / 2 + 0.5)
            {
                continue;
            }
            bool nearPin = false;
            for (int i = 0; i < sheath.PinCount && !nearPin; i++)
            {
                nearPin = (sheath.GetPinPosition(i) - world).Length <= pinReach;
            }
            if (nearPin || distance >= bestDistance)
            {
                continue;
            }
            best = sheath;
            bestDistance = distance;
            along = s;
        }
        return best;
    }

    private void FinishAttachedSheath(double to)
    {
        SheathEntity parent = _attachParent!;
        double from = _attachFrom;
        _attachParent = null;
        _previewPath.Visibility = Visibility.Collapsed;
        if (Math.Abs(to - from) < 1)
        {
            StatusHint.Text = "I due punti sono troppo vicini: scegli un tratto più lungo.";
            return;
        }
        string? text = Dialogs.Prompt(this, "Fascio lungo un altro",
            "Numero di fili che entrano (e quindi escono) dal nuovo fascio.\nCon 0 non si creano pin.",
            parent.WireCount.ToString(CultureInfo.CurrentCulture));
        if (text == null)
        {
            return;
        }
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int count) || count < 0 || count > 64)
        {
            StatusHint.Text = "Numero di fili non valido: serve un intero da 0 a 64.";
            return;
        }
        bool ferrite = MessageBox.Show(this,
            "Che cosa vuoi creare lungo il fascio?\n\nSì = una ferrite (disegnata attorno al fascio)\nNo = un fascio (disegnato dentro il fascio)",
            "Fascio lungo un altro", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        SheathEntity child = SheathEntity.CreateAttached(parent, from, to, ferrite ? 1.35 : 0.7);
        child.WireCount = count;
        if (ferrite)
        {
            child.SheathType = SheathEntity.FerriteType;
        }
        child.Tag = NextSheathTag(ferrite ? "FE" : "FA");
        child.Name = child.Tag;
        AddEntity(child);
        SetSelection(new[] { child });
        CommitChange();
        StatusHint.Text = $"{child.Tag} creata lungo {parent.Tag}: l'inizio e la fine si regolano dalle proprietà.";
    }

    private void SheathClick(Point world)
    {
        if (_attachParent != null)
        {
            if (_attachParent.TryProject(world, out double along, out _))
            {
                FinishAttachedSheath(along);
            }
            return;
        }
        if (_sheathPoints.Count == 0)
        {
            SheathEntity? parent = FindSheathBody(world, out double start);
            if (parent != null)
            {
                _attachParent = parent;
                _attachFrom = start;
                StatusHint.Text = $"Fascio lungo {parent.Tag}: clicca il punto finale sullo stesso fascio. Esc annulla.";
                return;
            }
        }
        Point point = SnapWithAngleLock(world).Point;
        if (_sheathPoints.Count > 0 && (point - _sheathPoints[_sheathPoints.Count - 1]).Length < 1e-6)
        {
            return;
        }
        _sheathPoints.Add(point);
    }

    private void CancelSheath()
    {
        _sheathPoints.Clear();
        _attachParent = null;
        if (_tool == ToolType.Sheath)
        {
            _previewPath.Visibility = Visibility.Collapsed;
        }
    }

    private void FinishSheath()
    {
        if (_sheathPoints.Count >= 2)
        {
            string? text = Dialogs.Prompt(this, "Fascio",
                "Numero di fili che entrano (e quindi escono) dal fascio.\nCon 0 non si creano pin: i fili vengono associati in base alla geometria.",
                _sheathWireCount.ToString(CultureInfo.CurrentCulture));
            if (text == null)
            {
                CancelSheath();
                return;
            }
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int count) || count < 0 || count > 64)
            {
                StatusHint.Text = "Numero di fili non valido: serve un intero da 0 a 64.";
                return;
            }
            _sheathWireCount = count;
            SheathEntity sheath = new(_sheathPoints.ToList()) { Tag = NextSheathTag(), WireCount = count };
            sheath.Name = sheath.Tag;
            AddEntity(sheath);
            SetSelection(new[] { sheath });
            CommitChange();
        }
        _sheathPoints.Clear();
        _previewPath.Visibility = Visibility.Collapsed;
    }

    private Geometry BuildSheathPreview(Point cursor)
    {
        List<Point> points = new(_sheathPoints) { cursor };
        return new SheathEntity(points).BuildGeometry();
    }

    /// <summary>Aggiorna per ogni guaina l'elenco dei fili che la attraversano (usato da etichette, proprietà e tabelle).</summary>
    private void UpdateSheathContents()
    {
        List<SheathEntity> sheaths = _entities.OfType<SheathEntity>().ToList();
        if (sheaths.Count == 0)
        {
            return;
        }
        List<WireEntity> wires = _entities.OfType<WireEntity>().ToList();
        foreach (SheathEntity sheath in sheaths)
        {
            List<WireEntity> inside = SheathAnalyzer.WiresIn(sheath, wires);
            sheath.ContainedCount = inside.Count;
            if (inside.Count == 0 && sheath.ParentId is Guid parentId
                && _byId.TryGetValue(parentId, out EntityBase? baseEntity) && baseEntity is SheathEntity baseSheath)
            {
                // una guaina disposta lungo un'altra racchiude gli stessi fili
                inside = SheathAnalyzer.WiresIn(baseSheath, wires);
            }
            sheath.ContainedCount = inside.Count;
            sheath.ContainedWires = string.Join(" ", inside
                .Select(wire => string.IsNullOrWhiteSpace(wire.Number) ? wire.Name : wire.Number)
                .OrderBy(text => text, StringComparer.OrdinalIgnoreCase));
            UpdateTwistColors(sheath, wires);
            UpdateSheathInner(sheath);
        }
        RefreshFields();
    }

    private void ShowSheathMenuItems(ContextMenu menu, SheathEntity sheath, Point worldPoint)
    {
        System.Windows.Controls.MenuItem add = new() { Header = "Aggiungi curva (vertice) qui" };
        add.Click += (_, _) =>
        {
            sheath.InsertVertexNear(worldPoint);
            UpdateGrips();
            CommitChange();
        };
        menu.Items.Add(add);

        System.Windows.Controls.MenuItem remove = new() { Header = "Rimuovi vertice più vicino" };
        remove.Click += (_, _) =>
        {
            if (sheath.RemoveVertexNear(worldPoint))
            {
                UpdateGrips();
                CommitChange();
            }
        };
        menu.Items.Add(remove);
        menu.Items.Add(new System.Windows.Controls.Separator());
    }

    private void BtnSheath_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Sheath);
}
