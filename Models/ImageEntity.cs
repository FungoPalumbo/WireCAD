using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WireCAD.Models;

/// <summary>
/// Immagine raster (JPG, BMP, PNG...) usata come riferimento sul foglio, ad esempio per ricalcarla.
/// I byte del file sono incorporati nel progetto; non viene esportata in DXF/DWG/PDF/SVG.
/// </summary>
public class ImageEntity : EntityBase
{
    public const string Filter =
        "Immagini (*.jpg;*.jpeg;*.bmp;*.png;*.gif;*.tif;*.tiff)|*.jpg;*.jpeg;*.bmp;*.png;*.gif;*.tif;*.tiff|Tutti i file (*.*)|*.*";

    private byte[] _bytes = Array.Empty<byte>();
    private BitmapSource? _bitmap;
    private ImageBrush? _brush;
    private double _ratio = 1;
    private Point _topLeft;
    private double _width = 100;
    private double _opacity = 1;

    /// <summary>Larghezza in mm che l'immagine avrebbe secondo la sua risoluzione (DPI) dichiarata.</summary>
    public double NaturalWidth { get; private set; } = 100;

    public Point TopLeft
    {
        get => _topLeft;
        set
        {
            _topLeft = value;
            NotifyChanged();
        }
    }

    public double Width
    {
        get => _width;
        set
        {
            _width = Math.Max(1, value);
            NotifyChanged();
        }
    }

    public double Height => _width * _ratio;

    public double Opacity
    {
        get => _opacity;
        set
        {
            _opacity = Math.Clamp(value, 0.05, 1);
            _brush = null;
            NotifyChanged();
        }
    }

    public override string TypeName => "Immagine";

    public override string TypeKey => "image";

    public override string Summary => $"{Width:0.#} × {Height:0.#} mm";

    public override bool SendToBack => true;

    public override double LineWidth => 0.15;

    public static ImageEntity FromFile(string path)
    {
        ImageEntity entity = new();
        entity.LoadBytes(File.ReadAllBytes(path));
        entity.Name = Path.GetFileName(path);
        return entity;
    }

    private void LoadBytes(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            throw new InvalidDataException("Immagine vuota.");
        }
        _bytes = bytes;
        _bitmap = image;
        _brush = null;
        _ratio = image.PixelHeight / (double)image.PixelWidth;
        double dpi = image.DpiX > 1 ? image.DpiX : 96;
        NaturalWidth = image.PixelWidth / dpi * 25.4;
    }

    /// <summary>Pennello con l'immagine, usato come riempimento del rettangolo.</summary>
    public Brush? GetBrush()
    {
        if (_bitmap == null)
        {
            return null;
        }
        if (_brush == null)
        {
            ImageBrush brush = new(_bitmap) { Stretch = Stretch.Fill, Opacity = _opacity };
            brush.Freeze();
            _brush = brush;
        }
        return _brush;
    }

    public override Geometry BuildGeometry()
    {
        RectangleGeometry geometry = new(new Rect(_topLeft, new Size(_width, Height)));
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Solo il bordo è selezionabile, così gli oggetti disegnati sopra restano raggiungibili.</summary>
    public override bool HitTest(Point point, double tolerance)
    {
        Rect outer = new(_topLeft, new Size(_width, Height));
        Rect inner = outer;
        inner.Inflate(-tolerance, -tolerance);
        outer.Inflate(tolerance, tolerance);
        return outer.Contains(point) && (inner.IsEmpty || !inner.Contains(point));
    }

    public override IReadOnlyList<EntityField> GetFields()
    {
        return new[]
        {
            new EntityField("X", () => TopLeft.X, v => TopLeft = new Point(v, TopLeft.Y)),
            new EntityField("Y", () => TopLeft.Y, v => TopLeft = new Point(TopLeft.X, v)),
            new EntityField("Larghezza", () => Width, v => Width = v),
            new EntityField("Altezza", () => Height, v => Width = v / _ratio),
            new EntityField("Opacità (%)", () => Math.Round(Opacity * 100), v => Opacity = v / 100)
        };
    }

    public override IReadOnlyList<Point> GetDefinitionPoints()
    {
        return new[] { _topLeft, new Point(_topLeft.X + _width, _topLeft.Y + Height) };
    }

    public override IReadOnlyList<Point> GetGripPoints()
    {
        return GetDefinitionPoints();
    }

    public override IReadOnlyList<Point> GetSnapPoints()
    {
        double w = _width;
        double h = Height;
        return new[]
        {
            _topLeft,
            new Point(_topLeft.X + w, _topLeft.Y),
            new Point(_topLeft.X, _topLeft.Y + h),
            new Point(_topLeft.X + w, _topLeft.Y + h),
            new Point(_topLeft.X + w / 2, _topLeft.Y + h / 2)
        };
    }

    public override void MoveGrip(int index, Point position)
    {
        if (index == 0)
        {
            TopLeft = position;
        }
        else if (index == 1)
        {
            Width = position.X - _topLeft.X;
        }
    }

    public override void SetDefinitionPoint(int index, Point position)
    {
        MoveGrip(index, position);
    }

    public override void Translate(Vector offset)
    {
        _topLeft += offset;
        NotifyChanged();
    }

    public override void ApplyMatrix(Matrix matrix)
    {
        _topLeft = matrix.Transform(_topLeft);
        _width = Math.Max(1, _width * UniformScale(matrix));
        NotifyChanged();
    }

    /// <summary>L'immagine non viene ruotata né specchiata: si sposta solo il suo centro.</summary>
    public override void Transform(TransformKind kind, Point center)
    {
        Vector half = new(_width / 2, Height / 2);
        Point middle = Map(kind, center, _topLeft + half);
        _topLeft = middle - half;
        NotifyChanged();
    }

    public override void Export(Dictionary<string, string> data)
    {
        data["opacity"] = _opacity.ToString("R", CultureInfo.InvariantCulture);
        data["data"] = Convert.ToBase64String(_bytes);
    }

    public override void Import(Dictionary<string, string> data)
    {
        _opacity = Math.Clamp(GetDouble(data, "opacity", 1), 0.05, 1);
        string encoded = GetText(data, "data");
        if (encoded.Length == 0)
        {
            return;
        }
        try
        {
            LoadBytes(Convert.FromBase64String(encoded));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or NotSupportedException or FileFormatException)
        {
            _bitmap = null;
        }
    }
}
