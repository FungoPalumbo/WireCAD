using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

public class ArcEntity : StrokedEntity
{
    private Point _start;
    private Point _through;
    private Point _end;

    public Point Start
    {
        get => _start;
        set
        {
            _start = value;
            NotifyChanged();
        }
    }

    public Point Through
    {
        get => _through;
        set
        {
            _through = value;
            NotifyChanged();
        }
    }

    public Point End
    {
        get => _end;
        set
        {
            _end = value;
            NotifyChanged();
        }
    }

    public override string TypeName => "Arco";

    public override string TypeKey => "arc";

    public override string Summary => $"Inizio {Format(Start)}   Intermedio {Format(Through)}   Fine {Format(End)}";

    public override Geometry BuildGeometry()
    {
        return CreateGeometry(Start, Through, End);
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("Inizio X", () => Start.X, v => Start = new Point(v, Start.Y)),
            new EntityField("Inizio Y", () => Start.Y, v => Start = new Point(Start.X, v)),
            new EntityField("Intermedio X", () => Through.X, v => Through = new Point(v, Through.Y)),
            new EntityField("Intermedio Y", () => Through.Y, v => Through = new Point(Through.X, v)),
            new EntityField("Fine X", () => End.X, v => End = new Point(v, End.Y)),
            new EntityField("Fine Y", () => End.Y, v => End = new Point(End.X, v))
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { Start, Through, End };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return new[] { Start, Through, End };
    }

    public override void MoveGrip(int index, Point position)
    {
        switch (index)
        {
            case 0:
                Start = position;
                break;
            case 1:
                Through = position;
                break;
            case 2:
                End = position;
                break;
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        MoveGrip(index, position);
    }

    public override void Translate(Vector offset)
    {
        _start += offset;
        _through += offset;
        _end += offset;
        NotifyChanged();
    }

    public override void ApplyMatrix(Matrix matrix)
    {
        _start = matrix.Transform(_start);
        _through = matrix.Transform(_through);
        _end = matrix.Transform(_end);
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        _start = Map(kind, center, _start);
        _through = Map(kind, center, _through);
        _end = Map(kind, center, _end);
        NotifyChanged();
    }

    public static Geometry CreateGeometry(Point start, Point through, Point end)
    {
        double d = 2 * (start.X * (through.Y - end.Y) + through.X * (end.Y - start.Y) + end.X * (start.Y - through.Y));
        if (Math.Abs(d) < 1e-9)
        {
            return FrozenLine(start, end);
        }

        double ss = start.X * start.X + start.Y * start.Y;
        double ts = through.X * through.X + through.Y * through.Y;
        double es = end.X * end.X + end.Y * end.Y;
        double cx = (ss * (through.Y - end.Y) + ts * (end.Y - start.Y) + es * (start.Y - through.Y)) / d;
        double cy = (ss * (end.X - through.X) + ts * (start.X - end.X) + es * (through.X - start.X)) / d;
        double r = Math.Sqrt((start.X - cx) * (start.X - cx) + (start.Y - cy) * (start.Y - cy));
        if (r < 1e-9)
        {
            return FrozenLine(start, end);
        }

        double a0 = Math.Atan2(start.Y - cy, start.X - cx);
        double a1 = Math.Atan2(through.Y - cy, through.X - cx);
        double a2 = Math.Atan2(end.Y - cy, end.X - cx);
        double total = Norm(a1 - a0) + Norm(a2 - a1);
        if (total < 1e-6)
        {
            return FrozenLine(start, end);
        }

        bool clockwise = total <= 2 * Math.PI;
        double sweep = clockwise ? total : 4 * Math.PI - total;
        if (sweep > 2 * Math.PI - 1e-3)
        {
            EllipseGeometry circle = new(new Point(cx, cy), r, r);
            circle.Freeze();
            return circle;
        }
        if (sweep < 1e-6)
        {
            return FrozenLine(start, end);
        }

        PathFigure figure = new() { StartPoint = start };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, sweep > Math.PI,
            clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static double Norm(double angle)
    {
        double twoPi = 2 * Math.PI;
        return ((angle % twoPi) + twoPi) % twoPi;
    }

    private static Geometry FrozenLine(Point a, Point b)
    {
        LineGeometry geometry = new(a, b);
        geometry.Freeze();
        return geometry;
    }
}
