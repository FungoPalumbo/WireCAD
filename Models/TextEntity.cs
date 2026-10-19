using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Testo libero (note, titoli di sezione, annotazioni).</summary>
public class TextEntity : EntityBase
{
    private Point _position;
    private string _text = "Testo";
    private double _textHeight = 3;

    public Point Position
    {
        get => _position;
        set
        {
            _position = value;
            NotifyChanged();
        }
    }

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public double TextHeight
    {
        get => _textHeight;
        set
        {
            _textHeight = Math.Max(0.5, value);
            NotifyChanged();
        }
    }

    public override string TypeName => "Testo";

    public override string TypeKey => "text";

    public override string Summary => _text.Length > 40 ? _text.Substring(0, 40) + "…" : _text;

    public override bool IsFilled => true;

    public override bool HasOutline => false;

    private FormattedText BuildFormatted()
    {
        string text = string.IsNullOrEmpty(_text) ? " " : _text;
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            Math.Max(0.5, _textHeight),
            Brushes.Black,
            1.0);
    }

    public override Geometry BuildGeometry()
    {
        Geometry geometry = BuildFormatted().BuildGeometry(_position);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }
        return geometry;
    }

    public override bool HitTest(Point point, double tolerance)
    {
        FormattedText formatted = BuildFormatted();
        Rect box = new(_position.X, _position.Y, formatted.Width, formatted.Height);
        box.Inflate(tolerance, tolerance);
        return box.Contains(point);
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("X", () => Position.X, v => Position = new Point(v, Position.Y)),
            new EntityField("Y", () => Position.Y, v => Position = new Point(Position.X, v)),
            new EntityField("Altezza testo", () => TextHeight, v => TextHeight = v)
        };
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[]
        {
            new TextField("Testo", () => Text, v => Text = v)
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
        _textHeight = Math.Max(0.5, _textHeight * UniformScale(matrix));
        Position = matrix.Transform(_position);
    }

    public override void Transform(TransformKind kind, Point center)
    {
        Position = Map(kind, center, Position);
    }

    public override void Export(Dictionary<string, string> data)
    {
        data["text"] = _text;
        data["height"] = _textHeight.ToString("R", CultureInfo.InvariantCulture);
    }

    public override void Import(Dictionary<string, string> data)
    {
        _text = GetText(data, "text");
        _textHeight = Math.Max(0.5, GetDouble(data, "height", 3));
    }
}
