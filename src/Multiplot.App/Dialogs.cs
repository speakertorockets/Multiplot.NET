// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Multiplot.Application;
using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.ImportExport;
using Multiplot.Plugins;

namespace Multiplot.App;

/// <summary>Generic input dialog (FR-UI-044).</summary>
public sealed class InputDialog : DialogBase
{
    private readonly ComboBox _combo;
    private readonly CheckBox? _all;

    private InputDialog(Window? owner, InputRequest r) : base(owner, r.Caption.Length > 0 ? r.Caption : "Multiplot", 400)
    {
        Row("", new TextBlock { Text = r.Prompt, TextWrapping = TextWrapping.Wrap });
        _combo = Row("", Combo(r.Default, r.Choices, !r.ListOnly));
        if (r.ListOnly && r.Choices != null)
        {
            int i = r.Choices.ToList().IndexOf(r.Default);
            _combo.SelectedIndex = Math.Max(0, i);
        }
        if (r.ShowApplyAll) _all = Row("", Check("Apply to all plots", r.ApplyAll));
        OkCancel();
        Loaded += (_, _) => { _combo.Focus(); };
    }

    /// <summary>Shows the dialog; null when cancelled.</summary>
    public static InputResult? Ask(Window? owner, InputRequest r)
    {
        var d = new InputDialog(owner, r);
        if (d.ShowDialog() != true) return null;
        string text = r.ListOnly ? (d._combo.SelectedItem as string ?? "") : d._combo.Text;
        return new InputResult(text, d._all?.IsChecked == true);
    }
}

/// <summary>Fix X / Fix Y dialog.</summary>
public sealed class RangeDialog : DialogBase
{
    /// <summary>Shows the dialog and updates the request.</summary>
    public static bool Ask(Window? owner, RangeRequest r)
    {
        var d = new RangeDialog(owner, r);
        return d.ShowDialog() == true;
    }

    private RangeDialog(Window? owner, RangeRequest r) : base(owner, "Set Range - " + r.Prompt, 360)
    {
        var min = Row("Minimum:", Text(r.Min));
        var max = Row("Maximum:", Text(r.Max));
        var inc = Row("Increment:", Combo(r.Increment, new[] { "Auto" }));
        var persist = Row("", Check("Persist (keep for new plots)", r.Persist));
        var all = Row("", Check("Apply to all plots", r.ApplyAll));
        all.IsEnabled = !r.DisableApplyAll;
        OkCancel(() =>
        {
            r.Min = min.Text; r.Max = max.Text; r.Increment = inc.Text; r.Persist = persist.IsChecked == true; r.ApplyAll = all.IsChecked == true;
            return true;
        });
    }
}

/// <summary>A row of the parameter list.</summary>
public sealed record ParmRow(string Name, string AltName, string Units, string Description, string Seq, string Rate, string Type, string ExtName, int File);

/// <summary>Parameter Selection dialog (FR-UI-020).</summary>
public sealed class ParameterSelectDialog : DialogBase
{
    private static bool s_plot = true, s_alt, s_ext, s_dups, s_retainFilter;
    private static string s_filter = "";
    private static int s_filterColumn = -1;
    private readonly Session _s;
    private readonly ParmSelectMode _mode;
    private readonly ComboBox _file, _x, _y;
    private readonly ListView _list = new() { SelectionMode = SelectionMode.Extended };
    private readonly TextBox _filterBox = new();
    private readonly CheckBox _plot, _alt, _ext, _dups, _retain;
    private List<ParmRow> _rows = new();
    private int _sortColumn = -1;
    private bool _sortDesc;

    /// <summary>Plot checkbox result.</summary>
    public bool DoPlot => _plot.IsChecked == true;

    /// <summary>Creates the dialog.</summary>
    public ParameterSelectDialog(Window? owner, Session s, ParmSelectMode mode)
        : base(owner, mode == ParmSelectMode.AddParameter ? "Add Parameter to Plot" : mode == ParmSelectMode.XAxis ? "Select X Axis Parameter" : "Parameter Selection", 720)
    {
        MinWidth = 560;
        MinHeight = 400;
        _s = s;
        _mode = mode;
        var files = new List<string> { "All Files" };
        files.AddRange(s.Files.Select(f => f.Info.Path));
        _file = Row("File:", Combo("", files, false));
        _file.SelectedIndex = s.CurrentFile >= 0 && s.CurrentFile < s.Files.Count ? s.CurrentFile + 1 : (s.Files.Count == 1 ? 1 : 0);
        var past = s.PastParms.Where(p => !string.IsNullOrEmpty(p)).ToList();
        _x = Row("X axis:", Combo(mode == ParmSelectMode.Normal ? s.XAxis : mode == ParmSelectMode.AddParameter ? "<modification not allowed>" : "<Make Selection Below>",
            new[] { "(default)" }.Concat(past)));
        _x.IsEnabled = mode == ParmSelectMode.Normal;
        _y = Row("Y axis:", Combo(mode == ParmSelectMode.Normal ? s.YAxis : "", past));
        var opts = new WrapPanel();
        _plot = Check("Plot", s_plot);
        _alt = Check("Pri/Alt Swap", s_alt || s.Settings.AltNames);
        _ext = Check("Use Extended", s_ext || s.Settings.ExtNomen);
        _dups = Check("Filter Duplicates", s_dups);
        _retain = Check("Retain Filter", s_retainFilter);
        foreach (var c in new[] { _plot, _alt, _ext, _dups, _retain }) { c.Margin = new Thickness(0, 0, 12, 0); opts.Children.Add(c); }
        _plot.IsEnabled = mode == ParmSelectMode.Normal;
        Row("", opts);
        var filterPanel = new DockPanel();
        var restore = new Button { Content = "Restore", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0) };
        DockPanel.SetDock(restore, Dock.Right);
        filterPanel.Children.Add(restore);
        filterPanel.Children.Add(_filterBox);
        Row("Filter:", filterPanel);
        restore.Click += (_, _) => { _filterBox.Text = ""; s_filterColumn = -1; Reload(); };
        var gv = new GridView();
        string[] heads = { "Name", "Name (alt)", "Units", "Description", "Seq#", "Rate", "Type" };
        string[] props = { "Name", "AltName", "Units", "Description", "Seq", "Rate", "Type" };
        for (int i = 0; i < heads.Length; i++)
        {
            int col = i;
            var h = new GridViewColumnHeader { Content = heads[i] };
            h.Click += (_, _) => HeaderClick(col);
            gv.Columns.Add(new GridViewColumn { Header = h, DisplayMemberBinding = new System.Windows.Data.Binding(props[i]), Width = ColumnWeights[i] * 80 });
        }
        _list.View = gv;
        // columns always fit the list width: no horizontal scroll bar over the last row, and room is kept
        // on the right for the vertical scroll bar so it does not cover the last column
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.SizeChanged += (_, e) => { if (e.WidthChanged) FitColumns(gv); };
        StretchRow(_list, 180);
        Height = 520;
        _list.SelectionChanged += (_, _) => SelectionToY();
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItems.Count > 0) { SelectionToY(); Accept(); } };
        _file.SelectionChanged += (_, _) => Reload();
        _alt.Click += (_, _) => SelectionToY();
        _ext.Click += (_, _) => Reload();
        _dups.Click += (_, _) => Reload();
        if (s_retainFilter && s_filterColumn >= 0) _filterBox.Text = s_filter;
        AddButton("X Axis", () =>
        {
            if (_list.SelectedItem is ParmRow r) _x.Text = _alt.IsChecked == true ? r.AltName : r.Name;
        });
        AddButton("Clear List", () =>
        {
            if (MessageBox.Show(this, "Clear remembered parameters list?", "Multiplot", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                Array.Fill(_s.PastParms, "");
                _y.Items.Clear();
            }
        });
        AddButton("Save", SaveList);
        AddButton("OK", Accept, isDefault: true);
        AddButton("Cancel", () => DialogResult = false, isCancel: true);
        Reload();
    }

    /// <summary>Relative column widths: Name, Name (alt), Units, Description, Seq#, Rate, Type.</summary>
    private static readonly double[] ColumnWeights = { 1.4, 1.2, 0.8, 2.9, 0.7, 0.8, 0.8 };

    /// <summary>Spreads the list width over the columns by <see cref="ColumnWeights"/>.</summary>
    private void FitColumns(GridView gv)
    {
        double avail = _list.ActualWidth - SystemParameters.VerticalScrollBarWidth - 12;
        if (avail < 200) return;
        double total = ColumnWeights.Sum();
        for (int i = 0; i < gv.Columns.Count; i++) gv.Columns[i].Width = Math.Floor(avail * ColumnWeights[i] / total);
    }

    private IEnumerable<int> SelectedFiles() => _file.SelectedIndex <= 0 ? Enumerable.Range(0, _s.Files.Count) : new[] { _file.SelectedIndex - 1 };

    private void Reload()
    {
        var rows = new List<ParmRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int f in SelectedFiles())
        {
            var file = _s.Files[f];
            foreach (var p in file.Parameters)
            {
                string name = _ext.IsChecked == true && p.Names[2] != null ? p.Names[2]! : p.Name;
                if (_dups.IsChecked == true && !seen.Add(name)) continue;
                string rate = p.Rate < 0 ? "Unknown" : p.Rate == 0 ? "Variable" : Crt.Sprintf("%g", (double)p.Rate);
                rows.Add(new ParmRow(name, p.AltName, p.Units, _ext.IsChecked == true && p.Descriptions[1] != null ? p.Descriptions[1]! : p.Description,
                    (p.SequenceNumber & 0x7fffffff).ToString(System.Globalization.CultureInfo.InvariantCulture), rate, p.TypeId, p.Names[2] ?? "", f));
            }
        }
        _rows = rows;
        ApplyView();
    }

    private void HeaderClick(int col)
    {
        if (_filterBox.Text.Trim().Length > 0)
        {
            s_filterColumn = col;
            s_filter = _filterBox.Text;
        }
        else
        {
            if (_sortColumn == col) _sortDesc = !_sortDesc;
            else { _sortColumn = col; _sortDesc = false; }
        }
        ApplyView();
    }

    private static string Cell(ParmRow r, int c) => c switch
    {
        0 => r.Name, 1 => r.AltName, 2 => r.Units, 3 => r.Description, 4 => r.Seq, 5 => r.Rate, _ => r.Type,
    };

    private void ApplyView()
    {
        IEnumerable<ParmRow> v = _rows;
        if (s_filterColumn >= 0 && s_filter.Trim().Length > 0)
        {
            var terms = s_filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            v = v.Where(r =>
            {
                string cell = Cell(r, s_filterColumn);
                foreach (var t in terms)
                {
                    bool neg = t.StartsWith('!');
                    string tt = neg ? t[1..] : t;
                    if (tt.Length == 0) continue;
                    bool has = cell.Contains(tt, StringComparison.OrdinalIgnoreCase);
                    if (neg ? has : !has) return false;
                }
                return true;
            });
        }
        if (_sortColumn >= 0) v = _sortDesc ? v.OrderByDescending(r => Cell(r, _sortColumn), StringComparer.OrdinalIgnoreCase) : v.OrderBy(r => Cell(r, _sortColumn), StringComparer.OrdinalIgnoreCase);
        _list.ItemsSource = v.ToList();
    }

    private void SelectionToY()
    {
        if (_list.SelectedItems.Count == 0) return;
        var names = _list.SelectedItems.Cast<ParmRow>().Select(r => _alt.IsChecked == true ? r.AltName : r.Name);
        if (_mode == ParmSelectMode.XAxis) _x.Text = names.First();
        else
        {
            string list = string.Join(",", names);
            // show the file qualifier when one file is selected, so the Y axis box says what will load
            if (_file.SelectedIndex > 0 && _s.QualifierForFile(_file.SelectedIndex - 1) is { } q) list = FileQualifier.Qualify(list, q);
            _y.Text = list;
        }
    }

    private void Accept()
    {
        // a single file selected: plain names are limited to that file (FR-UI-020)
        string y = _y.Text.Trim();
        if (_mode != ParmSelectMode.XAxis && _file.SelectedIndex > 0 && y.Length > 0)
        {
            string? q = _s.QualifierForFile(_file.SelectedIndex - 1);
            if (q == null)
            {
                MessageBox.Show(this, "The selected file can't be told apart from the other open files by its name or path,\n" +
                    "so its parameters can't be limited to it.\n\nSelect All Files, or type a qualified name (text of the path:NAME) in the Y axis box.",
                    "Multiplot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            y = FileQualifier.Qualify(y, q);
        }
        s_plot = _plot.IsChecked == true;
        s_alt = _alt.IsChecked == true;
        s_ext = _ext.IsChecked == true;
        s_dups = _dups.IsChecked == true;
        s_retainFilter = _retain.IsChecked == true;
        if (!s_retainFilter) s_filterColumn = -1;
        _s.Settings.AltNames = s_alt;
        _s.Settings.ExtNomen = s_ext;
        _s.CurrentFile = _file.SelectedIndex - 1;
        if (_mode == ParmSelectMode.XAxis) _s.XAxis = _x.Text.Trim().Length == 0 ? "(default)" : _x.Text.Trim();
        else
        {
            _s.YAxis = y;
            if (_mode == ParmSelectMode.Normal) _s.XAxis = _x.Text.Trim().Length == 0 ? "(default)" : _x.Text.Trim();
        }
        DialogResult = true;
    }

    private void SaveList()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = "Parameter List.csv", Filter = "Comma separated (*.csv)|*.csv|Tab separated (*.txt)|*.txt" };
        if (dlg.ShowDialog(this) != true) return;
        char d = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ',' : '\t';
        var sb = new System.Text.StringBuilder("Primary" + d + " Alternate" + d + " Units" + d + " Description" + d + " Sequence#\r\n");
        foreach (var r in (IEnumerable<ParmRow>)_list.ItemsSource) sb.Append(string.Join(d, r.Name, r.AltName, r.Units, r.Description, r.Seq)).Append("\r\n");
        try { AnsiText.WriteAllText(dlg.FileName, sb.ToString()); }
        catch (IOException ex) { MessageBox.Show(this, ex.Message); }
    }
}

/// <summary>Time Slice dialog (FR-UI-023) and the per-group dialog.</summary>
public sealed class TimeSliceDialog : DialogBase
{
    /// <summary>Shows the dialog; applies on OK.</summary>
    public static bool Ask(Window? owner, Session s, bool perGroup)
    {
        var events = s.Files.SelectMany(f => f.Events.Select(e => e.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!perGroup || s.Groups.Count == 0)
        {
            var d = new TimeSliceDialog(owner, "Global Time Slice");
            var start = d.Row("Start:", Combo(s.Settings.TStart, new[] { "-3.4e+038", "START", "EOF" }.Concat(events)));
            var stop = d.Row("Stop:", Combo(s.Settings.TStop, new[] { "+3.4e+038", "EOF" }.Concat(events)));
            var rf = d.Row("Reference:", Combo(s.Settings.TRef, new[] { "NONE", "START", "EOF", "CUTOFF" }.Concat(events)));
            d.OkCancel();
            if (d.ShowDialog() != true) return false;
            s.Settings.TStart = start.Text.Trim();
            s.Settings.TStop = stop.Text.Trim();
            s.Settings.TRef = rf.Text.Trim();
            foreach (var g in s.Groups) { g.TStart = s.Settings.TStart; g.TStop = s.Settings.TStop; g.TRef = s.Settings.TRef; }
            s.Record(Crt.Sprintf("time %s,%s,%s", s.Settings.TStart, s.Settings.TStop, s.Settings.TRef));
            s.TimeReferenceChanged();
            return true;
        }
        var dg = new TimeSliceDialog(owner, "Time Slice per Database");
        var boxes = new List<(FileGroup G, ComboBox A, ComboBox B, ComboBox C)>();
        foreach (var g in s.Groups)
        {
            dg.Row("", new TextBlock { Text = g.GroupName, FontWeight = FontWeights.Bold });
            boxes.Add((g, dg.Row("Start:", Combo(g.TStart, new[] { "-3.4e+038", "START", "EOF" }.Concat(events))),
                dg.Row("Stop:", Combo(g.TStop, new[] { "+3.4e+038", "EOF" }.Concat(events))),
                dg.Row("Reference:", Combo(g.TRef, new[] { "NONE", "START", "EOF", "CUTOFF" }.Concat(events)))));
        }
        dg.OkCancel();
        if (dg.ShowDialog() != true) return false;
        foreach (var (g, a, b, c) in boxes)
        {
            g.TStart = a.Text.Trim(); g.TStop = b.Text.Trim(); g.TRef = c.Text.Trim();
            s.Record(Crt.Sprintf("time %s:%s,%s,%s", g.GroupName, g.TStart, g.TStop, g.TRef));
        }
        s.TimeReferenceChanged();
        return true;
    }

    private TimeSliceDialog(Window? owner, string title) : base(owner, title, 420) { }
}

/// <summary>Trace Attributes dialog (FR-UI-025).</summary>
public sealed class TraceAttributesDialog : DialogBase
{
    private static readonly string[] Styles = { "Solid", "Dash", "Dot", "Dash-Dot", "Dash-Dot-Dot" };

    /// <summary>Shows the dialog for a plot; returns true on OK.</summary>
    public static bool Ask(Window? owner, Session s, PlotDocument plot, int trace, out bool applyAll)
    {
        applyAll = false;
        if (plot.NCurves == 0) return false;
        var d = new TraceAttributesDialog(owner, plot, Math.Clamp(trace, 0, plot.NCurves - 1));
        bool ok = d.ShowDialog() == true;
        if (ok)
        {
            applyAll = d._all.IsChecked == true;
            var c = plot.Curves[d._trace.SelectedIndex];
            s.Record(Crt.Sprintf("SET TRACE %d,LINE_SIZE=%d,LINE_STYLE=%d,USE_SYMBOLS=%d,SYMBOL_ID=%d,SCATTER_PLOT=%d,COLOR_INDEX=%d,TAB_LEGEND=%d,TAB_FORMAT=%s,LOGIC_INDEX=%d",
                d._trace.SelectedIndex + 1, c.LineSize, c.LineStyle + 1, c.UseSymbols ? 1 : 0, c.SymbolId + 1, c.ScatterPlot ? 1 : 0, c.ColorIndex + 1, c.TabLegend ? 1 : 0,
                c.TabFormat >= 0 ? "DEC" + c.TabFormat : TabFormat.NameOf(c.TabFormat), c.LogicTxt + 1));
        }
        return ok;
    }

    private readonly ComboBox _trace;
    private readonly CheckBox _all;

    private TraceAttributesDialog(Window? owner, PlotDocument plot, int trace) : base(owner, "Trace Attributes - " + plot.Curves[trace].Name, 400)
    {
        _trace = Row("Trace:", Combo("", plot.Curves.Select((c, i) => (i + 1) + ": " + c.Name), false));
        var size = Row("Line size:", Combo("", new[] { "1", "2", "3" }, false));
        var style = Row("Line style:", Combo("", Styles, false));
        var sym = Row("", Check("Use symbols", false));
        var symId = Row("Symbol id (1-100):", Text(""));
        var scatter = Row("", Check("Scatter plot", false));
        var color = Row("Color index (1-100):", Text(""));
        var legend = Row("", Check("Tabular legend text", true));
        var fmt = Row("Tabular format:", Combo("", TabFormat.Names, false));
        var logic = Row("Logic labels:", Combo("", new[] { "1", "2", "3", "4" }, false));
        var tx = Row("Tab X position:", Text(""));
        var ty = Row("Tab Y position:", Text(""));
        _all = Row("", Check("Apply to all plots", false));
        void Load(int i)
        {
            var c = plot.Curves[i];
            size.SelectedIndex = Math.Clamp(c.LineSize - 1, 0, 2);
            style.SelectedIndex = Math.Clamp(c.LineStyle, 0, 4);
            sym.IsChecked = c.UseSymbols;
            symId.Text = (c.SymbolId + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            scatter.IsChecked = c.ScatterPlot;
            color.Text = (c.ColorIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            legend.IsChecked = c.TabLegend;
            fmt.SelectedIndex = Math.Clamp(c.TabFormat + 7, 0, 14);
            logic.SelectedIndex = Math.Clamp(c.LogicTxt, 0, 3);
            tx.Text = c.TabX.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ty.Text = c.TabY.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        void Store(int i)
        {
            var c = plot.Curves[i];
            c.LineSize = size.SelectedIndex + 1;
            c.LineStyle = style.SelectedIndex;
            c.UseSymbols = sym.IsChecked == true;
            c.SymbolId = Math.Clamp(Crt.Atoi(symId.Text) - 1, 0, 99);
            c.ScatterPlot = scatter.IsChecked == true;
            c.ColorIndex = Math.Clamp(Crt.Atoi(color.Text) - 1, 0, 99);
            c.TabLegend = legend.IsChecked == true;
            c.TabFormat = fmt.SelectedIndex - 7;
            c.LogicTxt = logic.SelectedIndex;
            c.TabPosition = unchecked(((uint)(ushort)Crt.Atoi(ty.Text) << 16) | (ushort)Crt.Atoi(tx.Text));
        }
        int current = trace;
        _trace.SelectedIndex = trace;
        Load(trace);
        _trace.SelectionChanged += (_, _) =>
        {
            if (_trace.SelectedIndex < 0) return;
            Store(current);
            current = _trace.SelectedIndex;
            Title = "Trace Attributes - " + plot.Curves[current].Name;
            Load(current);
        };
        OkCancel(() => { Store(current); return true; });
    }
}

/// <summary>ASCII import dialog (FR-IMP-002).</summary>
public sealed class ImportWindow : DialogBase
{
    /// <summary>Shows the dialog and generates the file on OK.</summary>
    public static bool Ask(Window? owner, AsciiImporter imp)
    {
        var d = new ImportWindow(owner, imp);
        return d.ShowDialog() == true;
    }

    private ImportWindow(Window? owner, AsciiImporter imp) : base(owner, "Importing - " + Path.GetFileName(imp.SourceFile), 760)
    {
        var list = StretchRow(new ListBox { FontFamily = new System.Windows.Media.FontFamily("Consolas") }, 220);
        foreach (var l in imp.Preview) list.Items.Add(l);
        var delim = Row("Delimiter:", Combo(imp.Delimiter == ',' ? "Comma" : imp.Delimiter == '\t' ? "Tab" : "Space/Any", new[] { "Space/Any", "Comma", "Tab" }, false));
        delim.SelectedItem = imp.Delimiter == ',' ? "Comma" : imp.Delimiter == '\t' ? "Tab" : "Space/Any";
        var index = Row("Index column (1..10000):", Text(imp.FileIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var skip = Row("Skip rows:", Text(imp.SkipLines.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var title = Row("Title row (0 = file name):", Text(imp.TitleRecord.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var gen = Row("", Check("Generate TimeTags", imp.GenTimeTag));
        var std = Row("", Check("Standard format (COMMONTIME)", imp.StandardFormat));
        var unix = Row("", Check("Unix epoch time", imp.UnixTime));
        var reft = Row("Reference (yyyy ddd hh:mm:ss.mmm.uuu):", Text(imp.RefTime));
        var keep = Row("", Check("Keep file (save permanently)", false));
        var status = Row("", new TextBlock());
        AddButton("OK", () =>
        {
            imp.Delimiter = (delim.SelectedItem as string) switch { "Comma" => ',', "Tab" => '\t', _ => ' ' };
            imp.FileIndex = Math.Clamp(Crt.Atoi(index.Text), 1, 10000);
            imp.SkipLines = Math.Clamp(Crt.Atoi(skip.Text), 0, 1048576);
            imp.TitleRecord = Math.Clamp(Crt.Atoi(title.Text), 0, 1048576);
            imp.GenTimeTag = gen.IsChecked == true;
            imp.StandardFormat = std.IsChecked == true;
            imp.UnixTime = unix.IsChecked == true;
            imp.RefTime = reft.Text;
            Cursor = Cursors.Wait;
            imp.GenerateFile();
            Cursor = null;
            status.Text = imp.StatusMsg;
            if (imp.FileReady && keep.IsChecked == true)
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { Title = "Save import data", Filter = "Multiplot Imported Files (*.sun)|*.sun|All Files (*.*)|*.*", FileName = Path.ChangeExtension(imp.SourceFile, ".sun") };
                if (dlg.ShowDialog(this) == true)
                {
                    try
                    {
                        if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                        File.Move(imp.DestFile, dlg.FileName);
                        imp.DestFile = dlg.FileName;
                    }
                    catch (IOException ex) { MessageBox.Show(this, ex.Message); }
                }
            }
            DialogResult = true;
        }, isDefault: true);
        AddButton("Cancel", () =>
        {
            if (MessageBox.Show(this, "Abort import?", "Multiplot", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                imp.Abort = true;
                imp.StatusMsg = "Import aborted.";
                DialogResult = false;
            }
        });
        Height = 640;
    }
}
