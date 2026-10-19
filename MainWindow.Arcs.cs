using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using WireCAD.Controls;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata agli archi: scelta del tipo (pressione prolungata sull'icona) e raccordo tra due tratti.
public partial class MainWindow
{
    private enum ArcMode
    {
        StartEndMiddle,
        Fillet
    }

    private readonly DispatcherTimer _arcHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private ArcMode _arcMode = ArcMode.StartEndMiddle;
    private bool _arcMenuOpened;
    private double _filletRadius = 5;
    private LineEntity? _filletFirst;
    private Point _filletFirstClick;

    private ToolType CurrentArcTool => _arcMode == ArcMode.Fillet ? ToolType.Fillet : ToolType.Arc;

    // ---------------------------------------------------------------- icona dell'arco

    // Pressione prolungata sull'icona: il menu si apre mentre il tasto è ancora premuto. Si apre anche con il tasto destro.
    private void BtnArc_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _arcMenuOpened = false;
        _arcHoldTimer.Start();
    }

    private void BtnArc_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _arcHoldTimer.Stop();
    }

    private void BtnArc_MouseLeave(object sender, MouseEventArgs e)
    {
        _arcHoldTimer.Stop();
    }

    private void BtnArc_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowArcMenu();
    }

    private void ArcHoldTimer_Tick(object? sender, EventArgs e)
    {
        _arcHoldTimer.Stop();
        _arcMenuOpened = true;
        ShowArcMenu();
    }

    private void BtnArc_Click(object sender, RoutedEventArgs e)
    {
        if (_arcMenuOpened)
        {
            _arcMenuOpened = false;
            SetTool(_tool);
            return;
        }
        SetTool(CurrentArcTool);
    }

    private void ShowArcMenu()
    {
        ContextMenu menu = new() { PlacementTarget = BtnArc, Placement = PlacementMode.Bottom };

        MenuItem standard = new()
        {
            Header = "Arco: inizio, fine, punto sull'arco",
            IsChecked = _arcMode == ArcMode.StartEndMiddle
        };
        standard.Click += (_, _) =>
        {
            _arcMode = ArcMode.StartEndMiddle;
            SetTool(ToolType.Arc);
        };
        menu.Items.Add(standard);

        MenuItem fillet = new()
        {
            Header = "Raccordo tra due tratti...",
            IsChecked = _arcMode == ArcMode.Fillet
        };
        fillet.Click += (_, _) => ChooseFillet();
        menu.Items.Add(fillet);

        menu.IsOpen = true;
    }

    private void ChooseFillet()
    {
        string? text = Dialogs.Prompt(this, "Raccordo", "Raggio del raccordo (mm):", FormatMm(_filletRadius));
        if (text == null)
        {
            return;
        }
        if (!TryParseMm(text, out double radius) || radius <= 0 || radius > 1000)
        {
            StatusHint.Text = "Raggio non valido: serve un valore tra 0 e 1000 mm.";
            return;
        }
        _filletRadius = radius;
        _arcMode = ArcMode.Fillet;
        SetTool(ToolType.Fillet);
    }

    // ---------------------------------------------------------------- raccordo

    private LineEntity? HitLine(Point world)
    {
        double tolerance = Math.Max(2, 6 / _viewTransform.Matrix.M11);
        foreach (LineEntity line in _entities.OfType<LineEntity>().Reverse())
        {
            if (line.HitTest(world, tolerance))
            {
                return line;
            }
        }
        return null;
    }

    private void CancelFillet()
    {
        _filletFirst = null;
    }

    private void FilletClick(Point world)
    {
        LineEntity? line = HitLine(world);
        if (line == null)
        {
            StatusHint.Text = "Raccordo: clicca su un tratto (linea).";
            return;
        }
        if (_filletFirst == null)
        {
            _filletFirst = line;
            _filletFirstClick = world;
            SetSelection(new[] { line });
            StatusHint.Text = "Raccordo: ora clicca il secondo tratto, dal lato da conservare.";
            return;
        }
        if (ReferenceEquals(line, _filletFirst))
        {
            StatusHint.Text = "Raccordo: scegli un secondo tratto diverso dal primo.";
            return;
        }

        ArcEntity? arc = Fillet.Apply(_filletFirst, _filletFirstClick, line, world, _filletRadius, out string error);
        _filletFirst = null;
        if (arc == null)
        {
            StatusHint.Text = error;
            SetSelection(Array.Empty<EntityBase>());
            return;
        }
        AddEntity(arc);
        SetSelection(new[] { arc });
        CommitChange();
        StatusHint.Text = $"Raccordo creato (raggio {FormatMm(_filletRadius)} mm).";
    }
}
