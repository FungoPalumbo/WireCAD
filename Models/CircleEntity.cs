using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Cerchio definito da centro e raggio.</summary>
public class CircleEntity : StrokedEntity
{
    private Point _center;
    private double _radius = 5;

    public Point Center
    {
        get => _center;
        set
        {
            _center = value;
            NotifyChanged();
        }
    }

    public double Radius
    {
        get => _radius;
        set
        {
            _radius = Math.Max(0.01, value);
            NotifyChanged();
        }
    }

    public override string TypeName => "Cerchio";

    public override string TypeKey => "circle";

    public override string Summary => $"Centro {Format(Center)}   Raggio {Radius:0.###}";

    public override Geometry BuildGeometry()
    {
        EllipseGeometry geometry = new(_center, _radius, _radius);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Solo il contorno è selezionabile: l'interno resta libero per gli oggetti sottostanti.</summary>
    public override bool HitTest(Point point, double tolerance)
    {
        return Math.Abs((point - _center).Length - _radius) <= tolerance;
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("Centro X", () => Center.X, v => Center = new Point(v, Center.Y)),
            new EntityField("Centro Y", () => Center.Y, v => Center = new Point(Center.X, v)),
            new EntityField("Raggio", () => Radius, v => Radius = v)
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { _center, new Point(_center.X + _radius, _center.Y) };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return new[] { _center, new Point(_center.X + _radius, _center.Y) };
    }

    public override IReadOnlyList<Point> GetSnapPoints()
    {
        return new[]
        {
            _center,
            new Point(_center.X + _radius, _center.Y),
            new Point(_center.X - _radius, _center.Y),
            new Point(_center.X, _center.Y + _radius),
            new Point(_center.X, _center.Y - _radius)
        };
    }

    public override void MoveGrip(int index, Point position)
    {
        if (index == 0)
        {
            Center = position;
        }
        else if (index == 1)
        {
            Radius = (position - _center).Length;
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        MoveGrip(index, position);
    }

    public override void Translate(Vector offset)
    {
        _center += offset;
        NotifyChanged();
    }

    public override void ApplyMatrix(Matrix matrix)
    {
        _center = matrix.Transform(_center);
        _radius = Math.Max(0.01, _radius * UniformScale(matrix));
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        _center = Map(kind, center, _center);
        NotifyChanged();
    }
}
