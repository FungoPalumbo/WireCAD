using System.Globalization;
using System.IO;
using System.Text;

namespace WireCAD.Models;

/// <summary>Esportazione DXF ASCII (formato R12, compatibile con tutti i CAD). Le curve sono scritte come segmenti.</summary>
public static class DxfExporter
{
    public static void Write(string path, ExportScene scene)
    {
        File.WriteAllText(path, Build(scene), Encoding.Latin1);
    }

    public static string Build(ExportScene scene)
    {
        StringBuilder sb = new();
        double height = scene.Height;

        void Group(int code, string value)
        {
            sb.Append(code.ToString(CultureInfo.InvariantCulture)).Append("\r\n").Append(value).Append("\r\n");
        }
        void Number(int code, double value)
        {
            Group(code, value.ToString("0.######", CultureInfo.InvariantCulture));
        }
        void Line(double x1, double y1, double x2, double y2, string layer, short color)
        {
            Group(0, "LINE");
            Group(8, layer);
            Group(62, color.ToString(CultureInfo.InvariantCulture));
            Number(10, x1);
            Number(20, height - y1);
            Number(30, 0);
            Number(11, x2);
            Number(21, height - y2);
            Number(31, 0);
        }

        Group(0, "SECTION");
        Group(2, "HEADER");
        Group(9, "$ACADVER");
        Group(1, "AC1009");
        Group(9, "$DWGCODEPAGE");
        Group(3, "ANSI_1252");
        Group(0, "ENDSEC");

        List<ExportPolyline> polylines = scene.Polylines.Concat(SceneBuilder.BandOutlines(scene)).ToList();
        List<string> layers = polylines.Select(item => item.Layer)
            .Concat(scene.Texts.Select(item => item.Layer))
            .Distinct()
            .ToList();
        if (layers.Count == 0)
        {
            layers.Add("DISEGNO");
        }

        Group(0, "SECTION");
        Group(2, "TABLES");
        Group(0, "TABLE");
        Group(2, "LTYPE");
        Group(70, "1");
        Group(0, "LTYPE");
        Group(2, "CONTINUOUS");
        Group(70, "0");
        Group(3, "Solid line");
        Group(72, "65");
        Group(73, "0");
        Number(40, 0);
        Group(0, "ENDTAB");
        Group(0, "TABLE");
        Group(2, "LAYER");
        Group(70, layers.Count.ToString(CultureInfo.InvariantCulture));
        foreach (string layer in layers)
        {
            Group(0, "LAYER");
            Group(2, layer);
            Group(70, "0");
            Group(62, "7");
            Group(6, "CONTINUOUS");
        }
        Group(0, "ENDTAB");
        Group(0, "ENDSEC");

        Group(0, "SECTION");
        Group(2, "ENTITIES");
        foreach (ExportPolyline polyline in polylines)
        {
            short color = SceneBuilder.AciColor(polyline.Color);
            int count = polyline.Points.Count;
            for (int i = 0; i + 1 < count; i++)
            {
                Line(polyline.Points[i].X, polyline.Points[i].Y, polyline.Points[i + 1].X, polyline.Points[i + 1].Y, polyline.Layer, color);
            }
            if (polyline.Closed && count > 2)
            {
                Line(polyline.Points[count - 1].X, polyline.Points[count - 1].Y, polyline.Points[0].X, polyline.Points[0].Y, polyline.Layer, color);
            }
        }
        foreach (ExportText text in scene.Texts)
        {
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }
            int horizontal = text.AlignX < 0.25 ? 0 : text.AlignX < 0.75 ? 1 : 2;
            int vertical = text.AlignY < 0.25 ? 3 : text.AlignY < 0.75 ? 2 : 1;
            double y = height - text.Y;
            Group(0, "TEXT");
            Group(8, text.Layer);
            Group(62, "7");
            Number(10, text.X);
            Number(20, y);
            Number(30, 0);
            Number(40, text.Height);
            Group(1, text.Text.Replace("\r", " ").Replace("\n", " "));
            Group(72, horizontal.ToString(CultureInfo.InvariantCulture));
            Number(11, text.X);
            Number(21, y);
            Number(31, 0);
            Group(73, vertical.ToString(CultureInfo.InvariantCulture));
        }
        Group(0, "ENDSEC");
        Group(0, "EOF");
        return sb.ToString();
    }
}
