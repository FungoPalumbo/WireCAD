using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>
/// Guaina (corrugato, termorestringente, treccia...) che racchiude alcuni fili lungo un tracciato.
/// Il tracciato è una polilinea con curve di raggio assegnato; i fili contenuti sono calcolati dalla geometria.
/// </summary>
public class SheathEntity : EntityBase
{
    public const string InsideVisible = "Visibili";
    public const string InsideFaded = "Sbiaditi";
    public const string InsideHidden = "Nascosti";

    private List<Point> _points;
    private string _tag = "";
    private string _type = "Tubo";
    private string _color = "Nero";
    private string _inside = InsideFaded;
    private string _partNumber = "";
    private string _manufacturer = "";
    private string _realLength = "";
    private double _diameter = 8;
    private double _drawnWidth;
    private double _bendRadius = 8;
    private int _wireCount;
    private double _stripStart;
    private double _stripEnd;
    private string _innerLayer = InnerCalza;
    private double _innerPercent = 85;

    public const string InnerNone = "Nessuno";
    public const string InnerCalza = "Calza";
    public const string InnerTube = "Tubo";

    private string _innerColor = "Grigio";

    /// <summary>Colore dello strato interno scoperto (indipendente da quello della guaina); grigio di default.</summary>
    public string InnerColor
    {
        get => _innerColor;
        set => SetField(ref _innerColor, value);
    }

    public string InnerFillHex => ColorPalette.Resolve(_innerColor) ?? "#9CA3AF";

    /// <summary>Lunghezza sguainata all'inizio (mm reali): la guaina esterna è tagliata e si vede lo strato interno.</summary>
    public double StripStart
    {
        get => _stripStart;
        set
        {
            _stripStart = Math.Max(0, value);
            NotifyChanged();
        }
    }

    public double StripEnd
    {
        get => _stripEnd;
        set
        {
            _stripEnd = Math.Max(0, value);
            NotifyChanged();
        }
    }

    /// <summary>Strato visibile nei tratti sguainati: Nessuno oppure Calza.</summary>
    public string InnerLayer
    {
        get => _innerLayer;
        set => SetField(ref _innerLayer, value);
    }

    /// <summary>Larghezza dello strato interno in percentuale di quella della guaina.</summary>
    public double InnerPercent
    {
        get => _innerPercent;
        set
        {
            _innerPercent = Math.Clamp(value, 20, 100);
            NotifyChanged();
        }
    }

    public SheathEntity(IEnumerable<Point> points)
    {
        _points = points.ToList();
        if (_points.Count == 0)
        {
            _points.Add(new Point(0, 0));
        }
        if (_points.Count == 1)
        {
            _points.Add(_points[0]);
        }
    }

    /// <summary>Fili che attraversano la guaina (calcolato dalla finestra, non salvato).</summary>
    public string ContainedWires { get; set; } = "";

    public int ContainedCount { get; set; }

    public IReadOnlyList<Point> Points => _points;

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

    public string SheathType
    {
        get => _type;
        set => SetField(ref _type, value);
    }

    public string Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    /// <summary>Aspetto dei fili all'interno: Visibili, Sbiaditi o Nascosti.</summary>
    public string Inside
    {
        get => _inside;
        set => SetField(ref _inside, value);
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

    public string RealLength
    {
        get => _realLength;
        set => SetField(ref _realLength, value);
    }

    public double Diameter
    {
        get => _diameter;
        set
        {
            _diameter = Math.Clamp(value, 1, 200);
            NotifyChanged();
        }
    }

    /// <summary>Larghezza con cui la guaina è disegnata (mm); 0 = automatica (diametro reale × ingrandimento).</summary>
    public double DrawnWidth
    {
        get => _drawnWidth;
        set
        {
            _drawnWidth = Math.Clamp(value, 0, 400);
            NotifyChanged();
        }
    }

    /// <summary>Larghezza effettiva nel disegno, indipendente dal diametro reale.</summary>
    public double DrawWidth => _drawnWidth > 0 ? _drawnWidth : Math.Clamp(_diameter * DrawingScale.ThicknessFactor, 0.3, 400);

    /// <summary>Raggio delle curve nei vertici interni (mm); 0 per gomiti vivi.</summary>
    public double BendRadius
    {
        get => _bendRadius;
        set
        {
            _bendRadius = Math.Clamp(value, 0, 500);
            NotifyChanged();
        }
    }

    /// <summary>
    /// Numero di fili che entrano (e, accoppiati 1:1, escono) dalla guaina. Con un valore maggiore di 0 la guaina espone
    /// WireCount pin di ingresso (indici 0..N-1) e WireCount pin di uscita (indici N..2N-1) a cui agganciare i fili;
    /// con 0 non ha pin e i fili sono associati in base alla geometria.
    /// </summary>
    public int WireCount
    {
        get => _wireCount;
        set
        {
            _wireCount = Math.Clamp(value, 0, 64);
            NotifyChanged();
        }
    }

    private readonly HashSet<int> _brokenLinks = new();

    /// <summary>Vero se il collegamento interno ingresso→uscita è stato sostituito da fili veri (per inserire un componente).</summary>
    public bool IsLinkBroken(int link) => _brokenLinks.Contains(link);

    /// <summary>Interrompe il collegamento interno indicato: non viene più disegnato né considerato un conduttore unico.</summary>
    public void BreakLink(int link)
    {
        if (_brokenLinks.Add(link))
        {
            NotifyChanged();
        }
    }

    /// <summary>Linea del collegamento interno indicato, parallela al tracciato, dal pin d'ingresso a quello d'uscita.</summary>
    public List<Point> LinkPolyline(int link)
    {
        IReadOnlyList<Point> center = CenterLine();
        if (_wireCount <= 0 || link < 0 || link >= _wireCount || center.Count < 2)
        {
            return new List<Point>();
        }
        return SceneBuilder.OffsetPolyline(center, PinOffset(link));
    }

    public override string TypeName => "Guaina";

    public override string TypeKey => "sheath";

    public override string Summary => $"{_tag} – {_type} Ø{_diameter:0.#} – {ContainedCount} fili";

    public override double LineWidth => 0.35;

    public override string? FillHex => ColorPalette.Resolve(_color) ?? "#374151";

    // ---------------------------------------------------------------- pin della calza

    private bool ShieldAtStart => _innerLayer == InnerCalza && _stripStart > 0;

    private bool ShieldAtEnd => _innerLayer == InnerCalza && _stripEnd > 0;

    /// <summary>Numero di pin della calza (uno per ogni capo sguainato con calza scoperta).</summary>
    public int ShieldPinCount => (ShieldAtStart ? 1 : 0) + (ShieldAtEnd ? 1 : 0);

    public override int PinCount => _wireCount * 2 + ShieldPinCount;

    private bool IsShieldPin(int index) => index >= _wireCount * 2 && index < PinCount;

    private bool ShieldIsStart(int index) => ShieldAtStart && index == _wireCount * 2;

    /// <summary>Indici dei pin della calza (all'inizio e/o alla fine), in ordine.</summary>
    public IReadOnlyList<int> ShieldPinIndices
    {
        get
        {
            List<int> indices = new();
            int next = _wireCount * 2;
            if (ShieldAtStart)
            {
                indices.Add(next++);
            }
            if (ShieldAtEnd)
            {
                indices.Add(next);
            }
            return indices;
        }
    }

    /// <summary>Posizione del pin della calza: sull'asse, a metà della lunghezza del tratto scoperto.</summary>
    private Point ShieldPosition(bool atStart)
    {
        IReadOnlyList<Point> full = CenterLine();
        if (full.Count < 2)
        {
            return new Point();
        }
        double total = PathLength(full);
        (double start, double end) = StripLengths(total);
        double distance = atStart ? start / 2 : total - end / 2;
        Point point = new PathSampler(full).At(distance, 0);
        return new Point(Math.Round(point.X, 4), Math.Round(point.Y, 4));
    }

    private bool TryEnds(out Point start, out Vector startDirection, out Point end, out Vector endDirection)
    {
        IReadOnlyList<Point> center = CenterLine();
        start = new Point();
        end = new Point();
        startDirection = new Vector(0, 0);
        endDirection = new Vector(0, 0);
        if (center.Count < 2)
        {
            return false;
        }
        start = center[0];
        end = center[center.Count - 1];
        startDirection = Unit(center[1] - center[0]);
        endDirection = Unit(center[center.Count - 1] - center[center.Count - 2]);
        return true;
    }

    private double PinOffset(int k)
    {
        // con la calza scoperta i pin stanno nella larghezza della calza, che è quella che esce dalla guaina
        double width = HasInnerLayer ? InnerWidth : DrawWidth;
        return -width / 2 + (k + 1) * width / (_wireCount + 1);
    }

    public override Point GetPinPosition(int index)
    {
        if (IsShieldPin(index))
        {
            return ShieldPosition(ShieldIsStart(index));
        }
        if (index < 0 || index >= PinCount || !TryEnds(out Point start, out Vector startDir, out Point end, out Vector endDir))
        {
            return new Point();
        }
        bool entry = index < _wireCount;
        int k = entry ? index : index - _wireCount;
        Point origin = entry ? start : end;
        Vector direction = entry ? startDir : endDir;
        Vector normal = new(-direction.Y, direction.X);
        Point pin = origin + normal * PinOffset(k);
        return new Point(Math.Round(pin.X, 4), Math.Round(pin.Y, 4));
    }

    public override string GetPinLabel(int index)
    {
        if (index < 0 || index >= PinCount)
        {
            return string.Empty;
        }
        if (IsShieldPin(index))
        {
            return "C";
        }
        int k = index < _wireCount ? index : index - _wireCount;
        return (k + 1).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Descrizione per le tabelle, per esempio "G1:2 ing." o "G1:2 usc.".</summary>
    public string DescribePin(int index)
    {
        if (IsShieldPin(index))
        {
            return $"{_tag}:calza {(ShieldIsStart(index) ? "ing." : "usc.")}";
        }
        bool entry = index < _wireCount;
        return $"{_tag}:{GetPinLabel(index)} {(entry ? "ing." : "usc.")}";
    }

    public override Vector GetPinDirection(int index)
    {
        if (IsShieldPin(index))
        {
            return new Vector(0, 0);
        }
        if (index < 0 || index >= PinCount || !TryEnds(out _, out Vector startDir, out _, out Vector endDir))
        {
            return new Vector(0, 0);
        }
        return index < _wireCount ? -startDir : endDir;
    }

    /// <summary>
    /// Fili interni, uno per ciascun ingresso, paralleli al tracciato; null se la guaina non ha pin o nessun collegamento
    /// è in uso. Con <paramref name="used"/> si disegnano solo i collegamenti indicati.
    /// </summary>
    public Geometry? InnerGeometry(IReadOnlyList<bool>? used = null)
    {
        if (_wireCount <= 0)
        {
            return null;
        }
        IReadOnlyList<Point> center = CenterLine();
        if (center.Count < 2)
        {
            return null;
        }
        PathGeometry geometry = new();
        for (int k = 0; k < _wireCount; k++)
        {
            if (_brokenLinks.Contains(k) || (used != null && (k >= used.Count || !used[k])))
            {
                continue;
            }
            List<Point> line = SceneBuilder.OffsetPolyline(center, PinOffset(k));
            if (line.Count < 2)
            {
                continue;
            }
            PathFigure figure = new() { StartPoint = line[0], IsClosed = false, IsFilled = false };
            figure.Segments.Add(new PolyLineSegment(line.Skip(1).ToList(), true));
            geometry.Figures.Add(figure);
        }
        if (geometry.Figures.Count == 0)
        {
            return null;
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Linee dei fili interni per l'esportazione (coordinate del foglio).</summary>
    public List<List<Point>> InnerLines(IReadOnlyList<bool>? used = null)
    {
        List<List<Point>> lines = new();
        IReadOnlyList<Point> center = CenterLine();
        if (_wireCount <= 0 || center.Count < 2)
        {
            return lines;
        }
        for (int k = 0; k < _wireCount; k++)
        {
            if (_brokenLinks.Contains(k) || (used != null && (k >= used.Count || !used[k])))
            {
                continue;
            }
            lines.Add(SceneBuilder.OffsetPolyline(center, PinOffset(k)));
        }
        return lines;
    }

    public override double FillOpacity
    {
        get
        {
            if (_wireCount > 0)
            {
                return 0.15;
            }
            switch (_inside)
            {
                case InsideVisible:
                    return 0.15;
                case InsideHidden:
                    return 1;
                default:
                    return 0.65;
            }
        }
    }

    /// <summary>Lunghezza del tracciato (mm).</summary>
    public double Length
    {
        get
        {
            IReadOnlyList<Point> center = CenterLine();
            double total = 0;
            for (int i = 0; i + 1 < center.Count; i++)
            {
                total += (center[i + 1] - center[i]).Length;
            }
            return total;
        }
    }

    public double EffectiveLength
    {
        get
        {
            string text = _realLength.Trim();
            if (text.Length > 0
                && (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed)
                    || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                && parsed > 0)
            {
                return parsed;
            }
            return Length;
        }
    }

    private static Vector Unit(Vector vector)
    {
        double length = vector.Length;
        return length < 1e-12 ? new Vector(0, 0) : new Vector(vector.X / length, vector.Y / length);
    }

    private List<Point> Deduplicated()
    {
        List<Point> unique = new();
        foreach (Point point in _points)
        {
            if (unique.Count == 0 || (point - unique[unique.Count - 1]).Length > 1e-9)
            {
                unique.Add(point);
            }
        }
        return unique;
    }

    /// <summary>Asse della guaina con i vertici interni raccordati da archi di raggio <see cref="BendRadius"/>.</summary>
    public IReadOnlyList<Point> CenterLine()
    {
        List<Point> points = Deduplicated();
        if (_bendRadius <= 0 || points.Count < 3)
        {
            return points;
        }
        List<Point> result = new() { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector a = Unit(points[i] - points[i - 1]);
            Vector b = Unit(points[i + 1] - points[i]);
            double cross = a.X * b.Y - a.Y * b.X;
            double phi = Math.Atan2(Math.Abs(cross), a * b);
            if (phi < 1e-3 || phi > Math.PI - 1e-3)
            {
                result.Add(points[i]);
                continue;
            }
            double lengthPrev = (points[i] - points[i - 1]).Length;
            double lengthNext = (points[i + 1] - points[i]).Length;
            double radius = _bendRadius;
            double tangent = radius * Math.Tan(phi / 2);
            double limit = 0.5 * Math.Min(lengthPrev, lengthNext);
            if (tangent > limit)
            {
                tangent = limit;
                radius = tangent / Math.Tan(phi / 2);
            }
            Point entry = points[i] - a * tangent;
            double sign = cross > 0 ? 1 : -1;
            Vector normal = new Vector(-a.Y, a.X) * sign;
            Point center = entry + normal * radius;
            double start = Math.Atan2(entry.Y - center.Y, entry.X - center.X);
            const int steps = 8;
            for (int k = 0; k <= steps; k++)
            {
                double angle = start + sign * phi * k / steps;
                result.Add(new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
            }
        }
        result.Add(points[points.Count - 1]);
        return result;
    }

    /// <summary>Tratto di polilinea compreso tra due ascisse curvilinee.</summary>
    private static List<Point> SubPath(IReadOnlyList<Point> path, double from, double to)
    {
        List<Point> result = new();
        double cumulative = 0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Point a = path[i];
            Point b = path[i + 1];
            double length = (b - a).Length;
            if (length < 1e-12)
            {
                continue;
            }
            double segmentStart = cumulative;
            double segmentEnd = cumulative + length;
            if (segmentEnd > from && segmentStart < to)
            {
                double t0 = Math.Max(0, (from - segmentStart) / length);
                double t1 = Math.Min(1, (to - segmentStart) / length);
                Point p0 = a + (b - a) * t0;
                Point p1 = a + (b - a) * t1;
                if (result.Count == 0 || (result[result.Count - 1] - p0).Length > 1e-9)
                {
                    result.Add(p0);
                }
                result.Add(p1);
            }
            cumulative = segmentEnd;
        }
        return result;
    }

    private static double PathLength(IReadOnlyList<Point> path)
    {
        double total = 0;
        for (int i = 0; i + 1 < path.Count; i++)
        {
            total += (path[i + 1] - path[i]).Length;
        }
        return total;
    }

    /// <summary>Lunghezze di sguainatura effettive, ridotte se insieme superano il tracciato.</summary>
    private (double Start, double End) StripLengths(double total)
    {
        double start = Math.Max(0, _stripStart);
        double end = Math.Max(0, _stripEnd);
        double available = total - 1;
        if (available <= 0)
        {
            return (0, 0);
        }
        if (start + end > available)
        {
            double factor = available / (start + end);
            start *= factor;
            end *= factor;
        }
        return (start, end);
    }

    /// <summary>Asse della sola guaina esterna, senza i tratti sguainati alle estremità.</summary>
    private IReadOnlyList<Point> OuterCenterLine()
    {
        IReadOnlyList<Point> full = CenterLine();
        if (full.Count < 2 || (_stripStart <= 0 && _stripEnd <= 0))
        {
            return full;
        }
        double total = PathLength(full);
        (double start, double end) = StripLengths(total);
        List<Point> outer = SubPath(full, start, total - end);
        return outer.Count >= 2 ? outer : full;
    }

    private bool HasInnerLayer =>
        (_innerLayer == InnerCalza || _innerLayer == InnerTube) && (_stripStart > 0 || _stripEnd > 0);

    /// <summary>Larghezza disegnata dello strato interno (calza).</summary>
    private double InnerWidth => DrawWidth * Math.Clamp(_innerPercent, 20, 100) / 100;

    /// <summary>Contorno chiuso della guaina (i due bordi paralleli uniti alle estremità).</summary>
    public List<Point> Outline()
    {
        IReadOnlyList<Point> center = OuterCenterLine();
        if (center.Count < 2)
        {
            return center.ToList();
        }
        double half = DrawWidth / 2;
        List<Point> left = SceneBuilder.OffsetPolyline(center, half);
        List<Point> right = SceneBuilder.OffsetPolyline(center, -half);
        right.Reverse();
        left.AddRange(right);
        return left;
    }

    /// <summary>Punti dell'asse a una data ascissa curvilinea, con scostamento laterale.</summary>
    private sealed class PathSampler
    {
        private readonly IReadOnlyList<Point> _points;
        private readonly double[] _cumulative;

        public PathSampler(IReadOnlyList<Point> points)
        {
            _points = points;
            _cumulative = new double[points.Count];
            for (int i = 1; i < points.Count; i++)
            {
                _cumulative[i] = _cumulative[i - 1] + (points[i] - points[i - 1]).Length;
            }
            Length = _cumulative[points.Count - 1];
        }

        public double Length { get; }

        public Point At(double distance, double offset)
        {
            double s = Math.Clamp(distance, 0, Length);
            int i = 0;
            while (i < _points.Count - 2 && _cumulative[i + 1] < s)
            {
                i++;
            }
            Vector direction = _points[i + 1] - _points[i];
            double segment = direction.Length;
            Vector unit = segment < 1e-12 ? new Vector(1, 0) : new Vector(direction.X / segment, direction.Y / segment);
            Point position = _points[i] + unit * (s - _cumulative[i]);
            return position + new Vector(-unit.Y, unit.X) * offset;
        }
    }

    private static void AddLine(PathGeometry geometry, Point from, Point to)
    {
        PathFigure line = new() { StartPoint = from, IsClosed = false, IsFilled = false };
        line.Segments.Add(new LineSegment(to, true));
        geometry.Figures.Add(line);
    }

    /// <summary>
    /// Motivo della superficie: treccia = diagonali incrociate, guaina corrugata = linee trasversali ravvicinate.
    /// Gli altri tipi restano lisci.
    /// </summary>
    private void AddPattern(PathGeometry geometry)
    {
        bool braided = _type.Contains("treccia", StringComparison.OrdinalIgnoreCase);
        bool corrugated = _type.Contains("corrugat", StringComparison.OrdinalIgnoreCase);
        if (!braided && !corrugated)
        {
            return;
        }
        AddPatternOnPath(geometry, OuterCenterLine(), DrawWidth, braided);
    }

    /// <summary>
    /// Strato interno visibile nei tratti sguainati (calza con motivo a treccia, oppure tubo liscio), più stretto della
    /// guaina e con un proprio colore. Null se non ci sono tratti sguainati.
    /// </summary>
    public Geometry? InnerLayerGeometry()
    {
        if (!HasInnerLayer)
        {
            return null;
        }
        PathGeometry geometry = new();
        AddInnerLayer(geometry);
        if (geometry.Figures.Count == 0)
        {
            return null;
        }
        geometry.Freeze();
        return geometry;
    }

    public override bool HitTest(Point point, double tolerance)
    {
        if (base.HitTest(point, tolerance))
        {
            return true;
        }
        Geometry? inner = InnerLayerGeometry();
        return inner != null && inner.FillContains(point);
    }

    private void AddInnerLayer(PathGeometry geometry)
    {
        IReadOnlyList<Point> full = CenterLine();
        if (full.Count < 2)
        {
            return;
        }
        double total = PathLength(full);
        (double start, double end) = StripLengths(total);
        double width = InnerWidth;
        List<List<Point>> zones = new();
        if (start > 0)
        {
            zones.Add(SubPath(full, 0, start));
        }
        if (end > 0)
        {
            zones.Add(SubPath(full, total - end, total));
        }
        foreach (List<Point> zone in zones)
        {
            if (zone.Count < 2)
            {
                continue;
            }
            List<Point> left = SceneBuilder.OffsetPolyline(zone, width / 2);
            List<Point> right = SceneBuilder.OffsetPolyline(zone, -width / 2);
            right.Reverse();
            left.AddRange(right);
            if (left.Count >= 3)
            {
                PathFigure body = new() { StartPoint = left[0], IsClosed = true, IsFilled = true };
                body.Segments.Add(new PolyLineSegment(left.Skip(1).ToList(), true));
                geometry.Figures.Add(body);
            }
            if (_innerLayer == InnerCalza)
            {
                AddPatternOnPath(geometry, zone, width, true);
            }
        }
    }

    private static void AddPatternOnPath(PathGeometry geometry, IReadOnlyList<Point> center, double width, bool braided)
    {
        bool corrugated = !braided;
        if (center.Count < 2)
        {
            return;
        }
        PathSampler sampler = new(center);
        if (sampler.Length < 1)
        {
            return;
        }
        double half = width / 2;
        double length = sampler.Length;

        if (corrugated)
        {
            double ribSpacing = Math.Max(1.0, width * 0.3);
            double ribs = length / ribSpacing;
            if (ribs > 600)
            {
                ribSpacing *= ribs / 600;
            }
            for (double s = ribSpacing / 2; s <= length; s += ribSpacing)
            {
                AddLine(geometry, sampler.At(s, half), sampler.At(s, -half));
            }
            return;
        }

        // Treccia: due famiglie di diagonali incrociate (reticolo a losanghe), ritagliate alle estremità del tratto
        // così il motivo copre anche i tratti corti.
        double reach = width;
        double spacing = Math.Max(0.8, width * 0.35);
        double estimated = (length + reach) / spacing * 2;
        if (estimated > 600)
        {
            spacing *= estimated / 600;
        }
        for (double start = -reach + spacing / 2; start < length; start += spacing)
        {
            double t0 = Math.Max(0, -start / reach);
            double t1 = Math.Min(1, (length - start) / reach);
            if (t1 - t0 < 1e-3)
            {
                continue;
            }
            AddLine(geometry,
                sampler.At(start + t0 * reach, half - width * t0),
                sampler.At(start + t1 * reach, half - width * t1));
            AddLine(geometry,
                sampler.At(start + t0 * reach, -half + width * t0),
                sampler.At(start + t1 * reach, -half + width * t1));
        }
    }

    public override Geometry BuildGeometry()
    {
        List<Point> outline = Outline();
        if (outline.Count < 3)
        {
            LineGeometry fallback = new(_points[0], _points[_points.Count - 1]);
            fallback.Freeze();
            return fallback;
        }
        PathFigure figure = new() { StartPoint = outline[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(outline.Skip(1).ToList(), true));
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        AddPattern(geometry);
        for (int i = 0; i < PinCount; i++)
        {
            Point pin = GetPinPosition(i);
            List<Point> ring = new();
            for (int step = 0; step < 12; step++)
            {
                double angle = 2 * Math.PI * step / 12;
                ring.Add(new Point(pin.X + 0.6 * Math.Cos(angle), pin.Y + 0.6 * Math.Sin(angle)));
            }
            PathFigure marker = new() { StartPoint = ring[0], IsClosed = true, IsFilled = false };
            marker.Segments.Add(new PolyLineSegment(ring.Skip(1).ToList(), true));
            geometry.Figures.Add(marker);
        }
        geometry.Freeze();
        return geometry;
    }

    public override IReadOnlyList<EntityLabel> GetLabels()
    {
        List<EntityLabel> labels = new();
        if (_points.Count < 2)
        {
            return labels;
        }
        int best = 0;
        double bestLength = -1;
        for (int i = 0; i + 1 < _points.Count; i++)
        {
            double length = (_points[i + 1] - _points[i]).Length;
            if (length > bestLength)
            {
                bestLength = length;
                best = i;
            }
        }
        Point a = _points[best];
        Point b = _points[best + 1];
        Point mid = new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        string strip = _stripStart > 0 || _stripEnd > 0 ? $"  sguain. {_stripStart:0.#}/{_stripEnd:0.#}" : string.Empty;
        string text = $"{_tag}  {_type}  Ø{_diameter:0.#}  {EffectiveLength:0} mm{strip}";
        if (ShowLabel)
        {
            labels.Add(new EntityLabel(text, new Point(mid.X, mid.Y - DrawWidth / 2 - 1), 2.4, 0.5, 1));
        }
        for (int i = 0; i < PinCount; i++)
        {
            Point pin = GetPinPosition(i);
            if (IsShieldPin(i))
            {
                labels.Add(new EntityLabel(GetPinLabel(i), new Point(pin.X, pin.Y - 1.2), 1.8, 0.5, 1));
                continue;
            }
            Vector outward = GetPinDirection(i);
            labels.Add(new EntityLabel(GetPinLabel(i), new Point(pin.X + outward.X * 1.6, pin.Y + outward.Y * 1.6), 1.6, 0.5, 0.5));
        }
        return labels;
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        List<EntityField> fields = new()
        {
            new EntityField("Diametro reale", () => Diameter, v => Diameter = v),
            new EntityField("Larghezza nel disegno (0 = auto)", () => DrawnWidth, v => DrawnWidth = v),
            new EntityField("Raggio curve", () => BendRadius, v => BendRadius = v),
            new EntityField("Sguainatura inizio", () => StripStart, v => StripStart = v),
            new EntityField("Sguainatura fine", () => StripEnd, v => StripEnd = v),
            new EntityField("Larghezza calza (% guaina)", () => InnerPercent, v => InnerPercent = v),
            new EntityField("Fili (ingressi = uscite)", () => WireCount, v => WireCount = (int)Math.Round(v))
        };
        for (int i = 0; i < _points.Count; i++)
        {
            int index = i;
            fields.Add(new EntityField($"P{index + 1} X", () => _points[index].X, v =>
            {
                _points[index] = new Point(v, _points[index].Y);
                NotifyChanged();
            }));
            fields.Add(new EntityField($"P{index + 1} Y", () => _points[index].Y, v =>
            {
                _points[index] = new Point(_points[index].X, v);
                NotifyChanged();
            }));
        }
        return fields;
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[]
        {
            new TextField("Tag", () => Tag, v => Tag = v),
            new TextField("Tipo", () => SheathType, v => SheathType = v,
                new[] { "Guaina corrugata", "Guaina termorestringente", "Treccia", "Nastro", "Tubo" }),
            new TextField("Colore", () => Color, v => Color = v, ColorPalette.Choices),
            new TextField("Strato interno scoperto", () => InnerLayer, v => InnerLayer = v,
                new[] { InnerNone, InnerCalza, InnerTube }),
            new TextField("Colore strato interno", () => InnerColor, v => InnerColor = v, ColorPalette.Choices),
            new TextField("Fili all'interno", () => Inside, v => Inside = v,
                new[] { InsideVisible, InsideFaded, InsideHidden }),
            new TextField("Codice articolo", () => PartNumber, v => PartNumber = v),
            new TextField("Costruttore", () => Manufacturer, v => Manufacturer = v),
            new TextField("Lunghezza reale (mm)", () => RealLength, v => RealLength = v),
            new TextField("Fili contenuti", () => ContainedWires, _ => { })
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return _points.ToArray();
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return _points.ToArray();
    }

    public override void MoveGrip(int index, Point position)
    {
        if (index >= 0 && index < _points.Count)
        {
            _points[index] = position;
            NotifyChanged();
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        MoveGrip(index, position);
    }

    /// <summary>Inserisce un vertice (nuova curva) sul tratto più vicino al punto indicato.</summary>
    public void InsertVertexNear(Point point)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i + 1 < _points.Count; i++)
        {
            double distance = WireEntity.DistanceToSegment(point, _points[i], _points[i + 1]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        Vector direction = _points[best + 1] - _points[best];
        double lengthSquared = direction.LengthSquared;
        double t = lengthSquared < 1e-12 ? 0.5 : Math.Clamp(((point - _points[best]) * direction) / lengthSquared, 0.05, 0.95);
        _points.Insert(best + 1, _points[best] + direction * t);
        NotifyChanged();
    }

    /// <summary>Rimuove il vertice più vicino al punto indicato (restano almeno due vertici).</summary>
    public bool RemoveVertexNear(Point point)
    {
        if (_points.Count <= 2)
        {
            return false;
        }
        int best = 0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < _points.Count; i++)
        {
            double distance = (_points[i] - point).Length;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        _points.RemoveAt(best);
        NotifyChanged();
        return true;
    }

    public override void Translate(Vector offset)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] += offset;
        }
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] = Map(kind, center, _points[i]);
        }
        NotifyChanged();
    }

    public override void Export(Dictionary<string, string> data)
    {
        data["tag"] = _tag;
        data["type"] = _type;
        data["color"] = _color;
        data["inside"] = _inside;
        data["part"] = _partNumber;
        data["maker"] = _manufacturer;
        data["real"] = _realLength;
        data["dia"] = _diameter.ToString("R", CultureInfo.InvariantCulture);
        data["drawn"] = _drawnWidth.ToString("R", CultureInfo.InvariantCulture);
        data["bend"] = _bendRadius.ToString("R", CultureInfo.InvariantCulture);
        data["wires"] = _wireCount.ToString(CultureInfo.InvariantCulture);
        data["stripA"] = _stripStart.ToString("R", CultureInfo.InvariantCulture);
        data["stripB"] = _stripEnd.ToString("R", CultureInfo.InvariantCulture);
        data["layer"] = _innerLayer;
        data["layerPct"] = _innerPercent.ToString("R", CultureInfo.InvariantCulture);
        data["layerColor"] = _innerColor;
        if (_brokenLinks.Count > 0)
        {
            data["broken"] = string.Join(",", _brokenLinks.OrderBy(link => link).Select(link => link.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public override void Import(Dictionary<string, string> data)
    {
        _tag = GetText(data, "tag");
        _type = GetText(data, "type");
        _color = GetText(data, "color");
        _inside = GetText(data, "inside");
        _partNumber = GetText(data, "part");
        _manufacturer = GetText(data, "maker");
        _realLength = GetText(data, "real");
        _diameter = Math.Clamp(GetDouble(data, "dia", 8), 1, 200);
        _drawnWidth = Math.Clamp(GetDouble(data, "drawn", 0), 0, 400);
        _bendRadius = Math.Clamp(GetDouble(data, "bend", 8), 0, 500);
        _wireCount = Math.Clamp(GetInt(data, "wires", 0), 0, 64);
        _stripStart = Math.Max(0, GetDouble(data, "stripA", 0));
        _stripEnd = Math.Max(0, GetDouble(data, "stripB", 0));
        string layer = GetText(data, "layer");
        _innerLayer = layer.Length == 0 ? InnerCalza : layer;
        _innerPercent = Math.Clamp(GetDouble(data, "layerPct", 85), 20, 100);
        string innerColor = GetText(data, "layerColor");
        _innerColor = innerColor.Length == 0 ? "Grigio" : innerColor;
        _brokenLinks.Clear();
        foreach (string item in GetText(data, "broken").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out int link))
            {
                _brokenLinks.Add(link);
            }
        }
    }
}

/// <summary>Determina, dalla geometria, quali fili passano all'interno di una guaina.</summary>
public static class SheathAnalyzer
{
    private static double DistanceToPolyline(Point point, IReadOnlyList<Point> polyline)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < polyline.Count; i++)
        {
            best = Math.Min(best, WireEntity.DistanceToSegment(point, polyline[i], polyline[i + 1]));
        }
        return best;
    }

    /// <summary>
    /// Per ciascun collegamento ingresso→uscita, indica se almeno uno dei due pin ha un filo collegato
    /// (i collegamenti senza fili a entrambe le estremità non vengono disegnati).
    /// </summary>
    public static List<bool> UsedLinks(SheathEntity sheath, IEnumerable<WireEntity> wires)
    {
        List<WireEntity> list = wires as List<WireEntity> ?? wires.ToList();
        List<bool> used = new();
        for (int k = 0; k < sheath.WireCount; k++)
        {
            if (sheath.IsLinkBroken(k))
            {
                used.Add(false);
                continue;
            }
            Point entry = sheath.GetPinPosition(k);
            Point exit = sheath.GetPinPosition(sheath.WireCount + k);
            used.Add(list.Any(wire =>
                WireEntity.Near(entry, wire.StartPoint, 0.01) || WireEntity.Near(entry, wire.EndPoint, 0.01)
                || WireEntity.Near(exit, wire.StartPoint, 0.01) || WireEntity.Near(exit, wire.EndPoint, 0.01)));
        }
        return used;
    }

    public static List<WireEntity> WiresIn(SheathEntity sheath, IEnumerable<WireEntity> wires)
    {
        IReadOnlyList<Point> center = sheath.CenterLine();
        List<WireEntity> result = new();
        if (center.Count < 2)
        {
            return result;
        }
        if (sheath.WireCount > 0)
        {
            // Guaina con pin: contiene i fili agganciati ai suoi ingressi/uscite.
            List<Point> pins = new();
            for (int i = 0; i < sheath.WireCount * 2; i++)
            {
                pins.Add(sheath.GetPinPosition(i));
            }
            foreach (WireEntity wire in wires)
            {
                if (pins.Any(pin => WireEntity.Near(pin, wire.StartPoint, 0.01) || WireEntity.Near(pin, wire.EndPoint, 0.01)))
                {
                    result.Add(wire);
                }
            }
            return result;
        }
        double half = sheath.DrawWidth / 2 + 0.01;
        double threshold = Math.Min(Math.Max(1.5 * sheath.DrawWidth, 5), 0.5 * sheath.Length);
        foreach (WireEntity wire in wires)
        {
            double inside = 0;
            IReadOnlyList<Point> points = wire.Points;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                double segment = (points[i + 1] - points[i]).Length;
                int steps = Math.Max(1, (int)Math.Ceiling(segment / 0.5));
                double step = segment / steps;
                for (int s = 0; s < steps; s++)
                {
                    double t = (s + 0.5) / steps;
                    Point sample = points[i] + (points[i + 1] - points[i]) * t;
                    if (DistanceToPolyline(sample, center) <= half)
                    {
                        inside += step;
                    }
                }
            }
            if (inside >= threshold)
            {
                result.Add(wire);
            }
        }
        return result;
    }
}
