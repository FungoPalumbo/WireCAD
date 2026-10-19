using System.Windows;

namespace WireCAD.Models;

internal sealed class PointDto
{
    public double X { get; set; }
    public double Y { get; set; }
}

internal sealed class EntityDto
{
    public string Type { get; set; } = "";
    public string? Id { get; set; }
    public string Name { get; set; } = "";
    public string? Group { get; set; }
    public string? GroupPath { get; set; }
    public List<PointDto> Points { get; set; } = new();
    public Dictionary<string, string>? Data { get; set; }
}

/// <summary>Conversione tra entità e DTO, condivisa dai file di forme, dal progetto e dagli snapshot di undo.</summary>
internal static class EntitySerializer
{
    public static EntityDto ToDto(EntityBase entity, string? group, string? groupPath)
    {
        Dictionary<string, string> data = new();
        entity.Export(data);
        if (entity.ShownFields.Count > 0)
        {
            data["_show"] = string.Join("|", entity.ShownFields.OrderBy(key => key, StringComparer.Ordinal));
        }
        if (entity.ShowLabel)
        {
            data["_label"] = "1";
        }
        return new EntityDto
        {
            Type = entity.TypeKey,
            Id = entity.Id.ToString(),
            Name = entity.Name,
            Group = group,
            GroupPath = groupPath,
            Points = entity.GetDefinitionPoints()
                .Select(point => new PointDto { X = point.X, Y = point.Y })
                .ToList(),
            Data = data.Count > 0 ? data : null
        };
    }

    private static EntityBase? Create(string? type, List<Point> points)
    {
        switch (type)
        {
            case "line" when points.Count == 2:
                return new LineEntity { A = points[0], B = points[1] };
            case "rectangle" when points.Count == 2:
                return new RectangleEntity { P1 = points[0], P2 = points[1] };
            case "arc" when points.Count == 3:
                return new ArcEntity { Start = points[0], Through = points[1], End = points[2] };
            case "anchor" when points.Count == 1:
                return new AnchorPointEntity { Position = points[0] };
            case "component" when points.Count == 2:
                return new ComponentEntity { P1 = points[0], P2 = points[1] };
            case "wire" when points.Count >= 2:
                return new WireEntity(points);
            case "junction" when points.Count == 1:
                return new JunctionEntity { Position = points[0] };
            case "text" when points.Count == 1:
                return new TextEntity { Position = points[0] };
            case "table" when points.Count == 1:
                return new TableEntity { Position = points[0] };
            case "sheath" when points.Count >= 2:
                return new SheathEntity(points);
            case "pin" when points.Count == 1:
                return new PinEntity { Position = points[0] };
            case "dimension" when points.Count == 3:
                return new DimensionEntity { P1 = points[0], P2 = points[1], Offset = points[2] };
            case "image" when points.Count == 2:
                return new ImageEntity { TopLeft = points[0], Width = points[1].X - points[0].X };
            case "circle" when points.Count == 2:
                return new CircleEntity { Center = points[0], Radius = (points[1] - points[0]).Length };
            case "spline" when points.Count >= 2:
                return new SplineEntity(points);
            default:
                return null;
        }
    }

    private static EntityBase? FromDto(EntityDto dto)
    {
        List<Point> points = (dto.Points ?? new List<PointDto>())
            .Select(p => new Point(p.X, p.Y))
            .ToList();
        EntityBase? entity = Create(dto.Type, points);
        if (entity == null)
        {
            return null;
        }
        if (dto.Data != null)
        {
            entity.Import(dto.Data);
            if (dto.Data.TryGetValue("_label", out string? label) && label == "1")
            {
                entity.ShowLabel = true;
            }
            if (dto.Data.TryGetValue("_show", out string? shown))
            {
                foreach (string key in shown.Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    entity.ShownFields.Add(key);
                }
            }
        }
        entity.Name = dto.Name ?? "";
        return entity;
    }

    /// <summary>
    /// Crea le entità dai DTO. Con <paramref name="keepIds"/> false assegna nuovi Id e riscrive i riferimenti tra fili e componenti.
    /// </summary>
    public static List<(EntityDto Dto, EntityBase Entity)> LoadAll(IEnumerable<EntityDto> dtos, bool keepIds, out int skipped)
    {
        List<(EntityDto Dto, EntityBase Entity)> result = new();
        Dictionary<Guid, Guid> map = new();
        skipped = 0;
        foreach (EntityDto dto in dtos)
        {
            EntityBase? entity = FromDto(dto);
            if (entity == null)
            {
                skipped++;
                continue;
            }
            if (Guid.TryParse(dto.Id, out Guid oldId))
            {
                if (keepIds)
                {
                    entity.Id = oldId;
                }
                map[oldId] = entity.Id;
            }
            result.Add((dto, entity));
        }
        if (!keepIds)
        {
            foreach (var item in result)
            {
                item.Entity.RemapReferences(oldValue => map.TryGetValue(oldValue, out Guid mapped) ? mapped : (Guid?)null);
            }
        }
        return result;
    }
}
