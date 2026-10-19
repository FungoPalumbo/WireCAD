using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

public class AnchorPointEntity : EntityBase
{
    public const double MarkerRadius = 1.5;

    private Point _position;

    public Point Position
    {
        get => _position;
        set
        {
            _position = value;
            NotifyChanged();
        }
    }

    public override string TypeName => "Punto ancora";

    public override string TypeKey => "anchor";

    public override string Summary => $"Posizione {Format(Position)}";

    public override Geometry BuildGeometry()
    {
        return CreateGeometry(Position);
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("X", () => Position.X, v => Position = new Point(v, Position.Y)),
            new EntityField("Y", () => Position.Y, v => Position = new Point(Position.X, v))
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { Position };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return new[] { Position };
    }

    public override void MoveGrip(int index, Point position)
    {
        if (index == 0)
        {
            Position = position;
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        if (index == 0)
        {
            Position = position;
        }
    }

    public override void Translate(Vector offset)
    {
        Position += offset;
    }

    public override void Transform(TransformKind kind, Point center)
    {
        Position = Map(kind, center, Position);
    }

    public static Geometry CreateGeometry(Point position)
    {
        GeometryGroup group = new();
        group.Children.Add(new EllipseGeometry(position, MarkerRadius, MarkerRadius));
        group.Children.Add(new LineGeometry(
            new Point(position.X - MarkerRadius, position.Y),
            new Point(position.X + MarkerRadius, position.Y)));
        group.Children.Add(new LineGeometry(
            new Point(position.X, position.Y - MarkerRadius),
            new Point(position.X, position.Y + MarkerRadius)));
        group.Freeze();
        return group;
    }
}
