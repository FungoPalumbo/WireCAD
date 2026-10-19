using System.Windows;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata all'esportazione del disegno (DXF, DWG, PDF, SVG).
public partial class MainWindow
{
    private ExportScene BuildScene()
    {
        UpdateWireExtras();
        return SceneBuilder.Build(
            _entities.ToList(),
            _sheetWidth,
            _sheetHeight,
            CkTitleBlock.IsChecked == true,
            CaptureSettings().TitleBlock);
    }

    private void ExportDrawing(string title, string filter, string extension, Action<string, ExportScene> write)
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = title,
            Filter = filter,
            DefaultExt = extension,
            AddExtension = true,
            FileName = _projectPath != null ? System.IO.Path.GetFileNameWithoutExtension(_projectPath) : "disegno"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            write(dialog.FileName, BuildScene());
            StatusHint.Text = $"{title}: salvato in {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile esportare il disegno:\n{ex.Message}", title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MenuExportDxf_Click(object sender, RoutedEventArgs e)
    {
        ExportDrawing("Esporta DXF", "DXF (*.dxf)|*.dxf", ".dxf", DxfExporter.Write);
    }

    private void MenuExportDwg_Click(object sender, RoutedEventArgs e)
    {
        ExportDrawing("Esporta DWG", "DWG (*.dwg)|*.dwg", ".dwg", DwgExporter.Write);
    }

    private void MenuExportPdf_Click(object sender, RoutedEventArgs e)
    {
        ExportDrawing("Esporta PDF", "PDF (*.pdf)|*.pdf", ".pdf", PdfExporter.Write);
    }

    private void MenuExportSvg_Click(object sender, RoutedEventArgs e)
    {
        ExportDrawing("Esporta SVG", "SVG (*.svg)|*.svg", ".svg", SvgExporter.Write);
    }
}
