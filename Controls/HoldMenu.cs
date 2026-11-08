using System.Windows.Threading;

namespace WireCAD.Controls;

/// <summary>
/// Comportamento comune dei pulsanti con menu a scelta del tipo: il menu si apre tenendo premuto il pulsante
/// (mentre il tasto è ancora premuto) oppure con il tasto destro; il clic che chiude la pressione prolungata
/// non deve però attivare lo strumento.
/// </summary>
internal sealed class HoldMenu
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Action _show;
    private bool _opened;

    public HoldMenu(Action show)
    {
        _show = show;
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _opened = true;
            _show();
        };
    }

    /// <summary>Tasto premuto sul pulsante: parte il conto della pressione prolungata.</summary>
    public void Press()
    {
        _opened = false;
        _timer.Start();
    }

    /// <summary>Tasto rilasciato o mouse uscito dal pulsante: la pressione non è più prolungata.</summary>
    public void Release()
    {
        _timer.Stop();
    }

    /// <summary>Apre subito il menu (tasto destro).</summary>
    public void ShowNow()
    {
        _show();
    }

    /// <summary>Vero se il menu è stato aperto da una pressione prolungata; il clic che segue va ignorato.</summary>
    public bool ConsumeOpened()
    {
        bool opened = _opened;
        _opened = false;
        return opened;
    }
}
