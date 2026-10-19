using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WireCAD.Controls;

/// <summary>Piccole finestre di dialogo costruite da codice.</summary>
public static class Dialogs
{
    public static string? Prompt(Window owner, string title, string label, string initial)
    {
        Window window = new()
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 340
        };
        StackPanel panel = new() { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        TextBox box = new() { Text = initial, MinWidth = 280 };
        panel.Children.Add(box);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Button ok = new() { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => window.DialogResult = true;
        Button cancel = new() { Content = "Annulla", IsCancel = true, MinWidth = 72 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return window.ShowDialog() == true ? box.Text : null;
    }

    /// <summary>Finestra per i dati di un connettore salvato su file; restituisce null se annullata.</summary>
    public static Models.ConnectorInfo? PromptConnector(Window owner, Models.ConnectorInfo initial)
    {
        Window window = new()
        {
            Title = "Salva come connettore",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 420
        };
        Grid grid = new() { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        string[] labels = { "Nome", "Prefisso tag (es. X)", "Costruttore", "Codice articolo", "Descrizione" };
        string[] values = { initial.Name, initial.Prefix, initial.Manufacturer, initial.PartNumber, initial.Description };
        TextBox[] boxes = new TextBox[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock label = new() { Text = labels[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 6, 3) };
            Grid.SetRow(label, i);
            grid.Children.Add(label);
            TextBox box = new() { Text = values[i], Margin = new Thickness(0, 3, 0, 3), MinWidth = 240 };
            Grid.SetRow(box, i);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            boxes[i] = box;
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Button ok = new() { Content = "OK", IsDefault = true, MinWidth = 72, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(boxes[0].Text))
            {
                MessageBox.Show(window, "Indica il nome del connettore.", "Salva come connettore",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            window.DialogResult = true;
        };
        Button cancel = new() { Content = "Annulla", IsCancel = true, MinWidth = 72 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, labels.Length);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        window.Content = grid;
        window.Loaded += (_, _) =>
        {
            boxes[0].Focus();
            boxes[0].SelectAll();
        };
        if (window.ShowDialog() != true)
        {
            return null;
        }
        return new Models.ConnectorInfo
        {
            Name = boxes[0].Text.Trim(),
            Prefix = string.IsNullOrWhiteSpace(boxes[1].Text) ? "X" : boxes[1].Text.Trim(),
            Manufacturer = boxes[2].Text.Trim(),
            PartNumber = boxes[3].Text.Trim(),
            Description = boxes[4].Text.Trim()
        };
    }

    public static void ShowText(Window owner, string title, string text)
    {
        Window window = new()
        {
            Title = title,
            Width = 760,
            Height = 480,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        TextBox box = new()
        {
            Text = text,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        window.Content = box;
        window.ShowDialog();
    }
}
