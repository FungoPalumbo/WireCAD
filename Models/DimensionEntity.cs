using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>
/// Quota lineare: due punti misurati e un terzo punto che stabilisce dove passa la linea di quota.
/// Può essere allineata ai due punti, orizzontale o verticale. Il valore è calcolato automaticamente.
/// </summary>
public class DimensionEntity : EntityBase
{
    private const double ExtensionGap = 1;
    private const double ExtensionOvershoot = 1.5;

    private Point _p1;
    private Point _p2;
    private Point _offset;
    private string _override = string.Empty;
    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private int _decimals = 1;
    private double _textHeight = 3;
    private double _arrowSize = 2.5;

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

    /// <summary>Punto per il quale passa la linea di quota.</summary>
    public Point Offset
    {
        get => _offset;
        set
        {
            _offset = value;
            NotifyChanged();
        }
    }

    public override string TypeName => "Quota";

    public override string TypeKey => "dimension";

    public override string Summary => MeasuredText();

    /// <summary>Estremi della linea di quota e versore lungo la misura e quello perpendicolare.</summary>
    private void Layout(out Point a, out Point b, out Vector along, out Vector normal, out double value)
    {
        Vector delta = _p2 - _p1;
        along = delta.Length > 1e-9 ? delta / delta.Length : new Vector(1, 0);
        normal = new Vector(-along.Y, along.X);
        a = _p1 + normal * Vector.Multiply(_offset - _p1, normal);
        b = _p2 + normal * Vector.Multiply(_offset - _p2, normal);
        value = Math.Abs(Vector.Multiply(delta, along));
    }

    private string MeasuredText()
    {
        Layout(out _, out _, out _, out _, out double value);
        string number = value.ToString("F" + Math.Clamp(_decimals, 0, 4), CultureInfo.CurrentCulture);
        return _prefix + number + _suffix;
    }

    private string DisplayText()
    {
        return string.IsNullOrWhiteSpace(_override) ? MeasuredText() : _override;
    }

    private List<(Point From, Point To)> Segments()
    {
        Layout(out Point a, out Point b, out Vector along, out Vector normal, out _);
        List<(Point, Point)> segments = new();
        AddExtension(segments, _p1, a);
        AddExtension(segments, _p2, b);
        segments.Add((a, b));
        Vector direction = b - a;
        if (direction.Length > 1e-9)
        {
            direction /= direction.Length;
            Vector wing = new Vector(-direction.Y, direction.X) * (_arrowSize / 3);
            Point tipA = a + direction * _arrowSize;
            segments.Add((a, tipA + wing));
            segments.Add((a, tipA - wing));
            Point tipB = b - direction * _arrowSize;
            segments.Add((b, tipB + wing));
            segments.Add((b, tipB - wing));
        }
        return segments;
    }

    private static void AddExtension(List<(Point, Point)> segments, Point origin, Point end)
    {
        Vector direction = end - origin;
        double length = direction.Length;
        if (length <= ExtensionGap)
        {
            return;
        }
        direction /= length;
        segments.Add((origin + direction * ExtensionGap, end + direction * ExtensionOvershoot));
    }

    public override Geometry BuildGeometry()
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext context = geometry.Open())
        {
            foreach ((Point from, Point to) in Segments())
            {
                context.BeginFigure(from, false, false);
                context.LineTo(to, true, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    public override bool HitTest(Point point, double tolerance)
    {
        foreach ((Point from, Point to) in Segments())
        {
            Vector segment = to - from;
            double lengthSquared = segment.LengthSquared;
            double t = lengthSquared < 1e-12 ? 0 : Math.Clamp(Vector.Multiply(point - from, segment) / lengthSquared, 0, 1);
            if ((point - (from + segment * t)).Length <= tolerance)
            {
                return true;
            }
        }
        return false;
    }

    public override IReadOnlyList<EntityLabel> GetLabels()
    {
        Layout(out Point a, out Point b, out Vector along, out _, out _);
        Point middle = new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        string text = DisplayText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<EntityLabel>();
        }
        bool vertical = Math.Abs(along.Y) > Math.Abs(along.X) * 2;
        EntityLabel label = vertical
            ? new EntityLabel(text, new Point(middle.X + 1.2, middle.Y), _textHeight, 0, 0.5)
            : new EntityLabel(text, new Point(middle.X, middle.Y - 0.8), _textHeight, 0.5, 1);
        return new[] { label };
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("Punto 1 X", () => P1.X, v => P1 = new Point(v, P1.Y)),
            new EntityField("Punto 1 Y", () => P1.Y, v => P1 = new Point(P1.X, v)),
            new EntityField("Punto 2 X", () => P2.X, v => P2 = new Point(v, P2.Y)),
            new EntityField("Punto 2 Y", () => P2.Y, v => P2 = new Point(P2.X, v)),
            new EntityField("Altezza testo", () => _textHeight, v =>
            {
                _textHeight = Math.Max(0.5, v);
                NotifyChanged();
            }),
            new EntityField("Dimensione frecce", () => _arrowSize, v =>
            {
                _arrowSize = Math.Max(0.5, v);
                NotifyChanged();
            }),
            new EntityField("Decimali", () => _decimals, v =>
            {
                _decimals = (int)Math.Clamp(Math.Round(v), 0, 4);
                NotifyChanged();
            })
        };
    }

    public override IReadOnlyList<TextField> GetTextFields()
    {
        return new[]
        {
            new TextField("Testo manuale", () => _override, v => SetField(ref _override, v)),
            new TextField("Prefisso", () => _prefix, v => SetField(ref _prefix, v)),
            new TextField("Suffisso", () => _suffix, v => SetField(ref _suffix, v))
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { _p1, _p2, _offset };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        Layout(out Point a, out Point b, out _, out _, out _);
        return new[] { _p1, _p2, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2) };
    }

    public override IReadOnlyList<Point> GetSnapPoints()
    {
        Layout(out Point a, out Point b, out _, out _, out _);
        return new[] { _p1, _p2, a, b };
    }

    public override void MoveGrip(int index, Point position)
    {
        switch (index)
        {
            case 0:
                P1 = position;
                break;
            case 1:
                P2 = position;
                break;
            case 2:
                Offset = position;
                break;
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        MoveGrip(index, position);
    }

    public override void Translate(Vector offset)
    {
        _p1 += offset;
        _p2 += offset;
        _offset += offset;
        NotifyChanged();
    }

    public override void ApplyMatrix(Matrix matrix)
    {
        double scale = UniformScale(matrix);
        _p1 = matrix.Transform(_p1);
        _p2 = matrix.Transform(_p2);
        _offset = matrix.Transform(_offset);
        _textHeight = Math.Max(0.5, _textHeight * scale);
        _arrowSize = Math.Max(0.5, _arrowSize * scale);
        NotifyChanged();
    }

    public override void Transform(TransformKind kind, Point center)
    {
        _p1 = Map(kind, center, _p1);
        _p2 = Map(kind, center, _p2);
        _offset = Map(kind, center, _offset);
        NotifyChanged();
    }

    public override void Export(Dictionary<string, string> data)
    {
        data["height"] = _textHeight.ToString("R", CultureInfo.InvariantCulture);
        data["arrow"] = _arrowSize.ToString("R", CultureInfo.InvariantCulture);
        data["decimals"] = _decimals.ToString(CultureInfo.InvariantCulture);
        if (_override.Length > 0)
        {
            data["override"] = _override;
        }
        if (_prefix.Length > 0)
        {
            data["prefix"] = _prefix;
        }
        if (_suffix.Length > 0)
        {
            data["suffix"] = _suffix;
        }
    }

    public override void Import(Dictionary<string, string> data)
    {
        _textHeight = Math.Max(0.5, GetDouble(data, "height", 3));
        _arrowSize = Math.Max(0.5, GetDouble(data, "arrow", 2.5));
        _decimals = Math.Clamp(GetInt(data, "decimals", 1), 0, 4);
        _override = GetText(data, "override");
        _prefix = GetText(data, "prefix");
        _suffix = GetText(data, "suffix");
    }
}
