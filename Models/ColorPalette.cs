using System.Text.RegularExpressions;

namespace WireCAD.Models;

/// <summary>Colori di riempimento selezionabili per nome (tonalità tenui, che lasciano leggibili linee e testi).</summary>
public static class ColorPalette
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bianco"] = "#FFFFFF",
        ["Grigio chiaro"] = "#E5E7EB",
        ["Grigio"] = "#9CA3AF",
        ["Nero"] = "#374151",
        ["Rosso"] = "#FCA5A5",
        ["Arancio"] = "#FDBA74",
        ["Giallo"] = "#FDE68A",
        ["Verde"] = "#86EFAC",
        ["Azzurro"] = "#BAE6FD",
        ["Blu"] = "#93C5FD",
        ["Viola"] = "#C4B5FD",
        ["Rosa"] = "#F9A8D4",
        ["Marrone"] = "#D6B58C"
    };

    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "Nessuno", "Bianco", "Grigio chiaro", "Grigio", "Nero", "Rosso", "Arancio",
        "Giallo", "Verde", "Azzurro", "Blu", "Viola", "Rosa", "Marrone"
    };

    /// <summary>Colore esadecimale di un nome della tavolozza oppure di un valore "#RRGGBB"; null se vuoto o "Nessuno".</summary>
    public static string? Resolve(string? name)
    {
        string text = (name ?? string.Empty).Trim();
        if (text.Length == 0 || string.Equals(text, "Nessuno", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
        {
            return text.ToUpperInvariant();
        }
        return Map.TryGetValue(text, out string? hex) ? hex : null;
    }
}
