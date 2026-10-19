using System.Globalization;
using System.IO;
using System.Text;

namespace WireCAD.Models;

/// <summary>Esportazione PDF vettoriale (una pagina in scala 1:1, font Helvetica), scritta senza librerie esterne.</summary>
public static class PdfExporter
{
    private const double PointsPerMm = 72.0 / 25.4;

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string text)
    {
        StringBuilder sb = new();
        foreach (char c in text)
        {
            if (c > 255 || c < 32)
            {
                sb.Append(c == '\t' ? ' ' : '?');
            }
            else if (c == '(' || c == ')' || c == '\\')
            {
                sb.Append('\\').Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public static void Write(string path, ExportScene scene)
    {
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(Build(scene)));
    }

    public static string Build(ExportScene scene)
    {
        double k = PointsPerMm;
        double height = scene.Height;
        StringBuilder content = new();
        content.Append("1 j 1 J\n");

        Dictionary<double, string> alphas = new();
        string GsName(double opacity)
        {
            if (!alphas.TryGetValue(opacity, out string? name))
            {
                name = "GS" + alphas.Count.ToString(CultureInfo.InvariantCulture);
                alphas[opacity] = name;
            }
            return name;
        }

        foreach (ExportPolyline polyline in scene.Polylines)
        {
            bool translucent = polyline.Filled && polyline.FillOpacity < 0.999;
            if (translucent)
            {
                content.Append($"q /{GsName(polyline.FillOpacity)} gs\n");
            }
            (double r, double g, double b) = SceneBuilder.Rgb(polyline.Color);
            (double fr, double fg, double fb) = SceneBuilder.Rgb(polyline.FillColor ?? polyline.Color);
            content.Append($"{F(r)} {F(g)} {F(b)} RG {F(fr)} {F(fg)} {F(fb)} rg {F(Math.Max(0.15, polyline.Width * k))} w\n");
            for (int i = 0; i < polyline.Points.Count; i++)
            {
                content.Append($"{F(polyline.Points[i].X * k)} {F((height - polyline.Points[i].Y) * k)} {(i == 0 ? "m" : "l")}\n");
            }
            if (polyline.Closed && polyline.Filled)
            {
                content.Append("b\n");
            }
            else if (polyline.Closed)
            {
                content.Append("s\n");
            }
            else
            {
                content.Append("S\n");
            }
            if (translucent)
            {
                content.Append("Q\n");
            }
        }

        foreach (ExportBand band in scene.Bands)
        {
            if (band.Points.Count < 2)
            {
                continue;
            }
            StringBuilder route = new();
            for (int i = 0; i < band.Points.Count; i++)
            {
                route.Append($"{F(band.Points[i].X * k)} {F((height - band.Points[i].Y) * k)} {(i == 0 ? "m" : "l")}\n");
            }
            double innerWidth = Math.Max(0.2, band.Diameter - 2 * SceneBuilder.BandBorder);
            (double outR, double outG, double outB) = SceneBuilder.Rgb(band.OutlineColor);
            (double fillR, double fillG, double fillB) = SceneBuilder.Rgb(band.FillColor);
            content.Append("0 J 1 j\n");
            content.Append($"{F(outR)} {F(outG)} {F(outB)} RG {F(band.Diameter * k)} w\n{route}S\n");
            content.Append($"{F(fillR)} {F(fillG)} {F(fillB)} RG {F(innerWidth * k)} w\n{route}S\n");
            if (band.StripeColor != null)
            {
                (double sr, double sg, double sb) = SceneBuilder.Rgb(band.StripeColor);
                content.Append($"{F(sr)} {F(sg)} {F(sb)} RG {F(innerWidth * k)} w [{F(2 * k)} {F(2 * k)}] 0 d\n{route}S\n[] 0 d\n");
            }
            content.Append("1 J\n");
        }

        content.Append("0 0 0 rg\n");
        foreach (ExportText text in scene.Texts)
        {
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }
            System.Windows.Point origin = text.BaselineOrigin(height);
            content.Append($"BT /F1 {F(text.Height * k)} Tf {F(origin.X * k)} {F(origin.Y * k)} Td ({Escape(text.Text)}) Tj ET\n");
        }

        string stream = content.ToString();
        StringBuilder pdf = new();
        List<int> offsets = new();

        void AddObject(string body)
        {
            offsets.Add(pdf.Length);
            pdf.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        pdf.Append("%PDF-1.4\n");
        AddObject("<< /Type /Catalog /Pages 2 0 R >>");
        AddObject("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        string extGState = alphas.Count == 0
            ? string.Empty
            : " /ExtGState << " + string.Join(" ", alphas.Select(pair => $"/{pair.Value} << /Type /ExtGState /ca {F(pair.Key)} /CA 1 >>")) + " >>";
        AddObject($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(scene.Width * k)} {F(height * k)}] /Resources << /Font << /F1 5 0 R >>{extGState} >> /Contents 4 0 R >>");
        AddObject($"<< /Length {stream.Length} >>\nstream\n{stream}endstream");
        AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        int xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        pdf.Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }
        pdf.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\n");
        pdf.Append("startxref\n").Append(xref).Append("\n%%EOF\n");
        return pdf.ToString();
    }
}
