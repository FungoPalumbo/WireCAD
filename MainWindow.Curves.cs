using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WireCAD.Models;

namespace WireCAD;

// Parte della finestra dedicata a cerchio e spline.
public partial class MainWindow
{
    private readonly List<Point> _splinePoints = new();

    private void BtnCircle_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Circle);

    private void BtnSpline_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Spline);

    private void BtnDimension_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Dimension);

    private void BtnPin_Click(object sender, RoutedEventArgs e) => SetTool(ToolType.Pin);

    private void SplineClick(Point world)
    {
        Point point = SmartSnap(world).Point;
        if (_splinePoints.Count > 0 && (point - _splinePoints[_splinePoints.Count - 1]).Length < 1e-6)
        {
            return;
        }
        _splinePoints.Add(point);
    }

    private void CancelSpline()
    {
        _splinePoints.Clear();
        if (_tool == ToolType.Spline)
        {
            _previewPath.Visibility = Visibility.Collapsed;
        }
    }

    private void FinishSpline()
    {
        if (_splinePoints.Count >= 2)
        {
            SplineEntity spline = new(_splinePoints.ToList());
            AddEntity(spline);
            SetSelection(new[] { spline });
            CommitChange();
        }
        _splinePoints.Clear();
        _previewPath.Visibility = Visibility.Collapsed;
    }

    private Geometry BuildSplinePreview(Point cursor)
    {
        List<Point> points = new(_splinePoints) { cursor };
        return new SplineEntity(points).BuildGeometry();
    }

    private Geometry BuildCirclePreview(Point cursor)
    {
        double radius = Math.Max(0.01, (cursor - _currentPoints[0]).Length);
        return new EllipseGeometry(_currentPoints[0], radius, radius);
    }

    private void ShowSplineMenuItems(ContextMenu menu, SplineEntity spline, Point worldPoint)
    {
        MenuItem add = new() { Header = "Aggiungi punto qui" };
        add.Click += (_, _) =>
        {
            spline.InsertVertexNear(worldPoint);
            UpdateGrips();
            CommitChange();
        };
        menu.Items.Add(add);

        MenuItem remove = new() { Header = "Rimuovi punto più vicino" };
        remove.Click += (_, _) =>
        {
            if (spline.RemoveVertexNear(worldPoint))
            {
                UpdateGrips();
                CommitChange();
            }
        };
        menu.Items.Add(remove);
        menu.Items.Add(new Separator());
    }
}
