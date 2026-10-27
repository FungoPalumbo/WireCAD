using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Polilinea da esportare (coordinate del foglio in mm, Y verso il basso).</summary>
public sealed record ExportPolyline(IReadOnlyList<Point> Points, bool Closed, bool Filled, string Color, double Width, string Layer, string? FillColor = null, double FillOpacity = 1);

/// <summary>Filo realistico: fascia di spessore Diameter lungo il tracciato, con bordo e riempimento (ed eventuali strisce).</summary>
public sealed record ExportBand(IReadOnlyList<Point> Points, double Diameter, string OutlineColor, string FillColor, string? StripeColor, string Layer);

/// <summary>Testo da esportare: (X, Y) è il punto di ancoraggio; AlignX/AlignY (0..1) indicano quale punto del testo coincide con esso.</summary>
public sealed record ExportText(string Text, double X, double Y, double Height, double AlignX, double AlignY, string Layer)
{
    public double EstimatedWidth => Text.Length * Height * 0.52;

    /// <summary>Origine a sinistra sulla linea di base, con Y verso l'alto (sistema di DXF/PDF).</summary>
    public Point BaselineOrigin(double sceneHeight)
    {
        double left = X - EstimatedWidth * AlignX;
        double top = Y - Height * AlignY;
        double baseline = top + Height * 0.8;
        return new Point(left, sceneHeight - baseline);
    }
}

/// <summary>Disegno ridotto a primitive semplici, comune a tutti i formati di esportazione.</summary>
public sealed class ExportScene
{
    public ExportScene(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public double Width { get; }

    public double Height { get; }

    public List<ExportPolyline> Polylines { get; } = new();

    public List<ExportText> Texts { get; } = new();

    public List<ExportBand> Bands { get; } = new();
}

public static class SceneBuilder
{
    private static readonly Dictionary<string, short> AciColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#111827"] = 7, ["#78350F"] = 34, ["#DC2626"] = 1, ["#EA580C"] = 30, ["#CA8A04"] = 2,
        ["#16A34A"] = 3, ["#2563EB"] = 5, ["#7C3AED"] = 200, ["#6B7280"] = 8, ["#9CA3AF"] = 9,
        ["#EC4899"] = 6, ["#0891B2"] = 4, ["#65A30D"] = 82, ["#000000"] = 7
    };

    public const string BandOutlineColor = "#1F2937";
    public const string BandDefaultFill = "#F3F4F6";
    public const double BandBorder = 0.25;

    private static Vector Unit(Vector vector)
    {
        double length = vector.Length;
        return length < 1e-12 ? new Vector(0, 0) : new Vector(vector.X / length, vector.Y / length);
    }

    /// <summary>Polilinea parallela a quella data, spostata di <paramref name="distance"/> (positiva a sinistra rispetto alla direzione), con giunti a spigolo.</summary>
    public static List<Point> OffsetPolyline(IReadOnlyList<Point> points, double distance)
    {
        List<Point> unique = new();
        foreach (Point point in points)
        {
            if (unique.Count == 0 || (point - unique[unique.Count - 1]).Length > 1e-9)
            {
                unique.Add(point);
            }
        }
        List<Point> result = new();
        for (int i = 0; i < unique.Count; i++)
        {
            Vector previous = i > 0 ? Unit(unique[i] - unique[i - 1]) : new Vector(0, 0);
            Vector next = i < unique.Count - 1 ? Unit(unique[i + 1] - unique[i]) : new Vector(0, 0);
            Vector n1 = new(-previous.Y, previous.X);
            Vector n2 = new(-next.Y, next.X);
            Vector offset;
            if (i == 0)
            {
                offset = n2 * distance;
            }
            else if (i == unique.Count - 1)
            {
                offset = n1 * distance;
            }
            else
            {
                double denominator = 1 + n1 * n2;
                offset = denominator < 0.1 ? n1 * distance : (n1 + n2) * (distance / denominator);
            }
            result.Add(unique[i] + offset);
        }
        return result;
    }

    /// <summary>Contorno chiuso dei fili realistici, per i formati senza spessore di linea (DXF, DWG).</summary>
    public static List<ExportPolyline> BandOutlines(ExportScene scene)
    {
        List<ExportPolyline> outlines = new();
        foreach (ExportBand band in scene.Bands)
        {
            if (band.Points.Count < 2)
            {
                continue;
            }
            List<Point> left = OffsetPolyline(band.Points, band.Diameter / 2);
            List<Point> right = OffsetPolyline(band.Points, -band.Diameter / 2);
            right.Reverse();
            left.AddRange(right);
            outlines.Add(new ExportPolyline(left, true, false, band.FillColor, 0.25, band.Layer));
        }
        return outlines;
    }

    /// <summary>Indice colore AutoCAD più vicino a un colore esadecimale (7 = bianco/nero).</summary>
    public static short AciColor(string hex)
    {
        return AciColors.TryGetValue(hex, out short index) ? index : (short)7;
    }

    public static (double R, double G, double B) Rgb(string hex)
    {
        string value = hex.TrimStart('#');
        if (value.Length != 6
            || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return (0, 0, 0);
        }
        return (((rgb >> 16) & 255) / 255.0, ((rgb >> 8) & 255) / 255.0, (rgb & 255) / 255.0);
    }

    private static string LayerOf(EntityBase entity)
    {
        switch (entity)
        {
            case WireEntity:
            case JunctionEntity:
                return "FILI";
            case ComponentEntity:
                return "CONNETTORI";
            case TableEntity:
                return "TABELLE";
            case SheathEntity:
                return "FASCI";
            case TextEntity:
                return "TESTI";
            case DimensionEntity:
                return "QUOTE";
            default:
                return "DISEGNO";
        }
    }

    public static ExportScene Build(
        IEnumerable<EntityBase> entities,
        double width,
        double height,
        bool frame,
        IReadOnlyDictionary<string, string> titleBlock)
    {
        ExportScene scene = new(width, height);
        if (frame)
        {
            AddFrame(scene, titleBlock);
        }
        foreach (EntityBase entity in entities.OrderBy(item => item.SendToBack ? 0 : item is SheathEntity ? 2 : 1))
        {
            if (entity is ImageEntity)
            {
                continue;
            }
            string layer = LayerOf(entity);
            if (entity is TextEntity text)
            {
                string[] lines = text.Text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    scene.Texts.Add(new ExportText(
                        lines[i].TrimEnd('\r'),
                        text.Position.X,
                        text.Position.Y + i * text.TextHeight * 1.25,
                        text.TextHeight, 0, 0, layer));
                }
                continue;
            }

            if (entity is WireEntity { Realistic: true } band)
            {
                scene.Bands.Add(new ExportBand(
                    band.Points.ToList(), band.LineWidth, BandOutlineColor, band.StrokeColor ?? BandDefaultFill, band.StrokeColor2, layer));
            }
            else
            {
                if (entity is ComponentEntity colorful)
                {
                    foreach (var part in colorful.GetColoredParts())
                    {
                        List<Shape> partShapes = new();
                        CollectShapes(part.Geometry, partShapes);
                        foreach (Shape partShape in partShapes)
                        {
                            scene.Polylines.Add(new ExportPolyline(
                                partShape.Points, true, true, part.FillHex, 0.01, layer, part.FillHex, 1));
                        }
                    }
                }
                if (entity is not SheathEntity { IsTwisted: true })
                {
                    AddGeometry(scene, entity, layer);
                }
                if (entity is SheathEntity { IsFerrite: true } ferrite)
                {
                    foreach (SheathEntity.FerriteLayer part in ferrite.FerriteLayers())
                    {
                        scene.Polylines.Add(new ExportPolyline(part.Polygon, true, true, part.Fill, 0.01, layer, part.Fill, part.Opacity));
                    }
                }
                if (entity is SheathEntity { IsTwisted: true } twisted)
                {
                    SheathEntity.TwistLayout layout = twisted.BuildTwist();

                    void AddShape(SheathEntity.TwistShape shape)
                    {
                        (string primary, string? secondary) = twisted.StrandColors(shape.Strand);
                        scene.Polylines.Add(new ExportPolyline(shape.Body, true, true, primary, 0.01, layer, primary, 1));
                        foreach (List<Point> strip in shape.Edges)
                        {
                            scene.Polylines.Add(new ExportPolyline(strip, true, true, "#374151", 0.01, layer, "#374151", 1));
                        }
                        if (secondary != null)
                        {
                            foreach (List<Point> stripe in shape.Stripes)
                            {
                                scene.Polylines.Add(new ExportPolyline(stripe, true, true, secondary, 0.01, layer, secondary, 1));
                            }
                        }
                    }

                    // prima i fili interi, poi i tratti dal più lontano al più vicino (sopra/sotto agli incroci)
                    foreach (SheathEntity.TwistShape shape in layout.Strands)
                    {
                        AddShape(shape);
                    }
                    foreach (SheathEntity.TwistShape shape in layout.Chunks)
                    {
                        AddShape(shape);
                    }
                }
                if (entity is SheathEntity layered && layered.InnerLayerGeometry() is Geometry innerLayer)
                {
                    List<Shape> layerShapes = new();
                    CollectShapes(innerLayer, layerShapes);
                    foreach (Shape layerShape in layerShapes)
                    {
                        scene.Polylines.Add(new ExportPolyline(
                            layerShape.Points, layerShape.Closed, layerShape.Closed && layerShape.CanFill,
                            "#374151", 0.35, layer, layered.InnerFillHex, 1));
                    }
                }
                if (entity is SheathEntity { WireCount: > 0, IsTwisted: false } sheath
                    && sheath.Inside != SheathEntity.InsideHidden)
                {
                    string innerColor = sheath.Inside == SheathEntity.InsideVisible ? "#374151" : "#9CA3AF";
                    foreach (List<Point> line in sheath.InnerLines(SheathAnalyzer.UsedLinks(sheath, entities.OfType<WireEntity>(), entities.OfType<SheathEntity>())))
                    {
                        if (line.Count >= 2)
                        {
                            scene.Polylines.Add(new ExportPolyline(line, false, false, innerColor, 0.4, layer));
                        }
                    }
                }
            }
            foreach (EntityLabel label in entity.GetLabels().Concat(entity.GetPropertyLabels()))
            {
                scene.Texts.Add(new ExportText(label.Text, label.Anchor.X, label.Anchor.Y, label.Height, label.AlignX, label.AlignY, "ETICHETTE"));
            }
        }
        return scene;
    }

    private static void AddGeometry(ExportScene scene, EntityBase entity, string layer)
    {
        string color = entity.StrokeColor ?? "#000000";
        string? second = entity.StrokeColor2;
        List<double>? dash = entity.DashPattern?
            .Select(value => Math.Max(0.05, value * entity.LineWidth))
            .ToList();
        List<Shape> shapes = new();
        CollectShapes(entity.BuildGeometry(), shapes);
        foreach (Shape shape in shapes)
        {
            if (shape.Points.Count < 2)
            {
                continue;
            }
            if (second != null && !shape.Closed)
            {
                AddStriped(scene, shape.Points, color, second, entity.LineWidth, layer);
                continue;
            }
            string? fill = entity.FillHex;
            bool filled = (entity.IsFilled || fill != null) && shape.Closed && shape.CanFill;
            if (dash != null)
            {
                if (filled)
                {
                    // riempimento senza contorno visibile; il contorno è disegnato a tratti
                    scene.Polylines.Add(new ExportPolyline(
                        shape.Points, true, true, fill ?? color, 0.01, layer, fill, entity.FillOpacity));
                }
                AddDashed(scene, shape.Points, shape.Closed, dash, color, entity.LineWidth, layer);
                continue;
            }
            scene.Polylines.Add(new ExportPolyline(
                shape.Points, shape.Closed, filled,
                color, entity.LineWidth, layer, fill, entity.FillOpacity));
        }
    }

    /// <summary>Spezza il tracciato in tratti secondo uno schema (lunghezze in mm: tratto, vuoto, tratto, vuoto...).</summary>
    private static void AddDashed(
        ExportScene scene, List<Point> points, bool closed, IReadOnlyList<double> pattern,
        string color, double width, string layer)
    {
        List<Point> path = new(points);
        if (closed)
        {
            path.Add(points[0]);
        }
        int index = 0;
        double remaining = pattern[0];
        bool on = true;
        List<Point> current = new() { path[0] };
        for (int i = 0; i + 1 < path.Count; i++)
        {
            Point a = path[i];
            Point b = path[i + 1];
            double length = (b - a).Length;
            if (length < 1e-9)
            {
                continue;
            }
            double position = 0;
            while (length - position > remaining)
            {
                position += remaining;
                Point split = a + (b - a) * (position / length);
                if (on)
                {
                    current.Add(split);
                    scene.Polylines.Add(new ExportPolyline(current, false, false, color, width, layer));
                }
                index = (index + 1) % pattern.Count;
                on = !on;
                remaining = pattern[index];
                current = on ? new List<Point> { split } : new List<Point>();
            }
            remaining -= length - position;
            if (on)
            {
                current.Add(b);
            }
        }
        if (on && current.Count >= 2)
        {
            scene.Polylines.Add(new ExportPolyline(current, false, false, color, width, layer));
        }
    }

    private sealed record Shape(List<Point> Points, bool Closed, bool CanFill = true);

    /// <summary>
    /// Riduce una geometria a polilinee. Non usa GetFlattenedPathGeometry sull'intera geometria perché scarta le
    /// figure non riempite (come i fili).
    /// </summary>
    private static void CollectShapes(Geometry geometry, List<Shape> output)
    {
        switch (geometry)
        {
            case LineGeometry line:
                output.Add(new Shape(new List<Point> { line.StartPoint, line.EndPoint }, false));
                break;
            case RectangleGeometry rectangle:
                Rect rect = rectangle.Rect;
                double corner = Math.Min(rectangle.RadiusX, Math.Min(rect.Width, rect.Height) / 2);
                if (corner <= 0)
                {
                    output.Add(new Shape(new List<Point> { rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft }, true));
                    break;
                }
                List<Point> rounded = new();
                AddCorner(rounded, rect.Right - corner, rect.Top + corner, corner, -90);
                AddCorner(rounded, rect.Right - corner, rect.Bottom - corner, corner, 0);
                AddCorner(rounded, rect.Left + corner, rect.Bottom - corner, corner, 90);
                AddCorner(rounded, rect.Left + corner, rect.Top + corner, corner, 180);
                output.Add(new Shape(rounded, true));
                break;
            case EllipseGeometry ellipse:
                List<Point> ring = new();
                const int steps = 48;
                for (int i = 0; i < steps; i++)
                {
                    double angle = 2 * Math.PI * i / steps;
                    ring.Add(new Point(
                        ellipse.Center.X + ellipse.RadiusX * Math.Cos(angle),
                        ellipse.Center.Y + ellipse.RadiusY * Math.Sin(angle)));
                }
                output.Add(new Shape(ring, true));
                break;
            case GeometryGroup group:
                foreach (Geometry child in group.Children)
                {
                    CollectShapes(child, output);
                }
                break;
            case PathGeometry path:
                foreach (PathFigure figure in path.Figures)
                {
                    CollectFigure(figure, output);
                }
                break;
            default:
                PathGeometry flat = geometry.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
                foreach (PathFigure figure in flat.Figures)
                {
                    CollectFigure(figure, output);
                }
                break;
        }
    }

    /// <summary>Aggiunge un quarto di cerchio (angolo arrotondato) a partire dall'angolo indicato, in gradi.</summary>
    private static void AddCorner(List<Point> points, double centerX, double centerY, double radius, double startDegrees)
    {
        const int steps = 8;
        for (int i = 0; i <= steps; i++)
        {
            double angle = (startDegrees + 90.0 * i / steps) * Math.PI / 180.0;
            points.Add(new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)));
        }
    }

    private static void CollectFigure(PathFigure figure, List<Shape> output)
    {
        bool simple = figure.Segments.All(segment => segment is LineSegment || segment is PolyLineSegment);
        if (!simple)
        {
            // Archi e curve: si appiattisce una copia riempita della figura (le figure non riempite verrebbero scartate).
            PathFigure copy = figure.Clone();
            copy.IsFilled = true;
            PathGeometry single = new();
            single.Figures.Add(copy);
            foreach (PathFigure flatFigure in single.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute).Figures)
            {
                CollectFigure(flatFigure, output);
            }
            return;
        }
        List<Point> points = new() { figure.StartPoint };
        foreach (PathSegment segment in figure.Segments)
        {
            if (segment is PolyLineSegment poly)
            {
                points.AddRange(poly.Points);
            }
            else if (segment is LineSegment line)
            {
                points.Add(line.Point);
            }
        }
        output.Add(new Shape(points, figure.IsClosed, figure.IsFilled));
    }

    /// <summary>Suddivide il tracciato in tratti di 2 mm di colore alternato (fili bicolore).</summary>
    private static void AddStriped(ExportScene scene, List<Point> points, string firstColor, string secondColor, double width, string layer)
    {
        const double stripe = 2;
        List<Point> current = new() { points[0] };
        bool first = false; // a schermo il primo tratto è del secondo colore (sovrapposto tratteggiato)
        double remaining = stripe;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Point a = points[i];
            Point b = points[i + 1];
            double length = (b - a).Length;
            if (length < 1e-9)
            {
                continue;
            }
            double position = 0;
            while (length - position > remaining)
            {
                position += remaining;
                Point split = a + (b - a) * (position / length);
                current.Add(split);
                scene.Polylines.Add(new ExportPolyline(current, false, false, first ? firstColor : secondColor, width, layer));
                current = new List<Point> { split };
                first = !first;
                remaining = stripe;
            }
            remaining -= length - position;
            current.Add(b);
        }
        if (current.Count >= 2)
        {
            scene.Polylines.Add(new ExportPolyline(current, false, false, first ? firstColor : secondColor, width, layer));
        }
    }

    private static void AddRectangle(ExportScene scene, double x, double y, double w, double h, double lineWidth)
    {
        scene.Polylines.Add(new ExportPolyline(
            new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) },
            true, false, "#000000", lineWidth, "CORNICE"));
    }

    private static void AddFrame(ExportScene scene, IReadOnlyDictionary<string, string> titleBlock)
    {
        const double margin = 10;
        const double blockWidth = 130;
        const double blockHeight = 30;
        string Get(string key) => titleBlock.TryGetValue(key, out string? value) ? value : string.Empty;

        AddRectangle(scene, margin, margin, scene.Width - 2 * margin, scene.Height - 2 * margin, 0.5);

        double x = scene.Width - margin - blockWidth;
        double y = scene.Height - margin - blockHeight;
        AddRectangle(scene, x, y, blockWidth, blockHeight, 0.35);
        scene.Polylines.Add(new ExportPolyline(new[] { new Point(x, y + 10), new Point(x + blockWidth, y + 10) }, false, false, "#000000", 0.25, "CORNICE"));
        scene.Polylines.Add(new ExportPolyline(new[] { new Point(x, y + 20), new Point(x + blockWidth, y + 20) }, false, false, "#000000", 0.25, "CORNICE"));

        scene.Texts.Add(new ExportText(Get("title"), x + 2, y + 5, 4, 0, 0.5, "CORNICE"));
        scene.Texts.Add(new ExportText(
            $"Disegno {Get("number")}   Rev. {Get("revision")}   Scala {Get("scale")}   Foglio {Get("sheet")}",
            x + 2, y + 15, 2.5, 0, 0.5, "CORNICE"));
        scene.Texts.Add(new ExportText(
            $"{Get("company")}   {Get("author")}   {Get("date")}",
            x + 2, y + 25, 2.5, 0, 0.5, "CORNICE"));
    }
}
