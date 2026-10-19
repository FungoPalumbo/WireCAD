namespace WireCAD.Models;

/// <summary>
/// Rappresentazione grafica: le dimensioni reali (diametri, spessori) restano dati dell'oggetto, mentre nel disegno
/// possono essere mostrate più grandi per leggibilità.
/// </summary>
public static class DrawingScale
{
    public const double MinFactor = 0.2;
    public const double MaxFactor = 50;

    /// <summary>Ingrandimento applicato agli spessori di fili e guaine nel disegno (1 = dimensioni reali).</summary>
    public static double ThicknessFactor { get; set; } = 1;

    public static double ClampFactor(double value)
    {
        return Math.Clamp(value, MinFactor, MaxFactor);
    }
}
