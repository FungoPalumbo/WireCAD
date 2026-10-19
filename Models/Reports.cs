using System.Globalization;
using System.Text;
using System.Windows;

namespace WireCAD.Models;

public sealed record ErcIssue(string Severity, string Message);

/// <summary>Controlli di coerenza elettrica (ERC) sul disegno.</summary>
public static class ErcChecker
{
    public static List<ErcIssue> Run(IReadOnlyList<EntityBase> entities, NetResult nets)
    {
        List<ErcIssue> issues = new();
        List<ComponentEntity> components = entities.OfType<ComponentEntity>().ToList();
        List<WireEntity> wires = entities.OfType<WireEntity>().ToList();

        foreach (IGrouping<string, ComponentEntity> duplicate in components
            .Where(item => !string.IsNullOrWhiteSpace(item.Tag))
            .GroupBy(item => item.Tag.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            issues.Add(new ErcIssue("Errore", $"Tag duplicato \"{duplicate.Key}\" su {duplicate.Count()} componenti."));
        }

        foreach (ComponentEntity untagged in components.Where(item => string.IsNullOrWhiteSpace(item.Tag)))
        {
            issues.Add(new ErcIssue("Avviso", $"Componente senza tag ({untagged.Name})."));
        }

        foreach (WireEntity wire in wires)
        {
            string label = string.IsNullOrWhiteSpace(wire.Number) ? wire.Name : wire.Number;
            if (string.IsNullOrWhiteSpace(wire.Number))
            {
                issues.Add(new ErcIssue("Avviso", $"Filo senza numero ({wire.Name})."));
            }
            if (string.IsNullOrWhiteSpace(wire.Color))
            {
                issues.Add(new ErcIssue("Avviso", $"Filo {label}: colore non indicato."));
            }
            if (string.IsNullOrWhiteSpace(wire.Section))
            {
                issues.Add(new ErcIssue("Avviso", $"Filo {label}: sezione non indicata."));
            }
            if (!nets.IsEndConnected(wire, true))
            {
                issues.Add(new ErcIssue("Avviso", $"Filo {label}: estremità iniziale non collegata."));
            }
            if (!nets.IsEndConnected(wire, false))
            {
                issues.Add(new ErcIssue("Avviso", $"Filo {label}: estremità finale non collegata."));
            }
        }

        foreach (SheathEntity sheath in entities.OfType<SheathEntity>())
        {
            string sheathLabel = string.IsNullOrWhiteSpace(sheath.Tag) ? sheath.Name : sheath.Tag;
            if (string.IsNullOrWhiteSpace(sheath.Tag))
            {
                issues.Add(new ErcIssue("Avviso", $"Guaina senza tag ({sheath.Name})."));
            }
            if (SheathAnalyzer.WiresIn(sheath, wires).Count == 0)
            {
                issues.Add(new ErcIssue("Avviso", $"Guaina {sheathLabel}: nessun filo al suo interno."));
            }
            if (sheath.WireCount > 0)
            {
                List<string> freePins = new();
                for (int i = 0; i < sheath.WireCount * 2; i++)
                {
                    if (!nets.IsPinConnected(sheath, i))
                    {
                        freePins.Add(sheath.DescribePin(i));
                    }
                }
                if (freePins.Count > 0)
                {
                    issues.Add(new ErcIssue("Info", $"Guaina {sheathLabel}: pin non collegati ({string.Join(", ", freePins)})."));
                }
            }
        }

        foreach (Net net in nets.Nets)
        {
            List<string> numbers = net.Wires
                .Select(wire => wire.Number.Trim())
                .Where(number => number.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (numbers.Count > 1)
            {
                issues.Add(new ErcIssue("Avviso", $"Potenziale N{net.Index + 1}: fili con numeri diversi ({string.Join(", ", numbers)})."));
            }
        }

        foreach (IGrouping<string, Net> shared in nets.Nets
            .SelectMany(net => net.Wires
                .Select(wire => wire.Number.Trim())
                .Where(number => number.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(number => (Number: number, Net: net)))
            .GroupBy(item => item.Number, item => item.Net, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            issues.Add(new ErcIssue("Errore", $"Numero filo \"{shared.Key}\" usato su {shared.Count()} potenziali diversi."));
        }

        foreach (ComponentEntity component in components)
        {
            List<string> free = new();
            bool anyConnected = false;
            for (int i = 0; i < component.PinCount; i++)
            {
                if (nets.IsPinConnected(component, i))
                {
                    anyConnected = true;
                }
                else
                {
                    string pinName = component.GetPinLabel(i);
                    free.Add(string.IsNullOrEmpty(pinName) ? (i + 1).ToString(CultureInfo.InvariantCulture) : pinName);
                }
            }
            if (component.Bridged && anyConnected)
            {
                continue;
            }
            if (free.Count > 0)
            {
                issues.Add(new ErcIssue("Info", $"{component.Tag}: pin non collegati ({string.Join(", ", free)})."));
            }
        }

        return issues
            .OrderBy(issue => issue.Severity == "Errore" ? 0 : issue.Severity == "Avviso" ? 1 : 2)
            .ToList();
    }

    public static string Format(List<ErcIssue> issues, NetResult nets, IReadOnlyList<EntityBase> entities)
    {
        StringBuilder text = new();
        text.AppendLine($"Componenti: {entities.OfType<ComponentEntity>().Count()}   Fili: {entities.OfType<WireEntity>().Count()}   Potenziali: {nets.Nets.Count}");
        text.AppendLine($"Errori: {issues.Count(issue => issue.Severity == "Errore")}   Avvisi: {issues.Count(issue => issue.Severity == "Avviso")}   Info: {issues.Count(issue => issue.Severity == "Info")}");
        text.AppendLine();
        if (issues.Count == 0)
        {
            text.AppendLine("Nessun problema rilevato.");
        }
        foreach (ErcIssue issue in issues)
        {
            text.AppendLine($"[{issue.Severity.ToUpperInvariant()}] {issue.Message}");
        }
        return text.ToString();
    }
}

/// <summary>Generazione dei report di produzione in formato CSV (separatore punto e virgola).</summary>
public static class Reports
{
    private static string Cell(string? value)
    {
        string text = value ?? string.Empty;
        if (text.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0)
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        return text;
    }

    private static void Row(StringBuilder builder, params string?[] cells)
    {
        builder.AppendLine(string.Join(";", cells.Select(Cell)));
    }

    private static string Num(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);

    private static bool Near(Point a, Point b)
    {
        return Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;
    }

    /// <summary>Descrive cosa si trova in un punto: "TAG:pin", "Giunzione" oppure "libero".</summary>
    public static string EndName(IReadOnlyList<EntityBase> entities, Point point)
    {
        foreach (EntityBase entity in entities)
        {
            for (int i = 0; i < entity.PinCount; i++)
            {
                if (!Near(entity.GetPinPosition(i), point))
                {
                    continue;
                }
                if (entity is ComponentEntity component)
                {
                    return $"{component.Tag}:{component.GetPinLabel(i)}";
                }
                if (entity is SheathEntity sheath)
                {
                    return sheath.DescribePin(i);
                }
                if (entity is PinEntity pinEntity)
                {
                    return $"Pin {pinEntity.Number}";
                }
                return "Giunzione";
            }
        }
        return "libero";
    }

    public static string WireList(IReadOnlyList<EntityBase> entities, NetResult nets)
    {
        StringBuilder builder = new();
        Dictionary<WireEntity, string> sheathOf = SheathTags(entities);
        Row(builder, "N. filo", "Colore", "Sezione (mm2)", "Da", "A", "Lunghezza (mm)", "Cavo", "Segnale",
            "Capicorda da", "Capicorda a", "Potenziale", "Guaina", "Lunghezza totale taglio (mm)");
        Dictionary<WireEntity, double> totalOf = TotalLengths(entities);
        foreach (WireEntity wire in entities.OfType<WireEntity>().OrderBy(item => item.Number, StringComparer.OrdinalIgnoreCase))
        {
            nets.WireNets.TryGetValue(wire, out Net? net);
            string potential = net == null
                ? string.Empty
                : "N" + (net.Index + 1) + (string.IsNullOrEmpty(net.Name) ? string.Empty : " " + net.Name);
            Row(builder, wire.Number, wire.ColorText, wire.Section,
                EndName(entities, wire.StartPoint), EndName(entities, wire.EndPoint),
                Num(wire.EffectiveLength), wire.Cable, wire.Signal,
                wire.TerminalStart, wire.TerminalEnd, potential,
                sheathOf.TryGetValue(wire, out string? sheathTags) ? sheathTags : string.Empty,
                Num(totalOf.TryGetValue(wire, out double total) ? total : wire.EffectiveLength));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Lunghezza totale di taglio di ogni filo: i tratti collegati attraverso gli ingressi/uscite delle guaine
    /// formano un unico conduttore, la cui lunghezza è la somma dei tratti più quella della guaina attraversata.
    /// </summary>
    public static Dictionary<WireEntity, double> TotalLengths(IReadOnlyList<EntityBase> entities)
    {
        List<WireEntity> wires = entities.OfType<WireEntity>().ToList();
        Dictionary<WireEntity, WireEntity> parent = new();
        foreach (WireEntity wire in wires)
        {
            parent[wire] = wire;
        }

        WireEntity Find(WireEntity item)
        {
            while (!ReferenceEquals(parent[item], item))
            {
                parent[item] = parent[parent[item]];
                item = parent[item];
            }
            return item;
        }

        List<(WireEntity Anchor, SheathEntity Sheath)> passes = new();
        foreach (SheathEntity sheath in entities.OfType<SheathEntity>().Where(item => item.WireCount > 0))
        {
            for (int k = 0; k < sheath.WireCount; k++)
            {
                if (sheath.IsLinkBroken(k))
                {
                    // il collegamento è fatto da fili veri: la loro lunghezza è già nel conteggio
                    continue;
                }
                Point entry = sheath.GetPinPosition(k);
                Point exit = sheath.GetPinPosition(sheath.WireCount + k);
                List<WireEntity> before = wires.Where(item => Near(item.StartPoint, entry) || Near(item.EndPoint, entry)).ToList();
                List<WireEntity> after = wires.Where(item => Near(item.StartPoint, exit) || Near(item.EndPoint, exit)).ToList();
                if (before.Count == 0 || after.Count == 0)
                {
                    continue;
                }
                foreach (WireEntity first in before)
                {
                    foreach (WireEntity second in after)
                    {
                        parent[Find(first)] = Find(second);
                    }
                }
                passes.Add((before[0], sheath));
            }
        }

        Dictionary<WireEntity, double> totals = new();
        foreach (WireEntity wire in wires)
        {
            WireEntity root = Find(wire);
            totals[root] = (totals.TryGetValue(root, out double current) ? current : 0) + wire.EffectiveLength;
        }
        foreach (var pass in passes)
        {
            WireEntity root = Find(pass.Anchor);
            totals[root] += pass.Sheath.EffectiveLength;
        }

        Dictionary<WireEntity, double> result = new();
        foreach (WireEntity wire in wires)
        {
            result[wire] = totals[Find(wire)];
        }
        return result;
    }

    /// <summary>Per ogni filo, i tag delle guaine che lo contengono (separati da "+").</summary>
    public static Dictionary<WireEntity, string> SheathTags(IReadOnlyList<EntityBase> entities)
    {
        List<WireEntity> wires = entities.OfType<WireEntity>().ToList();
        Dictionary<WireEntity, string> map = new();
        foreach (SheathEntity sheath in entities.OfType<SheathEntity>())
        {
            foreach (WireEntity wire in SheathAnalyzer.WiresIn(sheath, wires))
            {
                map[wire] = map.TryGetValue(wire, out string? existing) ? existing + "+" + sheath.Tag : sheath.Tag;
            }
        }
        return map;
    }

    /// <summary>Righe della tabella fili da inserire nel disegno (prima riga: intestazioni).</summary>
    public static List<string[]> WireRows(IReadOnlyList<EntityBase> entities)
    {
        List<string[]> rows = new()
        {
            new[] { "N. filo", "Colore", "Sez. mm²", "Da", "A", "Lung. mm", "Capicorda", "Guaina", "Taglio mm" }
        };
        Dictionary<WireEntity, string> sheathOf = SheathTags(entities);
        Dictionary<WireEntity, double> totalOf = TotalLengths(entities);
        foreach (WireEntity wire in entities.OfType<WireEntity>().OrderBy(item => item.Number, StringComparer.OrdinalIgnoreCase))
        {
            string terminals = string.IsNullOrWhiteSpace(wire.TerminalStart) && string.IsNullOrWhiteSpace(wire.TerminalEnd)
                ? string.Empty
                : wire.TerminalStart + " / " + wire.TerminalEnd;
            rows.Add(new[]
            {
                wire.Number, wire.ColorText, wire.Section,
                EndName(entities, wire.StartPoint), EndName(entities, wire.EndPoint),
                Num(wire.EffectiveLength), terminals,
                sheathOf.TryGetValue(wire, out string? sheathTags) ? sheathTags : string.Empty,
                Num(totalOf.TryGetValue(wire, out double total) ? total : wire.EffectiveLength)
            });
        }
        return rows;
    }

    /// <summary>Righe della distinta materiali da inserire nel disegno (connettori e componenti).</summary>
    public static List<string[]> BomRows(IReadOnlyList<EntityBase> entities)
    {
        List<string[]> rows = new()
        {
            new[] { "Qta", "Rif.", "Tipo", "Codice articolo", "Costruttore" }
        };
        foreach (var group in entities.OfType<ComponentEntity>()
            .GroupBy(item => (item.Model, item.PartNumber, item.Manufacturer))
            .OrderBy(item => item.Key.Model, StringComparer.OrdinalIgnoreCase))
        {
            string references = string.Join(" ", group
                .Select(item => item.Tag)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            rows.Add(new[]
            {
                group.Count().ToString(CultureInfo.InvariantCulture), references,
                group.Key.Model, group.Key.PartNumber, group.Key.Manufacturer
            });
        }
        foreach (var sheathGroup in entities.OfType<SheathEntity>()
            .GroupBy(item => (item.SheathType, item.PartNumber, item.Manufacturer, item.Diameter))
            .OrderBy(item => item.Key.SheathType, StringComparer.OrdinalIgnoreCase))
        {
            string references = string.Join(" ", sheathGroup
                .Select(item => item.Tag)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            rows.Add(new[]
            {
                sheathGroup.Count().ToString(CultureInfo.InvariantCulture), references,
                $"{sheathGroup.Key.SheathType} Ø{sheathGroup.Key.Diameter:0.#}",
                sheathGroup.Key.PartNumber, sheathGroup.Key.Manufacturer
            });
        }
        return rows;
    }

    public static string Bom(IReadOnlyList<EntityBase> entities)
    {
        StringBuilder builder = new();
        List<ComponentEntity> components = entities.OfType<ComponentEntity>().ToList();
        List<WireEntity> wires = entities.OfType<WireEntity>().ToList();

        Row(builder, "COMPONENTI");
        Row(builder, "Qta", "Tipo", "Codice articolo", "Costruttore", "Descrizione", "Valore / portata", "Riferimenti");
        foreach (var group in components
            .GroupBy(item => (item.Model, item.PartNumber, item.Manufacturer, item.Designation, Rating: item.RatingSummary))
            .OrderBy(item => item.Key.Model, StringComparer.OrdinalIgnoreCase))
        {
            string references = string.Join(" ", group
                .Select(item => item.Tag)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase));
            Row(builder, group.Count().ToString(CultureInfo.InvariantCulture), group.Key.Model, group.Key.PartNumber,
                group.Key.Manufacturer, group.Key.Designation, group.Key.Rating, references);
        }

        builder.AppendLine();
        Row(builder, "FILI");
        Row(builder, "Qta", "Colore", "Sezione (mm2)", "Cavo", "Lunghezza totale (m)");
        foreach (var group in wires
            .GroupBy(item => (Color: item.ColorText, item.Section, item.Cable))
            .OrderBy(item => item.Key.Section, StringComparer.OrdinalIgnoreCase))
        {
            double meters = group.Sum(item => item.EffectiveLength) / 1000.0;
            Row(builder, group.Count().ToString(CultureInfo.InvariantCulture), group.Key.Color, group.Key.Section,
                group.Key.Cable, meters.ToString("0.###", CultureInfo.CurrentCulture));
        }

        builder.AppendLine();
        Row(builder, "CAPICORDA");
        Row(builder, "Qta", "Tipo");
        foreach (var group in wires
            .SelectMany(item => new[] { item.TerminalStart, item.TerminalEnd })
            .Where(terminal => !string.IsNullOrWhiteSpace(terminal))
            .GroupBy(terminal => terminal.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            Row(builder, group.Count().ToString(CultureInfo.InvariantCulture), group.Key);
        }

        builder.AppendLine();
        Row(builder, "GUAINE");
        Row(builder, "Tag", "Tipo", "Codice articolo", "Costruttore", "Diametro (mm)", "Lunghezza (mm)", "Fili contenuti",
            "Sguainatura inizio (mm)", "Sguainatura fine (mm)", "Strato interno");
        foreach (SheathEntity sheath in entities.OfType<SheathEntity>().OrderBy(item => item.Tag, StringComparer.OrdinalIgnoreCase))
        {
            string contained = string.Join(" ", SheathAnalyzer.WiresIn(sheath, wires)
                .Select(item => item.Number)
                .OrderBy(number => number, StringComparer.OrdinalIgnoreCase));
            Row(builder, sheath.Tag, sheath.SheathType, sheath.PartNumber, sheath.Manufacturer,
                Num(sheath.Diameter), Num(sheath.EffectiveLength), contained,
                Num(sheath.StripStart), Num(sheath.StripEnd),
                sheath.StripStart > 0 || sheath.StripEnd > 0 ? sheath.InnerLayer : string.Empty);
        }
        return builder.ToString();
    }

    public static string PinTable(IReadOnlyList<EntityBase> entities)
    {
        StringBuilder builder = new();
        List<WireEntity> wires = entities.OfType<WireEntity>().ToList();
        Row(builder, "Componente", "Pin", "Nome pin", "Filo", "Destinazione");
        foreach (ComponentEntity component in entities.OfType<ComponentEntity>().OrderBy(item => item.Tag, StringComparer.OrdinalIgnoreCase))
        {
            for (int i = 0; i < component.PinCount; i++)
            {
                Point pin = component.GetPinPosition(i);
                List<WireEntity> attached = wires
                    .Where(wire => Near(wire.StartPoint, pin) || Near(wire.EndPoint, pin))
                    .ToList();
                string pinIndex = (i + 1).ToString(CultureInfo.InvariantCulture);
                if (attached.Count == 0)
                {
                    Row(builder, component.Tag, pinIndex, component.GetPinLabel(i), string.Empty, string.Empty);
                    continue;
                }
                foreach (WireEntity wire in attached)
                {
                    Point other = Near(wire.StartPoint, pin) ? wire.EndPoint : wire.StartPoint;
                    Row(builder, component.Tag, pinIndex, component.GetPinLabel(i), wire.Number, EndName(entities, other));
                }
            }
        }
        return builder.ToString();
    }
}
