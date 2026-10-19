using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace WireCAD.Models;

/// <summary>Esportazione SVG (grafica vettoriale), in millimetri.</summary>
public static class SvgExporter
{
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static void Write(string path, ExportScene scene)
    {
        File.WriteAllText(path, Build(scene), new UTF8Encoding(false));
    }

    public static string Build(ExportScene scene)
    {
        StringBuilder sb = new();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(scene.Width)}mm\" height=\"{F(scene.Height)}mm\" viewBox=\"0 0 {F(scene.Width)} {F(scene.Height)}\">");
        sb.AppendLine($"<rect x=\"0\" y=\"0\" width=\"{F(scene.Width)}\" height=\"{F(scene.Height)}\" fill=\"#FFFFFF\"/>");

        foreach (ExportPolyline polyline in scene.Polylines)
        {
            StringBuilder d = new();
            for (int i = 0; i < polyline.Points.Count; i++)
            {
                d.Append(i == 0 ? "M" : "L").Append(F(polyline.Points[i].X)).Append(' ').Append(F(polyline.Points[i].Y)).Append(' ');
            }
            if (polyline.Closed)
            {
                d.Append('Z');
            }
            string fill = polyline.Filled ? (polyline.FillColor ?? polyline.Color) : "none";
            string opacity = polyline.Filled && polyline.FillOpacity < 0.999
                ? $" fill-opacity=\"{F(polyline.FillOpacity)}\""
                : string.Empty;
            sb.AppendLine($"<path d=\"{d.ToString().Trim()}\" fill=\"{fill}\"{opacity} stroke=\"{polyline.Color}\" stroke-width=\"{F(polyline.Width)}\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
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
                route.Append(i == 0 ? "M" : "L").Append(F(band.Points[i].X)).Append(' ').Append(F(band.Points[i].Y)).Append(' ');
            }
            string d = route.ToString().Trim();
            double innerWidth = Math.Max(0.2, band.Diameter - 2 * SceneBuilder.BandBorder);
            sb.AppendLine($"<path d=\"{d}\" fill=\"none\" stroke=\"{band.OutlineColor}\" stroke-width=\"{F(band.Diameter)}\" stroke-linejoin=\"round\" stroke-linecap=\"butt\"/>");
            sb.AppendLine($"<path d=\"{d}\" fill=\"none\" stroke=\"{band.FillColor}\" stroke-width=\"{F(innerWidth)}\" stroke-linejoin=\"round\" stroke-linecap=\"butt\"/>");
            if (band.StripeColor != null)
            {
                sb.AppendLine($"<path d=\"{d}\" fill=\"none\" stroke=\"{band.StripeColor}\" stroke-width=\"{F(innerWidth)}\" stroke-linejoin=\"round\" stroke-linecap=\"butt\" stroke-dasharray=\"2 2\"/>");
            }
        }

        foreach (ExportText text in scene.Texts)
        {
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }
            string anchor = text.AlignX < 0.25 ? "start" : text.AlignX < 0.75 ? "middle" : "end";
            double baseline = text.Y - text.Height * text.AlignY + text.Height * 0.8;
            sb.AppendLine($"<text x=\"{F(text.X)}\" y=\"{F(baseline)}\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"{F(text.Height)}\" text-anchor=\"{anchor}\" fill=\"#000000\">{SecurityElement.Escape(text.Text)}</text>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }
}
