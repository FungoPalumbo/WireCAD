using System.Text.RegularExpressions;
using System.Windows;
using ACadSharp;
using ACadSharp.IO;
using Cad = ACadSharp.Entities;

namespace WireCAD.Models;

public sealed record CadImportResult(List<EntityBase> Entities, int Skipped);

/// <summary>
/// Importazione di disegni DXF e DWG: le entità CAD sono convertite nelle primitive di WireCAD
/// (linee, archi, cerchi, spline, testi). L'asse Y è ribaltato (WireCAD ha Y verso il basso).
/// </summary>
public static class CadImporter
{
    public const string Filter =
        "Disegni CAD (*.dxf;*.dwg)|*.dxf;*.dwg|DXF (*.dxf)|*.dxf|DWG (*.dwg)|*.dwg|Tutti i file (*.*)|*.*";

    private const int MaxBlockDepth = 8;

    private sealed class Context
    {
        public Context(double scale)
        {
            Scale = scale;
        }

        public double Scale { get; }

        public List<EntityBase> Entities { get; } = new();

        public int Skipped { get; set; }

        public Point P(double x, double y)
        {
            return new Point(x * Scale, -y * Scale);
        }

        public Point P(CSMath.XYZ point)
        {
            return P(point.X, point.Y);
        }

        public void Add(EntityBase entity)
        {
            Entities.Add(entity);
        }
    }

    public static CadImportResult Import(string path, double unitScale)
    {
        CadDocument document = path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)
            ? DwgReader.Read(path)
            : DxfReader.Read(path);
        Context context = new(unitScale);
        foreach (Cad.Entity entity in document.Entities)
        {
            Convert(entity, context, 0);
        }
        return new CadImportResult(context.Entities, context.Skipped);
    }

    private static void Convert(Cad.Entity entity, Context context, int depth)
    {
        switch (entity)
        {
            case Cad.Line line:
                context.Add(new LineEntity { A = context.P(line.StartPoint), B = context.P(line.EndPoint) });
                break;
            case Cad.Arc arc:
                AddArc(arc, context);
                break;
            case Cad.Circle circle:
                {
                    double sign = circle.Normal.Z < 0 ? -1 : 1;
                    context.Add(new CircleEntity
                    {
                        Center = context.P(sign * circle.Center.X, circle.Center.Y),
                        Radius = Math.Max(0.01, circle.Radius * context.Scale)
                    });
                    break;
                }
            case Cad.Ellipse ellipse:
                AddEllipse(ellipse, context);
                break;
            case Cad.LwPolyline lw:
                AddLwPolyline(lw, context);
                break;
            case Cad.Polyline2D polyline:
                AddPolyline2D(polyline, context);
                break;
            case Cad.Spline spline:
                AddSampled(spline.PolygonalVertexes(40), spline.IsClosed, context);
                break;
            case Cad.TextEntity text:
                AddText(text.Value, text.InsertPoint, text.Height, context);
                break;
            case Cad.MText mtext:
                AddText(CleanMText(mtext.Value), mtext.InsertPoint, mtext.Height, context);
                break;
            case Cad.Insert insert:
                if (depth < MaxBlockDepth)
                {
                    foreach (Cad.Entity child in insert.Explode())
                    {
                        Convert(child, context, depth + 1);
                    }
                }
                else
                {
                    context.Skipped++;
                }
                break;
            default:
                context.Skipped++;
                break;
        }
    }

    private static void AddArc(Cad.Arc arc, Context context)
    {
        double sign = arc.Normal.Z < 0 ? -1 : 1;
        double start = arc.StartAngle;
        double end = arc.EndAngle;
        if (end <= start)
        {
            end += 2 * Math.PI;
        }
        double middle = (start + end) / 2;
        double radius = arc.Radius;
        Point Position(double angle)
        {
            return context.P(
                sign * (arc.Center.X + radius * Math.Cos(angle)),
                arc.Center.Y + radius * Math.Sin(angle));
        }
        context.Add(new ArcEntity { Start = Position(start), Through = Position(middle), End = Position(end) });
    }

    private static void AddEllipse(Cad.Ellipse ellipse, Context context)
    {
        if (ellipse.IsFullEllipse && Math.Abs(ellipse.RadiusRatio - 1) < 1e-6)
        {
            context.Add(new CircleEntity
            {
                Center = context.P(ellipse.Center),
                Radius = Math.Max(0.01, ellipse.MajorAxis / 2 * context.Scale)
            });
            return;
        }
        AddSampled(ellipse.PolygonalVertexes(ellipse.IsFullEllipse ? 32 : 20), ellipse.IsFullEllipse, context);
    }

    /// <summary>Curva campionata: diventa una spline che passa per i punti campionati.</summary>
    private static void AddSampled(List<CSMath.XYZ> vertices, bool closed, Context context)
    {
        List<Point> points = new();
        foreach (CSMath.XYZ vertex in vertices)
        {
            Point point = context.P(vertex);
            if (points.Count == 0 || (point - points[points.Count - 1]).Length > 1e-9)
            {
                points.Add(point);
            }
        }
        if (closed && points.Count > 2 && (points[0] - points[points.Count - 1]).Length < 1e-6)
        {
            points.RemoveAt(points.Count - 1);
        }
        if (points.Count >= 2)
        {
            context.Add(new SplineEntity(points) { Closed = closed && points.Count > 2 });
        }
        else
        {
            context.Skipped++;
        }
    }

    /// <summary>Segmento di polilinea con eventuale bulge (tangente dell'angolo/4): linea o arco.</summary>
    private static void AddSegment(double x1, double y1, double x2, double y2, double bulge, Context context)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double chord = Math.Sqrt(dx * dx + dy * dy);
        if (chord < 1e-12)
        {
            return;
        }
        if (Math.Abs(bulge) < 1e-9)
        {
            context.Add(new LineEntity { A = context.P(x1, y1), B = context.P(x2, y2) });
            return;
        }
        double sagitta = bulge * chord / 2;
        double mx = (x1 + x2) / 2 + dy / chord * sagitta;
        double my = (y1 + y2) / 2 - dx / chord * sagitta;
        context.Add(new ArcEntity { Start = context.P(x1, y1), Through = context.P(mx, my), End = context.P(x2, y2) });
    }

    private static void AddLwPolyline(Cad.LwPolyline polyline, Context context)
    {
        List<Cad.LwPolyline.Vertex> vertices = polyline.Vertices;
        for (int i = 0; i + 1 < vertices.Count; i++)
        {
            AddSegment(vertices[i].Location.X, vertices[i].Location.Y,
                vertices[i + 1].Location.X, vertices[i + 1].Location.Y, vertices[i].Bulge, context);
        }
        if (polyline.IsClosed && vertices.Count > 2)
        {
            Cad.LwPolyline.Vertex last = vertices[vertices.Count - 1];
            Cad.LwPolyline.Vertex first = vertices[0];
            AddSegment(last.Location.X, last.Location.Y, first.Location.X, first.Location.Y, last.Bulge, context);
        }
    }

    private static void AddPolyline2D(Cad.Polyline2D polyline, Context context)
    {
        List<Cad.Vertex2D> vertices = polyline.Vertices.ToList();
        for (int i = 0; i + 1 < vertices.Count; i++)
        {
            AddSegment(vertices[i].Location.X, vertices[i].Location.Y,
                vertices[i + 1].Location.X, vertices[i + 1].Location.Y, vertices[i].Bulge, context);
        }
        if (polyline.IsClosed && vertices.Count > 2)
        {
            Cad.Vertex2D last = vertices[vertices.Count - 1];
            Cad.Vertex2D first = vertices[0];
            AddSegment(last.Location.X, last.Location.Y, first.Location.X, first.Location.Y, last.Bulge, context);
        }
    }

    private static void AddText(string? value, CSMath.XYZ insertPoint, double height, Context context)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        double size = Math.Max(0.5, height * context.Scale);
        Point anchor = context.P(insertPoint);
        // il punto di inserimento DXF è sulla linea di base, quello di WireCAD in alto a sinistra
        context.Add(new TextEntity { Position = new Point(anchor.X, anchor.Y - size), Text = value, TextHeight = size });
    }

    private static string CleanMText(string? value)
    {
        string text = value ?? string.Empty;
        text = Regex.Replace(text, @"\\[A-Za-z][^;\\]*;", string.Empty);
        text = text.Replace("\\P", "\n").Replace("{", string.Empty).Replace("}", string.Empty);
        return text.Trim();
    }
}
