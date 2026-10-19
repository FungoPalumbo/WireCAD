using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

public class LineEntity : StrokedEntity
{
    private Point _a;
    private Point _b;

    public Point A
    {
        get => _a;
        set
        {
            _a = value;
            NotifyChanged();
        }
    }

    public Point B
    {
        get => _b;
        set
        {
            _b = value;
            NotifyChanged();
        }
    }

    public override string TypeName => "Linea";

    public override string TypeKey => "line";

    public override string Summary => $"Inizio {Format(A)}   Fine {Format(B)}";

    public override Geometry BuildGeometry()
    {
        LineGeometry geometry = new(A, B);
        geometry.Freeze();
        return geometry;
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("Inizio X", () => A.X, v => A = new Point(v, A.Y)),
            new EntityField("Inizio Y", () => A.Y, v => A = new Point(A.X, v)),
            new EntityField("Fine X", () => B.X, v => B = new Point(v, B.Y)),
            new EntityField("Fine Y", () => B.Y, v => B = new Point(B.X, v))
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { A, B };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return new[] { A, B };
    }

    public override void MoveGrip(int index, Point position)
    {
        if (index == 0)
        {
            A = position;
        }
        else if (index == 1)
        {
            B = position;
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        if (index == 0)
        {
            A = position;
        }
        else if (index == 1)
        {
            B = position;
        }
    }

    public override IReadOnlyList<Point> GetSnapPoints()
    {
        return new[]
        {
            A,
            B,
            new Point((A.X + B.X) / 2, (A.Y + B.Y) / 2)
        };
    }

    public override void Translate(Vector offset)
    {
        _a += offset;
        _b += offset;
        NotifyChanged();
    }

    public override void ApplyMatrix(Matrix matrix)
    {
        _a = matrix.Transform(_a);
        _b = matrix.Transform(_b);
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        _a = Map(kind, center, _a);
        _b = Map(kind, center, _b);
        NotifyChanged();
    }
}
