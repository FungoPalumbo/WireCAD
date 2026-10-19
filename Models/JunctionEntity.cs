using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Giunzione (nodo di derivazione) tra fili: espone un unico pin.</summary>
public class JunctionEntity : EntityBase
{
    public const double Radius = 0.9;

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

    public override string TypeName => "Giunzione";

    public override string TypeKey => "junction";

    public override string Summary => $"Posizione {Format(Position)}";

    public override bool IsFilled => true;

    public override int PinCount => 1;

    public override Point GetPinPosition(int index) => _position;

    public override string GetPinLabel(int index) => string.Empty;

    public override Geometry BuildGeometry()
    {
        EllipseGeometry geometry = new(_position, Radius, Radius);
        geometry.Freeze();
        return geometry;
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
        MoveGrip(index, position);
    }

    public override void Translate(Vector offset)
    {
        Position += offset;
    }

    public override void Transform(TransformKind kind, Point center)
    {
        Position = Map(kind, center, Position);
    }
}
