using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Spline che passa per i punti assegnati (curva di Catmull-Rom), aperta o chiusa.</summary>
public class SplineEntity : StrokedEntity
{
    private List<Point> _points;
    private bool _closed;

    public SplineEntity(IEnumerable<Point> points)
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

    public IReadOnlyList<Point> Points => _points;

    public bool Closed
    {
        get => _closed;
        set
        {
            _closed = value;
            NotifyChanged();
        }
    }

    public override string TypeName => "Spline";

    public override string TypeKey => "spline";

    public override string Summary => $"{_points.Count} punti{(_closed ? ", chiusa" : string.Empty)}";

    private Point At(int index)
    {
        int count = _points.Count;
        if (_closed)
        {
            return _points[((index % count) + count) % count];
        }
        return _points[Math.Clamp(index, 0, count - 1)];
    }

    public override Geometry BuildGeometry()
    {
        int count = _points.Count;
        PathFigure figure = new() { StartPoint = _points[0], IsClosed = _closed && count > 2, IsFilled = false };
        int segments = _closed && count > 2 ? count : count - 1;
        for (int i = 0; i < segments; i++)
        {
            Point p0 = At(i - 1);
            Point p1 = At(i);
            Point p2 = At(i + 1);
            Point p3 = At(i + 2);
            Point c1 = new(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            Point c2 = new(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            figure.Segments.Add(new BezierSegment(c1, c2, p2, true));
        }
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
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
        return fields;
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[]
        {
            DashField(),
            new TextField("Chiusa", () => Closed ? "sì" : "no",
                v => Closed = v.Trim().ToLowerInvariant() is "si" or "sì" or "s" or "yes" or "1",
                new[] { "no", "sì" })
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

    /// <summary>Aggiunge un punto di passaggio sul tratto più vicino al punto indicato.</summary>
    public void InsertVertexNear(Point point)
    {
        int best = 0;
        double bestDistance = double.MaxValue;
        int segments = _closed && _points.Count > 2 ? _points.Count : _points.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            double distance = WireEntity.DistanceToSegment(point, _points[i], _points[(i + 1) % _points.Count]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        Point a = _points[best];
        Point b = _points[(best + 1) % _points.Count];
        _points.Insert(best + 1, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2));
        NotifyChanged();
    }

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

    public override void ApplyMatrix(Matrix matrix)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            _points[i] = matrix.Transform(_points[i]);
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
        base.Export(data);
        data["closed"] = _closed ? "1" : "0";
    }

    public override void Import(Dictionary<string, string> data)
    {
        base.Import(data);
        _closed = GetText(data, "closed") == "1";
    }
}
