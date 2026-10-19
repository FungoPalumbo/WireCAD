using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

public enum TransformKind
{
    Rotate90,
    MirrorHorizontal,
    MirrorVertical
}

/// <summary>Etichetta di testo disegnata sopra l'entità (numero filo, tag, nomi pin).</summary>
public sealed record EntityLabel(string Text, Point Anchor, double Height, double AlignX, double AlignY);

public abstract class EntityBase : INotifyPropertyChanged
{
    private string _name = "";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? GeometryChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    private bool _showLabel;

    /// <summary>
    /// Di default è spenta. Se falso l'etichetta principale scritta in linea con l'oggetto (numero e lunghezza del filo, tag del componente,
    /// descrizione della guaina) non viene mostrata. I numeri dei pin e il contenuto delle tabelle restano visibili.
    /// </summary>
    public bool ShowLabel
    {
        get => _showLabel;
        set
        {
            if (_showLabel == value)
            {
                return;
            }
            _showLabel = value;
            NotifyChanged();
        }
    }

    /// <summary>Chiave con cui il nome dell'oggetto compare tra le proprietà mostrate sul foglio.</summary>
    public const string NameKey = "Nome";

    /// <summary>Etichette delle proprietà il cui valore è mostrato sul foglio accanto all'oggetto.</summary>
    public HashSet<string> ShownFields { get; } = new(StringComparer.Ordinal);

    public bool ShowName
    {
        get => ShownFields.Contains(NameKey);
        set => SetFieldShown(NameKey, value);
    }

    /// <summary>Mostra o nasconde sul foglio il valore di una proprietà.</summary>
    public void SetFieldShown(string key, bool shown)
    {
        bool changed = shown ? ShownFields.Add(key) : ShownFields.Remove(key);
        if (changed)
        {
            NotifyChanged();
        }
    }

    /// <summary>Etichette con i valori delle proprietà scelte dall'utente, in colonna sotto l'oggetto.</summary>
    public IReadOnlyList<EntityLabel> GetPropertyLabels()
    {
        List<EntityLabel> labels = new();
        if (ShownFields.Count == 0)
        {
            return labels;
        }
        Rect bounds = BuildGeometry().Bounds;
        if (bounds.IsEmpty)
        {
            return labels;
        }
        double y = bounds.Bottom + 1.5;
        const double height = 2.2;
        const double step = 2.8;
        if (ShownFields.Contains(NameKey) && !string.IsNullOrWhiteSpace(Name))
        {
            labels.Add(new EntityLabel($"{NameKey}: {Name}", new Point(bounds.Left, y), height, 0, 0));
            y += step;
        }
        foreach (TextField field in GetTextFields())
        {
            if (!ShownFields.Contains(field.Label))
            {
                continue;
            }
            string value = field.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            labels.Add(new EntityLabel($"{field.Label}: {value}", new Point(bounds.Left, y), height, 0, 0));
            y += step;
        }
        foreach (EntityField field in GetFields())
        {
            if (!ShownFields.Contains(field.Label))
            {
                continue;
            }
            labels.Add(new EntityLabel(
                $"{field.Label}: {field.Value.ToString("0.###", CultureInfo.CurrentCulture)}",
                new Point(bounds.Left, y), height, 0, 0));
            y += step;
        }
        return labels;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }
            _name = value;
            RaisePropertyChanged();
        }
    }

    public abstract string TypeName { get; }

    public abstract string TypeKey { get; }

    public abstract string Summary { get; }

    public abstract Geometry BuildGeometry();

    public abstract IReadOnlyList<EntityField> GetFields();

    public abstract IReadOnlyList<Point> GetGripPoints();

    public abstract IReadOnlyList<Point> GetDefinitionPoints();

    public abstract void MoveGrip(int index, Point position);

    public abstract void SetDefinitionPoint(int index, Point position);

    public virtual IReadOnlyList<Point> GetSnapPoints()
    {
        return GetGripPoints();
    }

    public abstract void Translate(Vector offset);

    public abstract void Transform(TransformKind kind, Point center);

    /// <summary>
    /// Applica una trasformazione affine (rotazioni di multipli di 90°, specchiature, traslazioni, scala uniforme).
    /// Serve a posizionare i disegni incorporati nei connettori personalizzati; le entità che non la supportano la ignorano.
    /// </summary>
    public virtual void ApplyMatrix(Matrix matrix)
    {
    }

    protected static double UniformScale(Matrix matrix)
    {
        return Math.Sqrt(Math.Abs(matrix.Determinant));
    }

    /// <summary>Numero di pin elettrici (punti di collegamento) esposti dall'entità.</summary>
    public virtual int PinCount => 0;

    public virtual Point GetPinPosition(int index) => new Point();

    public virtual string GetPinLabel(int index) => string.Empty;

    /// <summary>Direzione di uscita del pin (vettore unitario), (0,0) se non definita.</summary>
    public virtual Vector GetPinDirection(int index) => new Vector(0, 0);

    public virtual bool IsFilled => false;

    public virtual bool HasOutline => true;

    public virtual double LineWidth => 0.35;

    /// <summary>Schema del tratteggio come multipli dello spessore di linea (null = linea continua).</summary>
    public virtual DoubleCollection? DashPattern => null;

    /// <summary>Colore del tratto (esadecimale) oppure null per il nero predefinito.</summary>
    public virtual string? StrokeColor => null;

    /// <summary>Secondo colore (esadecimale): se presente il tratto è disegnato a strisce alternate con il primo.</summary>
    public virtual string? StrokeColor2 => null;

    /// <summary>Colore di riempimento (esadecimale) oppure null se la forma non è riempita.</summary>
    public virtual string? FillHex => null;

    /// <summary>Opacità del riempimento (1 = pieno, valori minori lasciano intravedere ciò che sta sotto).</summary>
    public virtual double FillOpacity => 1;

    /// <summary>Se vero la forma è disegnata sotto tutti gli altri oggetti.</summary>
    public virtual bool SendToBack => false;

    public virtual IReadOnlyList<TextField> GetTextFields() => Array.Empty<TextField>();

    public virtual IReadOnlyList<EntityLabel> GetLabels() => Array.Empty<EntityLabel>();

    /// <summary>Dati aggiuntivi da salvare nel file (attributi elettrici ecc.).</summary>
    public virtual void Export(Dictionary<string, string> data)
    {
    }

    public virtual void Import(Dictionary<string, string> data)
    {
    }

    /// <summary>Riscrive i riferimenti ad altre entità (Id) dopo una copia/importazione.</summary>
    public virtual void RemapReferences(Func<Guid, Guid?> map)
    {
    }

    public virtual bool HitTest(Point point, double tolerance)
    {
        Geometry geometry = BuildGeometry();
        return geometry.FillContains(point) || geometry.StrokeContains(new Pen(null, tolerance), point);
    }

    protected static string Format(Point point)
    {
        return $"({point.X:0.0}; {point.Y:0.0})";
    }

    protected static Point Map(TransformKind kind, Point center, Point point)
    {
        switch (kind)
        {
            case TransformKind.Rotate90:
                return new Point(
                    Math.Round(center.X - (point.Y - center.Y), 6),
                    Math.Round(center.Y + (point.X - center.X), 6));
            case TransformKind.MirrorHorizontal:
                return new Point(Math.Round(2 * center.X - point.X, 6), point.Y);
            default:
                return new Point(point.X, Math.Round(2 * center.Y - point.Y, 6));
        }
    }

    protected static string GetText(Dictionary<string, string> data, string key)
    {
        return data.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    protected static int GetInt(Dictionary<string, string> data, string key, int fallback)
    {
        return data.TryGetValue(key, out string? value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    protected static double GetDouble(Dictionary<string, string> data, string key, double fallback)
    {
        return data.TryGetValue(key, out string? value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;
    }

    protected void SetField(ref string field, string? value)
    {
        string next = value ?? string.Empty;
        if (field == next)
        {
            return;
        }
        field = next;
        NotifyChanged();
    }

    /// <summary>Forza il ridisegno (per esempio dopo la modifica dell'ingrandimento degli spessori).</summary>
    public void RefreshGeometry()
    {
        NotifyChanged();
    }

    protected void NotifyChanged()
    {
        GeometryChanged?.Invoke(this, EventArgs.Empty);
        RaisePropertyChanged(nameof(Summary));
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
