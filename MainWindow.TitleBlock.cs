using System.IO;
using System.Text.Json;
using System.Windows;

namespace WireCAD;

// Parte della finestra dedicata all'esportazione e all'importazione dei campi del cartiglio (file .wctb, JSON).
public partial class MainWindow
{
    private const string TitleBlockExtension = ".wctb";
    private const string TitleBlockFilter = "Cartiglio WireCAD (*.wctb)|*.wctb|Tutti i file (*.*)|*.*";

    private static readonly JsonSerializerOptions TitleBlockJson = new() { WriteIndented = true };

    private Dictionary<string, string> CaptureTitleBlockFields()
    {
        return new Dictionary<string, string>
        {
            ["title"] = TitleBlockCtl.DrawingTitle ?? string.Empty,
            ["company"] = TitleBlockCtl.CompanyName ?? string.Empty,
            ["author"] = TitleBlockCtl.AuthorName ?? string.Empty,
            ["number"] = TitleBlockCtl.DrawingNumber ?? string.Empty,
            ["scale"] = TitleBlockCtl.ScaleValue ?? string.Empty,
            ["date"] = TitleBlockCtl.DateValue ?? string.Empty,
            ["revision"] = TitleBlockCtl.RevisionValue ?? string.Empty,
            ["sheet"] = TitleBlockCtl.SheetValue ?? string.Empty
        };
    }

    private void MenuExportTitleBlock_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Esporta cartiglio",
            Filter = TitleBlockFilter,
            DefaultExt = TitleBlockExtension,
            AddExtension = true,
            FileName = "cartiglio"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(CaptureTitleBlockFields(), TitleBlockJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare il cartiglio:\n{ex.Message}", "Esporta cartiglio",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        StatusHint.Text = $"Cartiglio esportato in \"{System.IO.Path.GetFileName(dialog.FileName)}\".";
    }

    private void MenuImportTitleBlock_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Importa cartiglio",
            Filter = TitleBlockFilter,
            DefaultExt = TitleBlockExtension,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        Dictionary<string, string>? fields;
        try
        {
            fields = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, $"Impossibile leggere il cartiglio:\n{ex.Message}", "Importa cartiglio",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (fields == null || fields.Count == 0)
        {
            MessageBox.Show(this, "Il file non contiene campi del cartiglio.", "Importa cartiglio",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int applied = 0;
        string? Take(string key)
        {
            if (fields.TryGetValue(key, out string? value))
            {
                applied++;
                return value ?? string.Empty;
            }
            return null;
        }
        TitleBlockCtl.DrawingTitle = Take("title") ?? TitleBlockCtl.DrawingTitle;
        TitleBlockCtl.CompanyName = Take("company") ?? TitleBlockCtl.CompanyName;
        TitleBlockCtl.AuthorName = Take("author") ?? TitleBlockCtl.AuthorName;
        TitleBlockCtl.DrawingNumber = Take("number") ?? TitleBlockCtl.DrawingNumber;
        TitleBlockCtl.ScaleValue = Take("scale") ?? TitleBlockCtl.ScaleValue;
        TitleBlockCtl.DateValue = Take("date") ?? TitleBlockCtl.DateValue;
        TitleBlockCtl.RevisionValue = Take("revision") ?? TitleBlockCtl.RevisionValue;
        TitleBlockCtl.SheetValue = Take("sheet") ?? TitleBlockCtl.SheetValue;

        if (applied == 0)
        {
            MessageBox.Show(this, "Il file non contiene campi del cartiglio riconosciuti.", "Importa cartiglio",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        CommitChange();
        StatusHint.Text = $"Cartiglio importato ({applied} campi).";
    }
}
