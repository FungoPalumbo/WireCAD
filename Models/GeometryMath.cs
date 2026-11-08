using System.Windows;

namespace WireCAD.Models;

/// <summary>Funzioni geometriche comuni e tolleranze con nome, usate da modello, report ed esportazioni.</summary>
public static class GeometryMath
{
    /// <summary>Tolleranza (mm) con cui due punti dello stesso disegno si considerano coincidenti.</summary>
    public const double PointEpsilon = 1e-6;

    /// <summary>Tolleranza (mm) con cui l'estremo di un filo si considera collegato a un pin.</summary>
    public const double PinTolerance = 0.01;

    /// <summary>Versore del vettore; il vettore nullo resta nullo.</summary>
    public static Vector Unit(Vector vector)
    {
        double length = vector.Length;
        return length < 1e-12 ? new Vector(0, 0) : new Vector(vector.X / length, vector.Y / length);
    }

    public static bool Near(Point a, Point b, double tolerance = PointEpsilon)
    {
        return Math.Abs(a.X - b.X) < tolerance && Math.Abs(a.Y - b.Y) < tolerance;
    }

    /// <summary>Vero se il punto coincide con la posizione di un pin (con la tolleranza dei collegamenti).</summary>
    public static bool AtPin(Point point, Point pin)
    {
        return Near(point, pin, PinTolerance);
    }

    public static double DistanceToSegment(Point point, Point a, Point b)
    {
        Vector ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-12)
        {
            return (point - a).Length;
        }
        double t = Math.Clamp(((point - a) * ab) / lengthSquared, 0, 1);
        Point projection = a + ab * t;
        return (point - projection).Length;
    }
}
