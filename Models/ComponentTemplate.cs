using System.IO;
using System.Windows;
using System.Windows.Media;

namespace WireCAD.Models;

/// <summary>Cavità di un connettore: etichetta e posizione del centro (mm dall'angolo in alto a sinistra del corpo).</summary>
public sealed record CavitySpec(string Label, double X, double Y);

/// <summary>
/// Modello di connettore della libreria: generato da famiglie (righe/colonne/passo) oppure definito da un file di forme
/// marcato come connettore (disegno libero con pin numerati).
/// </summary>
public sealed class ComponentTemplate
{
    private readonly List<CavitySpec> _cavities;
    private readonly List<EntityDto>? _customParts;
    private readonly List<PinDef>? _customPins;
    private readonly ConnectorInfo? _info;

    public ComponentTemplate(
        string name,
        string prefix,
        double width,
        double height,
        double cavityDiameter,
        int keyCorner,
        IEnumerable<CavitySpec> cavities)
    {
        Name = name;
        ModelName = name;
        Prefix = prefix;
        Width = width;
        Height = height;
        CavityDiameter = cavityDiameter;
        KeyCorner = keyCorner;
        _cavities = cavities.ToList();
    }

    private ComponentTemplate(ConnectorInfo info, double width, double height, List<EntityDto> parts, List<PinDef> pins)
    {
        string name = string.IsNullOrWhiteSpace(info.Name) ? "Connettore" : info.Name.Trim();
        Name = name + " (personale)";
        ModelName = name;
        Prefix = string.IsNullOrWhiteSpace(info.Prefix) ? "X" : info.Prefix.Trim();
        Width = width;
        Height = height;
        _cavities = new List<CavitySpec>();
        _customParts = parts;
        _customPins = pins;
        _info = info;
    }

    private readonly InlineSpec? _inline;

    private ComponentTemplate(InlineSpec spec)
    {
        _inline = spec;
        Name = spec.Name;
        ModelName = spec.Name;
        Prefix = spec.Prefix;
        Width = spec.TotalLength;
        Height = spec.BodyDiameter;
        _cavities = new List<CavitySpec>();
    }

    /// <summary>Vero per i componenti da inserire in linea su un filo (resistenza, diodo...).</summary>
    public bool IsInline => _inline != null;

    /// <summary>Lunghezza complessiva (reofori compresi) del componente in linea.</summary>
    public double InlineLength => _inline?.TotalLength ?? 0;

    /// <summary>Crea un componente in linea con la matrice di posizionamento indicata (asse locale +X lungo il filo).</summary>
    public ComponentEntity CreateInline(Matrix placement)
    {
        ComponentEntity component = new();
        if (_inline != null)
        {
            component.SetInline(_inline, placement);
        }
        return component;
    }

    /// <summary>Testo mostrato nella libreria.</summary>
    public string Name { get; }

    /// <summary>Nome del modello scritto nel componente (senza l'indicazione "personale").</summary>
    public string ModelName { get; }

    public string Prefix { get; }

    public double Width { get; }

    public double Height { get; }

    public double CavityDiameter { get; private init; }

    public int KeyCorner { get; }

    /// <summary>Vero per i connettori definiti da un file di forme.</summary>
    public bool IsCustom => _customParts != null;

    public static IReadOnlyList<ComponentTemplate> All { get; private set; } = BuildAll();

    /// <summary>Rilegge famiglie predefinite e connettori personali (dopo un salvataggio o un'importazione).</summary>
    public static IReadOnlyList<ComponentTemplate> Reload()
    {
        All = BuildAll();
        return All;
    }

    private static IReadOnlyList<ComponentTemplate> BuildAll()
    {
        List<ComponentTemplate> list = ConnectorLibrary.Load(out _);
        list.InsertRange(0, InlineSpec.All.Select(spec => new ComponentTemplate(spec)));
        list.AddRange(CustomConnectorLibrary.Load());
        return list;
    }

    public override string ToString() => Name;

    /// <summary>Entità utilizzabili come parte del disegno di un connettore personalizzato.</summary>
    public static bool IsConnectorPart(EntityBase entity)
    {
        return entity is LineEntity or RectangleEntity or ArcEntity or CircleEntity
            or SplineEntity or TextEntity or PinEntity;
    }

    /// <summary>
    /// Costruisce un modello dalle forme di un file: i pin sono le entità Pin (con il loro numero), il resto è il disegno.
    /// Le coordinate vengono riportate con l'origine nell'angolo in alto a sinistra dell'ingombro.
    /// </summary>
    public static ComponentTemplate? FromShapes(IEnumerable<EntityBase> entities, ConnectorInfo info)
    {
        List<EntityBase> parts = entities.Where(IsConnectorPart).ToList();
        if (parts.Count == 0)
        {
            return null;
        }
        Rect bounds = Rect.Empty;
        foreach (EntityBase part in parts)
        {
            bounds.Union(part.BuildGeometry().Bounds);
        }
        if (bounds.IsEmpty)
        {
            return null;
        }
        Vector shift = new(-bounds.X, -bounds.Y);
        foreach (EntityBase part in parts)
        {
            part.Translate(shift);
        }
        List<PinDef> pins = parts
            .OfType<PinEntity>()
            .Select(pin => new PinDef
            {
                Side = 6,
                Name = pin.Number,
                Fraction = pin.Position.X,
                FractionY = pin.Position.Y
            })
            .ToList();
        List<EntityDto> dtos = parts
            .Where(part => part is not PinEntity)
            .Select(part => EntitySerializer.ToDto(part, null, null))
            .ToList();
        double pinDiameter = parts.OfType<PinEntity>().Select(pin => pin.Diameter).DefaultIfEmpty(PinEntity.DefaultDiameter).Max();
        return new ComponentTemplate(info, bounds.Width, bounds.Height, dtos, pins) { CavityDiameter = pinDiameter };
    }

    /// <summary>Crea un connettore con l'angolo in alto a sinistra in <paramref name="topLeft"/>.</summary>
    public ComponentEntity Create(Point topLeft)
    {
        if (_inline != null)
        {
            // posizione libera (orizzontale): l'inserimento reale avviene sul filo
            return CreateInline(new Matrix(1, 0, 0, 1, topLeft.X, topLeft.Y + _inline.BodyDiameter / 2));
        }
        if (_customParts != null && _customPins != null && _info != null)
        {
            ComponentEntity custom = new()
            {
                Model = ModelName,
                PartNumber = _info.PartNumber,
                Manufacturer = _info.Manufacturer,
                Designation = _info.Description,
                CavityDiameter = CavityDiameter
            };
            custom.SetCustom(
                _customParts,
                Width,
                Height,
                _customPins.Select(pin => new PinDef { Side = pin.Side, Name = pin.Name, Fraction = pin.Fraction, FractionY = pin.FractionY }),
                topLeft);
            return custom;
        }

        ComponentEntity component = new()
        {
            P1 = topLeft,
            P2 = new Point(topLeft.X + Width, topLeft.Y + Height),
            Model = ModelName,
            Physical = true,
            CavityDiameter = CavityDiameter,
            KeyCorner = KeyCorner,
            RealWidth = Width,
            RealHeight = Height
        };
        component.SetPins(_cavities.Select(cavity => new PinDef
        {
            Side = 4,
            Name = cavity.Label,
            Fraction = Width > 0 ? cavity.X / Width : 0.5,
            FractionY = Height > 0 ? cavity.Y / Height : 0.5
        }));
        return component;
    }
}

/// <summary>Cartella dei connettori personali: file di forme (.wcad) marcati come connettore.</summary>
public static class CustomConnectorLibrary
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WireCAD", "Connettori");

    public static List<ComponentTemplate> Load()
    {
        List<ComponentTemplate> templates = new();
        if (!Directory.Exists(Folder))
        {
            return templates;
        }
        foreach (string file in Directory.GetFiles(Folder, "*" + ShapeFile.Extension).OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                ShapeFileLoadResult result = ShapeFile.Load(file);
                if (result.Connector == null)
                {
                    continue;
                }
                ComponentTemplate? template = ComponentTemplate.FromShapes(result.Entities, result.Connector);
                if (template != null)
                {
                    templates.Add(template);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // file illeggibile: si ignora
            }
        }
        return templates;
    }
}
