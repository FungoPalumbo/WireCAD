using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using AcColor = ACadSharp.Color;
using AcText = ACadSharp.Entities.TextEntity;

namespace WireCAD.Models;

/// <summary>
/// Esportazione in DXF e DWG tramite la libreria ACadSharp (MIT): un solo costruttore del documento per entrambi i formati.
/// Le forme riempite diventano HATCH pieni; i riempimenti trasparenti sono fusi con il bianco del foglio e le sole
/// decorazioni semitrasparenti (riflessi, smussature) vengono omesse. Le curve sono scritte come segmenti.
/// </summary>
public static class CadExporter
{
    public static void WriteDwg(string path, ExportScene scene)
    {
        using DwgWriter writer = new(path, Build(scene));
        writer.Write();
    }

    public static void WriteDxf(string path, ExportScene scene)
    {
        using DxfWriter writer = new(path, Build(scene), false);
        writer.Write();
    }

    /// <summary>Documento CAD con tutte le primitive della scena (Y verso l'alto, come nei CAD).</summary>
    public static CadDocument Build(ExportScene scene)
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

        XYZ ToCad(System.Windows.Point point) => new(point.X, height - point.Y, 0);

        foreach (ExportPolyline polyline in scene.Polylines.Concat(SceneBuilder.BandOutlines(scene)))
        {
            if (polyline.Decorative || polyline.Points.Count < 2)
            {
                continue;
            }
            Layer layer = GetLayer(polyline.Layer);
            bool filled = polyline.Filled && polyline.Closed && polyline.Points.Count >= 3;
            if (filled)
            {
                string fillHex = polyline.FillColor ?? polyline.Color;
                if (polyline.FillOpacity < 0.999)
                {
                    fillHex = SceneBuilder.BlendWithWhite(fillHex, polyline.FillOpacity);
                }
                (double r, double g, double b) = SceneBuilder.Rgb(fillHex);
                Hatch hatch = new()
                {
                    Layer = layer,
                    IsSolid = true,
                    Pattern = HatchPattern.Solid,
                    Color = AcColor.FromTrueColor((uint)((int)Math.Round(r * 255) << 16 | (int)Math.Round(g * 255) << 8 | (int)Math.Round(b * 255)))
                };
                hatch.Paths.Add(new Hatch.BoundaryPath(new Hatch.BoundaryPath.Edge[]
                {
                    new Hatch.BoundaryPath.Polyline(polyline.Points.Select(ToCad).ToList(), true)
                }));
                document.Entities.Add(hatch);
                if (polyline.Width <= SceneBuilder.HairlineWidth)
                {
                    // forma di sola superficie (senza contorno proprio)
                    continue;
                }
            }

            LwPolyline outline = new()
            {
                Layer = layer,
                Color = new AcColor(SceneBuilder.AciColor(polyline.Color)),
                IsClosed = polyline.Closed && polyline.Points.Count > 2
            };
            foreach (System.Windows.Point point in polyline.Points)
            {
                outline.Vertices.Add(new LwPolyline.Vertex(point.X, height - point.Y));
            }
            document.Entities.Add(outline);
        }

        foreach (ExportText text in scene.Texts)
        {
            if (string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }
            System.Windows.Point origin = text.BaselineOrigin(height);
            document.Entities.Add(new AcText
            {
                Value = text.Text,
                InsertPoint = new XYZ(origin.X, origin.Y, 0),
                Height = text.Height,
                Layer = GetLayer(text.Layer)
            });
        }
        return document;
    }
}
