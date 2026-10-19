using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Filo elettrico: polilinea ortogonale con attributi (numero, colore, sezione...).</summary>
public class WireEntity : EntityBase
{
    private const double Eps = 1e-6;

    private static readonly Dictionary<string, string> ColorMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BK"] = "#111827", ["NERO"] = "#111827",
        ["BN"] = "#78350F", ["MARRONE"] = "#78350F",
        ["RD"] = "#DC2626", ["ROSSO"] = "#DC2626",
        ["OG"] = "#EA580C", ["ARANCIO"] = "#EA580C", ["ARANCIONE"] = "#EA580C",
        ["YE"] = "#CA8A04", ["GIALLO"] = "#CA8A04",
        ["GN"] = "#16A34A", ["VERDE"] = "#16A34A",
        ["BU"] = "#2563EB", ["BLU"] = "#2563EB",
        ["VT"] = "#7C3AED", ["VIOLA"] = "#7C3AED",
        ["GY"] = "#6B7280", ["GRIGIO"] = "#6B7280",
        ["WH"] = "#9CA3AF", ["BIANCO"] = "#9CA3AF",
        ["PK"] = "#EC4899", ["ROSA"] = "#EC4899",
        ["TQ"] = "#0891B2", ["TURCHESE"] = "#0891B2",
        ["GNYE"] = "#65A30D", ["YEGN"] = "#65A30D", ["GIALLOVERDE"] = "#65A30D", ["VERDEGIALLO"] = "#65A30D"
    };

    private static readonly string[] ColorChoices =
    {
        "Nero", "Marrone", "Rosso", "Arancio", "Giallo", "Verde", "Blu", "Viola",
        "Grigio", "Bianco", "Rosa", "Turchese"
    };

    private static readonly string[] SecondColorChoices =
    {
        "", "Nero", "Marrone", "Rosso", "Arancio", "Giallo", "Verde", "Blu", "Viola",
        "Grigio", "Bianco", "Rosa", "Turchese"
    };

    private static readonly string[] SectionChoices =
    {
        "0.22", "0.35", "0.5", "0.75", "1", "1.5", "2.5", "4", "6", "10", "16", "25"
    };

    private List<Point> _points;
    private string _number = "";
    private string _color = "";
    private string _color2 = "";
    private string _section = "";
    private string _cable = "";
    private string _signal = "";
    private string _terminalStart = "";
    private string _terminalEnd = "";
    private string _realLength = "";

    public WireEntity(IEnumerable<Point> points)
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

    /// <summary>Entità e pin a cui è agganciato l'inizio del filo (null = estremità libera).</summary>
    public Guid? StartComp { get; set; }

    public int StartPin { get; set; }

    public Guid? EndComp { get; set; }

    public int EndPin { get; set; }

    public IReadOnlyList<Point> Points => _points;

    /// <summary>Se vero il tracciato resta ortogonale (gomiti a 90°); se falso i segmenti possono avere qualsiasi angolo.</summary>
    public bool Orthogonal { get; set; } = true;

    private bool _realistic;
    private double _diameter = 2;

    /// <summary>Aspetto realistico: il filo è una fascia con due bordi paralleli e riempimento del colore scelto.</summary>
    public bool Realistic
    {
        get => _realistic;
        set
        {
            _realistic = value;
            NotifyChanged();
        }
    }

    /// <summary>Diametro esterno reale del filo (dato di progetto), in mm.</summary>
    public double Diameter
    {
        get => _diameter;
        set
        {
            _diameter = Math.Clamp(value, 0.6, 20);
            NotifyChanged();
        }
    }

    private double _drawnWidth;

    /// <summary>Larghezza con cui il filo realistico è disegnato (mm); 0 = automatica (diametro reale × ingrandimento).</summary>
    public double DrawnWidth
    {
        get => _drawnWidth;
        set
        {
            _drawnWidth = Math.Clamp(value, 0, 60);
            NotifyChanged();
        }
    }

    /// <summary>Larghezza effettiva nel disegno.</summary>
    public double DrawWidth => _drawnWidth > 0 ? _drawnWidth : Math.Clamp(_diameter * DrawingScale.ThicknessFactor, 0.3, 60);

    public Point StartPoint => _points[0];

    public Point EndPoint => _points[_points.Count - 1];

    public string Number
    {
        get => _number;
        set => SetField(ref _number, value);
    }

    public string Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    /// <summary>Secondo colore opzionale (filo bicolore a strisce).</summary>
    public string Color2
    {
        get => _color2;
        set => SetField(ref _color2, value);
    }

    public string Section
    {
        get => _section;
        set => SetField(ref _section, value);
    }

    public string Cable
    {
        get => _cable;
        set => SetField(ref _cable, value);
    }

    public string Signal
    {
        get => _signal;
        set => SetField(ref _signal, value);
    }

    public string TerminalStart
    {
        get => _terminalStart;
        set => SetField(ref _terminalStart, value);
    }

    public string TerminalEnd
    {
        get => _terminalEnd;
        set => SetField(ref _terminalEnd, value);
    }

    public string RealLength
    {
        get => _realLength;
        set => SetField(ref _realLength, value);
    }

    /// <summary>Lunghezza geometrica del tracciato nel disegno (mm).</summary>
    public double Length
    {
        get
        {
            double total = 0;
            for (int i = 0; i + 1 < _points.Count; i++)
            {
                total += (_points[i + 1] - _points[i]).Length;
            }
            return total;
        }
    }

    /// <summary>Lunghezza reale se indicata dall'utente, altrimenti quella del tracciato.</summary>
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
            return Length + ExtraLength;
        }
    }

    /// <summary>
    /// Lunghezza aggiunta dai reofori dei componenti in linea a cui il filo è collegato (calcolata dalla finestra,
    /// non salvata): fa parte della lunghezza di taglio quando non è indicata una lunghezza reale.
    /// </summary>
    public double ExtraLength { get; set; }

    /// <summary>Sostituisce il tracciato (per esempio dopo il taglio del filo per inserire un componente).</summary>
    public void ReplacePoints(IEnumerable<Point> points)
    {
        List<Point> list = points.ToList();
        if (list.Count >= 2)
        {
            _points = list;
            NotifyChanged();
        }
    }

    /// <summary>Copia l'aspetto e le caratteristiche (non numero, estremità e lunghezza reale) da un altro filo.</summary>
    public void CopyAppearanceFrom(WireEntity other)
    {
        _color = other._color;
        _color2 = other._color2;
        _section = other._section;
        _cable = other._cable;
        _realistic = other._realistic;
        _diameter = other._diameter;
        _drawnWidth = other._drawnWidth;
        Orthogonal = other.Orthogonal;
    }

    public override string TypeName => "Filo";

    public override string TypeKey => "wire";

    public override string Summary
    {
        get
        {
            string label = string.IsNullOrEmpty(_number) ? "(senza numero)" : _number;
            return $"{label}  {ColorText} {_section}  {Length:0} mm";
        }
    }

    public override double LineWidth => _realistic ? DrawWidth : 0.5;

    private static string? ResolveColor(string name)
    {
        string key = new string(name.Where(char.IsLetter).ToArray());
        return ColorMap.TryGetValue(key, out string? hex) ? hex : null;
    }

    public override string? StrokeColor => ResolveColor(_color);

    public override string? StrokeColor2
    {
        get
        {
            string? second = ResolveColor(_color2);
            return second != null && second != StrokeColor ? second : null;
        }
    }

    /// <summary>Colore per tabelle ed etichette: "Rosso" oppure "Giallo/Verde" per i fili bicolore.</summary>
    public string ColorText
    {
        get
        {
            string first = _color.Trim();
            string second = _color2.Trim();
            if (second.Length == 0 || string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            {
                return first;
            }
            return first.Length == 0 ? second : first + "/" + second;
        }
    }

    public static bool Near(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) < Eps && Math.Abs(a.Y - b.Y) < Eps;
    }

    public static bool Near(Point a, Point b, double tolerance)
    {
        return Math.Abs(a.X - b.X) < tolerance && Math.Abs(a.Y - b.Y) < tolerance;
    }

    public static double DistanceToSegment(Point point, Point a, Point b)
    {
        Vector ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-12)
        {
            return (point - a).Length;
        }
        double t = Math.Clamp(((point - a) * ab) / lengthSquared, 0, 1);
        Point projection = a + ab * t;
        return (point - projection).Length;
    }

    public static Geometry CreatePolyline(IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            Point only = points.Count == 1 ? points[0] : new Point();
            LineGeometry single = new(only, only);
            single.Freeze();
            return single;
        }
        PathFigure figure = new() { StartPoint = points[0], IsClosed = false, IsFilled = false };
        List<Point> rest = new();
        for (int i = 1; i < points.Count; i++)
        {
            rest.Add(points[i]);
        }
        figure.Segments.Add(new PolyLineSegment(rest, true));
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    public bool PassesThrough(Point point, double tolerance = 0.05)
    {
        for (int i = 0; i + 1 < _points.Count; i++)
        {
            if (DistanceToSegment(point, _points[i], _points[i + 1]) <= tolerance)
            {
                return true;
            }
        }
        return false;
    }

    public void SetStart(Guid? component, int pin)
    {
        StartComp = component;
        StartPin = pin;
    }

    public void SetEnd(Guid? component, int pin)
    {
        EndComp = component;
        EndPin = pin;
    }

    /// <summary>Stacca gli agganci verso entità non presenti in <paramref name="keep"/>.</summary>
    public void DetachFrom(HashSet<Guid> keep)
    {
        if (StartComp.HasValue && !keep.Contains(StartComp.Value))
        {
            StartComp = null;
        }
        if (EndComp.HasValue && !keep.Contains(EndComp.Value))
        {
            EndComp = null;
        }
    }

    public void ClearReferencesTo(Guid id)
    {
        if (StartComp == id)
        {
            StartComp = null;
        }
        if (EndComp == id)
        {
            EndComp = null;
        }
    }

    /// <summary>
    /// Riallinea le estremità ai pin a cui il filo è agganciato (il filo segue i componenti spostati).
    /// </summary>
    public bool SyncEndpoints(Func<Guid, int, Point?> resolve)
    {
        bool changed = false;
        if (StartComp.HasValue)
        {
            Point? target = resolve(StartComp.Value, StartPin);
            if (target == null)
            {
                StartComp = null;
            }
            else if (!Near(target.Value, _points[0]))
            {
                MovePoint(0, target.Value);
                changed = true;
            }
        }
        if (EndComp.HasValue)
        {
            int last = _points.Count - 1;
            Point? target = resolve(EndComp.Value, EndPin);
            if (target == null)
            {
                EndComp = null;
            }
            else if (!Near(target.Value, _points[last]))
            {
                MovePoint(last, target.Value);
                changed = true;
            }
        }
        if (changed)
        {
            Normalize();
        }
        return changed;
    }

    /// <summary>
    /// Sposta un vertice mantenendo l'ortogonalità: i vertici interni adiacenti scorrono lungo l'asse perpendicolare.
    /// Le estremità non vengono mai spostate implicitamente.
    /// </summary>
    private void MovePoint(int index, Point target)
    {
        Point old = _points[index];
        int[] neighbours = { index - 1, index + 1 };
        foreach (int n in neighbours)
        {
            if (!Orthogonal || n <= 0 || n >= _points.Count - 1)
            {
                continue;
            }
            Point q = _points[n];
            bool horizontal = Math.Abs(q.Y - old.Y) < Eps;
            bool vertical = Math.Abs(q.X - old.X) < Eps;
            if (horizontal && !vertical)
            {
                _points[n] = new Point(q.X, target.Y);
            }
            else if (vertical && !horizontal)
            {
                _points[n] = new Point(target.X, q.Y);
            }
        }
        _points[index] = target;
    }

    /// <summary>Elimina punti duplicati e collineari e inserisce gomiti dove il tracciato è diagonale.</summary>
    public void Normalize()
    {
        List<Point> unique = new();
        foreach (Point point in _points)
        {
            if (unique.Count == 0 || !Near(unique[unique.Count - 1], point))
            {
                unique.Add(point);
            }
        }
        if (unique.Count < 2)
        {
            unique = new List<Point> { _points[0], _points[_points.Count - 1] };
        }
        if (!Orthogonal)
        {
            _points = unique;
            NotifyChanged();
            return;
        }

        List<Point> orthogonal = new() { unique[0] };
        for (int i = 1; i < unique.Count; i++)
        {
            Point previous = orthogonal[orthogonal.Count - 1];
            Point next = unique[i];
            bool alignedX = Math.Abs(previous.X - next.X) < Eps;
            bool alignedY = Math.Abs(previous.Y - next.Y) < Eps;
            if (!alignedX && !alignedY)
            {
                orthogonal.Add(new Point(next.X, previous.Y));
            }
            orthogonal.Add(next);
        }

        List<Point> result = new() { orthogonal[0] };
        for (int i = 1; i < orthogonal.Count - 1; i++)
        {
            Point previous = result[result.Count - 1];
            Point current = orthogonal[i];
            Point next = orthogonal[i + 1];
            bool sameColumn = Math.Abs(previous.X - current.X) < Eps && Math.Abs(current.X - next.X) < Eps;
            bool sameRow = Math.Abs(previous.Y - current.Y) < Eps && Math.Abs(current.Y - next.Y) < Eps;
            if (sameColumn || sameRow)
            {
                continue;
            }
            result.Add(current);
        }
        result.Add(orthogonal[orthogonal.Count - 1]);

        _points = result;
        NotifyChanged();
    }

    public override Geometry BuildGeometry()
    {
        return CreatePolyline(_points);
    }

    public override IReadOnlyList<EntityLabel> GetLabels()
    {
        List<EntityLabel> labels = new();
        if (!ShowLabel || string.IsNullOrWhiteSpace(_number) || _points.Count < 2)
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
        List<string> parts = new() { _number };
        if (!string.IsNullOrWhiteSpace(ColorText))
        {
            parts.Add(ColorText);
        }
        if (!string.IsNullOrWhiteSpace(_section))
        {
            parts.Add(_section.Trim() + " mm²");
        }
        parts.Add($"{EffectiveLength:0} mm");
        string text = string.Join("  ", parts);
        double radius = _realistic ? DrawWidth / 2 : 0;
        if (Math.Abs(a.Y - b.Y) < Eps)
        {
            labels.Add(new EntityLabel(text, new Point(mid.X, mid.Y - 0.5 - radius), 2.2, 0.5, 1));
        }
        else
        {
            labels.Add(new EntityLabel(text, new Point(mid.X + 0.8 + radius, mid.Y), 2.2, 0, 0.5));
        }
        return labels;
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        List<EntityField> fields = new();
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
        fields.Add(new EntityField("Diametro reale", () => Diameter, v => Diameter = v));
        fields.Add(new EntityField("Larghezza nel disegno (0 = auto)", () => DrawnWidth, v => DrawnWidth = v));
        return fields;
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[]
        {
            new TextField("Aspetto", () => Realistic ? "Realistico" : "Linea semplice",
                v => Realistic = v.Trim().StartsWith("Real", StringComparison.OrdinalIgnoreCase),
                new[] { "Linea semplice", "Realistico" }),
            new TextField("Numero filo", () => Number, v => Number = v),
            new TextField("Colore 1", () => Color, v => Color = v, ColorChoices),
            new TextField("Colore 2 (a strisce)", () => Color2, v => Color2 = v, SecondColorChoices),
            new TextField("Sezione (mm²)", () => Section, v => Section = v, SectionChoices),
            new TextField("Cavo", () => Cable, v => Cable = v),
            new TextField("Segnale", () => Signal, v => Signal = v),
            new TextField("Capicorda inizio", () => TerminalStart, v => TerminalStart = v),
            new TextField("Capicorda fine", () => TerminalEnd, v => TerminalEnd = v),
            new TextField("Lunghezza reale (mm)", () => RealLength, v => RealLength = v)
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
        if (index < 0 || index >= _points.Count)
        {
            return;
        }
        if (index == 0)
        {
            StartComp = null;
        }
        if (index == _points.Count - 1)
        {
            EndComp = null;
        }
        MovePoint(index, position);
        NotifyChanged();
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        if (index < 0 || index >= _points.Count)
        {
            return;
        }
        _points[index] = position;
        NotifyChanged();
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

    public override void RemapReferences(Func<Guid, Guid?> map)
    {
        if (StartComp.HasValue)
        {
            StartComp = map(StartComp.Value);
        }
        if (EndComp.HasValue)
        {
            EndComp = map(EndComp.Value);
        }
    }

    public override void Export(Dictionary<string, string> data)
    {
        data["number"] = _number;
        data["color"] = _color;
        data["color2"] = _color2;
        data["section"] = _section;
        data["cable"] = _cable;
        data["signal"] = _signal;
        data["termA"] = _terminalStart;
        data["termB"] = _terminalEnd;
        data["real"] = _realLength;
        data["ortho"] = Orthogonal ? "1" : "0";
        data["style"] = _realistic ? "band" : "line";
        data["dia"] = _diameter.ToString("R", CultureInfo.InvariantCulture);
        data["drawn"] = _drawnWidth.ToString("R", CultureInfo.InvariantCulture);
        if (StartComp.HasValue)
        {
            data["sc"] = StartComp.Value.ToString();
            data["sp"] = StartPin.ToString(CultureInfo.InvariantCulture);
        }
        if (EndComp.HasValue)
        {
            data["ec"] = EndComp.Value.ToString();
            data["ep"] = EndPin.ToString(CultureInfo.InvariantCulture);
        }
    }

    public override void Import(Dictionary<string, string> data)
    {
        _number = GetText(data, "number");
        _color = GetText(data, "color");
        _color2 = GetText(data, "color2");
        _section = GetText(data, "section");
        _cable = GetText(data, "cable");
        _signal = GetText(data, "signal");
        _terminalStart = GetText(data, "termA");
        _terminalEnd = GetText(data, "termB");
        _realLength = GetText(data, "real");
        Orthogonal = !data.TryGetValue("ortho", out string? orthoValue) || orthoValue != "0";
        _realistic = GetText(data, "style") == "band";
        _diameter = Math.Clamp(GetDouble(data, "dia", 2), 0.6, 20);
        _drawnWidth = Math.Clamp(GetDouble(data, "drawn", 0), 0, 60);
        StartComp = null;
        EndComp = null;
        if (Guid.TryParse(GetText(data, "sc"), out Guid startId))
        {
            StartComp = startId;
            StartPin = GetInt(data, "sp", 0);
        }
        if (Guid.TryParse(GetText(data, "ec"), out Guid endId))
        {
            EndComp = endId;
            EndPin = GetInt(data, "ep", 0);
        }
    }
}
