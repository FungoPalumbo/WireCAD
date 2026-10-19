using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

public class RectangleEntity : StrokedEntity
{
    private Point _p1;
    private Point _p2;
    private double _cornerRadius;
    private string _fill = "";
    private bool _below = true;

    public Point P1
    {
        get => _p1;
        set
        {
            _p1 = value;
            NotifyChanged();
        }
    }

    public Point P2
    {
        get => _p2;
        set
        {
            _p2 = value;
            NotifyChanged();
        }
    }

    public override string TypeName => "Rettangolo";

    public override string TypeKey => "rectangle";

    public override string Summary => $"Angolo 1 {Format(P1)}   Angolo 2 {Format(P2)}";

    public override Geometry BuildGeometry()
    {
        Rect rect = new(P1, P2);
        double radius = Math.Min(_cornerRadius, Math.Min(rect.Width, rect.Height) / 2);
        RectangleGeometry geometry = radius > 0
            ? new RectangleGeometry(rect, radius, radius)
            : new RectangleGeometry(rect);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Raggio di arrotondamento degli angoli (mm), 0 per angoli vivi.</summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            NotifyChanged();
        }
    }

    /// <summary>Colore di riempimento: nome della tavolozza, "#RRGGBB" oppure vuoto per nessun riempimento.</summary>
    public string Fill
    {
        get => _fill;
        set => SetField(ref _fill, value);
    }

    public override string? FillHex => ColorPalette.Resolve(_fill);

    /// <summary>Se vero il rettangolo è disegnato sotto gli altri oggetti, altrimenti sopra.</summary>
    public bool Below
    {
        get => _below;
        set
        {
            _below = value;
            NotifyChanged();
        }
    }

    public override bool SendToBack => _below;

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[]
        {
            DashField(),
            new TextField("Riempimento", () => string.IsNullOrEmpty(Fill) ? "Nessuno" : Fill, v => Fill = v, ColorPalette.Choices),
            new TextField("Livello", () => Below ? "Sotto" : "Sopra", v => Below = !string.Equals(v.Trim(), "Sopra", StringComparison.OrdinalIgnoreCase), new[] { "Sotto", "Sopra" })
        };
    }

    public override void Export(Dictionary<string, string> data)
    {
        base.Export(data);
        data["fill"] = _fill;
        data["radius"] = _cornerRadius.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        data["below"] = _below ? "1" : "0";
    }

    public override void Import(Dictionary<string, string> data)
    {
        base.Import(data);
        _fill = GetText(data, "fill");
        _cornerRadius = Math.Max(0, GetDouble(data, "radius", 0));
        _below = data.TryGetValue("below", out string? below) ? below == "1" : _fill.Length > 0;
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("Angolo 1 X", () => P1.X, v => P1 = new Point(v, P1.Y)),
            new EntityField("Angolo 1 Y", () => P1.Y, v => P1 = new Point(P1.X, v)),
            new EntityField("Angolo 2 X", () => P2.X, v => P2 = new Point(v, P2.Y)),
            new EntityField("Angolo 2 Y", () => P2.Y, v => P2 = new Point(P2.X, v)),
            new EntityField("Raggio angoli", () => CornerRadius, v => CornerRadius = v)
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { P1, P2 };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return new[]
        {
            P1,
            new Point(P2.X, P1.Y),
            P2,
            new Point(P1.X, P2.Y)
        };
    }

    public override void MoveGrip(int index, Point position)
    {
        switch (index)
        {
            case 0:
                P1 = position;
                break;
            case 1:
                _p2.X = position.X;
                _p1.Y = position.Y;
                NotifyChanged();
                break;
            case 2:
                P2 = position;
                break;
            case 3:
                _p1.X = position.X;
                _p2.Y = position.Y;
                NotifyChanged();
                break;
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        if (index == 0)
        {
            P1 = position;
        }
        else if (index == 1)
        {
            P2 = position;
        }
    }

    public override IReadOnlyList<Point> GetSnapPoints()
    {
        Point corner1 = P1;
        Point corner2 = new(P2.X, P1.Y);
        Point corner3 = P2;
        Point corner4 = new(P1.X, P2.Y);
        return new[]
        {
            corner1,
            corner2,
            corner3,
            corner4,
            new Point((corner1.X + corner2.X) / 2, (corner1.Y + corner2.Y) / 2),
            new Point((corner2.X + corner3.X) / 2, (corner2.Y + corner3.Y) / 2),
            new Point((corner3.X + corner4.X) / 2, (corner3.Y + corner4.Y) / 2),
            new Point((corner4.X + corner1.X) / 2, (corner4.Y + corner1.Y) / 2),
            new Point((corner1.X + corner3.X) / 2, (corner1.Y + corner3.Y) / 2)
        };
    }

    public override void Translate(Vector offset)
    {
        _p1 += offset;
        _p2 += offset;
        NotifyChanged();
    }

    public override void ApplyMatrix(Matrix matrix)
    {
        _p1 = matrix.Transform(_p1);
        _p2 = matrix.Transform(_p2);
        _cornerRadius *= UniformScale(matrix);
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        _p1 = Map(kind, center, _p1);
        _p2 = Map(kind, center, _p2);
        NotifyChanged();
    }
}
