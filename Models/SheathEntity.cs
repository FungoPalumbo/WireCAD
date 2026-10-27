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

    public const string TwistedType = "Filo twistato";

    private string _twistColors = "";
    private double _twistPitch;

    /// <summary>Vero se il tracciato rappresenta fili twistati (intrecciati) invece di una guaina.</summary>
    public bool IsTwisted => _type.Contains("twist", StringComparison.OrdinalIgnoreCase);

    /// <summary>Colori dei fili intrecciati, separati da virgole (nomi della tavolozza); vuoto = colori predefiniti.</summary>
    public string TwistColors
    {
        get => _twistColors;
        set => SetField(ref _twistColors, value);
    }

    /// <summary>Passo dell'intreccio (mm) lungo il tracciato; 0 = automatico.</summary>
    public double TwistPitch
    {
        get => _twistPitch;
        set
        {
            _twistPitch = Math.Clamp(value, 0, 1000);
            NotifyChanged();
        }
    }

    public const string FerriteType = "Ferrite";

    private string _rating = "";

    /// <summary>Vero se il tracciato rappresenta una ferrite (corpo rigido scuro con scanalature).</summary>
    public bool IsFerrite => _type.Contains("ferrite", StringComparison.OrdinalIgnoreCase);

    /// <summary>Impedenza o altro valore caratteristico (per le ferriti).</summary>
    public string Rating
    {
        get => _rating;
        set => SetField(ref _rating, value);
    }

    // ---------------------------------------------------------------- guaina annidata in un'altra

    /// <summary>Guaina base lungo il cui tracciato è disposta questa guaina; null se è libera.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Ascissa (mm lungo l'asse della guaina base) dell'inizio e della fine del tratto occupato.</summary>
    public double ParentFrom { get; set; }

    public double ParentTo { get; set; }

    /// <summary>Vero se la guaina segue il tracciato di un'altra (e si sposta con essa).</summary>
    public bool IsAttached => ParentId != null;

    /// <summary>Lunghezza dell'asse (mm), per valutare le ascisse sulla guaina base.</summary>
    public double CenterLength => PathLength(CenterLine());

    /// <summary>Proietta un punto sull'asse: ascissa curvilinea e distanza dall'asse.</summary>
    public bool TryProject(Point point, out double along, out double distance)
    {
        along = 0;
        distance = double.MaxValue;
        IReadOnlyList<Point> center = CenterLine();
        double cumulative = 0;
        for (int i = 0; i + 1 < center.Count; i++)
        {
            Vector segment = center[i + 1] - center[i];
            double length = segment.Length;
            if (length < 1e-12)
            {
                continue;
            }
            double t = Math.Clamp(((point - center[i]) * segment) / (length * length), 0, 1);
            double d = (point - (center[i] + segment * t)).Length;
            if (d < distance)
            {
                distance = d;
                along = cumulative + t * length;
            }
            cumulative += length;
        }
        return distance < double.MaxValue;
    }

    /// <summary>Riallinea il tracciato a quello della guaina base, tra le ascisse memorizzate.</summary>
    public void FollowParent(SheathEntity parent)
    {
        IReadOnlyList<Point> center = parent.CenterLine();
        double total = PathLength(center);
        double from = Math.Clamp(Math.Min(ParentFrom, ParentTo), 0, total);
        double to = Math.Clamp(Math.Max(ParentFrom, ParentTo), 0, total);
        List<Point> sub = SubPath(center, from, to);
        if (sub.Count >= 2)
        {
            _points = sub;
            _bendRadius = 0;
        }
    }

    /// <summary>Crea una guaina disposta lungo il tracciato della guaina base, tra le due ascisse.</summary>
    public static SheathEntity CreateAttached(SheathEntity parent, double from, double to, double widthFactor)
    {
        SheathEntity child = new(new[] { new Point(0, 0), new Point(1, 0) })
        {
            ParentId = parent.Id,
            ParentFrom = Math.Min(from, to),
            ParentTo = Math.Max(from, to),
            BendRadius = 0
        };
        child.DrawnWidth = Math.Max(0.5, parent.DrawWidth * widthFactor);
        child.FollowParent(parent);
        return child;
    }

    public override void RemapReferences(Func<Guid, Guid?> map)
    {
        if (ParentId is Guid id)
        {
            ParentId = map(id);
        }
    }

    /// <summary>Numero di fili intrecciati: quelli della guaina con pin, altrimenti due.</summary>
    public int StrandCount => _wireCount > 0 ? _wireCount : 2;

    /// <summary>Larghezza con cui è disegnato ciascun filo dell'intreccio.</summary>
    public double StrandWidth
    {
        get
        {
            double widest = 0;
            for (int k = 0; k < StrandCount; k++)
            {
                widest = Math.Max(widest, StrandWidthOf(k));
            }
            return widest;
        }
    }

    /// <summary>Spessori (disegnati) dei fili collegati ai pin, calcolati dalla finestra; 0 dove non c'è un filo collegato.</summary>
    public IReadOnlyList<double> AutoStrandWidths { get; set; } = Array.Empty<double>();

    /// <summary>
    /// Spessore del filo indicato: quello del filo collegato al suo pin; senza filo collegato, quello del filo collegato più
    /// spesso, oppure (se non ce ne sono) un valore ricavato dalla larghezza della guaina.
    /// </summary>
    public double StrandWidthOf(int index)
    {
        if (index >= 0 && index < AutoStrandWidths.Count && AutoStrandWidths[index] > 0)
        {
            return AutoStrandWidths[index];
        }
        double known = AutoStrandWidths.Count > 0 ? AutoStrandWidths.Max() : 0;
        return known > 0 ? known : Math.Max(0.3, DrawWidth / (1 + StrandCount / 2.0));
    }

    /// <summary>Distanza dall'asse del filo indicato nel punto di massimo scostamento: fili tondi a contatto fra loro.</summary>
    private double StrandAmplitudeOf(int index)
    {
        int count = StrandCount;
        double width = StrandWidthOf(index);
        // il margine fa sì che i contorni di due fili non si sovrappongano nel punto di massimo scostamento,
        // dove cambia quale dei due sta sopra: così il cambio non si nota
        return count > 1 ? width / (2 * Math.Sin(Math.PI / count)) + Math.Clamp(width * 0.25, 0.15, 0.5) : 0;
    }

    /// <summary>Colore (esadecimale) del filo indicato dell'intreccio.</summary>
    public string StrandColor(int index) => StrandColors(index).Primary;

    /// <summary>
    /// Colori del filo indicato: quelli scelti a mano ("Rosso" oppure "Rosso/Bianco" per un filo bicolore) oppure, se vuoti,
    /// quelli del filo collegato al pin corrispondente; senza fili collegati il filo è bianco.
    /// </summary>
    public (string Primary, string? Secondary) StrandColors(int index)
    {
        string[] names = _twistColors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (index < names.Length)
        {
            string[] parts = names[index].Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string? first = parts.Length > 0 ? ColorPalette.Resolve(parts[0]) : null;
            string? second = parts.Length > 1 ? ColorPalette.Resolve(parts[1]) : null;
            if (first != null)
            {
                return (first, second);
            }
        }
        string? connected = index < AutoStrandColors.Count ? AutoStrandColors[index] : null;
        string? connectedSecond = index < AutoStrandColors2.Count ? AutoStrandColors2[index] : null;
        return connected == null ? ("#FFFFFF", null) : (connected, connectedSecond);
    }

    /// <summary>Colori dei fili collegati ai pin (calcolati dalla finestra, non salvati); null dove non c'è un filo colorato.</summary>
    public IReadOnlyList<string?> AutoStrandColors { get; set; } = Array.Empty<string?>();

    /// <summary>Secondo colore dei fili collegati ai pin, per quelli bicolore.</summary>
    public IReadOnlyList<string?> AutoStrandColors2 { get; set; } = Array.Empty<string?>();

    /// <summary>Strato dell'aspetto di una ferrite: poligono, colore di riempimento e opacità.</summary>
    public sealed record FerriteLayer(List<Point> Polygon, string Fill, double Opacity);

    private static List<Point> Band(IReadOnlyList<Point> center, double from, double to)
    {
        List<Point> upper = SceneBuilder.OffsetPolyline(center, to);
        List<Point> lower = SceneBuilder.OffsetPolyline(center, from);
        lower.Reverse();
        upper.AddRange(lower);
        return upper;
    }

    private static string ScaleColor(string hex, double factor)
    {
        string value = hex.TrimStart('#');
        int rgb = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int r = Math.Clamp((int)Math.Round(((rgb >> 16) & 255) * factor), 0, 255);
        int g = Math.Clamp((int)Math.Round(((rgb >> 8) & 255) * factor), 0, 255);
        int b = Math.Clamp((int)Math.Round((rgb & 255) * factor), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>
    /// Aspetto di una ferrite cilindrica vista di lato: il corpo è diviso in strisce parallele all'asse, più chiare dove la
    /// luce (da un lato) colpisce il cilindro e più scure verso il bordo, con un riflesso lucido, le smussature alle due
    /// estremità e le sottili linee di separazione. Segue il tracciato anche se curvo. Vuoto se non è una ferrite.
    /// </summary>
    public List<FerriteLayer> FerriteLayers()
    {
        List<FerriteLayer> layers = new();
        IReadOnlyList<Point> center = CenterLine();
        if (!IsFerrite || center.Count < 2)
        {
            return layers;
        }
        double half = DrawWidth / 2;
        double length = PathLength(center);
        // grigio-nero opaco dei nuclei in ferrite; con un colore scelto a mano il corpo prende quella tinta
        string baseHex = _color == "Nero" || ColorPalette.Resolve(_color) is not string chosen ? "#4B5563" : chosen;

        // la luce arriva da in alto a sinistra: il lato illuminato dipende dalla direzione del fascio
        Vector direction = center[center.Count - 1] - center[0];
        Vector normal = new(-direction.Y, direction.X);
        double lightSide = normal.X + normal.Y >= 0 ? -0.35 : 0.35;
        // sotto le strisce un corpo pieno: nasconde le sottili righe che l'antialiasing lascia tra strisce vicine
        layers.Add(new FerriteLayer(Band(center, -half, half), ScaleColor(baseHex, 0.7), 1));
        const int bands = 13;
        double lightAngle = Math.Asin(lightSide);
        for (int i = 0; i < bands; i++)
        {
            double from = -1.0 + 2.0 * i / bands;
            double to = -1.0 + 2.0 * (i + 1) / bands;
            double lambert = Math.Max(0, Math.Cos(Math.Asin((from + to) / 2) - lightAngle));
            layers.Add(new FerriteLayer(Band(center, from * half, to * half), ScaleColor(baseHex, 0.35 + 0.95 * lambert), 1));
        }

        double cap = Math.Min(Math.Max(DrawWidth * 0.2, 0.8), length / 4);
        double seam = Math.Max(0.25, DrawWidth * 0.035);
        foreach ((double a, double b) in new[] { (0.0, cap), (length - cap, length) })
        {
            List<Point> end = SubPath(center, a, b);
            if (end.Count >= 2)
            {
                // estremità smussata: leggermente più chiara del corpo
                layers.Add(new FerriteLayer(Band(end, -half, half), "#FFFFFF", 0.14));
            }
        }
        foreach (double at in new[] { cap, length - cap })
        {
            List<Point> joint = SubPath(center, at - seam / 2, at + seam / 2);
            if (joint.Count >= 2)
            {
                layers.Add(new FerriteLayer(Band(joint, -half, half), "#000000", 0.5));
            }
        }
        // riflesso lucido lungo il cilindro
        layers.Add(new FerriteLayer(Band(center, (lightSide - 0.08) * half, (lightSide + 0.08) * half), "#FFFFFF", 0.22));
        return layers;
    }

    /// <summary>
    /// Forma di un filo dell'intreccio (o di un suo tratto) come poligoni chiusi: il contorno scuro, il corpo colorato e le
    /// eventuali strisce del secondo colore. <see cref="Rank"/> è la posizione in profondità (0 = il più lontano).
    /// </summary>
    public sealed record TwistShape(int Strand, int Rank, List<List<Point>> Edges, List<Point> Body, List<List<Point>> Stripes);

    /// <summary>Intreccio: i fili interi e i loro tratti, ordinati dal rango più basso al più alto.</summary>
    public sealed record TwistLayout(List<TwistShape> Strands, List<TwistShape> Chunks);

    private static List<Point> Ribbon(Point[] left, Point[] right, int from, int to)
    {
        List<Point> polygon = new(2 * (to - from + 1));
        for (int i = from; i <= to; i++)
        {
            polygon.Add(left[i]);
        }
        for (int i = to; i >= from; i--)
        {
            polygon.Add(right[i]);
        }
        return polygon;
    }

    private static Point Interpolate(Point[] points, double[] distance, double at)
    {
        double step = distance[1] - distance[0];
        int i = Math.Clamp((int)Math.Floor((at - distance[0]) / step), 0, points.Length - 2);
        double t = Math.Clamp((at - distance[i]) / step, 0, 1);
        return points[i] + (points[i + 1] - points[i]) * t;
    }

    /// <summary>Parte del nastro compresa tra due distanze lungo il filo; null se troppo corta.</summary>
    private static List<Point>? RibbonSlice(Point[] left, Point[] right, double[] distance, double from, double to)
    {
        from = Math.Max(from, distance[0]);
        to = Math.Min(to, distance[distance.Length - 1]);
        if (to - from < 1e-6)
        {
            return null;
        }
        List<Point> l = new() { Interpolate(left, distance, from) };
        List<Point> r = new() { Interpolate(right, distance, from) };
        for (int i = 0; i < distance.Length; i++)
        {
            if (distance[i] > from + 1e-9 && distance[i] < to - 1e-9)
            {
                l.Add(left[i]);
                r.Add(right[i]);
            }
        }
        l.Add(Interpolate(left, distance, to));
        r.Add(Interpolate(right, distance, to));
        r.Reverse();
        l.AddRange(r);
        return l;
    }

    /// <summary>
    /// Fili dell'intreccio (sinusoidi sfasate lungo l'asse) come poligoni. Disegnando prima i fili interi e poi i tratti, dal
    /// rango più basso al più alto, ai punti d'incrocio un filo passa sopra e l'altro sotto, in alternanza, come in un
    /// intreccio vero. I tratti hanno i bordi in comune e sotto c'è il filo intero: le giunzioni non si vedono.
    /// <paramref name="extraEdge"/> allarga il contorno (per l'evidenziazione della selezione).
    /// Alle estremità i fili tornano alla posizione dei pin, per raccordarsi ai fili collegati. Vuoto se non è twistato.
    /// </summary>
    public TwistLayout BuildTwist(double extraEdge = 0)
    {
        List<TwistShape> strandShapes = new();
        List<TwistShape> chunks = new();
        TwistLayout empty = new(strandShapes, chunks);
        IReadOnlyList<Point> center = CenterLine();
        if (!IsTwisted || center.Count < 2)
        {
            return empty;
        }
        PathSampler sampler = new(center);
        double length = sampler.Length;
        if (length < 1)
        {
            return empty;
        }
        int count = StrandCount;
        double pitch = _twistPitch > 0 ? _twistPitch : Math.Max(StrandWidth * 8, 6);
        double ramp = Math.Min(pitch, length / 3);
        int steps = (int)Math.Clamp(Math.Ceiling(length / (pitch / 16)), 2, 1500);
        Point[][] lines = new Point[count][];
        double[][] depths = new double[count][];
        double[] distance = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            distance[i] = length * i / steps;
        }
        for (int k = 0; k < count; k++)
        {
            double amplitude = StrandAmplitudeOf(k);
            double endOffset = _wireCount > 0
                ? PinOffset(k)
                : count > 1 ? -amplitude + 2 * amplitude * k / (count - 1) : 0;
            lines[k] = new Point[steps + 1];
            depths[k] = new double[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                double s = distance[i];
                double t = Math.Clamp(Math.Min(s, length - s) / ramp, 0, 1);
                double blend = t * t * (3 - 2 * t);
                double phase = 2 * Math.PI * s / pitch + 2 * Math.PI * k / count;
                lines[k][i] = sampler.At(s, endOffset * (1 - blend) + amplitude * Math.Sin(phase) * blend);
                // nei tratti di raccordo alle estremità l'ordine sopra/sotto resta quello fissato all'inizio dell'intreccio:
                // lì i fili sono vicini e un cambio d'ordine si vedrebbe come un taglio
                double settled = Math.Clamp(s, ramp, length - ramp);
                depths[k][i] = Math.Cos(2 * Math.PI * settled / pitch + 2 * Math.PI * k / count);
            }
        }

        // bordi del nastro di ogni filo: le stesse coppie di punti sono usate da tutti i tratti vicini, così combaciano
        Point[][] bodyLeft = new Point[count][];
        Point[][] bodyRight = new Point[count][];
        Point[][] edgeLeft = new Point[count][];
        Point[][] edgeRight = new Point[count][];
        string?[] secondary = new string?[count];
        double[] stripeLength = new double[count];
        for (int k = 0; k < count; k++)
        {
            double width = StrandWidthOf(k);
            double bodyHalf = width / 2;
            double edgeHalf = (width + Math.Clamp(width * 0.25, 0.15, 0.5) + extraEdge) / 2;
            secondary[k] = StrandColors(k).Secondary;
            stripeLength[k] = Math.Max(width * 3, 2);
            bodyLeft[k] = new Point[steps + 1];
            bodyRight[k] = new Point[steps + 1];
            edgeLeft[k] = new Point[steps + 1];
            edgeRight[k] = new Point[steps + 1];
            Vector normal = new(0, 1);
            for (int i = 0; i <= steps; i++)
            {
                Vector direction = lines[k][Math.Min(i + 1, steps)] - lines[k][Math.Max(i - 1, 0)];
                if (direction.Length > 1e-9)
                {
                    direction.Normalize();
                    normal = new Vector(-direction.Y, direction.X);
                }
                bodyLeft[k][i] = lines[k][i] + normal * bodyHalf;
                bodyRight[k][i] = lines[k][i] - normal * bodyHalf;
                edgeLeft[k][i] = lines[k][i] + normal * edgeHalf;
                edgeRight[k][i] = lines[k][i] - normal * edgeHalf;
            }
        }

        TwistShape MakeShape(int k, int from, int to, int rank)
        {
            List<List<Point>> stripes = new();
            if (secondary[k] != null)
            {
                double step = stripeLength[k];
                for (int m = (int)Math.Floor(distance[from] / step); m * step < distance[to]; m++)
                {
                    if (m % 2 != 0 && RibbonSlice(bodyLeft[k], bodyRight[k], distance, Math.Max(m * step, distance[from]),
                        Math.Min((m + 1) * step, distance[to])) is List<Point> stripe)
                    {
                        stripes.Add(stripe);
                    }
                }
            }
            // il contorno è fatto di due strisce ai lati del corpo (non di un nastro pieno sotto di esso): così le giunzioni tra
            // tratti vicini mostrano il corpo del filo intero sottostante e non una riga scura
            List<List<Point>> edges = new()
            {
                Ribbon(edgeLeft[k], bodyLeft[k], from, to),
                Ribbon(bodyRight[k], edgeRight[k], from, to)
            };
            return new TwistShape(k, rank, edges, Ribbon(bodyLeft[k], bodyRight[k], from, to), stripes);
        }

        for (int k = 0; k < count; k++)
        {
            strandShapes.Add(MakeShape(k, 0, steps, 0));
        }

        // rango (posizione in profondità) di ogni filo in ogni tratto tra due campioni
        int[][] ranks = new int[count][];
        for (int k = 0; k < count; k++)
        {
            ranks[k] = new int[steps];
        }
        for (int j = 0; j < steps; j++)
        {
            int segment = j;
            // a pari profondità (estremità) resta l'ordine dei fili
            int[] order = Enumerable.Range(0, count)
                .OrderBy(strand => depths[strand][segment] + depths[strand][segment + 1])
                .ThenBy(strand => strand)
                .ToArray();
            for (int position = 0; position < count; position++)
            {
                ranks[order[position]][j] = position;
            }
        }

        for (int k = 0; k < count; k++)
        {
            int first = 0;
            for (int j = 1; j <= steps; j++)
            {
                if (j < steps && ranks[k][j] == ranks[k][first])
                {
                    continue;
                }
                chunks.Add(MakeShape(k, first, j, ranks[k][first]));
                first = j;
            }
        }
        return new TwistLayout(strandShapes, chunks.OrderBy(chunk => chunk.Rank).ToList());
    }

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

    public override string TypeName => "Fascio";

    public override string TypeKey => "sheath";

    public override string Summary => $"{_tag} – {_type} Ø{_diameter:0.#} – {ContainedCount} fili";

    public override double LineWidth => 0.35;

    /// <summary>I fili twistati non hanno rettangolo esterno: si vedono (e si evidenziano) solo i fili.</summary>
    public override bool HasOutline => !IsTwisted;

    public override string? FillHex =>
        IsTwisted || IsFerrite ? null : IsFerrite && (string.IsNullOrEmpty(_color) || _color == "Nero" || ColorPalette.Resolve(_color) == null)
            ? "#6B7280"
            : ColorPalette.Resolve(_color) ?? "#374151";

    // ---------------------------------------------------------------- pin della calza

    /// <summary>La sguainatura con calza scoperta riguarda solo le guaine vere: non i fili twistati né le ferriti.</summary>
    private bool CanStrip => !IsTwisted && !IsFerrite;

    private bool ShieldAtStart => CanStrip && _innerLayer == InnerCalza && _stripStart > 0;

    private bool ShieldAtEnd => CanStrip && _innerLayer == InnerCalza && _stripEnd > 0;

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
        if (_wireCount <= 0 || IsTwisted)
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
        if (_wireCount <= 0 || IsTwisted || center.Count < 2)
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
            if (IsFerrite)
            {
                return 0.9;
            }
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
        if (!CanStrip)
        {
            return (0, 0);
        }
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
        if (full.Count < 2 || !CanStrip || (_stripStart <= 0 && _stripEnd <= 0))
        {
            return full;
        }
        double total = PathLength(full);
        (double start, double end) = StripLengths(total);
        List<Point> outer = SubPath(full, start, total - end);
        return outer.Count >= 2 ? outer : full;
    }

    private bool HasInnerLayer =>
        CanStrip && (_innerLayer == InnerCalza || _innerLayer == InnerTube) && (_stripStart > 0 || _stripEnd > 0);

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
        if (IsFerrite)
        {
            // l'aspetto della ferrite è disegnato a parte (FerriteLayers)
            return;
        }
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
        string strip = CanStrip && (_stripStart > 0 || _stripEnd > 0) ? $"  sguain. {_stripStart:0.#}/{_stripEnd:0.#}" : string.Empty;
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
        // le proprietà dipendono dal tipo: sguainatura e calza solo per le guaine, il passo solo per i twistati
        List<EntityField> fields = new()
        {
            new EntityField("Diametro reale", () => Diameter, v => Diameter = v),
            new EntityField("Larghezza nel disegno (0 = auto)", () => DrawnWidth, v => DrawnWidth = v),
            new EntityField("Raggio curve", () => BendRadius, v => BendRadius = v)
        };
        if (CanStrip)
        {
            fields.Add(new EntityField("Sguainatura inizio", () => StripStart, v => StripStart = v));
            fields.Add(new EntityField("Sguainatura fine", () => StripEnd, v => StripEnd = v));
            fields.Add(new EntityField("Larghezza calza (% fascio)", () => InnerPercent, v => InnerPercent = v));
        }
        if (IsTwisted)
        {
            fields.Add(new EntityField("Passo twistato (0 = auto)", () => TwistPitch, v => TwistPitch = v));
        }
        fields.Add(new EntityField("Fili (ingressi = uscite)", () => WireCount, v => WireCount = (int)Math.Round(v)));
        if (IsAttached)
        {
            // il tracciato è quello della guaina base: si regola solo il tratto occupato
            fields.Add(new EntityField("Inizio sul fascio base (mm)", () => ParentFrom, v =>
            {
                ParentFrom = Math.Max(0, v);
                NotifyChanged();
            }));
            fields.Add(new EntityField("Fine sul fascio base (mm)", () => ParentTo, v =>
            {
                ParentTo = Math.Max(0, v);
                NotifyChanged();
            }));
            return fields;
        }
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
        List<TextField> fields = new()
        {
            new TextField("Tag", () => Tag, v => Tag = v),
            new TextField("Tipo", () => SheathType, v => SheathType = v,
                new[] { "Guaina corrugata", "Guaina termorestringente", "Treccia", "Nastro", "Tubo", TwistedType, FerriteType })
        };
        if (IsFerrite)
        {
            fields.Add(new TextField("Impedenza", () => Rating, v => Rating = v));
        }
        if (IsTwisted)
        {
            fields.Add(new TextField("Colori fili twistati (vuoto = come i fili collegati)", () => TwistColors, v => TwistColors = v));
        }
        else
        {
            fields.Add(new TextField("Colore", () => Color, v => Color = v, ColorPalette.Choices));
        }
        if (CanStrip)
        {
            fields.Add(new TextField("Strato interno scoperto", () => InnerLayer, v => InnerLayer = v,
                new[] { InnerNone, InnerCalza, InnerTube }));
            fields.Add(new TextField("Colore strato interno", () => InnerColor, v => InnerColor = v, ColorPalette.Choices));
        }
        if (!IsTwisted)
        {
            fields.Add(new TextField("Fili all'interno", () => Inside, v => Inside = v,
                new[] { InsideVisible, InsideFaded, InsideHidden }));
        }
        fields.Add(new TextField("Codice articolo", () => PartNumber, v => PartNumber = v));
        fields.Add(new TextField("Costruttore", () => Manufacturer, v => Manufacturer = v));
        fields.Add(new TextField("Lunghezza reale (mm)", () => RealLength, v => RealLength = v));
        fields.Add(new TextField("Fili contenuti", () => ContainedWires, _ => { }));
        return fields;
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return _points.ToArray();
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return IsAttached ? Array.Empty<Point>() : _points.ToArray();
    }

    public override void MoveGrip(int index, Point position)
    {
        if (!IsAttached && index >= 0 && index < _points.Count)
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
        if (IsAttached)
        {
            return;
        }
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
        if (IsAttached || _points.Count <= 2)
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
        if (IsAttached)
        {
            // si sposta insieme alla guaina base
            return;
        }
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] += offset;
        }
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        if (IsAttached)
        {
            return;
        }
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
        data["twistColors"] = _twistColors;
        data["twistPitch"] = _twistPitch.ToString("R", CultureInfo.InvariantCulture);
        data["rating"] = _rating;
        if (ParentId is Guid parentId)
        {
            data["parent"] = parentId.ToString();
            data["pfrom"] = ParentFrom.ToString("R", CultureInfo.InvariantCulture);
            data["pto"] = ParentTo.ToString("R", CultureInfo.InvariantCulture);
        }
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
        _twistColors = GetText(data, "twistColors");
        _twistPitch = Math.Clamp(GetDouble(data, "twistPitch", 0), 0, 1000);
        _rating = GetText(data, "rating");
        ParentId = Guid.TryParse(GetText(data, "parent"), out Guid parentGuid) ? parentGuid : null;
        ParentFrom = Math.Max(0, GetDouble(data, "pfrom", 0));
        ParentTo = Math.Max(0, GetDouble(data, "pto", 0));
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
    public static List<bool> UsedLinks(SheathEntity sheath, IEnumerable<WireEntity> wires, IEnumerable<SheathEntity> sheaths)
    {
        List<WireEntity> list = wires as List<WireEntity> ?? wires.ToList();
        List<bool> used = UsedLinks(sheath, list);
        List<SheathEntity> children = sheaths.Where(item => item.ParentId == sheath.Id && item.WireCount > 0).ToList();
        if (children.Count == 0)
        {
            return used;
        }
        List<Point> childPins = new();
        foreach (SheathEntity child in children)
        {
            for (int i = 0; i < child.PinCount; i++)
            {
                childPins.Add(child.GetPinPosition(i));
            }
        }
        for (int k = 0; k < used.Count; k++)
        {
            if (!used[k])
            {
                continue;
            }
            Point entry = sheath.GetPinPosition(k);
            Point exit = sheath.GetPinPosition(sheath.WireCount + k);
            // il collegamento è sostituito dalla guaina annidata se un filo lo congiunge a un suo pin
            bool carried = list.Any(wire =>
                (WireEntity.Near(entry, wire.StartPoint, 0.01) && childPins.Any(pin => WireEntity.Near(pin, wire.EndPoint, 0.01)))
                || (WireEntity.Near(entry, wire.EndPoint, 0.01) && childPins.Any(pin => WireEntity.Near(pin, wire.StartPoint, 0.01)))
                || (WireEntity.Near(exit, wire.StartPoint, 0.01) && childPins.Any(pin => WireEntity.Near(pin, wire.EndPoint, 0.01)))
                || (WireEntity.Near(exit, wire.EndPoint, 0.01) && childPins.Any(pin => WireEntity.Near(pin, wire.StartPoint, 0.01))));
            if (carried)
            {
                used[k] = false;
            }
        }
        return used;
    }

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
