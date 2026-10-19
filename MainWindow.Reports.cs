using System.IO;
using System.Text;
using System.Windows;
using WireCAD.Controls;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata a numerazione fili, controlli ERC e report di produzione.
public partial class MainWindow
{
    private void MenuNumberWires_Click(object sender, RoutedEventArgs e)
    {
        string? prefix = Dialogs.Prompt(this, "Numera fili",
            "Prefisso di numerazione. I fili dello stesso potenziale ricevono lo stesso numero:", _wirePrefix);
        if (prefix == null)
        {
            return;
        }
        _wirePrefix = string.IsNullOrWhiteSpace(prefix) ? "W" : prefix.Trim();
        NetResult nets = NetAnalyzer.Analyze(_entities.ToList());
        int counter = 1;
        foreach (Net net in nets.Nets)
        {
            string number = _wirePrefix + counter.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            counter++;
            foreach (WireEntity wire in net.Wires)
            {
                wire.Number = number;
            }
        }
        RefreshFields();
        ScheduleLabels();
        CommitChange();
        StatusHint.Text = $"Numerati {nets.Nets.Count} potenziali.";
    }

    private void InsertTable(List<string[]> rows)
    {
        TableEntity table = new() { Position = Snap(new Point(SheetMargin + 5, SheetMargin + 5)) };
        table.SetRows(rows);
        AddEntity(table);
        SetSelection(new[] { table });
        CommitChange();
        StatusHint.Text = "Tabella inserita in alto a sinistra: trascinala dove serve. È una copia: per aggiornarla eliminala e reinseriscila.";
    }

    private void MenuWireTable_Click(object sender, RoutedEventArgs e)
    {
        UpdateWireExtras();
        InsertTable(Reports.WireRows(_entities.ToList()));
    }

    private void MenuBomTable_Click(object sender, RoutedEventArgs e)
    {
        InsertTable(Reports.BomRows(_entities.ToList()));
    }

    private void MenuErc_Click(object sender, RoutedEventArgs e)
    {
        List<EntityBase> all = _entities.ToList();
        NetResult nets = NetAnalyzer.Analyze(all);
        List<ErcIssue> issues = ErcChecker.Run(all, nets);
        Dialogs.ShowText(this, "Controlli di coerenza (ERC)", ErcChecker.Format(issues, nets, all));
    }

    private void MenuWireList_Click(object sender, RoutedEventArgs e)
    {
        UpdateWireExtras();
        ExportCsv("Distinta fili", "distinta-fili", () =>
        {
            List<EntityBase> all = _entities.ToList();
            return Reports.WireList(all, NetAnalyzer.Analyze(all));
        });
    }

    private void MenuBom_Click(object sender, RoutedEventArgs e)
    {
        ExportCsv("Distinta materiali", "distinta-materiali", () => Reports.Bom(_entities.ToList()));
    }

    private void MenuPinTable_Click(object sender, RoutedEventArgs e)
    {
        ExportCsv("Tabella pin", "tabella-pin", () => Reports.PinTable(_entities.ToList()));
    }

    private void ExportCsv(string title, string defaultName, Func<string> build)
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = title,
            Filter = "CSV (*.csv)|*.csv|Tutti i file (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = defaultName
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, build(), new UTF8Encoding(true));
            StatusHint.Text = $"{title} salvata in {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare il file:\n{ex.Message}", title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
