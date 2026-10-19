using System.IO;
using System.Windows;
using System.Windows.Controls;
using WireCAD.Controls;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata ai connettori personalizzati: salvataggio come file, importazione, scomposizione.
public partial class MainWindow
{
    private void ReloadLibrary()
    {
        LibraryList.ItemsSource = null;
        LibraryList.ItemsSource = ComponentTemplate.Reload();
        _template = null;
    }

    private static string SafeFileName(string name)
    {
        string result = name;
        foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(result) ? "connettore" : result.Trim();
    }

    private void SaveSelectionAsConnector()
    {
        List<EntityBase> selected = _entities.Where(_selection.Contains).ToList();
        List<EntityBase> parts = selected.Where(ComponentTemplate.IsConnectorPart).ToList();
        if (parts.Count == 0)
        {
            MessageBox.Show(this,
                "Seleziona il disegno del connettore (linee, archi, cerchi, spline, rettangoli, testi) e i suoi pin numerati.",
                "Salva come connettore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!parts.OfType<PinEntity>().Any())
        {
            MessageBoxResult answer = MessageBox.Show(this,
                "La selezione non contiene nessun Pin: i fili non potranno agganciarsi al connettore.\nSalvare comunque?",
                "Salva come connettore", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        ConnectorInfo? info = Dialogs.PromptConnector(this, new ConnectorInfo { Name = "Nuovo connettore", Prefix = "X" });
        if (info == null)
        {
            return;
        }

        Directory.CreateDirectory(CustomConnectorLibrary.Folder);
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Salva come connettore",
            Filter = ShapeFile.Filter,
            DefaultExt = ShapeFile.Extension,
            AddExtension = true,
            InitialDirectory = CustomConnectorLibrary.Folder,
            FileName = SafeFileName(info.Name)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            ShapeFile.Save(dialog.FileName, parts, info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare il file:\n{ex.Message}", "Salva come connettore",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        ReloadLibrary();
        bool inLibrary = string.Equals(
            System.IO.Path.GetDirectoryName(dialog.FileName),
            CustomConnectorLibrary.Folder,
            StringComparison.OrdinalIgnoreCase);
        StatusHint.Text = inLibrary
            ? $"Connettore \"{info.Name}\" salvato e aggiunto alla libreria."
            : $"Connettore salvato in {dialog.FileName}. Per averlo nella libreria usa File > Importa connettore da file.";
    }

    private void ImportConnectorFile()
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Importa connettore da file",
            Filter = ShapeFile.Filter,
            DefaultExt = ShapeFile.Extension,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        ShapeFileLoadResult result;
        try
        {
            result = ShapeFile.Load(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, $"Impossibile leggere il file:\n{ex.Message}", "Importa connettore",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (result.Connector == null || ComponentTemplate.FromShapes(result.Entities, result.Connector) == null)
        {
            MessageBox.Show(this,
                "Il file non è un connettore. Per usarlo come connettore seleziona le sue forme e salvalo con \"Salva selezione come connettore\".",
                "Importa connettore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string destination = System.IO.Path.Combine(CustomConnectorLibrary.Folder, System.IO.Path.GetFileName(dialog.FileName));
        try
        {
            Directory.CreateDirectory(CustomConnectorLibrary.Folder);
            bool same = string.Equals(
                System.IO.Path.GetFullPath(dialog.FileName), System.IO.Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
            if (!same)
            {
                if (File.Exists(destination)
                    && MessageBox.Show(this, "Nella libreria esiste già un file con questo nome. Sostituirlo?", "Importa connettore",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
                File.Copy(dialog.FileName, destination, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile copiare il file nella libreria:\n{ex.Message}", "Importa connettore",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        ReloadLibrary();
        StatusHint.Text = $"Connettore \"{result.Connector.Name}\" aggiunto alla libreria.";
    }

    /// <summary>Trasforma il connettore in forme e pin indipendenti, raggruppati, mantenendo i fili agganciati.</summary>
    private void ExplodeConnector(ComponentEntity component)
    {
        List<WireEntity> attached = _entities
            .OfType<WireEntity>()
            .Where(wire => wire.StartComp == component.Id || wire.EndComp == component.Id)
            .ToList();
        string name = string.IsNullOrWhiteSpace(component.Tag) ? component.Model : component.Tag;
        List<EntityBase> pieces = component.Explode();

        EntityNode? group = null;
        _restoring = true;
        try
        {
            DeleteEntities(new List<EntityBase> { component });
            foreach (EntityBase piece in pieces)
            {
                AddEntity(piece);
            }
            group = new EntityNode(UniqueGroupName(name + " (scomposto)")) { IsExpanded = true };
            _roots.Add(group);
            MoveToGroup(pieces, group);
        }
        finally
        {
            _restoring = false;
        }
        foreach (WireEntity wire in attached)
        {
            AttachWireEnds(wire);
        }
        SetSelection(pieces);
        CommitChange();
        StatusHint.Text = $"Connettore scomposto in {pieces.Count} oggetti (gruppo \"{group.Name}\"); i fili restano agganciati ai pin.";
    }

    private void MenuSaveConnector_Click(object sender, RoutedEventArgs e) => SaveSelectionAsConnector();

    private void MenuImportConnector_Click(object sender, RoutedEventArgs e) => ImportConnectorFile();

    private void MenuOpenConnectorFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(CustomConnectorLibrary.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = CustomConnectorLibrary.Folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Impossibile aprire la cartella dei connettori:\n{ex.Message}", "Cartella connettori",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
