using System.IO;
using System.Text.Json;

namespace WireCAD.Models;

/// <summary>Dati di un connettore salvato come file di forme (se presenti il file è utilizzabile come connettore).</summary>
public sealed class ConnectorInfo
{
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "X";
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed record ShapeFileLoadResult(List<EntityBase> Entities, int Skipped, ConnectorInfo? Connector = null);

public static class ShapeFile
{
    public const string Extension = ".wcad";
    public const string Filter = "Forme WireCAD (*.wcad)|*.wcad|Tutti i file (*.*)|*.*";

    public const string Units = "mm";

    private const string FormatId = "WireCAD.Shapes";
    private const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(IEnumerable<EntityBase> entities, ConnectorInfo? connector = null)
    {
        ShapeFileDto dto = new()
        {
            Format = FormatId,
            Version = CurrentVersion,
            Units = Units,
            Connector = connector,
            Entities = entities
                .Select(entity => EntitySerializer.ToDto(entity, null, null))
                .ToList()
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static void Save(string path, IEnumerable<EntityBase> entities, ConnectorInfo? connector = null)
    {
        File.WriteAllText(path, Serialize(entities, connector));
    }

    public static ShapeFileLoadResult Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Legge un testo di forme; le entità ricevono nuovi Id e i riferimenti interni vengono riscritti.</summary>
    public static ShapeFileLoadResult Parse(string json)
    {
        ShapeFileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ShapeFileDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Il file non è in un formato WireCAD valido.", ex);
        }

        if (dto == null || dto.Format != FormatId)
        {
            throw new InvalidDataException("Il file non è un file di forme WireCAD.");
        }
        if (dto.Version > CurrentVersion)
        {
            throw new InvalidDataException("Il file è stato creato con una versione più recente di WireCAD.");
        }

        if (!string.IsNullOrWhiteSpace(dto.Units) && !string.Equals(dto.Units, Units, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Unità \"{dto.Units}\" non supportata: WireCAD lavora in {Units}.");
        }

        List<(EntityDto Dto, EntityBase Entity)> loaded =
            EntitySerializer.LoadAll(dto.Entities ?? new List<EntityDto>(), false, out int skipped);
        return new ShapeFileLoadResult(loaded.Select(item => item.Entity).ToList(), skipped, dto.Connector);
    }

    private sealed class ShapeFileDto
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string Units { get; set; } = "";
        public ConnectorInfo? Connector { get; set; }
        public List<EntityDto> Entities { get; set; } = new();
    }
}
