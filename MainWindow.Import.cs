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

        // prima lettura a fattore 1: serve a misurare il disegno e, se supera il foglio, a proporre un fattore adatto
        CadImportResult? result = ReadCad(dialog.FileName, 1);
        if (result == null)
        {
            return;
        }
        Rect bounds = BoundsOf(result.Entities);
        if (bounds.IsEmpty)
        {
            ShowMessage("Il file non contiene entità importabili.", "Importazione", MessageBoxImage.Warning);
            return;
        }

        double suggested = SuggestImportScale(bounds);
        string size = $"Il disegno misura {FormatMm(Math.Round(bounds.Width, 1))} × {FormatMm(Math.Round(bounds.Height, 1))} mm con fattore 1; "
            + $"il foglio è {FormatMm(_sheetWidth)} × {FormatMm(_sheetHeight)} mm.";
        string advice = suggested < 1
            ? $"Supera il foglio: con il fattore {FormatMm(suggested)} diventa {FormatMm(Math.Round(bounds.Width * suggested, 1))} × {FormatMm(Math.Round(bounds.Height * suggested, 1))} mm."
            : "Entra nel foglio.";
        string? scaleText = Dialogs.Prompt(this, "Importa DXF / DWG",
            $"{size}\n{advice}\n\nFattore per convertire le unità del file in millimetri\n(1 = millimetri, 10 = centimetri, 1000 = metri, 25,4 = pollici; sotto 1 riduce il disegno):",
            FormatMm(suggested));
        if (scaleText == null)
        {
            return;
        }
        if (!TryParseMm(scaleText, out double scale) || scale <= 0 || scale > 1e6)
        {
            ShowMessage("Fattore di conversione non valido.", "Importazione", MessageBoxImage.Warning);
            return;
        }

        if (Math.Abs(scale - 1) > 1e-12)
        {
            result = ReadCad(dialog.FileName, scale);
            if (result == null)
            {
                return;
            }
            bounds = BoundsOf(result.Entities);
            if (bounds.IsEmpty)
            {
                ShowMessage("Il disegno importato è vuoto.", "Importazione", MessageBoxImage.Warning);
                return;
            }
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

    /// <summary>Legge un DXF/DWG con il fattore dato, con il cursore di attesa; null (con messaggio) se non riesce.</summary>
    private CadImportResult? ReadCad(string path, double? scale)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            // senza fattore si usano le unità dichiarate nel file
            CadImportResult result = scale is double factor ? CadImporter.Import(path, factor) : CadImporter.ImportAutoUnits(path);
            if (result.Entities.Count == 0)
            {
                ShowMessage("Il file non contiene entità importabili.", "Importazione", MessageBoxImage.Warning);
                return null;
            }
            return result;
        }
        catch (Exception ex)
        {
            ShowMessage($"Impossibile importare il file:\n{ex.Message}", "Importazione", MessageBoxImage.Error);
            return null;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>
    /// Apre un DXF/DWG come documento: il foglio prende le dimensioni del disegno (con un margine), senza cornice né cartiglio,
    /// e il disegno vi è centrato. Le unità sono quelle dichiarate nel file.
    /// </summary>
    private void OpenCadDocument(string path)
    {
        CadImportResult? result = ReadCad(path, null);
        if (result == null)
        {
            return;
        }
        Rect bounds = BoundsOf(result.Entities);
        if (bounds.IsEmpty)
        {
            ShowMessage("Il disegno è vuoto.", "Apertura", MessageBoxImage.Warning);
            return;
        }

        const double margin = 10;
        double width = Math.Clamp(bounds.Width + 2 * margin, MinSheetSize, MaxSheetSize);
        double height = Math.Clamp(bounds.Height + 2 * margin, MinSheetSize, MaxSheetSize);
        bool tooBig = bounds.Width + 2 * margin > MaxSheetSize || bounds.Height + 2 * margin > MaxSheetSize;
        Vector offset = new((width - bounds.Width) / 2 - bounds.Left, (height - bounds.Height) / 2 - bounds.Top);

        _restoring = true;
        try
        {
            ClearDocument();
            _sheetWidth = width;
            _sheetHeight = height;
            CkTitleBlock.IsChecked = false;
            ApplySheetSize();
            foreach (EntityBase entity in result.Entities)
            {
                entity.Translate(offset);
                AddEntity(entity);
            }
            SyncAllWires();
            RebuildNameCounters();
        }
        finally
        {
            _restoring = false;
        }
        _projectPath = null;
        _openedCadName = System.IO.Path.GetFileName(path);
        ResetHistory();
        SetDirty(false);
        ScheduleLabels();
        FitView();
        string units = Math.Abs(result.UnitFactor - 1) > 1e-9 ? $" Unità del file convertite in millimetri (×{FormatMm(result.UnitFactor)})." : string.Empty;
        string skipped = result.Skipped > 0 ? $" {result.Skipped} elementi non supportati sono stati ignorati." : string.Empty;
        string big = tooBig ? $" Il disegno supera il foglio massimo ({MaxSheetSize:0} mm): importalo con Importa > DXF / DWG scegliendo un fattore di riduzione." : string.Empty;
        StatusHint.Text = $"Aperto \"{_openedCadName}\": {result.Entities.Count} oggetti, foglio {FormatMm(Math.Round(width, 1))} × {FormatMm(Math.Round(height, 1))} mm senza cornice.{units}{skipped}{big}";
    }

    private static Rect BoundsOf(IEnumerable<EntityBase> entities)
    {
        Rect bounds = Rect.Empty;
        foreach (EntityBase entity in entities)
        {
            bounds.Union(entity.BuildGeometry().Bounds);
        }
        return bounds;
    }

    private static readonly double[] ScaleSteps = { 5, 2, 1 };

    /// <summary>
    /// Fattore di scala (1, 0,5, 0,2, 0,1, 0,05...) con cui il disegno entra nel 90% del foglio; 1 se ci sta già.
    /// </summary>
    private double SuggestImportScale(Rect bounds)
    {
        double fit = Math.Min(
            _sheetWidth * 0.9 / Math.Max(bounds.Width, 1e-9),
            _sheetHeight * 0.9 / Math.Max(bounds.Height, 1e-9));
        if (fit >= 1)
        {
            return 1;
        }
        double decade = Math.Pow(10, Math.Floor(Math.Log10(fit)));
        foreach (double step in ScaleSteps)
        {
            if (step * decade <= fit * (1 + 1e-9))
            {
                return Math.Round(step * decade, 9);
            }
        }
        return Math.Round(decade, 9);
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
            ShowMessage($"Impossibile aprire l'immagine:\n{ex.Message}", "Importazione", MessageBoxImage.Error);
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
            ShowMessage("Larghezza non valida.", "Importazione", MessageBoxImage.Warning);
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
