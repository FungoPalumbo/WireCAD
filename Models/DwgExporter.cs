using ACadSharp;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using AcColor = ACadSharp.Color;
using AcLine = ACadSharp.Entities.Line;
using AcText = ACadSharp.Entities.TextEntity;

namespace WireCAD.Models;

/// <summary>
/// Esportazione DWG tramite la libreria ACadSharp (MIT). Le curve sono scritte come segmenti di linea.
/// </summary>
public static class DwgExporter
{
    public static void Write(string path, ExportScene scene)
    {
        CadDocument document = new();
        Dictionary<string, Layer> layers = new(StringComparer.OrdinalIgnoreCase);
        double height = scene.Height;

        Layer GetLayer(string name)
        {
            if (!layers.TryGetValue(name, out Layer? layer))
            {
                layer = new Layer(name);
                document.Layers.Add(layer);
                layers[name] = layer;
            }
            return layer;
        }

        foreach (ExportPolyline polyline in scene.Polylines.Concat(SceneBuilder.BandOutlines(scene)))
        {
            Layer layer = GetLayer(polyline.Layer);
            AcColor color = new(SceneBuilder.AciColor(polyline.Color));
            int count = polyline.Points.Count;
            int segments = polyline.Closed && count > 2 ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                System.Windows.Point a = polyline.Points[i];
                System.Windows.Point b = polyline.Points[(i + 1) % count];
                AcLine line = new()
                {
                    StartPoint = new XYZ(a.X, height - a.Y, 0),
                    EndPoint = new XYZ(b.X, height - b.Y, 0),
                    Layer = layer,
                    Color = color
                };
                document.Entities.Add(line);
            }
        }

        foreach (ExportText text in scene.Texts)
        {
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }
            System.Windows.Point origin = text.BaselineOrigin(height);
            AcText entity = new()
            {
                Value = text.Text,
                InsertPoint = new XYZ(origin.X, origin.Y, 0),
                Height = text.Height,
                Layer = GetLayer(text.Layer)
            };
            document.Entities.Add(entity);
        }

        using (DwgWriter writer = new(path, document))
        {
            writer.Write();
        }
    }
}
