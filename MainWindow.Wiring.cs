using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WireCAD.Controls;
using WireCAD.Models;
using Path = System.Windows.Shapes.Path;

namespace WireCAD;

// Parte della finestra dedicata al cablaggio: snap ai pin, tool filo/componente/giunzione/testo, etichette.
public partial class MainWindow
{
    private readonly record struct SnapHit(Point Point, EntityBase? Owner, int Pin);

    private readonly Dictionary<Guid, EntityBase> _byId = new();
    private readonly List<Point> _wirePoints = new();
    private readonly Path _snapMarker = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
        StrokeThickness = 0.4,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };
    private readonly Dictionary<string, SolidColorBrush> _colorBrushes = new();

    private readonly Dictionary<EntityBase, Path> _overlays = new();
    private readonly HashSet<EntityBase> _sentToBack = new();
    private readonly Dictionary<EntityBase, Path> _inners = new();

    private ComponentTemplate? _template;
    private Vector? _wireStartDir;
    private bool _wireFlip;
    private bool _labelsPending;

    // ---------------------------------------------------------------- snap

    private SnapHit SmartSnap(Point world, bool allowWireSegments = false, EntityBase? exclude = null)
    {
        if (CkObjSnap.IsChecked == true)
        {
            double tolerance = Math.Max(1.5, 8 / _viewTransform.Matrix.M11);

            double best = tolerance;
            bool found = false;
            Point bestPoint = new();
            EntityBase? bestOwner = null;
            int bestPin = -1;
            foreach (EntityBase entity in _entities)
            {
                if (ReferenceEquals(entity, exclude) || entity.PinCount == 0)
                {
                    continue;
                }
                for (int i = 0; i < entity.PinCount; i++)
                {
                    Point pin = entity.GetPinPosition(i);
                    double distance = (pin - world).Length;
                    if (distance <= best)
                    {
                        best = distance;
                        found = true;
                        bestPoint = pin;
                        bestOwner = entity;
                        bestPin = i;
                    }
                }
            }
            if (found)
            {
                return new SnapHit(bestPoint, bestOwner, bestPin);
            }

            best = tolerance;
            foreach (EntityBase entity in _entities)
            {
                if (ReferenceEquals(entity, exclude)
                    || !(entity is WireEntity || entity is LineEntity || entity is RectangleEntity || entity is ArcEntity || entity is SheathEntity || entity is SplineEntity || entity is CircleEntity))
                {
                    continue;
                }
                foreach (Point vertex in entity.GetDefinitionPoints())
                {
                    double distance = (vertex - world).Length;
                    if (distance <= best)
                    {
                        best = distance;
                        found = true;
                        bestPoint = vertex;
                    }
                }
            }
            if (found)
            {
                return new SnapHit(bestPoint, null, -1);
            }

            if (allowWireSegments)
            {
                best = tolerance;
                Point grid = Snap(world);
                foreach (WireEntity wire in _entities.OfType<WireEntity>())
                {
                    if (ReferenceEquals(wire, exclude))
                    {
                        continue;
                    }
                    IReadOnlyList<Point> points = wire.Points;
                    for (int k = 0; k + 1 < points.Count; k++)
                    {
                        Point a = points[k];
                        Point b = points[k + 1];
                        double distance = WireEntity.DistanceToSegment(world, a, b);
                        if (distance > best)
                        {
                            continue;
                        }
                        Point candidate;
                        if (Math.Abs(a.Y - b.Y) < 1e-6)
                        {
                            candidate = new Point(Math.Clamp(grid.X, Math.Min(a.X, b.X), Math.Max(a.X, b.X)), a.Y);
                        }
                        else if (Math.Abs(a.X - b.X) < 1e-6)
                        {
                            candidate = new Point(a.X, Math.Clamp(grid.Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
                        }
                        else
                        {
                            continue;
                        }
                        best = distance;
                        found = true;
                        bestPoint = candidate;
                    }
                }
                if (found)
                {
                    return new SnapHit(bestPoint, null, -1);
                }
            }
        }
        return new SnapHit(Snap(world), null, -1);
    }

    private void UpdateSnapMarker(SnapHit hit)
    {
        if (hit.Owner == null)
        {
            _snapMarker.Visibility = Visibility.Collapsed;
            return;
        }
        double zoom = _viewTransform.Matrix.M11;
        double radius = 5 / zoom;
        _snapMarker.Data = new EllipseGeometry(hit.Point, radius, radius);
        _snapMarker.StrokeThickness = 1.5 / zoom;
        _snapMarker.Visibility = Visibility.Visible;
    }

    // ---------------------------------------------------------------- libreria

    private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _template = LibraryList.SelectedItem as ComponentTemplate;
        if (_template != null)
        {
            SetTool(ToolType.Component);
        }
    }

    private string NextTag(string prefix)
    {
        int max = 0;
        foreach (ComponentEntity component in _entities.OfType<ComponentEntity>())
        {
            if (component.Tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(component.Tag.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number > max)
            {
                max = number;
            }
        }
        return prefix + (max + 1).ToString(CultureInfo.InvariantCulture);
    }

    private string NextWireNumber()
    {
        int max = 0;
        foreach (WireEntity wire in _entities.OfType<WireEntity>())
        {
            if (wire.Number.StartsWith(_wirePrefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(wire.Number.AsSpan(_wirePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number > max)
            {
                max = number;
            }
        }
        return _wirePrefix + (max + 1).ToString("000", CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- posizionamento

    private void PlaceComponent(Point world)
    {
        if (_template == null)
        {
            StatusHint.Text = "Seleziona prima un componente dalla libreria.";
            return;
        }
        if (_template.IsInline)
        {
            PlaceInline(world);
            return;
        }
        ComponentEntity component = _template.Create(SmartSnap(world).Point);
        component.Tag = NextTag(_template.Prefix);
        component.Name = component.Tag;
        AddEntity(component);
        SetSelection(new[] { component });
        CommitChange();
    }

    private void PlaceJunction(Point world)
    {
        JunctionEntity junction = new() { Position = SmartSnap(world, true).Point };
        AddEntity(junction);
        SetSelection(new[] { junction });
        CommitChange();
    }

    private void PlaceText(Point world)
    {
        string? text = Dialogs.Prompt(this, "Testo", "Testo da inserire:", "Testo");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        TextEntity entity = new() { Position = SmartSnap(world).Point, Text = text };
        AddEntity(entity);
        SetSelection(new[] { entity });
        CommitChange();
    }

    // ---------------------------------------------------------------- tool filo

    private bool ChooseHorizontalFirst(Vector? targetDirection)
    {
        bool horizontalFirst;
        if (targetDirection is Vector target && (target.X != 0 || target.Y != 0))
        {
            horizontalFirst = target.X == 0;
        }
        else if (_wirePoints.Count <= 1)
        {
            horizontalFirst = !(_wireStartDir is Vector start && start.X == 0 && start.Y != 0);
        }
        else
        {
            Point previous = _wirePoints[_wirePoints.Count - 2];
            Point last = _wirePoints[_wirePoints.Count - 1];
            horizontalFirst = Math.Abs(previous.Y - last.Y) >= 1e-6;
        }
        return _wireFlip ? !horizontalFirst : horizontalFirst;
    }

    private List<Point> BuildRoute(Point from, Point to, Vector? targetDirection)
    {
        List<Point> route = new();
        bool alignedX = Math.Abs(from.X - to.X) < 1e-6;
        bool alignedY = Math.Abs(from.Y - to.Y) < 1e-6;
        if (alignedX && alignedY)
        {
            return route;
        }
        if (CkOrtho.IsChecked != true || SpaceHeld)
        {
            route.Add(to);
            return route;
        }
        if (alignedX || alignedY)
        {
            route.Add(to);
            return route;
        }
        bool horizontalFirst = ChooseHorizontalFirst(targetDirection);
        route.Add(horizontalFirst ? new Point(to.X, from.Y) : new Point(from.X, to.Y));
        route.Add(to);
        return route;
    }

    private static Vector? TargetDirection(SnapHit hit)
    {
        if (hit.Owner == null)
        {
            return null;
        }
        return hit.Owner.GetPinDirection(hit.Pin);
    }

    private List<Point> BuildWirePreview(SnapHit hit)
    {
        List<Point> points = new(_wirePoints);
        points.AddRange(BuildRoute(points[points.Count - 1], hit.Point, TargetDirection(hit)));
        return points;
    }

    private void WireClick(Point world)
    {
        SnapHit hit = SnapWithAngleLock(world);
        if (SpaceHeld && _wirePoints.Count > 0)
        {
            _wireAngleLocked = true;
        }
        if (_wirePoints.Count == 0)
        {
            _wirePoints.Add(hit.Point);
            _wireStartDir = hit.Owner != null ? hit.Owner.GetPinDirection(hit.Pin) : null;
            _wireFlip = false;
            return;
        }
        _wirePoints.AddRange(BuildRoute(_wirePoints[_wirePoints.Count - 1], hit.Point, TargetDirection(hit)));
        _wireFlip = false;
        if (hit.Owner != null)
        {
            FinishWire();
        }
    }

    private void FinishWire()
    {
        if (_wirePoints.Count >= 2)
        {
            WireEntity wire = new(_wirePoints.ToList())
            {
                Orthogonal = CkOrtho.IsChecked == true && !_wireAngleLocked,
                Realistic = _wireRealistic
            };
            wire.Normalize();
            if (wire.Length > 0.01)
            {
                wire.Number = NextWireNumber();
                AttachWireEnds(wire);
                AddEntity(wire);
                SetSelection(new[] { wire });
                CommitChange();
            }
        }
        CancelWire();
    }

    private void CancelWire()
    {
        _wireAngleLocked = false;
        _wirePoints.Clear();
        _wireStartDir = null;
        _wireFlip = false;
        _previewPath.Visibility = Visibility.Collapsed;
    }

    /// <summary>Aggancia le estremità del filo ai pin che coincidono con esse.</summary>
    private void AttachWireEnds(WireEntity wire)
    {
        for (int end = 0; end < 2; end++)
        {
            Point point = end == 0 ? wire.StartPoint : wire.EndPoint;
            Guid? owner = null;
            int pinIndex = 0;
            foreach (EntityBase entity in _entities)
            {
                for (int i = 0; i < entity.PinCount; i++)
                {
                    if ((entity.GetPinPosition(i) - point).Length < 0.01)
                    {
                        owner = entity.Id;
                        pinIndex = i;
                    }
                }
            }
            if (end == 0)
            {
                wire.SetStart(owner, pinIndex);
            }
            else
            {
                wire.SetEnd(owner, pinIndex);
            }
        }
    }

    private void FinalizeWireGrip(WireEntity wire)
    {
        wire.Normalize();
        AttachWireEnds(wire);
    }

    // ---------------------------------------------------------------- collegamenti fili/pin

    private Point? ResolvePin(Guid id, int pin)
    {
        if (_byId.TryGetValue(id, out EntityBase? owner) && pin >= 0 && pin < owner.PinCount)
        {
            return owner.GetPinPosition(pin);
        }
        return null;
    }

    private void SyncWiresTo(EntityBase owner)
    {
        foreach (WireEntity wire in _entities.OfType<WireEntity>().ToList())
        {
            if (wire.StartComp == owner.Id || wire.EndComp == owner.Id)
            {
                wire.SyncEndpoints(ResolvePin);
            }
        }
    }

    private void SyncAllWires()
    {
        foreach (WireEntity wire in _entities.OfType<WireEntity>().ToList())
        {
            wire.SyncEndpoints(ResolvePin);
        }
    }

    private void DetachWiresFrom(Guid id)
    {
        foreach (WireEntity wire in _entities.OfType<WireEntity>())
        {
            wire.ClearReferencesTo(id);
        }
    }

    // ---------------------------------------------------------------- etichette

    private SolidColorBrush CachedBrush(string hex)
    {
        if (!_colorBrushes.TryGetValue(hex, out SolidColorBrush? brush))
        {
            brush = Brush(hex);
            brush.Freeze();
            _colorBrushes[hex] = brush;
        }
        return brush;
    }

    /// <summary>Strisce del secondo colore (2 mm) sovrapposte al tracciato principale (fili bicolore).</summary>
    private void UpdateOverlay(EntityBase entity, Path main, string? secondColor, bool selected, double thickness)
    {
        if (secondColor == null || selected || thickness <= 0)
        {
            RemoveOverlay(entity);
            return;
        }
        if (!_overlays.TryGetValue(entity, out Path? overlay))
        {
            overlay = new Path { IsHitTestVisible = false };
            _overlays[entity] = overlay;
            DrawCanvas.Children.Add(overlay);
        }
        double dash = 2 / thickness;
        overlay.Data = main.Data;
        overlay.Stroke = CachedBrush(secondColor);
        overlay.StrokeThickness = thickness;
        overlay.StrokeDashArray = new DoubleCollection { dash, dash };
        overlay.StrokeLineJoin = main.StrokeLineJoin;
    }

    /// <summary>Filo realistico: bordo scuro + riempimento del colore del filo (+ strisce del secondo colore).</summary>
    private void ApplyBandStyle(WireEntity wire, Path path, bool selected)
    {
        double diameter = wire.LineWidth;
        double inner = Math.Max(0.2, diameter - 2 * SceneBuilder.BandBorder);
        path.Fill = null;
        path.Stroke = selected ? SelectedStroke : CachedBrush(SceneBuilder.BandOutlineColor);
        path.StrokeThickness = diameter + (selected ? 0.5 : 0);
        path.StrokeDashArray = null;
        path.StrokeLineJoin = PenLineJoin.Round;

        if (!_inners.TryGetValue(wire, out Path? fill))
        {
            fill = new Path { IsHitTestVisible = false };
            _inners[wire] = fill;
            int index = DrawCanvas.Children.IndexOf(path);
            DrawCanvas.Children.Insert(index >= 0 ? index + 1 : DrawCanvas.Children.Count, fill);
            // le strisce devono restare sopra il riempimento
            if (_overlays.TryGetValue(wire, out Path? stripes))
            {
                DrawCanvas.Children.Remove(stripes);
                DrawCanvas.Children.Add(stripes);
            }
        }
        fill.Data = path.Data;
        fill.Stroke = CachedBrush(wire.StrokeColor ?? SceneBuilder.BandDefaultFill);
        fill.StrokeThickness = inner;
        fill.StrokeLineJoin = PenLineJoin.Round;

        Path inside = fill;
        UpdateOverlay(wire, inside, wire.StrokeColor2, false, inner);
    }

    private void RemoveInner(EntityBase entity)
    {
        if (_inners.Remove(entity, out Path? fill))
        {
            DrawCanvas.Children.Remove(fill);
        }
    }

    private void RemoveOverlay(EntityBase entity)
    {
        if (_overlays.Remove(entity, out Path? overlay))
        {
            DrawCanvas.Children.Remove(overlay);
        }
    }

    private void ScheduleLabels()
    {
        if (_labelsPending)
        {
            return;
        }
        _labelsPending = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _labelsPending = false;
            RefreshLabels();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshLabels()
    {
        UpdateWireExtras();
        UpdateSheathContents();
        LabelCanvas.Children.Clear();
        foreach (EntityBase entity in _entities)
        {
            bool selected = _selection.Contains(entity);
            foreach (EntityLabel label in entity.GetLabels().Concat(entity.GetPropertyLabels()))
            {
                TextBlock block = new()
                {
                    Text = label.Text,
                    FontSize = label.Height,
                    FontFamily = new FontFamily("Segoe UI"),
                    Foreground = selected ? SelectedStroke : Brushes.Black,
                    IsHitTestVisible = false
                };
                block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Size size = block.DesiredSize;
                Canvas.SetLeft(block, label.Anchor.X - size.Width * label.AlignX);
                Canvas.SetTop(block, label.Anchor.Y - size.Height * label.AlignY);
                LabelCanvas.Children.Add(block);
            }
        }
    }
}
