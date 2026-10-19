using System.IO;
using System.Text.Json;

namespace WireCAD.Models;

public sealed class ProjectSettings
{
    public double SheetWidth { get; set; } = 297;
    public double SheetHeight { get; set; } = 210;
    public double GridStep { get; set; } = 5;
    public int GridMajorEvery { get; set; } = 10;
    public double GridOriginX { get; set; }
    public double GridOriginY { get; set; }
    public bool ShowTitleBlock { get; set; } = true;
    public string WirePrefix { get; set; } = "W";

    /// <summary>Ingrandimento degli spessori di fili e guaine nel disegno (1 = dimensioni reali).</summary>
    public double ThicknessFactor { get; set; } = 1;
    public Dictionary<string, string> TitleBlock { get; set; } = new();
}

public sealed record LoadedEntity(EntityBase Entity, string? Group, string? GroupPath);

public sealed record ProjectData(ProjectSettings? Settings, List<LoadedEntity> Entities, int Skipped);

/// <summary>File di progetto completo (foglio, cartiglio, entità) e snapshot per undo/redo.</summary>
public static class ProjectFile
{
    public const string Extension = ".wcproj";
    public const string Filter = "Progetto WireCAD (*.wcproj)|*.wcproj|Tutti i file (*.*)|*.*";

    private const string FormatId = "WireCAD.Project";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(
        ProjectSettings? settings,
        IEnumerable<EntityBase> entities,
        Func<EntityBase, (string? Group, string? Path)>? groupOf,
        bool indented)
    {
        List<EntityDto> list = new();
        foreach (EntityBase entity in entities)
        {
            string? group = null;
            string? groupPath = null;
            if (groupOf != null)
            {
                (group, groupPath) = groupOf(entity);
            }
            list.Add(EntitySerializer.ToDto(entity, group, groupPath));
        }
        ProjectDto dto = new()
        {
            Format = FormatId,
            Version = CurrentVersion,
            Units = ShapeFile.Units,
            Settings = settings,
            Entities = list
        };
        return JsonSerializer.Serialize(dto, indented ? IndentedOptions : CompactOptions);
    }

    public static ProjectData Deserialize(string json, bool keepIds)
    {
        ProjectDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ProjectDto>(json, CompactOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Il file non è in un formato WireCAD valido.", ex);
        }

        if (dto == null || dto.Format != FormatId)
        {
            throw new InvalidDataException("Il file non è un progetto WireCAD.");
        }
        if (dto.Version > CurrentVersion)
        {
            throw new InvalidDataException("Il progetto è stato creato con una versione più recente di WireCAD.");
        }

        List<(EntityDto Dto, EntityBase Entity)> loaded =
            EntitySerializer.LoadAll(dto.Entities ?? new List<EntityDto>(), keepIds, out int skipped);
        List<LoadedEntity> items = loaded
            .Select(item => new LoadedEntity(item.Entity, item.Dto.Group, item.Dto.GroupPath))
            .ToList();
        return new ProjectData(dto.Settings, items, skipped);
    }

    public static void Save(
        string path,
        ProjectSettings settings,
        IEnumerable<EntityBase> entities,
        Func<EntityBase, (string? Group, string? Path)>? groupOf)
    {
        File.WriteAllText(path, Serialize(settings, entities, groupOf, true));
    }

    public static ProjectData Load(string path)
    {
        return Deserialize(File.ReadAllText(path), true);
    }

    private sealed class ProjectDto
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string Units { get; set; } = "";
        public ProjectSettings? Settings { get; set; }
        public List<EntityDto> Entities { get; set; } = new();
    }
}
