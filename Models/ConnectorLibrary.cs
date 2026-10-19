using System.IO;
using System.Text.Json;

namespace WireCAD.Models;

public sealed class CavityDto
{
    public string Label { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// Descrizione di una famiglia di connettori. Se <see cref="Cavities"/> è vuoto le cavità sono generate
/// da righe, colonne e passo; altrimenti sono usate le coordinate indicate (mm dall'angolo in alto a sinistra).
/// </summary>
public sealed class ConnectorSpec
{
    public string Name { get; set; } = "Connettore";
    public string Prefix { get; set; } = "X";
    public int Rows { get; set; } = 1;
    public int Cols { get; set; } = 2;
    public double PitchX { get; set; } = 2.5;
    public double PitchY { get; set; } = 2.5;
    public double MarginX { get; set; } = 2.5;
    public double MarginY { get; set; } = 2.5;
    public double CavityDiameter { get; set; } = 1.6;

    /// <summary>"rows": numerazione per righe (prima riga da sinistra a destra, poi la successiva); "columns": per colonne dall'alto in basso.</summary>
    public string Numbering { get; set; } = "rows";

    /// <summary>Angolo smussato di polarizzazione: 0 alto-sinistra, 1 alto-destra, 2 basso-destra, 3 basso-sinistra, -1 nessuno.</summary>
    public int KeyCorner { get; set; } = 0;

    public double? Width { get; set; }
    public double? Height { get; set; }
    public List<CavityDto>? Cavities { get; set; }
}

public sealed class ConnectorLibraryFile
{
    public string Note { get; set; } = "";
    public List<ConnectorSpec> Connectors { get; set; } = new();
}

/// <summary>
/// Libreria dei connettori fisici. Viene letta da %AppData%\WireCAD\libreria-connettori.json (creata con valori
/// indicativi se manca): le quote vanno verificate sui disegni del costruttore e modificate nel file.
/// </summary>
public static class ConnectorLibrary
{
    private const string FileName = "libreria-connettori.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WireCAD", FileName);

    public static List<ComponentTemplate> Load(out string? error)
    {
        error = null;
        ConnectorLibraryFile file;
        try
        {
            if (!File.Exists(FilePath))
            {
                file = Defaults();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(file, Options));
            }
            else
            {
                file = JsonSerializer.Deserialize<ConnectorLibraryFile>(File.ReadAllText(FilePath), Options) ?? Defaults();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = $"Libreria connettori non leggibile ({ex.Message}): uso i valori predefiniti.";
            file = Defaults();
        }

        List<ComponentTemplate> templates = new();
        foreach (ConnectorSpec spec in file.Connectors)
        {
            templates.Add(Build(spec));
        }
        return templates;
    }

    private static ComponentTemplate Build(ConnectorSpec spec)
    {
        List<CavitySpec> cavities = new();
        double width;
        double height;
        if (spec.Cavities != null && spec.Cavities.Count > 0)
        {
            foreach (CavityDto cavity in spec.Cavities)
            {
                cavities.Add(new CavitySpec(cavity.Label, cavity.X, cavity.Y));
            }
            width = spec.Width ?? cavities.Max(c => c.X) + spec.MarginX;
            height = spec.Height ?? cavities.Max(c => c.Y) + spec.MarginY;
        }
        else
        {
            int rows = Math.Max(1, spec.Rows);
            int cols = Math.Max(1, spec.Cols);
            bool byColumns = string.Equals(spec.Numbering, "columns", StringComparison.OrdinalIgnoreCase);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int number = byColumns ? c * rows + r + 1 : r * cols + c + 1;
                    cavities.Add(new CavitySpec(
                        number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        spec.MarginX + c * spec.PitchX,
                        spec.MarginY + r * spec.PitchY));
                }
            }
            width = spec.Width ?? (cols - 1) * spec.PitchX + 2 * spec.MarginX;
            height = spec.Height ?? (rows - 1) * spec.PitchY + 2 * spec.MarginY;
        }
        return new ComponentTemplate(spec.Name, spec.Prefix, width, height, spec.CavityDiameter, spec.KeyCorner, cavities);
    }

    private static ConnectorSpec Family(string name, int rows, int cols, double pitch, double margin, double diameter)
    {
        return new ConnectorSpec
        {
            Name = name,
            Prefix = "X",
            Rows = rows,
            Cols = cols,
            PitchX = pitch,
            PitchY = pitch,
            MarginX = margin,
            MarginY = margin,
            CavityDiameter = diameter
        };
    }

    /// <summary>Valori INDICATIVI: verificare passo, ingombri e numerazione sul disegno del costruttore.</summary>
    private static ConnectorLibraryFile Defaults()
    {
        ConnectorLibraryFile file = new()
        {
            Note = "Quote indicative: verificare sui disegni del costruttore. Unità: mm, origine nell'angolo in alto a sinistra del corpo (vista frontale)."
        };
        foreach (int cols in new[] { 2, 3, 4, 6 })
        {
            file.Connectors.Add(Family($"Micro-Fit 3.0 - 2x{cols}", 2, cols, 3.0, 2.2, 1.9));
        }
        foreach (int cols in new[] { 2, 3, 4, 6 })
        {
            file.Connectors.Add(Family($"Mini-Fit Jr 4.2 - 2x{cols}", 2, cols, 4.2, 3.0, 2.8));
        }
        foreach (int cols in new[] { 2, 3, 4, 6 })
        {
            file.Connectors.Add(Family($"JST XH 2.5 - 1x{cols}", 1, cols, 2.5, 2.5, 1.6));
        }
        file.Connectors.Add(Family("DT 2 vie", 1, 2, 6.3, 5.0, 3.2));
        file.Connectors.Add(Family("DT 3 vie", 1, 3, 6.3, 5.0, 3.2));
        file.Connectors.Add(Family("DT 4 vie", 2, 2, 6.3, 5.0, 3.2));
        file.Connectors.Add(Family("DT 6 vie", 2, 3, 6.3, 5.0, 3.2));
        file.Connectors.Add(Family("DT 8 vie", 2, 4, 6.3, 5.0, 3.2));
        file.Connectors.Add(Family("DT 12 vie", 2, 6, 6.3, 5.0, 3.2));
        return file;
    }
}
