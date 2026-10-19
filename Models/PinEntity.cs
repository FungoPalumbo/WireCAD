using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Pin libero a cui agganciare fili e linee; ha un numero modificabile dalle proprietà.</summary>
public class PinEntity : EntityBase
{
    public const double DefaultDiameter = 2.5;

    private Point _position;
    private string _number = "1";
    private double _diameter = DefaultDiameter;

    /// <summary>Diametro del cerchio del pin (come le cavità dei connettori), in mm.</summary>
    public double Diameter
    {
        get => _diameter;
        set
        {
            _diameter = Math.Clamp(value, 0.4, 50);
            NotifyChanged();
        }
    }

    public Point Position
    {
        get => _position;
        set
        {
            _position = value;
            NotifyChanged();
        }
    }

    public string Number
    {
        get => _number;
        set => SetField(ref _number, value);
    }

    public override string TypeName => "Pin";

    public override string TypeKey => "pin";

    public override string Summary => $"Pin {_number}   {Format(_position)}";

    public override int PinCount => 1;

    public override Point GetPinPosition(int index) => _position;

    public override string GetPinLabel(int index) => _number;

    public override Geometry BuildGeometry()
    {
        double radius = _diameter / 2;
        EllipseGeometry circle = new(_position, radius, radius);
        circle.Freeze();
        return circle;
    }

    public static Geometry CreateGeometry(Point position)
    {
        return new PinEntity { Position = position }.BuildGeometry();
    }

    public override IReadOnlyList<EntityLabel> GetLabels()
    {
        List<EntityLabel> labels = new();
        if (!string.IsNullOrWhiteSpace(_number))
        {
            // il numero sta al centro del cerchio, come nelle cavità dei connettori
            labels.Add(new EntityLabel(_number, _position, Math.Min(1.8, _diameter * 0.65), 0.5, 0.5));
        }
        return labels;
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("X", () => Position.X, v => Position = new Point(v, Position.Y)),
            new EntityField("Y", () => Position.Y, v => Position = new Point(Position.X, v)),
            new EntityField("Diametro", () => Diameter, v => Diameter = v)
        };
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[]
        {
            new TextField("Numero", () => Number, v => Number = v)
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

    public override void ApplyMatrix(Matrix matrix)
    {
        _diameter = Math.Clamp(_diameter * UniformScale(matrix), 0.4, 50);
        Position = matrix.Transform(_position);
    }

    public override void Transform(TransformKind kind, Point center)
    {
        Position = Map(kind, center, Position);
    }

    public override void Export(Dictionary<string, string> data)
    {
        data["number"] = _number;
        data["dia"] = _diameter.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    public override void Import(Dictionary<string, string> data)
    {
        _number = GetText(data, "number");
        _diameter = Math.Clamp(GetDouble(data, "dia", DefaultDiameter), 0.4, 50);
    }
}
