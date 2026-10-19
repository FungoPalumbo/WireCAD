using System.Windows;
using System.Windows.Controls;

namespace WireCAD.Controls;

public partial class TitleBlockControl : UserControl
{
    public TitleBlockControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty CompanyNameProperty =
        DependencyProperty.Register(nameof(CompanyName), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata(string.Empty));

    public string CompanyName
    {
        get => (string)GetValue(CompanyNameProperty);
        set => SetValue(CompanyNameProperty, value);
    }

    public static readonly DependencyProperty DrawingTitleProperty =
        DependencyProperty.Register(nameof(DrawingTitle), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata("Nuovo disegno"));

    public string DrawingTitle
    {
        get => (string)GetValue(DrawingTitleProperty);
        set => SetValue(DrawingTitleProperty, value);
    }

    public static readonly DependencyProperty DrawingNumberProperty =
        DependencyProperty.Register(nameof(DrawingNumber), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata("001"));

    public string DrawingNumber
    {
        get => (string)GetValue(DrawingNumberProperty);
        set => SetValue(DrawingNumberProperty, value);
    }

    public static readonly DependencyProperty ScaleValueProperty =
        DependencyProperty.Register(nameof(ScaleValue), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata("1:1"));

    public string ScaleValue
    {
        get => (string)GetValue(ScaleValueProperty);
        set => SetValue(ScaleValueProperty, value);
    }

    public static readonly DependencyProperty DateValueProperty =
        DependencyProperty.Register(nameof(DateValue), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata(string.Empty));

    public string DateValue
    {
        get => (string)GetValue(DateValueProperty);
        set => SetValue(DateValueProperty, value);
    }

    public static readonly DependencyProperty SheetValueProperty =
        DependencyProperty.Register(nameof(SheetValue), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata("1/1"));

    public string SheetValue
    {
        get => (string)GetValue(SheetValueProperty);
        set => SetValue(SheetValueProperty, value);
    }

    public static readonly DependencyProperty RevisionValueProperty =
        DependencyProperty.Register(nameof(RevisionValue), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata("-"));

    public string RevisionValue
    {
        get => (string)GetValue(RevisionValueProperty);
        set => SetValue(RevisionValueProperty, value);
    }

    public static readonly DependencyProperty AuthorNameProperty =
        DependencyProperty.Register(nameof(AuthorName), typeof(string), typeof(TitleBlockControl),
            new FrameworkPropertyMetadata(string.Empty));

    public string AuthorName
    {
        get => (string)GetValue(AuthorNameProperty);
        set => SetValue(AuthorNameProperty, value);
    }
}
