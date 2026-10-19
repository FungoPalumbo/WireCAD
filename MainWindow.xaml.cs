using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WireCAD.Models;
using Path = System.Windows.Shapes.Path;

namespace WireCAD;

public partial class MainWindow : Window
{
    private enum DragMode
    {
        None,
        MoveEntity,
        MoveGrip,
        Marquee
    }

    private const double SheetMargin = 10;
    private const double MinSheetSize = 50;
    private const double MaxSheetSize = 5000;
    private const double MinGridStep = 0.5;
    private const double MaxGridStep = 1000;
    private const double MaxOrigin = 100000;
    private const double MinGridScreenSpacing = 4;
    private const double MinZoom = 0.08;
    private const double MaxZoom = 80;
    private const double DefaultStroke = 0.35;
    private const double GripScreenSize = 8;

    private sealed record SheetPreset(string Label, double Width, double Height);

    private static readonly SheetPreset[] SheetPresets =
    {
        new("A5 (210 × 148 mm)", 210, 148),
        new("A4 (297 × 210 mm)", 297, 210),
        new("A3 (420 × 297 mm)", 420, 297),
        new("A2 (594 × 420 mm)", 594, 420),
        new("A1 (841 × 594 mm)", 841, 594),
        new("A0 (1189 × 841 mm)", 1189, 841),
        new("Personalizzato", 0, 0)
    };

    private static readonly SolidColorBrush PreviewStroke = Brush("#2563EB");
    private static readonly SolidColorBrush AnchorStroke = Brush("#BE123C");
    private static readonly SolidColorBrush SelectedStroke = Brush("#1D4ED8");
    private static readonly SolidColorBrush WindowStroke = Brush("#2563EB");
    private static readonly SolidColorBrush WindowFill = Brush("#302563EB");
    private static readonly SolidColorBrush CrossingStroke = Brush("#16A34A");
    private static readonly SolidColorBrush CrossingFill = Brush("#3016A34A");

    private readonly MatrixTransform _viewTransform = new();
    private readonly ObservableCollection<EntityBase> _entities = new();
    private readonly Dictionary<EntityBase, Path> _paths = new();
    private readonly List<EntityBase> _selection = new();
    private readonly ObservableCollection<EntityNode> _roots = new();
    private readonly Dictionary<EntityBase, EntityNode> _nodes = new();
    private readonly Dictionary<(EntityNode? Group, string Type), int> _nameCounters = new();
    private readonly List<Point> _currentPoints = new();
    private readonly Path _previewPath = new()
    {
        Stroke = PreviewStroke,
        StrokeThickness = 0.3,
        StrokeDashArray = { 1.5, 1 },
        Visibility = Visibility.Collapsed
    };
    private readonly Rectangle _marqueeRect = new()
    {
        StrokeThickness = 1,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed
    };

    private double _sheetWidth = 297;
    private double _sheetHeight = 210;
    private double _gridStep = 5;
    private int _gridMajorEvery = 10;
    private Point _gridOrigin = new(0, 0);
    private bool _initialized;
    private bool _updatingPreset;

    private ToolType _tool = ToolType.Select;
    private IReadOnlyList<EntityField>? _currentFields;
    private IReadOnlyList<TextField>? _currentTextFields;
    private EntityNode? _pendingGroupMenu;
    private bool _panning;
    private Point _panStart;
    private DragMode _dragMode = DragMode.None;
    private int _dragGrip;
    private Point _dragLast;
    private Point _marqueeStartScreen;
    private Point _marqueeStartWorld;
    private bool _marqueeAdditive;
    private bool _marqueeMoved;
    private bool _dragMoved;

    public MainWindow()
    {
        InitializeComponent();
        WorldCanvas.RenderTransform = _viewTransform;
        WorldCanvas.Children.Add(_previewPath);
        WorldCanvas.Children.Add(_snapMarker);
        HostCanvas.Children.Add(_marqueeRect);
        EntityTree.ItemsSource = _roots;
        _entities.CollectionChanged += (_, _) => StatusCount.Text = $"Entità: {_entities.Count}";
        StatusCount.Text = "Entità: 0";
        PropertiesPanel.IsEnabled = false;
        SheetPresetBox.ItemsSource = SheetPresets;
        LibraryList.ItemsSource = ComponentTemplate.All;        _initialized = true;
        ApplySheetSize();
        UpdateGridBoxes();
        Loaded += (_, _) => FitView();
        StateChanged += MainWindow_StateChanged;
        SetTool(ToolType.Select);
        _arcHoldTimer.Tick += ArcHoldTimer_Tick;
        _wireHoldTimer.Tick += WireHoldTimer_Tick;
        _lastSnapshot = Snapshot();
        UpdateTitle();
    }

    private EntityBase? PrimarySelection => _selection.Count == 1 ? _selection[0] : null;

    private static SolidColorBrush Brush(string hex)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private Point ToWorld(Point screen)
    {
        Matrix inverse = _viewTransform.Matrix;
        inverse.Invert();
        return inverse.Transform(screen);
    }

    private Point Snap(Point point)
    {
        if (CkSnap.IsChecked != true)
        {
            return point;
        }
        return new Point(
            Math.Round(_gridOrigin.X + Math.Round((point.X - _gridOrigin.X) / _gridStep) * _gridStep, 6),
            Math.Round(_gridOrigin.Y + Math.Round((point.Y - _gridOrigin.Y) / _gridStep) * _gridStep, 6));
    }

    private void SetTool(ToolType tool)
    {
        _tool = tool;
        if (tool != ToolType.PlaceImport)
        {
            _pendingImport = null;
            _pendingImage = null;
        }
        _currentPoints.Clear();
        CancelWire();
        CancelFillet();
        _sheathPoints.Clear();
        _splinePoints.Clear();
        _snapMarker.Visibility = Visibility.Collapsed;
        _previewPath.Visibility = Visibility.Collapsed;
        BtnSelect.IsChecked = tool == ToolType.Select;
        BtnLine.IsChecked = tool == ToolType.Line;
        BtnRectangle.IsChecked = tool == ToolType.Rectangle;
        BtnArc.IsChecked = tool == ToolType.Arc || tool == ToolType.Fillet;
        BtnAnchor.IsChecked = tool == ToolType.AnchorPoint;
        BtnComponent.IsChecked = tool == ToolType.Component;
        BtnWire.IsChecked = tool == ToolType.Wire;
        BtnJunction.IsChecked = tool == ToolType.Junction;
        BtnText.IsChecked = tool == ToolType.Text;
        BtnSheath.IsChecked = tool == ToolType.Sheath;
        BtnCircle.IsChecked = tool == ToolType.Circle;
        BtnPin.IsChecked = tool == ToolType.Pin;
        BtnDimension.IsChecked = tool == ToolType.Dimension;
        BtnSpline.IsChecked = tool == ToolType.Spline;
        HostCanvas.Cursor = tool == ToolType.Select ? Cursors.Arrow : Cursors.Cross;
        StatusHint.Text = tool switch
        {
            ToolType.Select => "Clic o trascina un rettangolo per selezionare (Ctrl/Maiusc aggiunge). Trascina per spostare, i quadratini per modificare. Canc elimina.",
            ToolType.Line => "Linea: clic per il punto iniziale, secondo clic per completare. Tieni premuto Spazio per vincolare a 0°/45°/90°.",
            ToolType.Rectangle => "Rettangolo: due clic sugli angoli opposti.",
            ToolType.Arc => "Arco: clic sull'inizio, poi sulla fine, poi su un punto qualsiasi dell'arco. Tieni premuto sull'icona per cambiare tipo.",
            ToolType.Fillet => $"Raccordo (raggio {FormatMm(_filletRadius)} mm): clicca il primo tratto, poi il secondo, dal lato da conservare. Tieni premuto sull'icona dell'arco per cambiare raggio.",
            ToolType.AnchorPoint => "Punto ancora: un clic per posizionarlo.",
            ToolType.Component => "Componente: clic per posizionare l'angolo in alto a sinistra (scelto dalla libreria). Ctrl+R ruota, Ctrl+M specchia. Tasto destro: torna al selettore.",
            ToolType.Wire => "Filo: clic su un pin per iniziare, clic per i vertici (instradamento ortogonale), clic su un pin o doppio clic/Invio/tasto destro per terminare. Tieni premuto Spazio per vincolare a 0°/45°/90°; X inverte il gomito. Esc annulla.",
            ToolType.Junction => "Giunzione: clic per posizionarla (si aggancia ai fili). Tasto destro: torna al selettore.",
            ToolType.Text => "Testo: clic per posizionare, poi digita il testo. Tasto destro: torna al selettore.",
            ToolType.Sheath => "Guaina: clic sull'inizio, clic per i vertici (curve), doppio clic, Invio o tasto destro per terminare. Tieni premuto Spazio per vincolare a 0°/45°/90°. Esc annulla.",
            ToolType.Pin => "Pin: clic per posizionarlo (numero 1, modificabile dalle proprietà). Fili e linee si agganciano ai pin.",
            ToolType.PlaceImport => "Importazione: clic sul foglio per posizionare il disegno (angolo in alto a sinistra). Esc o tasto destro annullano.",
            ToolType.Circle => "Cerchio: clic sul centro, poi clic su un punto della circonferenza.",
            ToolType.Dimension => "Quota: clic sul primo punto, clic sul secondo punto, poi clic dove deve passare la linea di quota. La quota è allineata ai due punti.",
            ToolType.Spline => "Spline: clic per ogni punto di passaggio, doppio clic, Invio o tasto destro per terminare. Esc annulla.",
            ToolType.GridOrigin => "Origine griglia: clic sul foglio per impostarla. Esc annulla.",
            _ => string.Empty
        };
    }

    private static string FormatMm(double value)
    {
        return value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private static bool TryParseMm(string? text, out double value)
    {
        text = (text ?? string.Empty).Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value))
        {
            return true;
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private void ApplySheetSize()
    {
        WorldCanvas.Width = _sheetWidth;
        WorldCanvas.Height = _sheetHeight;
        DrawCanvas.Width = _sheetWidth;
        DrawCanvas.Height = _sheetHeight;
        GripCanvas.Width = _sheetWidth;
        GripCanvas.Height = _sheetHeight;
        ChromeCanvas.Width = _sheetWidth;
        ChromeCanvas.Height = _sheetHeight;
        LabelCanvas.Width = _sheetWidth;
        LabelCanvas.Height = _sheetHeight;
        SheetRect.Width = _sheetWidth;
        SheetRect.Height = _sheetHeight;

        FrameBorder.Width = _sheetWidth - 2 * SheetMargin;
        FrameBorder.Height = _sheetHeight - 2 * SheetMargin;

        double blockScale = Math.Min(1, Math.Min(
            FrameBorder.Width / TitleBlockCtl.Width,
            FrameBorder.Height / TitleBlockCtl.Height));
        TitleBlockCtl.RenderTransformOrigin = new Point(1, 1);
        TitleBlockCtl.RenderTransform = new ScaleTransform(blockScale, blockScale);
        Canvas.SetLeft(TitleBlockCtl, _sheetWidth - SheetMargin - TitleBlockCtl.Width);
        Canvas.SetTop(TitleBlockCtl, _sheetHeight - SheetMargin - TitleBlockCtl.Height);

        UpdateGrid();
        UpdateSheetBoxes();
        FitView();
    }

    private void UpdateSheetBoxes()
    {
        SheetWidthBox.Text = FormatMm(_sheetWidth);
        SheetHeightBox.Text = FormatMm(_sheetHeight);
        double longSide = Math.Max(_sheetWidth, _sheetHeight);
        double shortSide = Math.Min(_sheetWidth, _sheetHeight);
        SheetPreset? match = SheetPresets.FirstOrDefault(preset =>
            preset.Width > 0
            && Math.Abs(preset.Width - longSide) < 0.01
            && Math.Abs(preset.Height - shortSide) < 0.01);
        _updatingPreset = true;
        SheetPresetBox.SelectedItem = match ?? SheetPresets[^1];
        _updatingPreset = false;
    }

    private void UpdateGridBoxes()
    {
        GridStepBox.Text = FormatMm(_gridStep);
        GridMajorBox.Text = _gridMajorEvery.ToString(CultureInfo.CurrentCulture);
        OriginXBox.Text = FormatMm(_gridOrigin.X);
        OriginYBox.Text = FormatMm(_gridOrigin.Y);
    }

    private bool IsMajorLine(int index)
    {
        return ((index % _gridMajorEvery) + _gridMajorEvery) % _gridMajorEvery == 0;
    }

    private static void AddLine(StreamGeometryContext context, Point from, Point to)
    {
        context.BeginFigure(from, false, false);
        context.LineTo(to, true, false);
    }

    private void UpdateGrid()
    {
        StreamGeometry minor = new();
        StreamGeometry major = new();
        using (StreamGeometryContext minorContext = minor.Open())
        using (StreamGeometryContext majorContext = major.Open())
        {
            int firstX = (int)Math.Ceiling((0 - _gridOrigin.X) / _gridStep - 1e-9);
            int lastX = (int)Math.Floor((_sheetWidth - _gridOrigin.X) / _gridStep + 1e-9);
            for (int k = firstX; k <= lastX; k++)
            {
                double x = _gridOrigin.X + k * _gridStep;
                AddLine(minorContext, new Point(x, 0), new Point(x, _sheetHeight));
                if (IsMajorLine(k))
                {
                    AddLine(majorContext, new Point(x, 0), new Point(x, _sheetHeight));
                }
            }

            int firstY = (int)Math.Ceiling((0 - _gridOrigin.Y) / _gridStep - 1e-9);
            int lastY = (int)Math.Floor((_sheetHeight - _gridOrigin.Y) / _gridStep + 1e-9);
            for (int k = firstY; k <= lastY; k++)
            {
                double y = _gridOrigin.Y + k * _gridStep;
                AddLine(minorContext, new Point(0, y), new Point(_sheetWidth, y));
                if (IsMajorLine(k))
                {
                    AddLine(majorContext, new Point(0, y), new Point(_sheetWidth, y));
                }
            }
        }
        minor.Freeze();
        major.Freeze();
        GridMinorPath.Data = minor;
        GridMajorPath.Data = major;

        const double markerSize = 3;
        StreamGeometry marker = new();
        using (StreamGeometryContext context = marker.Open())
        {
            AddLine(context, new Point(_gridOrigin.X - markerSize, _gridOrigin.Y), new Point(_gridOrigin.X + markerSize, _gridOrigin.Y));
            AddLine(context, new Point(_gridOrigin.X, _gridOrigin.Y - markerSize), new Point(_gridOrigin.X, _gridOrigin.Y + markerSize));
        }
        marker.Freeze();
        OriginMarker.Data = marker;

        UpdateGridVisibility();
    }

    private void UpdateGridVisibility()
    {
        if (!_initialized)
        {
            return;
        }
        bool show = CkGrid.IsChecked == true;
        double spacing = _gridStep * _viewTransform.Matrix.M11;
        GridMinorPath.Visibility = show && spacing >= MinGridScreenSpacing ? Visibility.Visible : Visibility.Collapsed;
        GridMajorPath.Visibility = show && spacing * _gridMajorEvery >= MinGridScreenSpacing ? Visibility.Visible : Visibility.Collapsed;
        OriginMarker.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplySheetFromBoxes()
    {
        if (!_initialized)
        {
            return;
        }
        if (!(TryParseMm(SheetWidthBox.Text, out double width)
            && TryParseMm(SheetHeightBox.Text, out double height)
            && width >= MinSheetSize && width <= MaxSheetSize
            && height >= MinSheetSize && height <= MaxSheetSize))
        {
            StatusHint.Text = $"Dimensioni del foglio non valide: servono valori tra {MinSheetSize} e {MaxSheetSize} mm.";
            UpdateSheetBoxes();
            return;
        }
        if (Math.Abs(width - _sheetWidth) < 1e-9 && Math.Abs(height - _sheetHeight) < 1e-9)
        {
            UpdateSheetBoxes();
            return;
        }
        _sheetWidth = width;
        _sheetHeight = height;
        ApplySheetSize();
        StatusHint.Text = $"Foglio impostato a {FormatMm(width)} × {FormatMm(height)} mm.";
    }

    private void ApplyGridFromBoxes()
    {
        if (!_initialized)
        {
            return;
        }
        if (!(TryParseMm(GridStepBox.Text, out double step)
            && step >= MinGridStep && step <= MaxGridStep
            && int.TryParse(GridMajorBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int majorEvery)
            && majorEvery >= 1 && majorEvery <= 100
            && TryParseMm(OriginXBox.Text, out double originX)
            && TryParseMm(OriginYBox.Text, out double originY)
            && Math.Abs(originX) <= MaxOrigin && Math.Abs(originY) <= MaxOrigin))
        {
            StatusHint.Text = $"Impostazioni griglia non valide: passo tra {MinGridStep} e {MaxGridStep} mm, linea principale ogni 1–100 righe.";
            UpdateGridBoxes();
            return;
        }
        _gridStep = step;
        _gridMajorEvery = majorEvery;
        _gridOrigin = new Point(originX, originY);
        UpdateGrid();
        UpdateGridBoxes();
    }

    private void SheetPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPreset || !_initialized || SheetPresetBox.SelectedItem is not SheetPreset preset || preset.Width <= 0)
        {
            return;
        }
        bool portrait = _sheetHeight > _sheetWidth;
        _sheetWidth = portrait ? preset.Height : preset.Width;
        _sheetHeight = portrait ? preset.Width : preset.Height;
        ApplySheetSize();
    }

    private void BtnRotateSheet_Click(object sender, RoutedEventArgs e)
    {
        (_sheetWidth, _sheetHeight) = (_sheetHeight, _sheetWidth);
        ApplySheetSize();
    }

    private void SheetBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        ApplySheetFromBoxes();
        ((TextBox)sender).SelectAll();
        e.Handled = true;
    }

    private void SheetBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplySheetFromBoxes();
    }

    private void GridBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }
        ApplyGridFromBoxes();
        ((TextBox)sender).SelectAll();
        e.Handled = true;
    }

    private void GridBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyGridFromBoxes();
    }

    private void GridVisibility_Changed(object sender, RoutedEventArgs e)
    {
        UpdateGridVisibility();
    }

    private void BtnPickOrigin_Click(object sender, RoutedEventArgs e)
    {
        SetTool(ToolType.GridOrigin);
    }

    private void BtnResetOrigin_Click(object sender, RoutedEventArgs e)
    {
        _gridOrigin = new Point(0, 0);
        UpdateGrid();
        UpdateGridBoxes();
    }

    private void BtnOriginOnFrame_Click(object sender, RoutedEventArgs e)
    {
        _gridOrigin = new Point(SheetMargin, SheetMargin);
        UpdateGrid();
        UpdateGridBoxes();
        StatusHint.Text = CkTitleBlock.IsChecked == true
            ? $"Origine impostata sulla cornice: ({FormatMm(_gridOrigin.X)}; {FormatMm(_gridOrigin.Y)}) mm."
            : $"Origine impostata sulla posizione della cornice ({FormatMm(_gridOrigin.X)}; {FormatMm(_gridOrigin.Y)}) mm: la cornice è nascosta.";
    }

    /// <summary>Passando a schermo intero, se il foglio è più piccolo di quanto la finestra permetterebbe, adatta la vista.</summary>
    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Maximized)
        {
            return;
        }
        // Aspetta che il layout abbia assegnato la nuova dimensione all'area di disegno.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            double width = HostCanvas.ActualWidth;
            double height = HostCanvas.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }
            double fit = Math.Clamp(
                Math.Min((width - 40) / _sheetWidth, (height - 40) / _sheetHeight),
                MinZoom, MaxZoom);
            if (_viewTransform.Matrix.M11 < fit - 1e-9)
            {
                FitView();
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void FitView()
    {
        double width = HostCanvas.ActualWidth;
        double height = HostCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }
        double scale = Math.Clamp(
            Math.Min((width - 40) / _sheetWidth, (height - 40) / _sheetHeight),
            MinZoom, MaxZoom);
        _viewTransform.Matrix = new Matrix(scale, 0, 0, scale,
            (width - _sheetWidth * scale) / 2,
            (height - _sheetHeight * scale) / 2);
        StatusZoom.Text = $"{scale * 100:0}%";
        UpdateGridVisibility();
        UpdateGrips();
    }

    private void UpdatePreview(SnapHit hit)
    {
        Point cursor = hit.Point;
        Geometry? geometry = null;
        switch (_tool)
        {
            case ToolType.Line when _currentPoints.Count >= 1:
                geometry = new LineGeometry(_currentPoints[0], cursor);
                break;
            case ToolType.Rectangle when _currentPoints.Count >= 1:
                geometry = new RectangleGeometry(new Rect(_currentPoints[0], cursor));
                break;
            case ToolType.Arc when _currentPoints.Count == 1:
                geometry = new LineGeometry(_currentPoints[0], cursor);
                break;
            case ToolType.Arc when _currentPoints.Count >= 2:
                geometry = ArcEntity.CreateGeometry(_currentPoints[0], cursor, _currentPoints[1]);
                break;
            case ToolType.AnchorPoint:
                geometry = AnchorPointEntity.CreateGeometry(cursor);
                break;
            case ToolType.Wire when _wirePoints.Count >= 1:
                geometry = WireEntity.CreatePolyline(BuildWirePreview(hit));
                break;
            case ToolType.Component when _template != null:
                geometry = _template.IsInline
                    ? BuildInlinePreview(cursor)
                    : _template.IsCustom
                        ? new RectangleGeometry(new Rect(cursor, new Size(_template.Width, _template.Height)))
                        : _template.Create(cursor).BuildGeometry();
                break;
            case ToolType.Junction:
                geometry = new JunctionEntity { Position = cursor }.BuildGeometry();
                break;
            case ToolType.Sheath when _sheathPoints.Count >= 1:
                geometry = BuildSheathPreview(cursor);
                break;
            case ToolType.Circle when _currentPoints.Count == 1:
                geometry = BuildCirclePreview(cursor);
                break;
            case ToolType.Dimension when _currentPoints.Count == 1:
                geometry = new LineGeometry(_currentPoints[0], cursor);
                break;
            case ToolType.Dimension when _currentPoints.Count >= 2:
                geometry = new DimensionEntity { P1 = _currentPoints[0], P2 = _currentPoints[1], Offset = cursor }.BuildGeometry();
                break;
            case ToolType.PlaceImport:
                geometry = BuildImportPreview(cursor);
                break;
            case ToolType.Pin:
                geometry = PinEntity.CreateGeometry(cursor);
                break;
            case ToolType.Spline when _splinePoints.Count >= 1:
                geometry = BuildSplinePreview(cursor);
                break;
        }
        if (geometry == null)
        {
            _previewPath.Visibility = Visibility.Collapsed;
            return;
        }
        _previewPath.Data = geometry;
        _previewPath.Visibility = Visibility.Visible;
    }

    private void PlacePoint(Point world, int requiredPoints, Func<List<Point>, EntityBase> factory)
    {
        _currentPoints.Add(SnapWithAngleLock(world).Point);
        if (_currentPoints.Count >= requiredPoints)
        {
            EntityBase created = factory(_currentPoints);
            AddEntity(created);
            SetSelection(new[] { created });
            _currentPoints.Clear();
            _previewPath.Visibility = Visibility.Collapsed;
            CommitChange();
        }
    }

    private void AddEntity(EntityBase entity, EntityNode? group = null)
    {
        if (string.IsNullOrWhiteSpace(entity.Name))
        {
            (EntityNode?, string) key = (group, entity.TypeName);
            _nameCounters.TryGetValue(key, out int count);
            count++;
            _nameCounters[key] = count;
            entity.Name = $"{entity.TypeName} {count}";
        }

        Path path = new() { Data = entity.BuildGeometry() };
        DrawCanvas.Children.Add(path);
        _paths[entity] = path;
        _byId[entity.Id] = entity;
        entity.GeometryChanged += Entity_GeometryChanged;
        _entities.Add(entity);
        ApplyStyle(entity);
        if (entity is not SheathEntity)
        {
            RaiseSheaths();
        }
        ScheduleLabels();

        EntityNode node = new(entity) { Parent = group };
        _nodes[entity] = node;
        if (group == null)
        {
            _roots.Add(node);
        }
        else
        {
            group.Children.Add(node);
        }
    }

    private void Entity_GeometryChanged(object? sender, EventArgs e)
    {
        if (sender is not EntityBase entity || !_paths.TryGetValue(entity, out Path? path))
        {
            return;
        }
        path.Data = entity.BuildGeometry();
        ApplyStyle(entity);
        ScheduleLabels();
        if (entity.PinCount > 0)
        {
            SyncWiresTo(entity);
        }
        if (_nodes.TryGetValue(entity, out EntityNode? node))
        {
            node.Parent?.UpdateModifiedState();
        }
        if (ReferenceEquals(entity, PrimarySelection))
        {
            RefreshFields();
            UpdateGrips();
        }
    }

    private void ApplyStyle(EntityBase entity)
    {
        if (!_paths.TryGetValue(entity, out Path? path))
        {
            return;
        }
        bool selected = _selection.Contains(entity);
        if (entity is WireEntity { Realistic: true } bandWire)
        {
            ApplyBandStyle(bandWire, path, selected);
            return;
        }
        if (entity is not SheathEntity)
        {
            RemoveInner(entity);
        }
        path.StrokeLineJoin = PenLineJoin.Miter;
        bool anchor = entity is AnchorPointEntity;
        string? custom = entity.StrokeColor;
        SolidColorBrush stroke = selected
            ? SelectedStroke
            : anchor ? AnchorStroke : custom != null ? CachedBrush(custom) : Brushes.Black;
        string? fillHex = entity.FillHex;
        path.Fill = entity is ImageEntity picture
            ? picture.GetBrush()
            : fillHex != null ? FillBrush(fillHex, entity.FillOpacity) : entity.IsFilled ? stroke : null;
        bool toBack = entity.SendToBack;
        bool wasBack = _sentToBack.Contains(entity);
        if (toBack && !wasBack)
        {
            DrawCanvas.Children.Remove(path);
            DrawCanvas.Children.Insert(0, path);
            _sentToBack.Add(entity);
        }
        else if (!toBack && wasBack)
        {
            DrawCanvas.Children.Remove(path);
            DrawCanvas.Children.Add(path);
            _sentToBack.Remove(entity);
        }
        if (entity.HasOutline)
        {
            path.Stroke = stroke;
            path.StrokeThickness = selected ? entity.LineWidth + 0.45 : entity.LineWidth;
            path.StrokeDashArray = selected ? new DoubleCollection { 2, 1 } : entity.DashPattern;
        }
        else
        {
            path.Stroke = null;
            path.StrokeThickness = 0;
            path.StrokeDashArray = null;
        }
        UpdateOverlay(entity, path, entity.StrokeColor2, selected, path.StrokeThickness);
        if (entity is SheathEntity sheathEntity)
        {
            UpdateSheathLayer(sheathEntity);
            UpdateSheathInner(sheathEntity);
        }
        else if (entity is ComponentEntity componentEntity)
        {
            UpdateComponentParts(componentEntity);
        }
    }

    private void SetSelection(IEnumerable<EntityBase> entities)
    {
        List<EntityBase> previous = _selection.ToList();
        List<EntityBase> next = entities.Distinct().ToList();
        _selection.Clear();
        _selection.AddRange(next);
        foreach (EntityBase entity in previous)
        {
            ApplyStyle(entity);
        }
        foreach (EntityBase entity in _selection)
        {
            ApplyStyle(entity);
        }

        UpdateTreeMarks();

        EntityBase? primary = PrimarySelection;
        _currentFields = primary?.GetFields();
        if (_currentFields != null)
        {
            foreach (EntityField field in _currentFields)
            {
                field.Owner = primary;
            }
        }
        FieldsList.ItemsSource = _currentFields;
        _currentTextFields = primary?.GetTextFields();
        if (_currentTextFields != null)
        {
            foreach (TextField field in _currentTextFields)
            {
                field.Owner = primary;
            }
        }
        TextFieldsList.ItemsSource = _currentTextFields;
        NameBox.DataContext = primary;
        NameShowBox.DataContext = primary;
        LabelShowBox.DataContext = primary;
        PropertiesPanel.IsEnabled = primary != null;
        UpdateGrips();
        ScheduleLabels();
    }

    private void UpdateTreeMarks()
    {
        foreach (EntityNode root in _roots)
        {
            if (root.IsGroup)
            {
                foreach (EntityNode child in root.Children)
                {
                    child.IsMarked = _selection.Contains(child.Entity!);
                }
                root.IsMarked = root.Children.Count > 0 && root.Children.All(child => child.IsMarked);
                // selezionando dal foglio il gruppo si apre; selezionando dall'albero lo stato resta quello scelto dall'utente
                if (!_selectingFromTree && root.Children.Any(child => child.IsMarked))
                {
                    root.IsExpanded = true;
                }
            }
            else
            {
                root.IsMarked = _selection.Contains(root.Entity!);
            }
        }
    }

    private void RefreshFields()
    {
        if (_currentFields != null)
        {
            foreach (EntityField field in _currentFields)
            {
                field.Refresh();
            }
        }
        if (_currentTextFields != null)
        {
            foreach (TextField field in _currentTextFields)
            {
                field.Refresh();
            }
        }
    }

    private void UpdateGrips()
    {
        GripCanvas.Children.Clear();
        EntityBase? primary = PrimarySelection;
        if (primary == null)
        {
            return;
        }
        double size = GripScreenSize / _viewTransform.Matrix.M11;
        foreach (Point point in primary.GetGripPoints())
        {
            Rectangle grip = new()
            {
                Width = size,
                Height = size,
                Fill = Brushes.White,
                Stroke = SelectedStroke,
                StrokeThickness = size / 8
            };
            Canvas.SetLeft(grip, point.X - size / 2);
            Canvas.SetTop(grip, point.Y - size / 2);
            GripCanvas.Children.Add(grip);
        }
    }

    private void DeleteSelected()
    {
        DeleteEntities(_selection.ToList());
    }

    private void DeleteEntities(List<EntityBase> toDelete)
    {
        if (toDelete.Count == 0)
        {
            return;
        }
        EndDrag();
        SetSelection(Array.Empty<EntityBase>());
        foreach (EntityBase entity in toDelete)
        {
            entity.GeometryChanged -= Entity_GeometryChanged;
            DrawCanvas.Children.Remove(_paths[entity]);
            RemoveOverlay(entity);
            RemoveInner(entity);
            RemoveLayer(entity);
            RemoveComponentParts(entity);
            _sentToBack.Remove(entity);
            _paths.Remove(entity);
            _entities.Remove(entity);
            _byId.Remove(entity.Id);
            RemoveNode(entity);
        }
        foreach (EntityBase entity in toDelete)
        {
            DetachWiresFrom(entity.Id);
        }
        ScheduleLabels();
        CommitChange();
    }

    private void RemoveNode(EntityBase entity)
    {
        if (!_nodes.Remove(entity, out EntityNode? node))
        {
            return;
        }
        node.Detach();
        EntityNode? group = node.Parent;
        if (group == null)
        {
            _roots.Remove(node);
            return;
        }
        group.Children.Remove(node);
        if (group.Children.Count == 0)
        {
            _roots.Remove(group);
        }
        else
        {
            group.UpdateModifiedState();
        }
    }

    private EntityBase? HitTestEntities(Point world)
    {
        double tolerance = Math.Max(2, 6 / _viewTransform.Matrix.M11);
        // Ordine di priorità: oggetti normali, poi guaine (che coprono i fili), infine le forme mandate sotto.
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = _entities.Count - 1; i >= 0; i--)
            {
                EntityBase candidate = _entities[i];
                int rank = candidate.SendToBack ? 2 : candidate is SheathEntity ? 1 : 0;
                if (rank == pass && candidate.HitTest(world, tolerance))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private int FindGrip(EntityBase entity, Point world)
    {
        double tolerance = GripScreenSize / _viewTransform.Matrix.M11;
        IReadOnlyList<Point> grips = entity.GetGripPoints();
        for (int i = 0; i < grips.Count; i++)
        {
            if (Math.Abs(grips[i].X - world.X) <= tolerance && Math.Abs(grips[i].Y - world.Y) <= tolerance)
            {
                return i;
            }
        }
        return -1;
    }

    private void BeginSelectInteraction(Point world, Point screen)
    {
        bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        _dragMoved = false;

        EntityBase? single = PrimarySelection;
        if (single != null && !additive)
        {
            int grip = FindGrip(single, world);
            if (grip >= 0)
            {
                _dragMode = DragMode.MoveGrip;
                _dragGrip = grip;
                HostCanvas.CaptureMouse();
                return;
            }
        }

        EntityBase? hit = HitTestEntities(world);
        if (hit != null)
        {
            if (additive)
            {
                List<EntityBase> toggled = _selection.ToList();
                if (!toggled.Remove(hit))
                {
                    toggled.Add(hit);
                }
                SetSelection(toggled);
                return;
            }
            if (!_selection.Contains(hit))
            {
                SetSelection(new[] { hit });
            }
            _dragMode = DragMode.MoveEntity;
            _dragLast = Snap(world);
            HostCanvas.CaptureMouse();
            return;
        }

        if (!additive)
        {
            SetSelection(Array.Empty<EntityBase>());
        }
        _dragMode = DragMode.Marquee;
        _marqueeAdditive = additive;
        _marqueeMoved = false;
        _marqueeStartScreen = screen;
        _marqueeStartWorld = world;
        HostCanvas.CaptureMouse();
    }

    private void ContinueDrag(Point snapped)
    {
        if (_dragMode == DragMode.MoveGrip)
        {
            PrimarySelection?.MoveGrip(_dragGrip, snapped);
            _dragMoved = true;
        }
        else if (_dragMode == DragMode.MoveEntity)
        {
            Vector delta = snapped - _dragLast;
            if (delta.X != 0 || delta.Y != 0)
            {
                List<EntityBase> moving = _selection.ToList();
                HashSet<Guid> movingIds = moving.Select(item => item.Id).ToHashSet();
                foreach (WireEntity wire in moving.OfType<WireEntity>())
                {
                    wire.DetachFrom(movingIds);
                }
                foreach (EntityBase entity in moving)
                {
                    if (entity is WireEntity)
                    {
                        entity.Translate(delta);
                    }
                }
                foreach (EntityBase entity in moving)
                {
                    if (entity is not WireEntity)
                    {
                        entity.Translate(delta);
                    }
                }
                _dragLast = snapped;
                _dragMoved = true;
            }
        }
    }

    private void UpdateMarquee(Point screen)
    {
        Vector moved = screen - _marqueeStartScreen;
        if (!_marqueeMoved && Math.Abs(moved.X) < 4 && Math.Abs(moved.Y) < 4)
        {
            return;
        }
        _marqueeMoved = true;
        bool window = screen.X >= _marqueeStartScreen.X;
        Rect rect = new(_marqueeStartScreen, screen);
        Canvas.SetLeft(_marqueeRect, rect.X);
        Canvas.SetTop(_marqueeRect, rect.Y);
        _marqueeRect.Width = rect.Width;
        _marqueeRect.Height = rect.Height;
        _marqueeRect.Stroke = window ? WindowStroke : CrossingStroke;
        _marqueeRect.Fill = window ? WindowFill : CrossingFill;
        _marqueeRect.StrokeDashArray = window ? null : new DoubleCollection { 4, 2 };
        _marqueeRect.Visibility = Visibility.Visible;
    }

    private void CompleteMarquee(Point screen)
    {
        if (!_marqueeMoved)
        {
            return;
        }
        Point endWorld = ToWorld(screen);
        bool window = screen.X >= _marqueeStartScreen.X;
        Rect rect = new(_marqueeStartWorld, endWorld);
        List<EntityBase> picked = _entities.Where(entity => MarqueeHits(entity, rect, window)).ToList();
        if (_marqueeAdditive)
        {
            picked.AddRange(_selection);
        }
        SetSelection(picked);
    }

    private static bool MarqueeHits(EntityBase entity, Rect rect, bool window)
    {
        Geometry geometry = entity.BuildGeometry();
        if (window)
        {
            return rect.Contains(geometry.Bounds);
        }
        Geometry outline = geometry.GetWidenedPathGeometry(new Pen(Brushes.Black, 0.01));
        return outline.FillContainsWithDetail(new RectangleGeometry(rect)) != IntersectionDetail.Empty;
    }

    private void EndDrag()
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }
        DragMode mode = _dragMode;
        _dragMode = DragMode.None;
        _marqueeRect.Visibility = Visibility.Collapsed;
        HostCanvas.ReleaseMouseCapture();
        if (_dragMoved && mode != DragMode.Marquee)
        {
            if (mode == DragMode.MoveGrip && PrimarySelection is WireEntity wire)
            {
                FinalizeWireGrip(wire);
            }
            CommitChange();
        }
        _dragMoved = false;
    }

    private void HostCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_panning)
        {
            return;
        }
        HostCanvas.Focus();
        Point screenPoint = e.GetPosition(HostCanvas);
        Point world = ToWorld(screenPoint);
        if (_tool == ToolType.Wire && e.ClickCount >= 2)
        {
            if (_wirePoints.Count >= 2)
            {
                FinishWire();
            }
            e.Handled = true;
            return;
        }
        if (_tool == ToolType.Spline && e.ClickCount >= 2)
        {
            if (_splinePoints.Count >= 2)
            {
                FinishSpline();
            }
            e.Handled = true;
            return;
        }
        if (_tool == ToolType.Sheath && e.ClickCount >= 2)
        {
            if (_sheathPoints.Count >= 2)
            {
                FinishSheath();
            }
            e.Handled = true;
            return;
        }
        switch (_tool)
        {
            case ToolType.Select:
                BeginSelectInteraction(world, screenPoint);
                break;
            case ToolType.Sheath:
                SheathClick(world);
                break;
            case ToolType.Spline:
                SplineClick(world);
                break;
            case ToolType.PlaceImport:
                PlaceImport(world);
                break;
            case ToolType.Pin:
                PlacePoint(world, 1, points => new PinEntity { Position = points[0], Number = "1" });
                break;
            case ToolType.Circle:
                PlacePoint(world, 2, points => new CircleEntity
                {
                    Center = points[0],
                    Radius = Math.Max(0.01, (points[1] - points[0]).Length)
                });
                break;
            case ToolType.Dimension:
                PlacePoint(world, 3, points => new DimensionEntity { P1 = points[0], P2 = points[1], Offset = points[2] });
                break;
            case ToolType.Component:
                PlaceComponent(world);
                break;
            case ToolType.Wire:
                WireClick(world);
                break;
            case ToolType.Junction:
                PlaceJunction(world);
                break;
            case ToolType.Text:
                PlaceText(world);
                break;
            case ToolType.Line:
                PlacePoint(world, 2, points => new LineEntity { A = points[0], B = points[1] });
                break;
            case ToolType.Rectangle:
                PlacePoint(world, 2, points => new RectangleEntity { P1 = points[0], P2 = points[1] });
                break;
            case ToolType.Arc:
                PlacePoint(world, 3, points => new ArcEntity { Start = points[0], End = points[1], Through = points[2] });
                break;
            case ToolType.Fillet:
                FilletClick(world);
                break;
            case ToolType.AnchorPoint:
                PlacePoint(world, 1, points => new AnchorPointEntity { Position = points[0] });
                break;
            case ToolType.GridOrigin:
                _gridOrigin = Snap(world);
                UpdateGrid();
                UpdateGridBoxes();
                SetTool(ToolType.Select);
                break;
        }
        e.Handled = true;
    }

    private void HostCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning || _dragMode != DragMode.None)
        {
            return;
        }
        e.Handled = true;
        if (_tool == ToolType.GridOrigin)
        {
            SetTool(ToolType.Select);
            return;
        }
        if (_tool == ToolType.Wire && _wirePoints.Count > 0)
        {
            if (_wirePoints.Count >= 2)
            {
                FinishWire();
            }
            else
            {
                CancelWire();
            }
            return;
        }
        if (_tool == ToolType.Spline && _splinePoints.Count > 0)
        {
            if (_splinePoints.Count >= 2)
            {
                FinishSpline();
            }
            else
            {
                _splinePoints.Clear();
                _previewPath.Visibility = Visibility.Collapsed;
            }
            return;
        }
        if (_tool == ToolType.Sheath && _sheathPoints.Count > 0)
        {
            if (_sheathPoints.Count >= 2)
            {
                FinishSheath();
            }
            else
            {
                _sheathPoints.Clear();
                _previewPath.Visibility = Visibility.Collapsed;
            }
            return;
        }
        if (_tool == ToolType.Fillet && _filletFirst != null)
        {
            CancelFillet();
            SetSelection(Array.Empty<EntityBase>());
            return;
        }
        if (_tool is ToolType.Component or ToolType.Junction or ToolType.Text or ToolType.Wire or ToolType.Fillet or ToolType.Sheath or ToolType.Spline or ToolType.Circle or ToolType.PlaceImport or ToolType.Pin or ToolType.Dimension)
        {
            SetTool(ToolType.Select);
            return;
        }
        if (_tool != ToolType.Select && _currentPoints.Count > 0)
        {
            _currentPoints.Clear();
            _previewPath.Visibility = Visibility.Collapsed;
            return;
        }

        Point world = ToWorld(e.GetPosition(HostCanvas));
        EntityBase? hit = HitTestEntities(world);
        if (hit != null && !_selection.Contains(hit))
        {
            SetSelection(new[] { hit });
        }
        ShowContextMenu(hit == null, Snap(world));
    }

    private void ShowContextMenu(bool onEmptyArea, Point worldPoint)
    {
        ContextMenu menu = new();
        if (_selection.Count > 0)
        {
            MenuItem save = new()
            {
                Header = _selection.Count == 1
                    ? "Salva selezione in file..."
                    : $"Salva selezione ({_selection.Count} forme) in file..."
            };
            save.Click += (_, _) => SaveSelection();
            menu.Items.Add(save);

            if (PrimarySelection is SheathEntity selectedSheath)
            {
                ShowSheathMenuItems(menu, selectedSheath, worldPoint);
            }
            else if (PrimarySelection is SplineEntity selectedSpline)
            {
                ShowSplineMenuItems(menu, selectedSpline, worldPoint);
            }

            if (PrimarySelection is ComponentEntity { IsCustom: true, IsInline: false } customConnector)
            {
                MenuItem explode = new() { Header = "Scomponi connettore" };
                explode.Click += (_, _) => ExplodeConnector(customConnector);
                menu.Items.Add(explode);
            }
            if (_selection.Any(ComponentTemplate.IsConnectorPart))
            {
                MenuItem saveConnector = new() { Header = "Salva selezione come connettore..." };
                saveConnector.Click += (_, _) => SaveSelectionAsConnector();
                menu.Items.Add(saveConnector);
            }
            if (_selection.Count >= 2)
            {
                MenuItem group = new() { Header = $"Raggruppa selezione ({_selection.Count} oggetti)..." };
                group.Click += (_, _) => GroupSelection();
                menu.Items.Add(group);
            }
            if (_selection.Any(item => _nodes.TryGetValue(item, out EntityNode? member) && member.Parent != null))
            {
                MenuItem ungroup = new() { Header = "Separa dal gruppo" };
                ungroup.Click += (_, _) => UngroupSelection();
                menu.Items.Add(ungroup);
            }

            MenuItem copy = new() { Header = "Copia", InputGestureText = "Ctrl+C" };
            copy.Click += (_, _) => CopySelection();
            menu.Items.Add(copy);

            MenuItem rotate = new() { Header = "Ruota 90°", InputGestureText = "Ctrl+R" };
            rotate.Click += (_, _) => TransformSelection(TransformKind.Rotate90);
            menu.Items.Add(rotate);

            MenuItem mirror = new() { Header = "Specchia orizzontalmente", InputGestureText = "Ctrl+M" };
            mirror.Click += (_, _) => TransformSelection(TransformKind.MirrorHorizontal);
            menu.Items.Add(mirror);

            MenuItem delete = new() { Header = "Elimina", InputGestureText = "Canc" };
            delete.Click += (_, _) => DeleteSelected();
            menu.Items.Add(delete);
        }
        if (onEmptyArea)
        {
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }
            if (_clipboard != null)
            {
                MenuItem paste = new() { Header = "Incolla", InputGestureText = "Ctrl+V" };
                paste.Click += (_, _) => PasteClipboard();
                menu.Items.Add(paste);
            }
            MenuItem import = new() { Header = "Importa forme da file..." };
            import.Click += (_, _) => ImportShapes(worldPoint);
            menu.Items.Add(import);
        }
        if (menu.Items.Count == 0)
        {
            return;
        }
        menu.PlacementTarget = HostCanvas;
        menu.IsOpen = true;
    }

    private void SaveSelection()
    {
        List<EntityBase> toSave = _entities.Where(_selection.Contains).ToList();
        if (toSave.Count == 0)
        {
            return;
        }
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Salva le forme selezionate",
            Filter = ShapeFile.Filter,
            DefaultExt = ShapeFile.Extension,
            AddExtension = true,
            FileName = "forme"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        TrySaveShapes(dialog.FileName, toSave);
    }

    private bool TrySaveShapes(string path, IReadOnlyCollection<EntityBase> entities)
    {
        try
        {
            ShapeFile.Save(path, entities);
            StatusHint.Text = $"Salvate {entities.Count} forme in {path}";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare il file:\n{ex.Message}", "Salvataggio",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static List<EntityBase> GroupEntities(EntityNode group)
    {
        return group.Children.Select(child => child.Entity!).ToList();
    }

    private void ShowGroupMenu(EntityNode group)
    {
        ContextMenu menu = new() { Placement = PlacementMode.MousePoint };

        MenuItem toggle = new() { Header = group.IsExpanded ? "Comprimi" : "Espandi" };
        toggle.Click += (_, _) => group.IsExpanded = !group.IsExpanded;
        menu.Items.Add(toggle);

        MenuItem rename = new() { Header = "Rinomina gruppo..." };
        rename.Click += (_, _) => RenameGroup(group);
        menu.Items.Add(rename);

        MenuItem dissolve = new() { Header = "Separa gruppo (sciogli)" };
        dissolve.Click += (_, _) => DissolveGroup(group);
        menu.Items.Add(dissolve);

        menu.Items.Add(new Separator());

        if (!string.IsNullOrEmpty(group.FilePath))
        {
            MenuItem save = new() { Header = "Salva file" };
            save.Click += (_, _) => SaveGroup(group);
            menu.Items.Add(save);
        }

        MenuItem saveCopy = new() { Header = string.IsNullOrEmpty(group.FilePath) ? "Salva in file..." : "Salva una copia..." };
        saveCopy.Click += (_, _) => SaveGroupCopy(group);
        menu.Items.Add(saveCopy);

        menu.Items.Add(new Separator());

        MenuItem remove = new() { Header = "Elimina dal foglio" };
        remove.Click += (_, _) => RemoveGroupFromSheet(group);
        menu.Items.Add(remove);

        menu.IsOpen = true;
    }

    private void SaveGroup(EntityNode group)
    {
        if (TrySaveShapes(group.FilePath, GroupEntities(group)))
        {
            group.CaptureBaseline();
        }
    }

    private void SaveGroupCopy(EntityNode group)
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Salva una copia del file",
            Filter = ShapeFile.Filter,
            DefaultExt = ShapeFile.Extension,
            AddExtension = true,
            InitialDirectory = System.IO.Path.GetDirectoryName(group.FilePath) ?? "",
            FileName = $"{System.IO.Path.GetFileNameWithoutExtension(group.FilePath)} - copia"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        TrySaveShapes(dialog.FileName, GroupEntities(group));
    }

    private void RemoveGroupFromSheet(EntityNode group)
    {
        if (group.IsModified)
        {
            MessageBoxResult answer = MessageBox.Show(this,
                $"\"{group.Name}\" contiene modifiche non salvate.\nEliminarlo dal foglio comunque?",
                "Elimina dal foglio", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }
        DeleteEntities(GroupEntities(group));
        _roots.Remove(group);
    }

    private void ImportShapes(Point target)
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Importa forme da file",
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
            MessageBox.Show(this, $"Impossibile importare il file:\n{ex.Message}", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (result.Entities.Count == 0)
        {
            MessageBox.Show(this, "Il file non contiene forme valide.", "Importazione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Rect bounds = Rect.Empty;
        foreach (EntityBase entity in result.Entities)
        {
            bounds.Union(entity.BuildGeometry().Bounds);
        }
        Vector offset = target - bounds.TopLeft;
        EntityNode group = new(System.IO.Path.GetFileName(dialog.FileName))
        {
            IsExpanded = true,
            FilePath = dialog.FileName
        };
        _roots.Add(group);
        foreach (EntityBase entity in result.Entities)
        {
            entity.Translate(offset);
            AddEntity(entity, group);
        }
        group.CaptureBaseline();
        SetSelection(result.Entities);
        StatusHint.Text = result.Skipped > 0
            ? $"Importate {result.Entities.Count} forme ({result.Skipped} non valide, ignorate)."
            : $"Importate {result.Entities.Count} forme.";
        SyncAllWires();
        CommitChange();
    }

    private void HostCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point screen = e.GetPosition(HostCanvas);
        Point world = ToWorld(screen);
        StatusCoords.Text = $"X: {world.X:0.0}  Y: {world.Y:0.0} mm";
        if (_panning)
        {
            Matrix matrix = _viewTransform.Matrix;
            matrix.OffsetX += screen.X - _panStart.X;
            matrix.OffsetY += screen.Y - _panStart.Y;
            _viewTransform.Matrix = matrix;
            _panStart = screen;
            return;
        }
        if (_dragMode != DragMode.None)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
                return;
            }
            if (_dragMode == DragMode.Marquee)
            {
                UpdateMarquee(screen);
            }
            else
            {
                ContinueDrag(_dragMode == DragMode.MoveGrip
                    ? SmartSnap(world, false, PrimarySelection).Point
                    : Snap(world));
            }
            return;
        }
        _lastMouseWorld = world;
        SnapHit hit = _tool == ToolType.Select
            ? new SnapHit(world, null, -1)
            : SnapWithAngleLock(world, _tool == ToolType.Junction);
        UpdateSnapMarker(hit);
        UpdatePreview(hit);
    }

    private void HostCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double scale = Math.Clamp(
            _viewTransform.Matrix.M11 * (e.Delta > 0 ? 1.2 : 1 / 1.2),
            MinZoom, MaxZoom);
        Point screen = e.GetPosition(HostCanvas);
        Point world = ToWorld(screen);
        _viewTransform.Matrix = new Matrix(scale, 0, 0, scale,
            screen.X - world.X * scale,
            screen.Y - world.Y * scale);
        StatusZoom.Text = $"{scale * 100:0}%";
        UpdateGridVisibility();
        UpdateGrips();
        e.Handled = true;
    }

    private void HostCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _dragMode != DragMode.None)
        {
            return;
        }
        _panning = true;
        _panStart = e.GetPosition(HostCanvas);
        HostCanvas.CaptureMouse();
        HostCanvas.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void HostCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && _dragMode != DragMode.None)
        {
            if (_dragMode == DragMode.Marquee)
            {
                CompleteMarquee(e.GetPosition(HostCanvas));
            }
            EndDrag();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton != MouseButton.Middle || !_panning)
        {
            return;
        }
        _panning = false;
        HostCanvas.ReleaseMouseCapture();
        SetTool(_tool);
        e.Handled = true;
    }

    private EntityNode? FindNode(DependencyObject? source, out bool onToggle)
    {
        onToggle = false;
        DependencyObject? current = source;
        while (current != null && !ReferenceEquals(current, EntityTree))
        {
            if (current is ToggleButton or ScrollBar)
            {
                onToggle = true;
                return null;
            }
            if (current is FrameworkElement element && element.DataContext is EntityNode found)
            {
                return found;
            }
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }

    private void EntityTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EntityNode? node = FindNode(e.OriginalSource as DependencyObject, out bool onToggle);
        if (onToggle)
        {
            return;
        }

        bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        EntityTree.Focus();
        e.Handled = true;
        _pendingGroupMenu = null;
        BeginTreeSelection();
        if (node == null)
        {
            if (!additive)
            {
                SetSelection(Array.Empty<EntityBase>());
            }
            return;
        }

        // doppio clic sul nome di un gruppo: espande o comprime, come in un normale albero
        if (e.ClickCount == 2 && node.IsGroup && node.Children.Count > 0)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        List<EntityBase> targets = node.IsGroup
            ? GroupEntities(node)
            : new List<EntityBase> { node.Entity! };
        if (!additive)
        {
            SetSelection(targets);
            return;
        }
        List<EntityBase> combined = _selection.ToList();
        if (targets.Count > 0 && targets.All(combined.Contains))
        {
            combined.RemoveAll(targets.Contains);
        }
        else
        {
            combined.AddRange(targets);
        }
        SetSelection(combined);
    }

    private void EntityTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EntityNode? group = _pendingGroupMenu;
        _pendingGroupMenu = null;
        if (group == null)
        {
            return;
        }
        e.Handled = true;
        ShowGroupMenu(group);
    }

    private void EntityTree_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        EntityNode? node = FindNode(e.OriginalSource as DependencyObject, out bool onToggle);
        if (onToggle || node == null)
        {
            return;
        }
        e.Handled = true;
        BeginTreeSelection();
        if (node.IsGroup)
        {
            SetSelection(GroupEntities(node));
            ShowGroupMenu(node);
        }
        else
        {
            if (!_selection.Contains(node.Entity!))
            {
                SetSelection(new[] { node.Entity! });
            }
            ShowEntityTreeMenu(node);
        }
    }

    private void FieldTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
        {
            return;
        }
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        textBox.SelectAll();
        CommitChange();
        e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool inTextBox = Keyboard.FocusedElement is TextBox;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (HandleCtrlKey(e.Key, inTextBox))
            {
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
            return;
        }
        if (inTextBox)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                // Con un'operazione in corso Esc la annulla; altrimenti torna al selettore.
                bool creating = _currentPoints.Count > 0
                    || _wirePoints.Count > 0
                    || _sheathPoints.Count > 0
                    || _splinePoints.Count > 0
                    || _filletFirst != null
                    || _dragMode != DragMode.None;
                if (!creating)
                {
                    SetTool(ToolType.Select);
                }
                EndDrag();
                CancelWire();
                CancelFillet();
                CancelSheath();
                CancelSpline();
                _currentPoints.Clear();
                _previewPath.Visibility = Visibility.Collapsed;
                SetSelection(Array.Empty<EntityBase>());
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.S:
                SetTool(ToolType.Select);
                e.Handled = true;
                break;
            case Key.L:
                SetTool(ToolType.Line);
                e.Handled = true;
                break;
            case Key.R:
                SetTool(ToolType.Rectangle);
                e.Handled = true;
                break;
            case Key.A:
                SetTool(CurrentArcTool);
                e.Handled = true;
                break;
            case Key.P:
                SetTool(ToolType.AnchorPoint);
                e.Handled = true;
                break;
            case Key.C:
                if (_template == null && ComponentTemplate.All.Count > 0)
                {
                    LibraryList.SelectedIndex = 0;
                }
                SetTool(ToolType.Component);
                e.Handled = true;
                break;
            case Key.W:
                SetTool(ToolType.Wire);
                e.Handled = true;
                break;
            case Key.J:
                SetTool(ToolType.Junction);
                e.Handled = true;
                break;
            case Key.T:
                SetTool(ToolType.Text);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_tool == ToolType.Wire && _wirePoints.Count >= 2)
                {
                    FinishWire();
                    e.Handled = true;
                }
                else if (_tool == ToolType.Sheath && _sheathPoints.Count >= 2)
                {
                    FinishSheath();
                    e.Handled = true;
                }
                else if (_tool == ToolType.Spline && _splinePoints.Count >= 2)
                {
                    FinishSpline();
                    e.Handled = true;
                }
                break;
            case Key.O:
                SetTool(ToolType.Circle);
                e.Handled = true;
                break;
            case Key.N:
                SetTool(ToolType.Pin);
                e.Handled = true;
                break;
            case Key.D:
                SetTool(ToolType.Dimension);
                e.Handled = true;
                break;
            case Key.B:
                SetTool(ToolType.Spline);
                e.Handled = true;
                break;
            case Key.G:
                SetTool(ToolType.Sheath);
                e.Handled = true;
                break;
            case Key.Space:
                // Spazio tenuto premuto = vincolo a 0°/45°/90°; non deve attivare i pulsanti della barra.
                if (_tool is ToolType.Line or ToolType.Arc or ToolType.Wire or ToolType.Sheath)
                {
                    e.Handled = true;
                    RefreshPreviewAtMouse();
                }
                break;
            case Key.X:
                if (_tool == ToolType.Wire)
                {
                    _wireFlip = !_wireFlip;
                    e.Handled = true;
                }
                break;
            case Key.F:
                FitView();
                e.Handled = true;
                break;
        }
        base.OnPreviewKeyDown(e);
    }

    private void BtnSelect_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Select);

    private void BtnLine_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Line);

    private void BtnRectangle_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Rectangle);

    private void BtnAnchor_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.AnchorPoint);

    private void BtnComponent_Click(object sender, RoutedEventArgs e)
    {
        if (_template == null && ComponentTemplate.All.Count > 0)
        {
            LibraryList.SelectedIndex = 0;
        }
        SetTool(ToolType.Component);
    }

    private void BtnJunction_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Junction);

    private void BtnText_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Text);

    private void BtnDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    private void BtnFit_Click(object sender, RoutedEventArgs e) => FitView();
}
