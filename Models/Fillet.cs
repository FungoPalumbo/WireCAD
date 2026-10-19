using System.Windows;

namespace WireCAD.Models;

/// <summary>Raccordo (fillet) tra due linee con un arco di raggio assegnato.</summary>
public static class Fillet
{
    private static Vector Unit(Vector vector)
    {
        double length = vector.Length;
        return length < 1e-12 ? new Vector(0, 0) : new Vector(vector.X / length, vector.Y / length);
    }

    private static Point Round(Point point)
    {
        return new Point(Math.Round(point.X, 6), Math.Round(point.Y, 6));
    }

    /// <summary>
    /// Raccorda le due linee: ciascuna viene accorciata (o prolungata) fino al punto di tangenza e viene restituito l'arco.
    /// Le parti conservate sono quelle dal lato in cui si è cliccato. Se non è possibile restituisce null, spiega il motivo
    /// in <paramref name="error"/> e non modifica le linee.
    /// </summary>
    public static ArcEntity? Apply(
        LineEntity first,
        Point firstClick,
        LineEntity second,
        Point secondClick,
        double radius,
        out string error)
    {
        error = string.Empty;
        Vector d1 = Unit(first.B - first.A);
        Vector d2 = Unit(second.B - second.A);
        if (d1.Length < 0.5 || d2.Length < 0.5)
        {
            error = "Raccordo impossibile: un tratto ha lunghezza nulla.";
            return null;
        }
        double cross = Vector.CrossProduct(d1, d2);
        if (Math.Abs(cross) < 1e-6)
        {
            error = "Raccordo impossibile: i due tratti sono paralleli.";
            return null;
        }

        double distance = Vector.CrossProduct(second.A - first.A, d2) / cross;
        Point corner = first.A + d1 * distance;

        Vector u1 = (firstClick - corner) * d1 >= 0 ? d1 : -d1;
        Vector u2 = (secondClick - corner) * d2 >= 0 ? d2 : -d2;
        double theta = Math.Acos(Math.Clamp(u1 * u2, -1, 1));
        if (theta < 0.01 || theta > Math.PI - 0.01)
        {
            error = "Raccordo impossibile: l'angolo tra i tratti è troppo piccolo o piatto.";
            return null;
        }

        double tangent = radius / Math.Tan(theta / 2);
        double a1 = (first.A - corner) * u1;
        double b1 = (first.B - corner) * u1;
        double a2 = (second.A - corner) * u2;
        double b2 = (second.B - corner) * u2;
        if (Math.Max(a1, b1) < tangent - 1e-6)
        {
            error = "Raggio troppo grande per il primo tratto.";
            return null;
        }
        if (Math.Max(a2, b2) < tangent - 1e-6)
        {
            error = "Raggio troppo grande per il secondo tratto.";
            return null;
        }

        Point t1 = Round(corner + u1 * tangent);
        Point t2 = Round(corner + u2 * tangent);
        Vector bisector = Unit(u1 + u2);
        Point middle = Round(corner + bisector * (radius / Math.Sin(theta / 2) - radius));

        if (a1 <= b1)
        {
            first.A = t1;
        }
        else
        {
            first.B = t1;
        }
        if (a2 <= b2)
        {
            second.A = t2;
        }
        else
        {
            second.B = t2;
        }
        return new ArcEntity { Start = t1, Through = middle, End = t2 };
    }
}
