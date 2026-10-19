using System.Windows;
using System.Windows.Media;
using WireCAD.Models;
using Path = System.Windows.Shapes.Path;

namespace WireCAD;

// Parte della finestra dedicata ai componenti in linea (resistenza, diodo, fusibile, giunzione saldata):
// inserimento su un filo (o su un collegamento interno di una guaina) con taglio del filo, lunghezza dei reofori
// e parti colorate dei componenti.
public partial class MainWindow
{
    private readonly Dictionary<EntityBase, List<Path>> _partPaths = new();

    /// <summary>Punto di inserimento: un filo, oppure un collegamento interno di una guaina con pin.</summary>
    private sealed record InlineTarget(
        WireEntity? Wire, SheathEntity? Sheath, int Link, IReadOnlyList<Point> Points, int Segment, Point Projected);

    private static Point ProjectOnSegment(Point point, Point a, Point b)
    {
        Vector ab = b - a;
        double t = ab.LengthSquared < 1e-12 ? 0 : ((point - a) * ab) / ab.LengthSquared;
        return a + ab * Math.Clamp(t, 0, 1);
    }

    private InlineTarget? FindInlineTarget(Point world)
    {
        double best = Math.Max(2, 8 / _viewTransform.Matrix.M11);
        InlineTarget? target = null;
        foreach (WireEntity wire in _entities.OfType<WireEntity>())
        {
            IReadOnlyList<Point> points = wire.Points;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                double distance = WireEntity.DistanceToSegment(world, points[i], points[i + 1]);
                if (distance <= best)
                {
                    best = distance;
                    target = new InlineTarget(wire, null, -1, points, i, ProjectOnSegment(world, points[i], points[i + 1]));
                }
            }
        }
        foreach (SheathEntity sheath in _entities.OfType<SheathEntity>().Where(item => item.WireCount > 0))
        {
            for (int link = 0; link < sheath.WireCount; link++)
            {
                if (sheath.IsLinkBroken(link))
                {
                    continue;
                }
                List<Point> line = sheath.LinkPolyline(link);
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    double distance = WireEntity.DistanceToSegment(world, line[i], line[i + 1]);
                    if (distance <= best)
                    {
                        best = distance;
                        target = new InlineTarget(null, sheath, link, line, i, ProjectOnSegment(world, line[i], line[i + 1]));
                    }
                }
            }
        }
        return target;
    }

    /// <summary>
    /// Posiziona il componente lungo il tratto, centrato sul punto indicato e orientato come il tratto.
    /// Fallisce se il tratto è più corto del componente.
    /// </summary>
    private bool TryComputePlacement(
        ComponentTemplate template, InlineTarget target, out Matrix placement, out string error)
    {
        placement = Matrix.Identity;
        error = string.Empty;
        Point a = target.Points[target.Segment];
        Point b = target.Points[target.Segment + 1];
        Vector segmentVector = b - a;
        double segmentLength = segmentVector.Length;
        double total = template.InlineLength;
        if (segmentLength < total - 1e-6)
        {
            error = $"Il tratto ({segmentLength:0.#} mm) è più corto del componente ({total:0.#} mm): scegli un tratto più lungo.";
            return false;
        }
        Vector direction = new(segmentVector.X / segmentLength, segmentVector.Y / segmentLength);
        double center = (target.Projected - a) * direction;
        Point origin = a + direction * (center - total / 2);
        if (CkSnap.IsChecked == true)
        {
            Point grid = GridRound(origin);
            if (Math.Abs(direction.Y) < 1e-6)
            {
                origin = new Point(grid.X, origin.Y);
            }
            else if (Math.Abs(direction.X) < 1e-6)
            {
                origin = new Point(origin.X, grid.Y);
            }
        }
        double along = Math.Clamp((origin - a) * direction, 0, segmentLength - total);
        origin = a + direction * along;
        placement = new Matrix(direction.X, direction.Y, -direction.Y, direction.X, origin.X, origin.Y);
        return true;
    }

    private Geometry? BuildInlinePreview(Point cursor)
    {
        if (_template == null)
        {
            return null;
        }
        InlineTarget? target = FindInlineTarget(_lastMouseWorld);
        if (target != null && TryComputePlacement(_template, target, out Matrix placement, out _))
        {
            return _template.CreateInline(placement).BuildGeometry();
        }
        return new RectangleGeometry(new Rect(cursor.X, cursor.Y - _template.Height / 2, _template.InlineLength, _template.Height));
    }

    /// <summary>
    /// Trasforma il collegamento interno di una guaina in un filo vero, agganciato ai pin della guaina,
    /// così da poterlo dividere inserendo un componente.
    /// </summary>
    private WireEntity MaterializeLink(SheathEntity sheath, int link, IReadOnlyList<Point> line)
    {
        List<Point> points = line.ToList();
        points[0] = sheath.GetPinPosition(link);
        points[points.Count - 1] = sheath.GetPinPosition(sheath.WireCount + link);
        WireEntity wire = new(points) { Orthogonal = false, Realistic = _wireRealistic };
        wire.Number = NextWireNumber();
        wire.SetStart(sheath.Id, link);
        wire.SetEnd(sheath.Id, sheath.WireCount + link);
        sheath.BreakLink(link);
        AddEntity(wire);
        return wire;
    }

    private void PlaceInline(Point world)
    {
        if (_template == null)
        {
            return;
        }
        InlineTarget? target = FindInlineTarget(world);
        if (target == null)
        {
            StatusHint.Text = "Clicca su un filo (o sul collegamento interno di una guaina) per inserire il componente in linea.";
            return;
        }
        if (!TryComputePlacement(_template, target, out Matrix placement, out string error))
        {
            StatusHint.Text = error;
            return;
        }

        WireEntity wire = target.Wire ?? MaterializeLink(target.Sheath!, target.Link, target.Points);
        int segment = target.Segment;

        ComponentEntity component = _template.CreateInline(placement);
        component.Tag = NextTag(_template.Prefix);
        component.Name = component.Tag;
        AddEntity(component);

        Point p1 = component.GetPinPosition(0);
        Point p2 = component.GetPinPosition(1);
        IReadOnlyList<Point> points = wire.Points;
        List<Point> first = points.Take(segment + 1).ToList();
        first.Add(p1);
        List<Point> second = new() { p2 };
        second.AddRange(points.Skip(segment + 1));

        // il secondo tratto è un filo nuovo (potenziale diverso): prende un nuovo numero
        WireEntity tail = new(second);
        tail.CopyAppearanceFrom(wire);
        tail.Number = NextWireNumber();
        tail.TerminalEnd = wire.TerminalEnd;
        wire.TerminalEnd = string.Empty;
        Guid? oldEndComponent = wire.EndComp;
        int oldEndPin = wire.EndPin;

        wire.ReplacePoints(first);
        wire.Normalize();
        wire.SetEnd(component.Id, 0);

        tail.Normalize();
        tail.SetStart(component.Id, 1);
        tail.SetEnd(oldEndComponent, oldEndPin);
        AddEntity(tail);

        SetSelection(new[] { component });
        CommitChange();
        StatusHint.Text = $"{component.Tag} inserito sul filo {wire.Number}: il filo è stato diviso (nuovo filo {tail.Number}).";
    }

    // ---------------------------------------------------------------- reofori

    /// <summary>Aggiorna per ogni filo la lunghezza aggiunta dai reofori dei componenti in linea a cui è collegato.</summary>
    private void UpdateWireExtras()
    {
        List<ComponentEntity> inlines = _entities
            .OfType<ComponentEntity>()
            .Where(component => component.IsInline && component.LeadLength > 0)
            .ToList();
        foreach (WireEntity wire in _entities.OfType<WireEntity>())
        {
            double extra = 0;
            foreach (ComponentEntity component in inlines)
            {
                for (int pin = 0; pin < component.PinCount; pin++)
                {
                    Point position = component.GetPinPosition(pin);
                    if (WireEntity.Near(position, wire.StartPoint, 0.01))
                    {
                        extra += component.LeadLength;
                    }
                    if (WireEntity.Near(position, wire.EndPoint, 0.01))
                    {
                        extra += component.LeadLength;
                    }
                }
            }
            if (Math.Abs(wire.ExtraLength - extra) > 1e-9)
            {
                wire.ExtraLength = extra;
            }
        }
    }

    // ---------------------------------------------------------------- parti colorate

    /// <summary>Corpo e bande colorate dei componenti con riempimenti: disegnati sotto il contorno.</summary>
    private void UpdateComponentParts(ComponentEntity component)
    {
        RemoveComponentParts(component);
        if (!component.IsCustom || !_paths.TryGetValue(component, out Path? main))
        {
            return;
        }
        var parts = component.GetColoredParts();
        if (parts.Count == 0)
        {
            return;
        }
        List<Path> created = new();
        foreach (var part in parts)
        {
            Path path = new()
            {
                Data = part.Geometry,
                Fill = CachedBrush(part.FillHex),
                IsHitTestVisible = false
            };
            int index = DrawCanvas.Children.IndexOf(main);
            DrawCanvas.Children.Insert(index < 0 ? DrawCanvas.Children.Count : index, path);
            created.Add(path);
        }
        _partPaths[component] = created;
    }

    private void RemoveComponentParts(EntityBase entity)
    {
        if (_partPaths.Remove(entity, out List<Path>? list))
        {
            foreach (Path path in list)
            {
                DrawCanvas.Children.Remove(path);
            }
        }
    }
}
