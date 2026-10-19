using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Tabella sul foglio (distinta fili, distinta materiali...). Il contenuto è una copia al momento dell'inserimento.</summary>
public class TableEntity : EntityBase
{
    public const double RowHeight = 5;
    public const double FontHeight = 2.4;
    private const double CharWidth = 1.45;
    private const double Padding = 1.5;
    private const double MinColumnWidth = 8;

    private Point _position;
    private List<string[]> _rows = new();

    public Point Position
    {
        get => _position;
        set
        {
            _position = value;
            NotifyChanged();
        }
    }

    public IReadOnlyList<string[]> Rows => _rows;

    public void SetRows(IEnumerable<string[]> rows)
    {
        _rows = rows.ToList();
        NotifyChanged();
    }

    public override string TypeName => "Tabella";

    public override string TypeKey => "table";

    public override string Summary => _rows.Count == 0 ? "vuota" : $"{_rows.Count} righe";

    private double[] ColumnWidths()
    {
        int columns = _rows.Count == 0 ? 0 : _rows.Max(row => row.Length);
        double[] widths = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            double width = MinColumnWidth;
            foreach (string[] row in _rows)
            {
                if (c < row.Length)
                {
                    width = Math.Max(width, (row[c] ?? string.Empty).Length * CharWidth + 2 * Padding);
                }
            }
            widths[c] = width;
        }
        return widths;
    }

    private Rect Bounds()
    {
        double[] widths = ColumnWidths();
        return new Rect(_position.X, _position.Y, widths.Sum(), Math.Max(1, _rows.Count) * RowHeight);
    }

    public override Geometry BuildGeometry()
    {
        Rect bounds = Bounds();
        GeometryGroup group = new();
        group.Children.Add(new RectangleGeometry(bounds));
        for (int r = 1; r < _rows.Count; r++)
        {
            double y = bounds.Top + r * RowHeight;
            group.Children.Add(new LineGeometry(new Point(bounds.Left, y), new Point(bounds.Right, y)));
        }
        double x = bounds.Left;
        double[] widths = ColumnWidths();
        for (int c = 0; c + 1 < widths.Length; c++)
        {
            x += widths[c];
            group.Children.Add(new LineGeometry(new Point(x, bounds.Top), new Point(x, bounds.Bottom)));
        }
        group.Freeze();
        return group;
    }

    public override bool HitTest(Point point, double tolerance)
    {
        Rect box = Bounds();
        box.Inflate(tolerance, tolerance);
        return box.Contains(point);
    }

    public override IReadOnlyList<EntityLabel> GetLabels()
    {
        List<EntityLabel> labels = new();
        double[] widths = ColumnWidths();
        for (int r = 0; r < _rows.Count; r++)
        {
            double x = _position.X;
            for (int c = 0; c < widths.Length; c++)
            {
                if (c < _rows[r].Length && !string.IsNullOrEmpty(_rows[r][c]))
                {
                    labels.Add(new EntityLabel(
                        _rows[r][c],
                        new Point(x + Padding, _position.Y + r * RowHeight + RowHeight / 2),
                        FontHeight, 0, 0.5));
                }
                x += widths[c];
            }
        }
        return labels;
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

    public override void Export(Dictionary<string, string> data)
    {
        data["rows"] = JsonSerializer.Serialize(_rows);
    }

    public override void Import(Dictionary<string, string> data)
    {
        _rows = new List<string[]>();
        string json = GetText(data, "rows");
        if (json.Length == 0)
        {
            return;
        }
        try
        {
            List<string[]>? rows = JsonSerializer.Deserialize<List<string[]>>(json);
            if (rows != null)
            {
                _rows = rows;
            }
        }
        catch (JsonException)
        {
            _rows = new List<string[]>();
        }
    }
}
