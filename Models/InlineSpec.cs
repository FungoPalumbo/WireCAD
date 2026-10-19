using System.Globalization;

namespace WireCAD.Models;

/// <summary>Descrizione di un componente da inserire in linea su un filo (assiale, con due reofori).</summary>
public sealed record InlineSpec(
    string Kind,
    string Name,
    string Prefix,
    double BodyLength,
    double BodyDiameter,
    double LeadLength,
    double DrawnLeadLength,
    string Pin1,
    string Pin2,
    bool Bridged,
    string DefaultRating)
{
    /// <summary>Lunghezza complessiva come disegnata (i reofori reali, più lunghi, contano solo nelle lunghezze di taglio).</summary>
    public double TotalLength => BodyLength + 2 * DrawnLeadLength;

    public static IReadOnlyList<InlineSpec> All { get; } = new InlineSpec[]
    {
        new("resistor", "Resistenza (in linea)", "R", 6.3, 2.4, 15, 4, "1", "2", false, "1k"),
        new("diode", "Diodo (in linea)", "D", 5, 2.6, 15, 4, "A", "K", false, "1N4007"),
        new("fuse", "Fusibile in linea", "F", 14, 3.2, 10, 4, "1", "2", false, "5 A"),
        new("splice", "Giunzione saldata (in linea)", "J", 8, 3, 0, 0, "1", "2", true, string.Empty)
    };

    public static InlineSpec? Find(string kind)
    {
        return All.FirstOrDefault(spec => spec.Kind == kind);
    }
}

/// <summary>Codice colori delle resistenze (4 bande: due cifre, moltiplicatore, tolleranza).</summary>
public static class ResistorCode
{
    private static readonly string[] Digit =
    {
        "#111827", "#78350F", "#DC2626", "#EA580C", "#CA8A04",
        "#16A34A", "#2563EB", "#7C3AED", "#6B7280", "#F9FAFB"
    };

    private const string Gold = "#D4A017";
    private const string Silver = "#C0C0C0";

    /// <summary>Interpreta valori come "220", "4.7k", "4k7", "1M", "10R", "0.5", con eventuale simbolo Ω.</summary>
    public static bool TryParse(string? text, out double ohms)
    {
        ohms = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string value = text.Trim()
            .Replace("Ω", string.Empty)
            .Replace("ohm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace(',', '.');
        if (value.Length == 0)
        {
            return false;
        }
        double multiplier = 1;
        int position = value.IndexOfAny(new[] { 'R', 'r', 'k', 'K', 'M', 'G' });
        if (position >= 0)
        {
            switch (char.ToUpperInvariant(value[position]))
            {
                case 'K':
                    multiplier = 1e3;
                    break;
                case 'M':
                    multiplier = 1e6;
                    break;
                case 'G':
                    multiplier = 1e9;
                    break;
            }
            string left = value.Substring(0, position);
            string right = value.Substring(position + 1);
            value = left + (right.Length > 0 ? "." + right : string.Empty);
        }
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed <= 0)
        {
            return false;
        }
        ohms = parsed * multiplier;
        return true;
    }

    private static string ToleranceColor(string? tolerance)
    {
        string text = (tolerance ?? string.Empty).Replace("%", string.Empty).Replace(" ", string.Empty).Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
        {
            return Gold;
        }
        if (Math.Abs(percent - 1) < 1e-9)
        {
            return Digit[1];
        }
        if (Math.Abs(percent - 2) < 1e-9)
        {
            return Digit[2];
        }
        if (Math.Abs(percent - 0.5) < 1e-9)
        {
            return Digit[5];
        }
        if (Math.Abs(percent - 0.25) < 1e-9)
        {
            return Digit[6];
        }
        if (Math.Abs(percent - 0.1) < 1e-9)
        {
            return Digit[7];
        }
        if (Math.Abs(percent - 10) < 1e-9)
        {
            return Silver;
        }
        return Gold;
    }

    /// <summary>I quattro colori delle bande (cifra, cifra, moltiplicatore, tolleranza) oppure null se il valore non è valido.</summary>
    public static IReadOnlyList<string>? Bands(string? value, string? tolerance)
    {
        if (!TryParse(value, out double ohms))
        {
            return null;
        }
        int exponent = (int)Math.Floor(Math.Log10(ohms)) - 1;
        int digits = (int)Math.Round(ohms / Math.Pow(10, exponent));
        if (digits >= 100)
        {
            digits /= 10;
            exponent += 1;
        }
        if (digits < 10 || exponent < -2 || exponent > 9)
        {
            return null;
        }
        string multiplier = exponent == -2 ? Silver : exponent == -1 ? Gold : Digit[exponent];
        return new[] { Digit[digits / 10], Digit[digits % 10], multiplier, ToleranceColor(tolerance) };
    }
}
