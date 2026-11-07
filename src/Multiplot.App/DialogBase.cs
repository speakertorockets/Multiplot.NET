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

    /// <summary>Check box helper.</summary>
    protected static CheckBox Check(string text, bool value) => new() { Content = text, IsChecked = value };
}
