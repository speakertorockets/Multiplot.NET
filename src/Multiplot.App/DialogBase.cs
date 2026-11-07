// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Multiplot.App;

/// <summary>Code-built modal dialog: rows of label + control, optional extra buttons, OK/Cancel (keyboard accessible).</summary>
public class DialogBase : Window
{
    /// <summary>Content grid (two columns: label, control).</summary>
    protected readonly Grid Body = new() { Margin = new Thickness(10) };
    /// <summary>Button row.</summary>
    protected readonly StackPanel Buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 10, 10) };
    private int _row;

    /// <summary>Creates the dialog.</summary>
    public DialogBase(Window? owner, string title, double width = 420)
    {
        Title = title;
        Owner = owner is { IsVisible: true } ? owner : null;
        WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Width = width;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        UseCompactDensity();
        Body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dock = new DockPanel();
        DockPanel.SetDock(Buttons, Dock.Bottom);
        dock.Children.Add(Buttons);
        dock.Children.Add(Body);
        Content = dock;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    /// <summary>Adds a labelled row.</summary>
    protected T Row<T>(string label, T control) where T : FrameworkElement
    {
        Body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (label.Length > 0)
        {
            var l = new Label { Content = label, Target = control, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 0, 8, 0) };
            Grid.SetRow(l, _row);
            Body.Children.Add(l);
            Grid.SetColumn(control, 1);
        }
        else Grid.SetColumnSpan(control, 2);
        control.Margin = new Thickness(0, 3, 0, 3);
        Grid.SetRow(control, _row++);
        Body.Children.Add(control);
        return control;
    }

    /// <summary>Adds a stretching row (lists).</summary>
    protected T StretchRow<T>(T control, double minHeight = 200) where T : FrameworkElement
    {
        Body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = minHeight });
        Grid.SetColumnSpan(control, 2);
        Grid.SetRow(control, _row++);
        control.Margin = new Thickness(0, 3, 0, 3);
        Body.Children.Add(control);
        SizeToContent = SizeToContent.Manual;
        Height = Math.Max(Height is double.NaN ? 0 : Height, minHeight + 200);
        return control;
    }

    /// <summary>Adds a button.</summary>
    protected Button AddButton(string text, Action click, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button { Content = text, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), IsDefault = isDefault, IsCancel = isCancel };
        b.Click += (_, _) => click();
        Buttons.Children.Add(b);
        return b;
    }

    /// <summary>Adds OK and Cancel.</summary>
    protected void OkCancel(Func<bool>? validate = null)
    {
        AddButton("OK", () => { if (validate == null || validate()) DialogResult = true; }, isDefault: true);
        AddButton("Cancel", () => DialogResult = false, isCancel: true);
    }

    /// <summary>Text box helper.</summary>
    protected static TextBox Text(string value) => new() { Text = value };

    /// <summary>Editable combo helper.</summary>
    protected static ComboBox Combo(string value, IEnumerable<string>? items, bool editable = true)
    {
        var c = new ComboBox { IsEditable = editable, IsTextSearchEnabled = false };
        if (items != null) foreach (var i in items) c.Items.Add(i);
        if (editable) c.Text = value;
        else c.SelectedItem = items?.FirstOrDefault(i => i == value) ?? items?.FirstOrDefault();
        return c;
    }

    /// <summary>
    /// Compact density, applied to every dialog: keeps the Windows 11 (Fluent) look of the current
    /// appearance but with shorter text boxes, combo boxes, buttons, list rows and column headers, so the
    /// dialogs take less space. The styles are based on the current theme styles, so light and dark both
    /// work. Multi-line text boxes (<c>AcceptsReturn</c>) keep their height.
    /// </summary>
    private void UseCompactDensity()
    {
        Style Based(object key, Type target, params Setter[] setters)
        {
            var style = new Style(target, TryFindResource(key) as Style);
            foreach (var x in setters) style.Setters.Add(x);
            return style;
        }
        void Add(Type t, params Setter[] setters) => Resources[t] = Based(t, t, setters);

        var text = Based(typeof(TextBox), typeof(TextBox), new Setter(MinHeightProperty, 0.0), new Setter(HeightProperty, 28.0),
            new Setter(PaddingProperty, new Thickness(6, 3, 6, 2)));
        var multi = new Trigger { Property = TextBox.AcceptsReturnProperty, Value = true };
        multi.Setters.Add(new Setter(HeightProperty, double.NaN));
        text.Triggers.Add(multi);
        Resources[typeof(TextBox)] = text;
        Add(typeof(ComboBox), new Setter(MinHeightProperty, 0.0), new Setter(HeightProperty, 28.0), new Setter(PaddingProperty, new Thickness(8, 3, 8, 2)));
        Add(typeof(Button), new Setter(MinHeightProperty, 0.0), new Setter(HeightProperty, 28.0), new Setter(PaddingProperty, new Thickness(10, 2, 10, 2)));
        Add(typeof(CheckBox), new Setter(MinHeightProperty, 0.0));
        Add(typeof(RadioButton), new Setter(MinHeightProperty, 0.0));
        Add(typeof(GridViewColumnHeader), new Setter(MinHeightProperty, 0.0), new Setter(HeightProperty, 26.0), new Setter(PaddingProperty, new Thickness(6, 2, 6, 2)));
        Setter[] row =
        {
            new(MinHeightProperty, 0.0), new(HeightProperty, 24.0), new(PaddingProperty, new Thickness(4, 0, 0, 0)),
            new(MarginProperty, new Thickness(0)), new(VerticalContentAlignmentProperty, VerticalAlignment.Center),
        };
        Add(typeof(ListBoxItem), row);
        Add(typeof(ListViewItem), row);
        // ListView with a GridView takes its rows from this key instead of the ListViewItem style
        Resources[GridView.GridViewItemContainerStyleKey] = Based(GridView.GridViewItemContainerStyleKey, typeof(ListViewItem), row);
    }

    /// <summary>Check box helper.</summary>
    protected static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };
}
