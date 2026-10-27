using System.Windows;
using System.Windows.Media;
using WireCAD.Models;
using Path = System.Windows.Shapes.Path;

namespace WireCAD;

// Parte della finestra dedicata ai fili twistati: ogni filo dell'intreccio è composto da tratti colorati con contorno
// scuro, disegnati dal più lontano al più vicino così che agli incroci un filo passi sopra e l'altro sotto.
public partial class MainWindow
{
    private static Geometry PolygonGeometry(IReadOnlyList<Point> points)
    {
        PathFigure figure = new() { StartPoint = points[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(points.Skip(1).ToList(), true));
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    private static Path FilledPath(IReadOnlyList<Point> polygon, System.Windows.Media.Brush fill)
    {
        return new Path { Data = PolygonGeometry(polygon), Fill = fill, IsHitTestVisible = false };
    }

    /// <summary>Ferrite: strisce ombreggiate, riflesso e smussature, disegnate sotto il contorno del fascio.</summary>
    private void UpdateFerrite(SheathEntity sheath, Path main)
    {
        List<Path> created = new();
        foreach (SheathEntity.FerriteLayer layer in sheath.FerriteLayers())
        {
            created.Add(FilledPath(layer.Polygon, FillBrush(layer.Fill, layer.Opacity)));
        }
        foreach (Path path in created)
        {
            int index = DrawCanvas.Children.IndexOf(main);
            DrawCanvas.Children.Insert(index < 0 ? DrawCanvas.Children.Count : index, path);
        }
        _partPaths[sheath] = created;
    }

    private void UpdateTwist(SheathEntity sheath)
    {
        RemoveComponentParts(sheath);
        if (!_paths.TryGetValue(sheath, out Path? main))
        {
            return;
        }
        if (sheath.IsFerrite)
        {
            UpdateFerrite(sheath, main);
            return;
        }
        if (!sheath.IsTwisted)
        {
            return;
        }
        bool selected = _selection.Contains(sheath);
        SheathEntity.TwistLayout layout = sheath.BuildTwist(selected ? 0.4 : 0);
        if (layout.Strands.Count == 0)
        {
            return;
        }
        // il contorno dei fili diventa blu quando la guaina è selezionata
        System.Windows.Media.Brush edge = selected ? SelectedStroke : CachedBrush("#374151");
        List<Path> created = new();

        void AddShape(SheathEntity.TwistShape shape)
        {
            (string primary, string? secondary) = sheath.StrandColors(shape.Strand);
            created.Add(FilledPath(shape.Body, CachedBrush(primary)));
            foreach (List<Point> strip in shape.Edges)
            {
                created.Add(FilledPath(strip, edge));
            }
            if (secondary != null)
            {
                // filo bicolore: strisce alternate del secondo colore
                foreach (List<Point> stripe in shape.Stripes)
                {
                    created.Add(FilledPath(stripe, CachedBrush(secondary)));
                }
            }
        }

        // prima i fili interi, poi i tratti dal più lontano al più vicino: agli incroci un filo passa sopra e l'altro sotto
        foreach (SheathEntity.TwistShape shape in layout.Strands)
        {
            AddShape(shape);
        }
        foreach (SheathEntity.TwistShape shape in layout.Chunks)
        {
            AddShape(shape);
        }
        foreach (Path path in created)
        {
            int index = DrawCanvas.Children.IndexOf(main);
            DrawCanvas.Children.Insert(index < 0 ? DrawCanvas.Children.Count : index, path);
        }
        _partPaths[sheath] = created;
    }

    /// <summary>Ogni filo dell'intreccio prende i colori del filo collegato al suo pin (ingresso o uscita); bianco se non c'è.</summary>
    private void UpdateTwistColors(SheathEntity sheath, List<WireEntity> wires)
    {
        if (!sheath.IsTwisted)
        {
            return;
        }
        int count = sheath.StrandCount;
        List<string?> first = new();
        List<string?> second = new();
        List<double> widths = new();
        for (int k = 0; k < count; k++)
        {
            string? color = null;
            string? color2 = null;
            double thickness = 0;
            if (sheath.WireCount > 0)
            {
                Point entry = sheath.GetPinPosition(k);
                Point exit = sheath.GetPinPosition(sheath.WireCount + k);
                foreach (WireEntity wire in wires)
                {
                    bool attached = WireEntity.Near(entry, wire.StartPoint, 0.01) || WireEntity.Near(entry, wire.EndPoint, 0.01)
                        || WireEntity.Near(exit, wire.StartPoint, 0.01) || WireEntity.Near(exit, wire.EndPoint, 0.01);
                    if (!attached)
                    {
                        continue;
                    }
                    if (thickness <= 0)
                    {
                        // ogni filo dell'intreccio ha lo stesso spessore disegnato del filo collegato al suo pin
                        thickness = wire.LineWidth;
                    }
                    if (wire.StrokeColor != null)
                    {
                        color = wire.StrokeColor;
                        color2 = wire.StrokeColor2;
                        break;
                    }
                }
            }
            first.Add(color);
            second.Add(color2);
            widths.Add(thickness);
        }
        if (!first.SequenceEqual(sheath.AutoStrandColors) || !second.SequenceEqual(sheath.AutoStrandColors2)
            || !widths.SequenceEqual(sheath.AutoStrandWidths))
        {
            sheath.AutoStrandColors = first;
            sheath.AutoStrandColors2 = second;
            sheath.AutoStrandWidths = widths;
            // ridisegna intreccio e corpo della guaina con i nuovi colori e spessori
            sheath.RefreshGeometry();
        }
    }

    /// <summary>Riporta i fili intrecciati subito sotto il contorno della guaina dopo che questo è stato spostato in cima.</summary>
    private void RaiseTwistStrands(SheathEntity sheath)
    {
        if (!_partPaths.TryGetValue(sheath, out List<Path>? parts) || !_paths.TryGetValue(sheath, out Path? main))
        {
            return;
        }
        foreach (Path part in parts)
        {
            DrawCanvas.Children.Remove(part);
            int index = DrawCanvas.Children.IndexOf(main);
            DrawCanvas.Children.Insert(index < 0 ? DrawCanvas.Children.Count : index, part);
        }
    }
}
