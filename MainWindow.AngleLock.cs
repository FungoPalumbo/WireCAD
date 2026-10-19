using System.Windows;
using System.Windows.Input;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata al vincolo angolare: tenendo premuto Spazio i tracciati
// (linee, archi, fili, guaine) sono vincolati a direzioni orizzontali, verticali o a 45°.
public partial class MainWindow
{
    private Point _lastMouseWorld;
    private bool _wireAngleLocked;

    private static bool SpaceHeld => Keyboard.IsKeyDown(Key.Space);

    private bool TryGetLockAnchor(out Point anchor)
    {
        anchor = new Point();
        switch (_tool)
        {
            case ToolType.Line when _currentPoints.Count == 1:
            case ToolType.Arc when _currentPoints.Count == 1:
                anchor = _currentPoints[0];
                return true;
            case ToolType.Wire when _wirePoints.Count >= 1:
                anchor = _wirePoints[_wirePoints.Count - 1];
                return true;
            case ToolType.Sheath when _sheathPoints.Count >= 1:
                anchor = _sheathPoints[_sheathPoints.Count - 1];
                return true;
            default:
                return false;
        }
    }

    /// <summary>Proietta il punto sulla direzione (multiplo di 45°) più vicina uscente dall'ancora.</summary>
    private Point ConstrainAngle(Point anchor, Point target)
    {
        Vector offset = target - anchor;
        if (offset.Length < 1e-9)
        {
            return target;
        }
        double angle = Math.Atan2(offset.Y, offset.X);
        int sector = (int)Math.Round(angle / (Math.PI / 4));
        bool diagonal = ((sector % 2) + 2) % 2 == 1;
        double snappedAngle = sector * Math.PI / 4;
        Vector direction = new(Math.Round(Math.Cos(snappedAngle), 12), Math.Round(Math.Sin(snappedAngle), 12));
        double length = Math.Max(0, offset * direction);
        if (CkSnap.IsChecked == true)
        {
            // si arrotonda alla griglia la componente lungo l'asse dominante
            double component = diagonal ? length * Math.Abs(direction.X) : length;
            double rounded = Math.Round(component / _gridStep) * _gridStep;
            length = diagonal ? rounded / Math.Abs(direction.X) : rounded;
        }
        return new Point(
            Math.Round(anchor.X + direction.X * length, 6),
            Math.Round(anchor.Y + direction.Y * length, 6));
    }

    /// <summary>
    /// Come <see cref="SmartSnap"/>, ma con Spazio premuto il punto è vincolato a 0°/45°/90°...
    /// rispetto all'ultimo punto del tracciato; l'aggancio a un pin resta valido se il pin giace sulla direzione.
    /// </summary>
    private SnapHit SnapWithAngleLock(Point world, bool allowWireSegments = false)
    {
        if (!SpaceHeld || !TryGetLockAnchor(out Point anchor))
        {
            return SmartSnap(world, allowWireSegments);
        }
        Point constrained = ConstrainAngle(anchor, world);
        foreach (EntityBase entity in _entities)
        {
            for (int i = 0; i < entity.PinCount; i++)
            {
                Point pin = entity.GetPinPosition(i);
                if ((pin - constrained).Length <= 0.05)
                {
                    return new SnapHit(pin, entity, i);
                }
            }
        }
        return new SnapHit(constrained, null, -1);
    }

    private void RefreshPreviewAtMouse()
    {
        if (_tool == ToolType.Select || _dragMode != DragMode.None)
        {
            return;
        }
        SnapHit hit = SnapWithAngleLock(_lastMouseWorld, _tool == ToolType.Junction);
        UpdateSnapMarker(hit);
        UpdatePreview(hit);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
        {
            RefreshPreviewAtMouse();
        }
        base.OnPreviewKeyUp(e);
    }
}
