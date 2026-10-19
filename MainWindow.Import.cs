using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WireCAD.Controls;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata all'importazione di disegni DXF/DWG con posizionamento a clic.
public partial class MainWindow
{
    private List<EntityBase>? _pendingImport;
    private string _pendingImportPath = "";
    private Rect _pendingImportBounds;

    private void MenuImportCad_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Importa DXF / DWG",
            Filter = CadImporter.Filter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string? scaleText = Dialogs.Prompt(this, "Importa DXF / DWG",
            "Fattore per convertire le unità del file in millimetri\n(1 = millimetri, 10 = centimetri, 1000 = metri, 25,4 = pollici):",
            "1");
        if (scaleText == null)
        {
            return;
        }
        if (!TryParseMm(scaleText, out double scale) || scale <= 0 || scale > 1e6)
        {
            MessageBox.Show(this, "Fattore di conversione non valido.", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CadImportResult result;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            result = CadImporter.Import(dialog.FileName, scale);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile importare il file:\n{ex.Message}", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (result.Entities.Count == 0)
        {
            MessageBox.Show(this, "Il file non contiene entità importabili.", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Rect bounds = Rect.Empty;
        foreach (EntityBase entity in result.Entities)
        {
            bounds.Union(entity.BuildGeometry().Bounds);
        }
        if (bounds.IsEmpty)
        {
            MessageBox.Show(this, "Il disegno importato è vuoto.", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetTool(ToolType.Select);
        _pendingImport = result.Entities;
        _pendingImportPath = dialog.FileName;
        _pendingImportBounds = bounds;
        SetTool(ToolType.PlaceImport);
        StatusHint.Text = result.Skipped > 0
            ? $"Importate {result.Entities.Count} entità ({result.Skipped} non supportate, ignorate). Clicca sul foglio per posizionare il disegno (angolo in alto a sinistra). Esc annulla."
            : $"Importate {result.Entities.Count} entità. Clicca sul foglio per posizionare il disegno (angolo in alto a sinistra). Esc annulla.";
    }

    private ImageEntity? _pendingImage;

    private void MenuImportImage_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Importa immagine",
            Filter = ImageEntity.Filter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ImageEntity image;
        try
        {
            image = ImageEntity.FromFile(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile aprire l'immagine:\n{ex.Message}", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        double suggested = Math.Min(image.NaturalWidth, _sheetWidth * 0.8);
        string? widthText = Dialogs.Prompt(this, "Importa immagine",
            "Larghezza dell'immagine sul foglio in millimetri\n(l'altezza segue le proporzioni; si può cambiare anche dopo):",
            FormatMm(Math.Round(suggested, 1)));
        if (widthText == null)
        {
            return;
        }
        if (!TryParseMm(widthText, out double width) || width < 1 || width > 1e5)
        {
            MessageBox.Show(this, "Larghezza non valida.", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        image.Width = width;

        SetTool(ToolType.Select);
        _pendingImage = image;
        SetTool(ToolType.PlaceImport);
        StatusHint.Text = "Immagine pronta: clicca sul foglio per posizionarla (angolo in alto a sinistra). Esc annulla.";
    }

    private Geometry? BuildImportPreview(Point cursor)
    {
        if (_pendingImage != null)
        {
            return new RectangleGeometry(new Rect(cursor, new Size(_pendingImage.Width, _pendingImage.Height)));
        }
        if (_pendingImport == null)
        {
            return null;
        }
        return new RectangleGeometry(new Rect(cursor, new Size(_pendingImportBounds.Width, _pendingImportBounds.Height)));
    }

    private void PlaceImport(Point world)
    {
        if (_pendingImage != null)
        {
            ImageEntity image = _pendingImage;
            _pendingImage = null;
            image.TopLeft = SmartSnap(world).Point;
            AddEntity(image);
            SetSelection(new[] { image });
            CommitChange();
            SetTool(ToolType.Select);
            StatusHint.Text = $"Immagine \"{image.Name}\" posizionata. Per selezionarla clicca sul suo bordo; opacità e dimensioni sono nelle proprietà.";
            return;
        }
        if (_pendingImport == null)
        {
            return;
        }
        Point target = SmartSnap(world).Point;
        Vector offset = target - _pendingImportBounds.TopLeft;
        List<EntityBase> entities = _pendingImport;
        _pendingImport = null;

        string name = System.IO.Path.GetFileName(_pendingImportPath);
        EntityNode group = new(name)
        {
            IsExpanded = false,
            FilePath = System.IO.Path.ChangeExtension(_pendingImportPath, ShapeFile.Extension)
        };
        _roots.Add(group);
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            foreach (EntityBase entity in entities)
            {
                entity.Translate(offset);
                AddEntity(entity, group);
            }
            group.CaptureBaseline();
            SetSelection(entities);
            CommitChange();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
        SetTool(ToolType.Select);
        StatusHint.Text = $"Importato \"{name}\": {entities.Count} oggetti, raggruppati nell'elenco entità.";
    }
}
