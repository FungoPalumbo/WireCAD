using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Pin di un componente. Side: 0 = sinistra, 1 = destra, 2 = alto, 3 = basso.</summary>
public class PinDef
{
    public int Side { get; set; }

    /// <summary>Posizione lungo il lato (0..1): dall'alto per i lati verticali, da sinistra per quelli orizzontali.</summary>
    public double Fraction { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Solo per Side = 4 (cavità interna al corpo): posizione verticale (0..1) dall'alto; Fraction è quella orizzontale.</summary>
    public double FractionY { get; set; }
}

/// <summary>Componente elettrico (connettore, relè, fusibile...) con corpo rettangolare e pin.</summary>
public class ComponentEntity : EntityBase
{
    /// <summary>Lunghezza del tratto di pin fuori dal corpo (mm).</summary>
    public const double Stub = 5;

    private Point _p1;
    private Point _p2;
    private readonly List<PinDef> _pins = new();
    private string _tag = "";
    private string _model = "";
    private string _designation = "";
    private string _partNumber = "";
    private string _manufacturer = "";
    private string _rating = "";
    private bool _bridged;
    private bool _physical;
    private double _cavityDiameter = 2.5;
    private int _keyCorner;
    private double _realWidth;
    private double _realHeight;
    private double _drawScale = 1;

    /// <summary>Ingombro reale del connettore (mm), dato di progetto indipendente dalla scala di disegno.</summary>
    public double RealWidth
    {
        get => _realWidth;
        set => _realWidth = Math.Max(0, value);
    }

    public double RealHeight
    {
        get => _realHeight;
        set => _realHeight = Math.Max(0, value);
    }

    /// <summary>
    /// Ingrandimento con cui il connettore fisico è disegnato (1 = dimensioni reali). Corpo e cavità si ingrandiscono,
    /// i dati reali restano invariati.
    /// </summary>
    public double DrawScale
    {
        get => _drawScale;
        set
        {
            double previous = _drawScale;
            _drawScale = Math.Clamp(value, 0.1, 50);
            if (_custom)
            {
                double ratio = _drawScale / previous;
                Point origin = _placement.Transform(new Point(0, 0));
                Matrix scaling = Matrix.Identity;
                scaling.ScaleAt(ratio, ratio, origin.X, origin.Y);
                _placement = _placement * scaling;
                UpdateCustomBody();
            }
            else if (_physical && _realWidth > 0 && _realHeight > 0)
            {
                Rect rect = Body;
                _p1 = rect.TopLeft;
                _p2 = new Point(rect.Left + _realWidth * _drawScale, rect.Top + _realHeight * _drawScale);
            }
            NotifyChanged();
        }
    }

    // ---------------------------------------------------------------- connettore personalizzato

    private bool _custom;
    private List<EntityDto>? _parts;
    private double _partsWidth;
    private double _partsHeight;
    private Matrix _placement = Matrix.Identity;
    private Geometry? _customGeometry;
    private Matrix _geometryPlacement = Matrix.Identity;
    private List<(string Text, Point Position, double Height)> _customTexts = new();

    private Rect _localBounds;
    private List<(Geometry Geometry, string FillHex)> _coloredParts = new();

    // componente in linea (resistenza, diodo, fusibile, giunzione saldata)
    private string? _inline;
    private double _bodyLength;
    private double _bodyDiameter;
    private double _leadLength;
    private double _drawnLead;
    private string _power = "";
    private string _tolerance = "";

    /// <summary>Vero se il connettore ha un disegno proprio (definito da un file di forme) o è un componente in linea.</summary>
    public bool IsCustom => _custom;

    /// <summary>Vero per i componenti da inserire in linea su un filo.</summary>
    public bool IsInline => _inline != null;

    /// <summary>Lunghezza reale di ciascun reoforo (mm): si somma alla lunghezza di taglio dei fili collegati.</summary>
    public double LeadLength
    {
        get => _leadLength;
        set
        {
            _leadLength = Math.Clamp(value, 0, 500);
            NotifyChanged();
        }
    }

    /// <summary>Lunghezza con cui i reofori sono disegnati (mm), indipendente da quella reale.</summary>
    public double DrawnLead
    {
        get => _drawnLead;
        set
        {
            _drawnLead = Math.Clamp(value, 0, 200);
            if (_inline != null)
            {
                RebuildInline();
            }
            NotifyChanged();
        }
    }

    public string Power
    {
        get => _power;
        set => SetField(ref _power, value);
    }

    public string Tolerance
    {
        get => _tolerance;
        set => SetField(ref _tolerance, value);
    }

    /// <summary>Valore, potenza e tolleranza in un'unica stringa (per le distinte).</summary>
    public string RatingSummary =>
        string.Join(" ", new[] { _rating, _power, _tolerance }.Where(item => !string.IsNullOrWhiteSpace(item)));

    /// <summary>
    /// Definisce un componente in linea: asse lungo +X locale, pin 1 nell'origine e pin 2 a fine reoforo.
    /// <paramref name="placement"/> porta le coordinate locali sul foglio.
    /// </summary>
    public void SetInline(InlineSpec spec, Matrix placement)
    {
        _inline = spec.Kind;
        _custom = true;
        _parts = null;
        _bodyLength = spec.BodyLength;
        _bodyDiameter = spec.BodyDiameter;
        _leadLength = spec.LeadLength;
        _drawnLead = spec.DrawnLeadLength;
        _bridged = spec.Bridged;
        _model = spec.Name;
        _cavityDiameter = 1.6;
        _drawScale = 1;
        if (string.IsNullOrEmpty(_rating))
        {
            _rating = spec.DefaultRating;
        }
        if (spec.Kind == "resistor")
        {
            if (string.IsNullOrEmpty(_power))
            {
                _power = "0.25 W";
            }
            if (string.IsNullOrEmpty(_tolerance))
            {
                _tolerance = "5 %";
            }
        }
        _placement = placement;
        _pins.Clear();
        _pins.Add(new PinDef { Side = 6, Name = spec.Pin1 });
        _pins.Add(new PinDef { Side = 6, Name = spec.Pin2 });
        RebuildInline();
        NotifyChanged();
    }

    private void RebuildInline()
    {
        double length = TotalLength;
        _localBounds = new Rect(0, -_bodyDiameter / 2, length, _bodyDiameter);
        if (_pins.Count == 2)
        {
            _pins[0].Fraction = 0;
            _pins[0].FractionY = 0;
            _pins[1].Fraction = length;
            _pins[1].FractionY = 0;
        }
        UpdateCustomBody();
    }

    public double TotalLength => _bodyLength + 2 * _drawnLead;

    private const string DiodeCathode = "K";
    private const string ForwardText = "Anodo → Catodo";
    private const string ReverseText = "Catodo → Anodo";

    /// <summary>Vero se nel diodo il catodo (striscia) è dal lato del primo pin, cioè il pin 1 si chiama K.</summary>
    private bool CathodeAtStart => _inline == "diode" && _pins.Count == 2 && _pins[0].Name == DiodeCathode;

    /// <summary>Scambia anodo e catodo: i nomi dei pin si invertono e la striscia passa dall'altro lato.</summary>
    public void ReverseDiode()
    {
        if (_inline != "diode" || _pins.Count != 2)
        {
            return;
        }
        (_pins[0].Name, _pins[1].Name) = (_pins[1].Name, _pins[0].Name);
        RebuildInline();
        NotifyChanged();
    }

    /// <summary>Forme del componente in coordinate locali: punti, se chiusa e colore di riempimento (null = solo contorno).</summary>
    private List<(List<Point> Points, bool Closed, string? Fill, bool Outline)> InlineShapes()
    {
        List<(List<Point> Points, bool Closed, string? Fill, bool Outline)> shapes = new();
        double length = TotalLength;
        double half = _bodyDiameter / 2;
        double x0 = _drawnLead;
        double x1 = length - _drawnLead;
        double bodyLength = x1 - x0;

        static List<Point> Box(double xa, double ya, double xb, double yb)
        {
            return new List<Point> { new Point(xa, ya), new Point(xb, ya), new Point(xb, yb), new Point(xa, yb) };
        }

        if (_drawnLead > 0)
        {
            shapes.Add((new List<Point> { new Point(0, 0), new Point(x0, 0) }, false, null, true));
            shapes.Add((new List<Point> { new Point(x1, 0), new Point(length, 0) }, false, null, true));
        }
        string bodyFill = _inline switch
        {
            "resistor" => "#E8D9B5",
            "diode" => "#1F2937",
            "fuse" => "#E0F2FE",
            _ => "#9CA3AF"
        };
        shapes.Add((Box(x0, -half, x1, half), true, bodyFill, true));

        switch (_inline)
        {
            case "resistor":
                IReadOnlyList<string>? bands = ResistorCode.Bands(_rating, _tolerance);
                if (bands != null)
                {
                    double[] positions = { 0.14, 0.28, 0.42, 0.78 };
                    double width = bodyLength * 0.09;
                    for (int i = 0; i < 4; i++)
                    {
                        double center = x0 + bodyLength * positions[i];
                        // le bande colorate non hanno bordo: solo il colore
                        shapes.Add((Box(center - width / 2, -half, center + width / 2, half), true, bands[i], false));
                    }
                }
                break;
            case "diode":
                shapes.Add(CathodeAtStart
                    ? (Box(x0 + bodyLength * 0.1, -half, x0 + bodyLength * 0.24, half), true, "#D1D5DB", false)
                    : (Box(x1 - bodyLength * 0.24, -half, x1 - bodyLength * 0.1, half), true, "#D1D5DB", false));
                break;
            case "fuse":
                shapes.Add((new List<Point> { new Point(x0 + 1, 0), new Point(x1 - 1, 0) }, false, null, true));
                break;
        }
        return shapes;
    }

    private Geometry BuildInlineGeometry()
    {
        GeometryGroup outline = new();
        List<(Geometry Geometry, string FillHex)> colored = new();
        foreach (var shape in InlineShapes())
        {
            List<Point> world = shape.Points.Select(point => _placement.Transform(point)).ToList();
            if (shape.Outline)
            {
                PathFigure line = new() { StartPoint = world[0], IsClosed = shape.Closed, IsFilled = false };
                line.Segments.Add(new PolyLineSegment(world.Skip(1).ToList(), true));
                PathGeometry lineGeometry = new();
                lineGeometry.Figures.Add(line);
                lineGeometry.Freeze();
                outline.Children.Add(lineGeometry);
            }

            if (shape.Fill != null)
            {
                PathFigure area = new() { StartPoint = world[0], IsClosed = true, IsFilled = true };
                area.Segments.Add(new PolyLineSegment(world.Skip(1).ToList(), true));
                PathGeometry areaGeometry = new();
                areaGeometry.Figures.Add(area);
                areaGeometry.Freeze();
                colored.Add((areaGeometry, shape.Fill));
            }
        }
        double markerRadius = _cavityDiameter * _drawScale / 2;
        foreach (PinDef pin in _pins)
        {
            outline.Children.Add(new EllipseGeometry(WorldOf(pin), markerRadius, markerRadius));
        }
        outline.Freeze();
        _coloredParts = colored;
        return outline;
    }

    /// <summary>Parti con riempimento colorato (corpo, bande...), già in coordinate del foglio.</summary>
    public IReadOnlyList<(Geometry Geometry, string FillHex)> GetColoredParts()
    {
        if (!_custom)
        {
            return Array.Empty<(Geometry Geometry, string FillHex)>();
        }
        _ = BuildCustomGeometry();
        return _coloredParts;
    }

    /// <summary>
    /// Definisce il connettore da un disegno canonico (millimetri reali, origine in alto a sinistra), con i suoi pin.
    /// <paramref name="origin"/> è la posizione sul foglio dell'angolo in alto a sinistra.
    /// </summary>
    internal void SetCustom(List<EntityDto> parts, double width, double height, IEnumerable<PinDef> pins, Point origin)
    {
        _custom = true;
        _parts = parts;
        _partsWidth = width;
        _partsHeight = height;
        _drawScale = 1;
        _placement = new Matrix(1, 0, 0, 1, origin.X, origin.Y);
        _customGeometry = null;
        _pins.Clear();
        _pins.AddRange(pins);
        RebuildCustomCache();
        UpdateCustomBody();
        NotifyChanged();
    }

    private void UpdateCustomBody()
    {
        Rect local = _inline != null ? _localBounds : new Rect(0, 0, _partsWidth, _partsHeight);
        Point[] corners =
        {
            _placement.Transform(local.TopLeft),
            _placement.Transform(local.TopRight),
            _placement.Transform(local.BottomRight),
            _placement.Transform(local.BottomLeft)
        };
        _p1 = new Point(corners.Min(point => point.X), corners.Min(point => point.Y));
        _p2 = new Point(corners.Max(point => point.X), corners.Max(point => point.Y));
        _customGeometry = null;
    }

    /// <summary>Testi del disegno incorporato (mostrati come etichette).</summary>
    private void RebuildCustomCache()
    {
        _customTexts = new List<(string Text, Point Position, double Height)>();
        if (_parts == null)
        {
            return;
        }
        foreach (var item in EntitySerializer.LoadAll(_parts, true, out _))
        {
            if (item.Entity is TextEntity text)
            {
                _customTexts.Add((text.Text, text.Position, text.TextHeight));
            }
        }
    }

    private Point WorldOf(PinDef pin)
    {
        Point world = _placement.Transform(new Point(pin.Fraction, pin.FractionY));
        return new Point(Math.Round(world.X, 4), Math.Round(world.Y, 4));
    }

    private static Matrix TransformMatrix(TransformKind kind, Point center)
    {
        switch (kind)
        {
            case TransformKind.Rotate90:
                return new Matrix(0, 1, -1, 0, center.X + center.Y, center.Y - center.X);
            case TransformKind.MirrorHorizontal:
                return new Matrix(-1, 0, 0, 1, 2 * center.X, 0);
            default:
                return new Matrix(1, 0, 0, -1, 0, 2 * center.Y);
        }
    }

    private Geometry BuildCustomGeometry()
    {
        if (_inline != null)
        {
            return BuildInlineGeometry();
        }
        if (_customGeometry != null && _geometryPlacement == _placement)
        {
            return _customGeometry;
        }
        GeometryGroup group = new();
        List<(Geometry Geometry, string FillHex)> colored = new();
        if (_parts != null)
        {
            foreach (var item in EntitySerializer.LoadAll(_parts, true, out _))
            {
                EntityBase part = item.Entity;
                if (part is TextEntity || part is PinEntity)
                {
                    continue;
                }
                part.ApplyMatrix(_placement);
                Geometry partGeometry = part.BuildGeometry();
                group.Children.Add(partGeometry);
                if (part.FillHex != null)
                {
                    colored.Add((partGeometry, part.FillHex));
                }
            }
        }
        _coloredParts = colored;
        // i pin hanno l'aspetto delle cavità dei connettori predefiniti: cerchio con il numero al centro
        double markerRadius = _cavityDiameter * _drawScale / 2;
        foreach (PinDef pin in _pins)
        {
            group.Children.Add(new EllipseGeometry(WorldOf(pin), markerRadius, markerRadius));
        }
        group.Freeze();
        _customGeometry = group;
        _geometryPlacement = _placement;
        return group;
    }

    /// <summary>
    /// Scompone il connettore nelle sue forme (e nei pin numerati) in coordinate del foglio,
    /// come oggetti indipendenti modificabili.
    /// </summary>
    public List<EntityBase> Explode()
    {
        List<EntityBase> pieces = new();
        if (_inline != null)
        {
            return pieces;
        }
        if (_parts != null)
        {
            foreach (var item in EntitySerializer.LoadAll(_parts, false, out _))
            {
                item.Entity.ApplyMatrix(_placement);
                pieces.Add(item.Entity);
            }
        }
        foreach (PinDef pin in _pins)
        {
            pieces.Add(new PinEntity
            {
                Position = WorldOf(pin),
                Number = pin.Name,
                Diameter = _cavityDiameter * _drawScale
            });
        }
        return pieces;
    }

    public override bool HitTest(Point point, double tolerance)
    {
        if (_custom)
        {
            Rect area = Body;
            area.Inflate(tolerance, tolerance);
            return area.Contains(point);
        }
        return base.HitTest(point, tolerance);
    }

    /// <summary>Vista fisica del connettore (faccia con cavità) invece del simbolo elettrico.</summary>
    public bool Physical
    {
        get => _physical;
        set
        {
            _physical = value;
            NotifyChanged();
        }
    }

    public double CavityDiameter
    {
        get => _cavityDiameter;
        set
        {
            _cavityDiameter = Math.Max(0.2, value);
            NotifyChanged();
        }
    }

    /// <summary>Angolo smussato di polarizzazione: 0 alto-sinistra, 1 alto-destra, 2 basso-destra, 3 basso-sinistra, -1 nessuno.</summary>
    public int KeyCorner
    {
        get => _keyCorner;
        set
        {
            _keyCorner = value;
            NotifyChanged();
        }
    }

    public Point P1
    {
        get => _p1;
        set
        {
            _p1 = value;
            NotifyChanged();
        }
    }

    public Point P2
    {
        get => _p2;
        set
        {
            _p2 = value;
            NotifyChanged();
        }
    }

    public Rect Body => new(_p1, _p2);

    public IReadOnlyList<PinDef> Pins => _pins;

    public string Tag
    {
        get => _tag;
        set
        {
            string next = value ?? string.Empty;
            if (_tag == next)
            {
                return;
            }
            string previous = _tag;
            _tag = next;
            if (string.IsNullOrEmpty(Name) || Name == previous)
            {
                Name = next;
            }
            NotifyChanged();
        }
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value);
    }

    public string Designation
    {
        get => _designation;
        set => SetField(ref _designation, value);
    }

    public string PartNumber
    {
        get => _partNumber;
        set => SetField(ref _partNumber, value);
    }

    public string Manufacturer
    {
        get => _manufacturer;
        set => SetField(ref _manufacturer, value);
    }

    public string Rating
    {
        get => _rating;
        set => SetField(ref _rating, value);
    }

    /// <summary>Se vero tutti i pin sono internamente collegati (es. morsetto passante).</summary>
    public bool Bridged
    {
        get => _bridged;
        set
        {
            _bridged = value;
            NotifyChanged();
        }
    }

    public override string TypeName => "Componente";

    public override string TypeKey => "component";

    public override string Summary => string.IsNullOrEmpty(_model) ? _tag : $"{_tag} – {_model}";

    public override int PinCount => _pins.Count;

    public void SetPins(IEnumerable<PinDef> pins)
    {
        _pins.Clear();
        _pins.AddRange(pins);
        NotifyChanged();
    }

    private static Vector SideDirection(int side)
    {
        switch (side)
        {
            case 0:
                return new Vector(-1, 0);
            case 1:
                return new Vector(1, 0);
            case 2:
                return new Vector(0, -1);
            default:
                return new Vector(0, 1);
        }
    }

    private static Point EdgePoint(PinDef pin, Rect rect)
    {
        double x;
        double y;
        switch (pin.Side)
        {
            case 0:
                x = rect.Left;
                y = rect.Top + pin.Fraction * rect.Height;
                break;
            case 1:
                x = rect.Right;
                y = rect.Top + pin.Fraction * rect.Height;
                break;
            case 2:
                x = rect.Left + pin.Fraction * rect.Width;
                y = rect.Top;
                break;
            case 4:
                x = rect.Left + pin.Fraction * rect.Width;
                y = rect.Top + pin.FractionY * rect.Height;
                break;
            default:
                x = rect.Left + pin.Fraction * rect.Width;
                y = rect.Bottom;
                break;
        }
        return new Point(Math.Round(x, 4), Math.Round(y, 4));
    }

    public override Point GetPinPosition(int index)
    {
        if (index < 0 || index >= _pins.Count)
        {
            return new Point();
        }
        PinDef pin = _pins[index];
        if (pin.Side == 6)
        {
            return WorldOf(pin);
        }
        Point edge = EdgePoint(pin, Body);
        if (pin.Side == 4)
        {
            return edge;
        }
        Vector direction = SideDirection(pin.Side);
        return new Point(
            Math.Round(edge.X + direction.X * Stub, 4),
            Math.Round(edge.Y + direction.Y * Stub, 4));
    }

    public override string GetPinLabel(int index)
    {
        return index >= 0 && index < _pins.Count ? _pins[index].Name : string.Empty;
    }

    public override Vector GetPinDirection(int index)
    {
        if (index < 0 || index >= _pins.Count || _pins[index].Side == 4 || _pins[index].Side == 6)
        {
            return new Vector(0, 0);
        }
        return SideDirection(_pins[index].Side);
    }

    private Geometry BuildPhysicalGeometry()
    {
        Rect rect = Body;
        double chamfer = Math.Min(2.5 * _drawScale, Math.Min(rect.Width, rect.Height) / 4);
        Point tl = rect.TopLeft;
        Point tr = rect.TopRight;
        Point br = rect.BottomRight;
        Point bl = rect.BottomLeft;
        List<Point> outline = new();
        outline.Add(_keyCorner == 0 ? new Point(tl.X + chamfer, tl.Y) : tl);
        outline.Add(_keyCorner == 1 ? new Point(tr.X - chamfer, tr.Y) : tr);
        if (_keyCorner == 1)
        {
            outline.Add(new Point(tr.X, tr.Y + chamfer));
        }
        outline.Add(_keyCorner == 2 ? new Point(br.X, br.Y - chamfer) : br);
        if (_keyCorner == 2)
        {
            outline.Add(new Point(br.X - chamfer, br.Y));
        }
        outline.Add(_keyCorner == 3 ? new Point(bl.X + chamfer, bl.Y) : bl);
        if (_keyCorner == 3)
        {
            outline.Add(new Point(bl.X, bl.Y - chamfer));
        }
        if (_keyCorner == 0)
        {
            outline.Add(new Point(tl.X, tl.Y + chamfer));
        }
        PathFigure figure = new() { StartPoint = outline[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(outline.Skip(1).ToList(), true));
        PathGeometry body = new();
        body.Figures.Add(figure);

        GeometryGroup group = new();
        group.Children.Add(body);
        double radius = _cavityDiameter * _drawScale / 2;
        foreach (PinDef pin in _pins)
        {
            group.Children.Add(new EllipseGeometry(EdgePoint(pin, rect), radius, radius));
        }
        group.Freeze();
        return group;
    }

    public override Geometry BuildGeometry()
    {
        if (_custom)
        {
            return BuildCustomGeometry();
        }
        if (_physical)
        {
            return BuildPhysicalGeometry();
        }
        Rect rect = Body;
        GeometryGroup group = new();
        group.Children.Add(new RectangleGeometry(rect));
        foreach (PinDef pin in _pins)
        {
            Point edge = EdgePoint(pin, rect);
            Vector direction = SideDirection(pin.Side);
            Point tip = new(edge.X + direction.X * Stub, edge.Y + direction.Y * Stub);
            group.Children.Add(new LineGeometry(edge, tip));
            group.Children.Add(new EllipseGeometry(tip, 0.7, 0.7));
        }
        group.Freeze();
        return group;
    }

    public override IReadOnlyList<EntityLabel> GetLabels()
    {
        List<EntityLabel> labels = new();
        Rect rect = Body;
        if (ShowLabel && !string.IsNullOrWhiteSpace(_tag))
        {
            labels.Add(new EntityLabel(_tag, new Point(rect.Left, rect.Top - 1), 3, 0, 1));
        }
        string description = _inline != null
            ? string.Join("  ", new[] { _designation, RatingSummary }.Where(item => !string.IsNullOrWhiteSpace(item)))
            : _designation;
        if (ShowLabel && !string.IsNullOrWhiteSpace(description))
        {
            if (_physical || _custom)
            {
                labels.Add(new EntityLabel(description, new Point(rect.Left + rect.Width / 2, rect.Bottom + 1), 2, 0.5, 0));
            }
            else
            {
                labels.Add(new EntityLabel(description, new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2), 2, 0.5, 0.5));
            }
        }
        if (_custom)
        {
            double textScale = UniformScale(_placement);
            foreach (var text in _customTexts)
            {
                Point at = _placement.Transform(text.Position);
                labels.Add(new EntityLabel(text.Text, at, Math.Max(0.8, text.Height * textScale), 0, 0));
            }
        }
        foreach (PinDef pin in _pins)
        {
            if (string.IsNullOrWhiteSpace(pin.Name))
            {
                continue;
            }
            if (pin.Side == 6)
            {
                if (_inline != null)
                {
                    // pin all'estremità dei reofori: il nome sta accanto, non dentro il cerchietto
                    Point tip = WorldOf(pin);
                    labels.Add(new EntityLabel(pin.Name, new Point(tip.X, tip.Y - 1.2), 1.8, 0.5, 1));
                }
                else
                {
                    labels.Add(new EntityLabel(
                        pin.Name, WorldOf(pin), Math.Min(1.8 * _drawScale, _cavityDiameter * _drawScale * 0.65), 0.5, 0.5));
                }
                continue;
            }
            Point edge = EdgePoint(pin, rect);
            switch (pin.Side)
            {
                case 4:
                    labels.Add(new EntityLabel(pin.Name, edge, Math.Min(1.8 * _drawScale, _cavityDiameter * _drawScale * 0.65), 0.5, 0.5));
                    break;
                case 0:
                    labels.Add(new EntityLabel(pin.Name, new Point(edge.X + 1, edge.Y), 1.8, 0, 0.5));
                    break;
                case 1:
                    labels.Add(new EntityLabel(pin.Name, new Point(edge.X - 1, edge.Y), 1.8, 1, 0.5));
                    break;
                case 2:
                    labels.Add(new EntityLabel(pin.Name, new Point(edge.X, edge.Y + 1), 1.8, 0.5, 0));
                    break;
                default:
                    labels.Add(new EntityLabel(pin.Name, new Point(edge.X, edge.Y - 1), 1.8, 0.5, 1));
                    break;
            }
        }
        return labels;
    }

    private void Resize(double width, double height)
    {
        Rect rect = Body;
        _p1 = rect.TopLeft;
        _p2 = new Point(rect.Left + Math.Max(width, 2), rect.Top + Math.Max(height, 2));
        NotifyChanged();
    }

    private string GetPinNames()
    {
        return string.Join(",", _pins.Select(pin => pin.Name));
    }

    private void SetPinNames(string text)
    {
        string[] names = (text ?? string.Empty).Split(',');
        for (int i = 0; i < _pins.Count && i < names.Length; i++)
        {
            _pins[i].Name = names[i].Trim();
        }
        if (_inline == "diode")
        {
            // la striscia del catodo segue i nomi dei pin
            RebuildInline();
        }
        NotifyChanged();
    }

    private static bool IsYes(string text)
    {
        string value = (text ?? string.Empty).Trim().ToLowerInvariant();
        return value is "si" or "sì" or "s" or "y" or "yes" or "1" or "true" or "x";
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        if (_inline != null)
        {
            return new[]
            {
                new EntityField("X", () => Body.X, v => Translate(new Vector(v - Body.X, 0))),
                new EntityField("Y", () => Body.Y, v => Translate(new Vector(0, v - Body.Y))),
                new EntityField("Lunghezza reofori reale", () => LeadLength, v => LeadLength = v),
                new EntityField("Lunghezza reofori nel disegno", () => DrawnLead, v => DrawnLead = v),
                new EntityField("Scala nel disegno (×)", () => DrawScale, v => DrawScale = v)
            };
        }
        if (_custom)
        {
            return new[]
            {
                new EntityField("X", () => Body.X, v => Translate(new Vector(v - Body.X, 0))),
                new EntityField("Y", () => Body.Y, v => Translate(new Vector(0, v - Body.Y))),
                new EntityField("Diametro pin reale", () => CavityDiameter, v => CavityDiameter = v),
                new EntityField("Scala nel disegno (×)", () => DrawScale, v => DrawScale = v)
            };
        }
        if (_physical)
        {
            return new[]
            {
                new EntityField("X", () => Body.X, v => Translate(new Vector(v - Body.X, 0))),
                new EntityField("Y", () => Body.Y, v => Translate(new Vector(0, v - Body.Y))),
                new EntityField("Diametro cavità reale", () => CavityDiameter, v => CavityDiameter = v),
                new EntityField("Scala nel disegno (×)", () => DrawScale, v => DrawScale = v)
            };
        }
        return new[]
        {
            new EntityField("X", () => Body.X, v => Translate(new Vector(v - Body.X, 0))),
            new EntityField("Y", () => Body.Y, v => Translate(new Vector(0, v - Body.Y))),
            new EntityField("Larghezza", () => Body.Width, v => Resize(v, Body.Height)),
            new EntityField("Altezza", () => Body.Height, v => Resize(Body.Width, v))
        };
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        List<TextField> fields = new()
        {
            new TextField("Tag", () => Tag, v => Tag = v),
            new TextField("Tipo", () => Model, v => Model = v),
            new TextField("Descrizione", () => Designation, v => Designation = v),
            new TextField("Codice articolo", () => PartNumber, v => PartNumber = v),
            new TextField("Costruttore", () => Manufacturer, v => Manufacturer = v),
            new TextField(_inline == "resistor" ? "Valore (Ω)" : "Valore / portata", () => Rating, v => Rating = v)
        };
        if (_inline == "resistor")
        {
            fields.Add(new TextField("Potenza", () => Power, v => Power = v));
            fields.Add(new TextField("Tolleranza", () => Tolerance, v => Tolerance = v));
        }
        if (_inline == "diode" && _pins.Count == 2)
        {
            fields.Add(new TextField("Polarità (lungo il filo)",
                () => CathodeAtStart ? ReverseText : ForwardText,
                v =>
                {
                    if ((v == ReverseText) != CathodeAtStart)
                    {
                        ReverseDiode();
                    }
                },
                new[] { ForwardText, ReverseText }));
        }
        fields.Add(new TextField("Nomi pin (virgole)", GetPinNames, SetPinNames));
        fields.Add(new TextField("Pin ponticellati", () => Bridged ? "sì" : "no", v => Bridged = IsYes(v), new[] { "sì", "no" }));
        return fields;
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { _p1, _p2 };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        if (_physical || _custom)
        {
            return Array.Empty<Point>();
        }
        Rect rect = Body;
        return new[] { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft };
    }

    public override void MoveGrip(int index, Point position)
    {
        if (_physical || _custom)
        {
            return;
        }
        Rect rect = Body;
        double left = rect.Left;
        double top = rect.Top;
        double right = rect.Right;
        double bottom = rect.Bottom;
        switch (index)
        {
            case 0:
                left = position.X;
                top = position.Y;
                break;
            case 1:
                right = position.X;
                top = position.Y;
                break;
            case 2:
                right = position.X;
                bottom = position.Y;
                break;
            case 3:
                left = position.X;
                bottom = position.Y;
                break;
            default:
                return;
        }
        _p1 = new Point(left, top);
        _p2 = new Point(right, bottom);
        NotifyChanged();
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        if (index == 0)
        {
            P1 = position;
        }
        else if (index == 1)
        {
            P2 = position;
        }
    }

    public override void Translate(Vector offset)
    {
        _p1 += offset;
        _p2 += offset;
        if (_custom)
        {
            _placement.Translate(offset.X, offset.Y);
        }
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        if (_custom)
        {
            _placement = _placement * TransformMatrix(kind, center);
            UpdateCustomBody();
            NotifyChanged();
            return;
        }
        Rect rect = Body;
        Point a = Map(kind, center, rect.TopLeft);
        Point b = Map(kind, center, rect.BottomRight);
        _p1 = a;
        _p2 = b;
        if (kind == TransformKind.Rotate90)
        {
            (_realWidth, _realHeight) = (_realHeight, _realWidth);
        }
        if (_keyCorner >= 0)
        {
            switch (kind)
            {
                case TransformKind.Rotate90:
                    _keyCorner = (_keyCorner + 1) % 4;
                    break;
                case TransformKind.MirrorHorizontal:
                    _keyCorner = _keyCorner switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };
                    break;
                default:
                    _keyCorner = _keyCorner switch { 0 => 3, 3 => 0, 1 => 2, _ => 1 };
                    break;
            }
        }
        foreach (PinDef pin in _pins)
        {
            int side = pin.Side;
            double fraction = pin.Fraction;
            if (side == 4)
            {
                double u = pin.Fraction;
                double v = pin.FractionY;
                switch (kind)
                {
                    case TransformKind.Rotate90:
                        pin.Fraction = 1 - v;
                        pin.FractionY = u;
                        break;
                    case TransformKind.MirrorHorizontal:
                        pin.Fraction = 1 - u;
                        break;
                    default:
                        pin.FractionY = 1 - v;
                        break;
                }
                continue;
            }
            switch (kind)
            {
                case TransformKind.Rotate90:
                    switch (side)
                    {
                        case 0:
                            pin.Side = 2;
                            pin.Fraction = 1 - fraction;
                            break;
                        case 2:
                            pin.Side = 1;
                            break;
                        case 1:
                            pin.Side = 3;
                            pin.Fraction = 1 - fraction;
                            break;
                        default:
                            pin.Side = 0;
                            break;
                    }
                    break;
                case TransformKind.MirrorHorizontal:
                    if (side == 0)
                    {
                        pin.Side = 1;
                    }
                    else if (side == 1)
                    {
                        pin.Side = 0;
                    }
                    else
                    {
                        pin.Fraction = 1 - fraction;
                    }
                    break;
                default:
                    if (side == 2)
                    {
                        pin.Side = 3;
                    }
                    else if (side == 3)
                    {
                        pin.Side = 2;
                    }
                    else
                    {
                        pin.Fraction = 1 - fraction;
                    }
                    break;
            }
        }
        NotifyChanged();
    }

    public override void Export(Dictionary<string, string> data)
    {
        data["tag"] = _tag;
        data["model"] = _model;
        data["desc"] = _designation;
        data["part"] = _partNumber;
        data["maker"] = _manufacturer;
        data["rating"] = _rating;
        data["bridged"] = _bridged ? "1" : "0";
        data["physical"] = _physical ? "1" : "0";
        if (_custom && _parts != null)
        {
            data["custom"] = "1";
            data["parts"] = JsonSerializer.Serialize(_parts);
            data["pw"] = _partsWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            data["ph"] = _partsHeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            data["pm"] = string.Join(",", new[]
            {
                _placement.M11, _placement.M12, _placement.M21, _placement.M22, _placement.OffsetX, _placement.OffsetY
            }.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        }
        data["cavity"] = _cavityDiameter.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        data["key"] = _keyCorner.ToString(System.Globalization.CultureInfo.InvariantCulture);
        data["rw"] = _realWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        data["rh"] = _realHeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        data["scale"] = _drawScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (_inline != null)
        {
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            data["inline"] = _inline;
            data["ib"] = _bodyLength.ToString("R", invariant);
            data["id"] = _bodyDiameter.ToString("R", invariant);
            data["il"] = _leadLength.ToString("R", invariant);
            data["idl"] = _drawnLead.ToString("R", invariant);
            data["power"] = _power;
            data["tol"] = _tolerance;
            data["pm"] = string.Join(",", new[]
            {
                _placement.M11, _placement.M12, _placement.M21, _placement.M22, _placement.OffsetX, _placement.OffsetY
            }.Select(value => value.ToString("R", invariant)));
        }
        data["pins"] = JsonSerializer.Serialize(_pins);
    }

    public override void Import(Dictionary<string, string> data)
    {
        _tag = GetText(data, "tag");
        _model = GetText(data, "model");
        _designation = GetText(data, "desc");
        _partNumber = GetText(data, "part");
        _manufacturer = GetText(data, "maker");
        _rating = GetText(data, "rating");
        _bridged = GetText(data, "bridged") == "1";
        _physical = GetText(data, "physical") == "1";
        _cavityDiameter = Math.Max(0.2, GetDouble(data, "cavity", 2.5));
        _keyCorner = GetInt(data, "key", 0);
        _drawScale = Math.Clamp(GetDouble(data, "scale", 1), 0.1, 50);
        _realWidth = Math.Max(0, GetDouble(data, "rw", 0));
        _realHeight = Math.Max(0, GetDouble(data, "rh", 0));
        if (_physical && (_realWidth <= 0 || _realHeight <= 0))
        {
            // file precedenti: l'ingombro attuale (a scala 1) è quello reale
            Rect current = Body;
            _realWidth = current.Width / _drawScale;
            _realHeight = current.Height / _drawScale;
        }
        _pins.Clear();
        string json = GetText(data, "pins");
        if (json.Length > 0)
        {
            try
            {
                List<PinDef>? list = JsonSerializer.Deserialize<List<PinDef>>(json);
                if (list != null)
                {
                    _pins.AddRange(list);
                }
            }
            catch (JsonException)
            {
                _pins.Clear();
            }
        }

        _custom = false;
        _parts = null;
        _inline = null;
        _customGeometry = null;
        string inlineKind = GetText(data, "inline");
        if (inlineKind.Length > 0)
        {
            string[] inlineMatrix = GetText(data, "pm").Split(',');
            double[] inlineValues = new double[6];
            bool inlineValid = inlineMatrix.Length == 6;
            for (int i = 0; inlineValid && i < 6; i++)
            {
                inlineValid &= double.TryParse(inlineMatrix[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out inlineValues[i]);
            }
            if (inlineValid && _pins.Count == 2)
            {
                _inline = inlineKind;
                _custom = true;
                _bodyLength = GetDouble(data, "ib", 6);
                _bodyDiameter = GetDouble(data, "id", 2.4);
                _leadLength = GetDouble(data, "il", 0);
                _drawnLead = GetDouble(data, "idl", _leadLength);
                _power = GetText(data, "power");
                _tolerance = GetText(data, "tol");
                _placement = new Matrix(inlineValues[0], inlineValues[1], inlineValues[2], inlineValues[3], inlineValues[4], inlineValues[5]);
                RebuildInline();
            }
        }
        string partsJson = GetText(data, "parts");
        if (GetText(data, "custom") == "1" && partsJson.Length > 0)
        {
            try
            {
                List<EntityDto>? parts = JsonSerializer.Deserialize<List<EntityDto>>(partsJson);
                string[] matrix = GetText(data, "pm").Split(',');
                if (parts != null && matrix.Length == 6)
                {
                    double[] values = new double[6];
                    bool valid = true;
                    for (int i = 0; i < 6; i++)
                    {
                        valid &= double.TryParse(matrix[i], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out values[i]);
                    }
                    if (valid)
                    {
                        _custom = true;
                        _parts = parts;
                        _partsWidth = GetDouble(data, "pw", 0);
                        _partsHeight = GetDouble(data, "ph", 0);
                        _placement = new Matrix(values[0], values[1], values[2], values[3], values[4], values[5]);
                        RebuildCustomCache();
                        UpdateCustomBody();
                    }
                }
            }
            catch (JsonException)
            {
                _custom = false;
                _parts = null;
            }
        }
    }
}
