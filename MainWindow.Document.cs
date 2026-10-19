using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata al documento: undo/redo, progetto, copia/incolla, trasformazioni, menu.
public partial class MainWindow
{
    private const int MaxUndo = 200;

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _lastSnapshot = "";
    private bool _restoring;
    private string? _projectPath;
    private bool _dirty;
    private string? _clipboard;
    private int _pasteCount;
    private string _wirePrefix = "W";

    // ---------------------------------------------------------------- snapshot / undo

    private (string? Group, string? Path) GroupOf(EntityBase entity)
    {
        if (_nodes.TryGetValue(entity, out EntityNode? node) && node.Parent != null)
        {
            return (node.Parent.Name, node.Parent.FilePath);
        }
        return (null, null);
    }

    private string Snapshot()
    {
        return ProjectFile.Serialize(null, _entities, GroupOf, false);
    }

    /// <summary>Registra lo stato corrente come nuovo punto di undo, se è cambiato qualcosa.</summary>
    private void CommitChange()
    {
        if (_restoring)
        {
            return;
        }
        string now = Snapshot();
        if (now == _lastSnapshot)
        {
            return;
        }
        _undo.Add(_lastSnapshot);
        if (_undo.Count > MaxUndo)
        {
            _undo.RemoveAt(0);
        }
        _redo.Clear();
        _lastSnapshot = now;
        SetDirty(true);
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            StatusHint.Text = "Niente da annullare.";
            return;
        }
        _redo.Add(_lastSnapshot);
        string snapshot = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreSnapshot(snapshot);
        StatusHint.Text = "Annullato.";
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            StatusHint.Text = "Niente da ripetere.";
            return;
        }
        _undo.Add(_lastSnapshot);
        string snapshot = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreSnapshot(snapshot);
        StatusHint.Text = "Ripetuto.";
    }

    private void RestoreSnapshot(string snapshot)
    {
        ProjectData data = ProjectFile.Deserialize(snapshot, true);
        _restoring = true;
        try
        {
            ClearDocument();
            LoadEntities(data.Entities);
        }
        finally
        {
            _restoring = false;
        }
        _lastSnapshot = Snapshot();
        ScheduleLabels();
        SetDirty(true);
    }

    private void ClearDocument()
    {
        _dragMode = DragMode.None;
        _dragMoved = false;
        _marqueeRect.Visibility = Visibility.Collapsed;
        HostCanvas.ReleaseMouseCapture();
        _pendingGroupMenu = null;
        CancelWire();
        SetSelection(Array.Empty<EntityBase>());
        foreach (EntityBase entity in _entities)
        {
            entity.GeometryChanged -= Entity_GeometryChanged;
        }
        foreach (EntityNode node in _nodes.Values)
        {
            node.Detach();
        }
        DrawCanvas.Children.Clear();
        _overlays.Clear();
        _inners.Clear();
        _layers.Clear();
        _partPaths.Clear();
        _sentToBack.Clear();
        _paths.Clear();
        _entities.Clear();
        _nodes.Clear();
        _roots.Clear();
        _byId.Clear();
        LabelCanvas.Children.Clear();
    }

    private void LoadEntities(IEnumerable<LoadedEntity> items)
    {
        Dictionary<string, EntityNode> groups = new();
        foreach (LoadedEntity item in items)
        {
            EntityNode? group = null;
            if (!string.IsNullOrEmpty(item.Group))
            {
                string key = item.Group + "|" + item.GroupPath;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new EntityNode(item.Group) { IsExpanded = true, FilePath = item.GroupPath ?? "" };
                    groups[key] = group;
                    _roots.Add(group);
                }
            }
            AddEntity(item.Entity, group);
        }
        foreach (EntityNode groupNode in groups.Values)
        {
            // solo i gruppi collegati a un file tengono traccia delle modifiche non salvate
            if (!string.IsNullOrEmpty(groupNode.FilePath))
            {
                groupNode.CaptureBaseline();
            }
        }
        SyncAllWires();
        RebuildNameCounters();
    }

    private void RebuildNameCounters()
    {
        _nameCounters.Clear();
        foreach (EntityBase entity in _entities)
        {
            EntityNode? group = _nodes.TryGetValue(entity, out EntityNode? node) ? node.Parent : null;
            string prefix = entity.TypeName + " ";
            if (entity.Name.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(entity.Name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                (EntityNode?, string) key = (group, entity.TypeName);
                _nameCounters.TryGetValue(key, out int current);
                if (number > current)
                {
                    _nameCounters[key] = number;
                }
            }
        }
    }

    // ---------------------------------------------------------------- progetto

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string name = _projectPath == null ? "Senza titolo" : System.IO.Path.GetFileName(_projectPath);
        Title = $"WireCAD – {name}{(_dirty ? " *" : "")}";
    }

    private ProjectSettings CaptureSettings()
    {
        ProjectSettings settings = new()
        {
            SheetWidth = _sheetWidth,
            SheetHeight = _sheetHeight,
            GridStep = _gridStep,
            GridMajorEvery = _gridMajorEvery,
            GridOriginX = _gridOrigin.X,
            GridOriginY = _gridOrigin.Y,
            ShowTitleBlock = CkTitleBlock.IsChecked == true,
            WirePrefix = _wirePrefix,
            ThicknessFactor = DrawingScale.ThicknessFactor
        };
        settings.TitleBlock["title"] = TitleBlockCtl.DrawingTitle;
        settings.TitleBlock["company"] = TitleBlockCtl.CompanyName;
        settings.TitleBlock["author"] = TitleBlockCtl.AuthorName;
        settings.TitleBlock["number"] = TitleBlockCtl.DrawingNumber;
        settings.TitleBlock["scale"] = TitleBlockCtl.ScaleValue;
        settings.TitleBlock["date"] = TitleBlockCtl.DateValue;
        settings.TitleBlock["revision"] = TitleBlockCtl.RevisionValue;
        settings.TitleBlock["sheet"] = TitleBlockCtl.SheetValue;
        return settings;
    }

    private void ApplySettings(ProjectSettings settings)
    {
        if (settings.SheetWidth >= MinSheetSize && settings.SheetWidth <= MaxSheetSize
            && settings.SheetHeight >= MinSheetSize && settings.SheetHeight <= MaxSheetSize)
        {
            _sheetWidth = settings.SheetWidth;
            _sheetHeight = settings.SheetHeight;
        }
        if (settings.GridStep >= MinGridStep && settings.GridStep <= MaxGridStep)
        {
            _gridStep = settings.GridStep;
        }
        if (settings.GridMajorEvery >= 1 && settings.GridMajorEvery <= 100)
        {
            _gridMajorEvery = settings.GridMajorEvery;
        }
        if (Math.Abs(settings.GridOriginX) <= MaxOrigin && Math.Abs(settings.GridOriginY) <= MaxOrigin)
        {
            _gridOrigin = new Point(settings.GridOriginX, settings.GridOriginY);
        }
        DrawingScale.ThicknessFactor = DrawingScale.ClampFactor(settings.ThicknessFactor <= 0 ? 1 : settings.ThicknessFactor);
        CkTitleBlock.IsChecked = settings.ShowTitleBlock;
        _wirePrefix = string.IsNullOrWhiteSpace(settings.WirePrefix) ? "W" : settings.WirePrefix.Trim();

        string Field(string key, string fallback)
        {
            return settings.TitleBlock.TryGetValue(key, out string? value) ? value : fallback;
        }
        TitleBlockCtl.DrawingTitle = Field("title", TitleBlockCtl.DrawingTitle);
        TitleBlockCtl.CompanyName = Field("company", TitleBlockCtl.CompanyName);
        TitleBlockCtl.AuthorName = Field("author", TitleBlockCtl.AuthorName);
        TitleBlockCtl.DrawingNumber = Field("number", TitleBlockCtl.DrawingNumber);
        TitleBlockCtl.ScaleValue = Field("scale", TitleBlockCtl.ScaleValue);
        TitleBlockCtl.DateValue = Field("date", TitleBlockCtl.DateValue);
        TitleBlockCtl.RevisionValue = Field("revision", TitleBlockCtl.RevisionValue);
        TitleBlockCtl.SheetValue = Field("sheet", TitleBlockCtl.SheetValue);

        ApplySheetSize();
        UpdateGridBoxes();
    }

    private bool SaveProject(bool saveAs)
    {
        string? path = _projectPath;
        if (saveAs || path == null)
        {
            Microsoft.Win32.SaveFileDialog dialog = new()
            {
                Title = "Salva progetto",
                Filter = ProjectFile.Filter,
                DefaultExt = ProjectFile.Extension,
                AddExtension = true,
                FileName = path != null ? System.IO.Path.GetFileName(path) : "cablaggio"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }
            path = dialog.FileName;
        }
        try
        {
            ProjectFile.Save(path, CaptureSettings(), _entities, GroupOf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare il progetto:\n{ex.Message}", "Salvataggio",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        _projectPath = path;
        SetDirty(false);
        StatusHint.Text = $"Progetto salvato in {path}";
        return true;
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty)
        {
            return true;
        }
        MessageBoxResult answer = MessageBox.Show(this,
            "Ci sono modifiche non salvate. Salvarle prima di continuare?",
            "WireCAD", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel)
        {
            return false;
        }
        if (answer == MessageBoxResult.Yes)
        {
            return SaveProject(false);
        }
        return true;
    }

    private void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _lastSnapshot = Snapshot();
    }

    private void NewProject()
    {
        if (!ConfirmDiscard())
        {
            return;
        }
        _restoring = true;
        try
        {
            ClearDocument();
        }
        finally
        {
            _restoring = false;
        }
        _nameCounters.Clear();
        _projectPath = null;
        ResetHistory();
        SetDirty(false);
        ScheduleLabels();
        StatusHint.Text = "Nuovo progetto.";
    }

    private void OpenProject()
    {
        if (!ConfirmDiscard())
        {
            return;
        }
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Apri progetto",
            Filter = ProjectFile.Filter,
            DefaultExt = ProjectFile.Extension,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        ProjectData data;
        try
        {
            data = ProjectFile.Load(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, $"Impossibile aprire il progetto:\n{ex.Message}", "Apertura",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _restoring = true;
        try
        {
            ClearDocument();
            if (data.Settings != null)
            {
                ApplySettings(data.Settings);
            }
            LoadEntities(data.Entities);
        }
        finally
        {
            _restoring = false;
        }
        _projectPath = dialog.FileName;
        ResetHistory();
        SetDirty(false);
        ScheduleLabels();
        StatusHint.Text = data.Skipped > 0
            ? $"Progetto aperto ({data.Skipped} oggetti non validi ignorati)."
            : "Progetto aperto.";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }

    // ---------------------------------------------------------------- copia / incolla / trasformazioni

    private void CopySelection()
    {
        List<EntityBase> items = _entities.Where(_selection.Contains).ToList();
        if (items.Count == 0)
        {
            return;
        }
        _clipboard = ShapeFile.Serialize(items);
        _pasteCount = 0;
        StatusHint.Text = $"Copiati {items.Count} oggetti.";
    }

    private void PasteClipboard()
    {
        if (_clipboard == null)
        {
            return;
        }
        List<EntityBase> pasted;
        try
        {
            pasted = ShapeFile.Parse(_clipboard).Entities;
        }
        catch (InvalidDataException)
        {
            return;
        }
        if (pasted.Count == 0)
        {
            return;
        }
        _pasteCount++;
        double step = Math.Ceiling(10 / _gridStep) * _gridStep * _pasteCount;
        Vector offset = new(step, step);
        foreach (EntityBase item in pasted)
        {
            item.Translate(offset);
        }
        foreach (EntityBase item in pasted)
        {
            if (item is ComponentEntity component)
            {
                string prefix = new string(component.Tag.TakeWhile(char.IsLetter).ToArray());
                component.Tag = NextTag(prefix.Length > 0 ? prefix : "X");
                component.Name = component.Tag;
            }
            else
            {
                item.Name = "";
                if (item is WireEntity wire)
                {
                    wire.Number = NextWireNumber();
                }
            }
            AddEntity(item);
        }
        SyncAllWires();
        SetSelection(pasted);
        CommitChange();
        StatusHint.Text = $"Incollati {pasted.Count} oggetti.";
    }

    private Point GridRound(Point point)
    {
        return new Point(
            Math.Round(_gridOrigin.X + Math.Round((point.X - _gridOrigin.X) / _gridStep) * _gridStep, 6),
            Math.Round(_gridOrigin.Y + Math.Round((point.Y - _gridOrigin.Y) / _gridStep) * _gridStep, 6));
    }

    private void TransformSelection(TransformKind kind)
    {
        List<EntityBase> items = _entities.Where(_selection.Contains).ToList();
        if (items.Count == 0)
        {
            return;
        }
        Rect bounds = Rect.Empty;
        foreach (EntityBase item in items)
        {
            bounds.Union(item.BuildGeometry().Bounds);
        }
        if (bounds.IsEmpty)
        {
            return;
        }
        Point center = GridRound(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));

        HashSet<Guid> ids = items.Select(item => item.Id).ToHashSet();
        foreach (WireEntity wire in items.OfType<WireEntity>())
        {
            wire.DetachFrom(ids);
        }
        foreach (EntityBase item in items)
        {
            if (item is WireEntity)
            {
                item.Transform(kind, center);
            }
        }
        foreach (EntityBase item in items)
        {
            if (item is not WireEntity)
            {
                item.Transform(kind, center);
            }
        }
        UpdateGrips();
        ScheduleLabels();
        CommitChange();
    }

    // ---------------------------------------------------------------- tastiera

    private bool HandleCtrlKey(Key key, bool inTextBox)
    {
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (key)
        {
            case Key.S:
                SaveProject(shift);
                return true;
            case Key.O:
                OpenProject();
                return true;
            case Key.N:
                NewProject();
                return true;
        }
        if (inTextBox)
        {
            return false;
        }
        switch (key)
        {
            case Key.Z:
                if (shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }
                return true;
            case Key.Y:
                Redo();
                return true;
            case Key.C:
                CopySelection();
                return true;
            case Key.V:
                PasteClipboard();
                return true;
            case Key.R:
                TransformSelection(TransformKind.Rotate90);
                return true;
            case Key.M:
                TransformSelection(shift ? TransformKind.MirrorVertical : TransformKind.MirrorHorizontal);
                return true;
            case Key.A:
                SetSelection(_entities.ToList());
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- menu

    private void MenuNew_Click(object sender, RoutedEventArgs e) => NewProject();

    private void MenuOpen_Click(object sender, RoutedEventArgs e) => OpenProject();

    private void MenuSave_Click(object sender, RoutedEventArgs e) => SaveProject(false);

    private void MenuSaveAs_Click(object sender, RoutedEventArgs e) => SaveProject(true);

    private void MenuImport_Click(object sender, RoutedEventArgs e) => ImportShapes(Snap(new Point(SheetMargin * 2, SheetMargin * 2)));

    private void MenuSaveSelection_Click(object sender, RoutedEventArgs e) => SaveSelection();

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    private void MenuUndo_Click(object sender, RoutedEventArgs e) => Undo();

    private void MenuRedo_Click(object sender, RoutedEventArgs e) => Redo();

    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopySelection();

    private void MenuPaste_Click(object sender, RoutedEventArgs e) => PasteClipboard();

    private void MenuSelectAll_Click(object sender, RoutedEventArgs e) => SetSelection(_entities.ToList());

    private void MenuRotate_Click(object sender, RoutedEventArgs e) => TransformSelection(TransformKind.Rotate90);

    private void MenuMirrorH_Click(object sender, RoutedEventArgs e) => TransformSelection(TransformKind.MirrorHorizontal);

    private void MenuMirrorV_Click(object sender, RoutedEventArgs e) => TransformSelection(TransformKind.MirrorVertical);

    private void FieldTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
        else if (sender is ComboBox comboBox)
        {
            comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        }
        CommitChange();
    }

    private void FieldCombo_DropDownClosed(object sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        }
        CommitChange();
    }

    private void FieldCombo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ComboBox comboBox)
        {
            return;
        }
        comboBox.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        CommitChange();
        e.Handled = true;
    }

    private void NameBox_LostFocus(object sender, RoutedEventArgs e) => CommitChange();

    /// <summary>Casella "mostra sul foglio" accanto a una proprietà: la modifica entra nella cronologia.</summary>
    private void FieldShown_Click(object sender, RoutedEventArgs e) => CommitChange();
}
