using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata alla scelta dell'aspetto dei nuovi fili (pressione prolungata sull'icona Filo).
public partial class MainWindow
{
    private readonly DispatcherTimer _wireHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _wireMenuOpened;
    private bool _wireRealistic = true;

    private void BtnWire_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _wireMenuOpened = false;
        _wireHoldTimer.Start();
    }

    private void BtnWire_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _wireHoldTimer.Stop();
    }

    private void BtnWire_MouseLeave(object sender, MouseEventArgs e)
    {
        _wireHoldTimer.Stop();
    }

    private void BtnWire_RightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowWireMenu();
    }

    private void WireHoldTimer_Tick(object? sender, EventArgs e)
    {
        _wireHoldTimer.Stop();
        _wireMenuOpened = true;
        ShowWireMenu();
    }

    private void BtnWire_Click(object sender, RoutedEventArgs e)
    {
        if (_wireMenuOpened)
        {
            _wireMenuOpened = false;
            SetTool(_tool);
            return;
        }
        SetTool(ToolType.Wire);
    }

    private void ShowWireMenu()
    {
        ContextMenu menu = new() { PlacementTarget = BtnWire, Placement = PlacementMode.Bottom };

        MenuItem simple = new() { Header = "Filo semplice (linea)", IsChecked = !_wireRealistic };
        simple.Click += (_, _) =>
        {
            _wireRealistic = false;
            SetTool(ToolType.Wire);
        };
        menu.Items.Add(simple);

        MenuItem realistic = new()
        {
            Header = "Filo realistico (due linee parallele con riempimento)",
            IsChecked = _wireRealistic
        };
        realistic.Click += (_, _) =>
        {
            _wireRealistic = true;
            SetTool(ToolType.Wire);
        };
        menu.Items.Add(realistic);

        menu.IsOpen = true;
    }
}
