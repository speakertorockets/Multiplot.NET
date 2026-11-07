// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Multiplot.Application;
using Multiplot.Core;
using Multiplot.Core.Model;
using Multiplot.ImportExport;
using Multiplot.Plugins;
using Multiplot.Scripting;
using InputRequest = Multiplot.Application.InputRequest;

namespace Multiplot.App;

/// <summary>Dialog services of the WPF shell; hidden mode cancels every dialog.</summary>
public sealed class WpfDialogs : IDialogs
{
    private readonly MainWindow _main;
    private readonly bool _hidden;

    /// <summary>Session (set after creation).</summary>
    public Session? Session { get; set; }

    /// <summary>Creates the dialog service.</summary>
    public WpfDialogs(MainWindow main, bool hidden)
    {
        _main = main;
        _hidden = hidden;
    }

    private Window? Owner => _main.IsVisible ? _main : null;

    private static string Filter(string filter) => filter.TrimEnd('|');

    /// <inheritdoc/>
    public string[]? OpenFile(string title, string filter, string? initialDir, bool multiSelect = false)
    {
        if (_hidden) return null;
        var d = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = Filter(filter), Multiselect = multiSelect };
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir)) d.InitialDirectory = initialDir;
        return d.ShowDialog(Owner) == true ? d.FileNames : null;
    }

    /// <inheritdoc/>
    public string? SaveFile(string title, string filter, string? defaultName, string? initialDir)
    {
        if (_hidden) return null;
        var d = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = Filter(filter), FileName = defaultName ?? "" };
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir)) d.InitialDirectory = initialDir;
        else if (defaultName != null && Path.IsPathRooted(defaultName)) d.InitialDirectory = Path.GetDirectoryName(defaultName);
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    /// <inheritdoc/>
    public InputResult? Input(InputRequest request) => _hidden ? null : InputDialog.Ask(Owner, request);

    /// <inheritdoc/>
    public bool ParameterSelect(Session session, ParmSelectMode mode, out bool plot)
    {
        plot = false;
        if (_hidden) return false;
        var d = new ParameterSelectDialog(Owner, session, mode);
        bool ok = d.ShowDialog() == true;
        plot = d.DoPlot;
        return ok;
    }

    /// <inheritdoc/>
    public bool TimeSlice(Session session, bool perGroup) => !_hidden && TimeSliceDialog.Ask(Owner, session, perGroup);

    /// <inheritdoc/>
    public bool Range(RangeRequest request) => !_hidden && RangeDialog.Ask(Owner, request);

    /// <inheritdoc/>
    public bool TraceAttributes(Session session, PlotDocument plot, int trace, out bool applyAll)
    {
        applyAll = false;
        return !_hidden && TraceAttributesDialog.Ask(Owner, session, plot, trace, out applyAll);
    }

    /// <inheritdoc/>
    public bool Import(AsciiImporter importer) => !_hidden && ImportWindow.Ask(Owner, importer);

    /// <inheritdoc/>
    public int? ChooseColor(int initialColorRef, int[] customColors) => _hidden ? null : ColorWindow.Ask(Owner, initialColorRef, customColors);

    /// <inheritdoc/>
    public bool Show(Session s, DialogKind kind)
    {
        if (_hidden) return false;
        var o = Owner;
        switch (kind)
        {
            case DialogKind.SliceAverage: return SliceAverageWindow.Show(o, s);
            case DialogKind.DataPick: return DataPickWindow.Show(o, s);
            case DialogKind.XNomenclature: return NomenWindow.Show(o, s, false);
            case DialogKind.YNomenclature: return NomenWindow.Show(o, s, true);
            case DialogKind.MarkerEdit: return MarkerWindow.Show(o, s, false);
            case DialogKind.MarkerDraw: return MarkerWindow.Show(o, s, true);
            case DialogKind.RedlineEdit: return RedlineWindow.Show(o, s, false);
            case DialogKind.RedlineDraw: return RedlineWindow.Show(o, s, true);
            case DialogKind.Favorites: return FavoritesWindow.Show(o, s);
            case DialogKind.FilterHelper: return FilterWindow.Show(o, s);
            case DialogKind.FileProperties: return TextReportWindow.FileProperties(o, s);
            case DialogKind.ParameterProperties: return TextReportWindow.ParameterProperties(o, s);
            case DialogKind.SymbolicNames: return SymbolsWindow.Show(o, s);
            case DialogKind.FilePaths: return SimpleForms.Paths(o, s);
            case DialogKind.ExternalApplications: return SimpleForms.ExternalApps(o, s);
            case DialogKind.ManagePlugins: return PluginsWindow.Show(o, s, _main);
            case DialogKind.AutoRun: return SimpleForms.AutoRun(o, s);
            case DialogKind.WindowList: return SimpleForms.WindowList(o, s);
            case DialogKind.About: return SimpleForms.About(o, s);
            case DialogKind.TimeAlign: return SimpleForms.TimeAlign(o, s);
            case DialogKind.LabelFont: return FontWindow.Show(o, s, false);
            case DialogKind.TitleFont: return FontWindow.Show(o, s, true);
            case DialogKind.UserButtonAssign: return SimpleForms.UserButtons(o, s, _main);
            case DialogKind.WindowPosition:
                _main.WindowState = WindowState.Normal;
                _main.Left = (SystemParameters.WorkArea.Width - _main.Width) / 2;
                _main.Top = (SystemParameters.WorkArea.Height - _main.Height) / 2;
                return true;
            case DialogKind.RightButton:
                {
                    string[] opts = { "Delta", "Popup Menus", "Slice Average", "Data Tracking" };
                    var r = InputDialog.Ask(o, new InputRequest("Select right button click action", opts[Math.Clamp(s.Settings.RbFunction, 0, 3)], "Right Button", opts, true, s.Settings.PopupsGlobal, true));
                    if (r == null) return false;
                    s.Settings.RbFunction = Math.Max(0, Array.IndexOf(opts, r.Text));
                    s.Settings.PopupsGlobal = r.ApplyAll;
                    return true;
                }
            case DialogKind.AverageRate:
                {
                    var r = InputDialog.Ask(o, new InputRequest("Default data rate for average function:", Crt.Sprintf("%.3f", (double)s.Settings.AverageRate), "Data Rate"));
                    if (r == null) return false;
                    float v = (float)Crt.Atof(r.Text);
                    if (v > 0f && v <= 100f) s.Settings.AverageRate = v;
                    else s.Message("Data rate out of range [0<rate<100]");
                    return true;
                }
            case DialogKind.AverageTimeStamp:
                {
                    string[] opts = { "First Point", "Last Point", "Averaged" };
                    var r = InputDialog.Ask(o, new InputRequest("Select timestamp mode for average function", opts[Math.Clamp(s.Settings.TimeStampMode, 0, 2)], "Timestamp Mode", opts, false, false, true));
                    if (r == null) return false;
                    s.Settings.TimeStampMode = Math.Max(0, Array.IndexOf(opts, r.Text));
                    return true;
                }
            case DialogKind.Monitors:
                {
                    var r = InputDialog.Ask(o, new InputRequest("Number of monitors (0 for default tiling):", s.Settings.Monitors.ToString(System.Globalization.CultureInfo.InvariantCulture), "Monitors",
                        new[] { "0 (default)", "1", "2", "3", "4" }));
                    if (r == null) return false;
                    int n = Crt.Atoi(r.Text);
                    if (n >= 0 && n <= 10) s.Settings.Monitors = n;
                    else s.Message("Number out of range [0 .. 10]");
                    return true;
                }
            case DialogKind.LayoutGrid:
                {
                    var r = InputDialog.Ask(o, new InputRequest("Tabular layout grid spacing (logical units):", s.LayoutGridSpacingValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "Tabular Layout Grid", new[] { "0 (OFF)", "50", "100", "200", "500" }));
                    if (r == null) return false;
                    s.LayoutGridSpacingValue = Math.Abs(Crt.Atoi(r.Text));
                    return true;
                }
            default:
                return false;
        }
    }
}

/// <summary>Owner handle for the Windows common dialogs (colour and font).</summary>
internal sealed class Win32Owner(IntPtr handle) : System.Windows.Forms.IWin32Window
{
    public IntPtr Handle { get; } = handle;

    /// <summary>Shows a Windows Forms common dialog owned by <paramref name="owner"/>.</summary>
    public static System.Windows.Forms.DialogResult Show(System.Windows.Forms.CommonDialog dlg, Window? owner)
    {
        var hwnd = owner != null ? new System.Windows.Interop.WindowInteropHelper(owner).Handle : IntPtr.Zero;
        return hwnd != IntPtr.Zero ? dlg.ShowDialog(new Win32Owner(hwnd)) : dlg.ShowDialog();
    }
}

/// <summary>
/// Colour selection (FR-UI-028): the standard Windows colour dialog, opened with the custom colour editor.
/// Its custom colours are the first 16 curve colours. Colours are COLORREF (0x00BBGGRR).
/// </summary>
public static class ColorWindow
{
    /// <summary>Shows the dialog; the chosen colour, or null when cancelled.</summary>
    public static int? Ask(Window? owner, int initial, int[] table)
    {
        using var dlg = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = true,
            Color = System.Drawing.ColorTranslator.FromWin32(initial & 0xFFFFFF),
            CustomColors = table.Take(16).Select(c => c & 0xFFFFFF).ToArray(),
        };
        if (Win32Owner.Show(dlg, owner) != System.Windows.Forms.DialogResult.OK) return null;
        return System.Drawing.ColorTranslator.ToWin32(dlg.Color) & 0xFFFFFF;
    }
}

/// <summary>
/// Label and title fonts (FR-UI-029): the standard Windows font dialog (font, style and size). The chosen
/// font applies to the active plot (all plots with Apply Changes Globally) and to new plots.
/// </summary>
public static class FontWindow
{
    /// <summary>Face shown in the dialog: fonts that are not installed (such as "Multiplot") show as Arial.</summary>
    private static string InstalledFace(string face) =>
        Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, face, StringComparison.OrdinalIgnoreCase)) ? face : "Arial";

    /// <summary>Shows the Windows font dialog for the title or label font and applies the choice.</summary>
    public static bool Show(Window? owner, Session s, bool title)
    {
        var c = s.ActivePlot?.Config ?? s.PlotConfig;
        var lf = title ? c.TitleFont : c.LabelFont;
        int tenths = title ? c.TitlePointSize : c.LabelPointSize;
        var style = System.Drawing.FontStyle.Regular;
        if (lf.Weight >= 600 || lf.Weight == 0) style |= System.Drawing.FontStyle.Bold;
        if (lf.Italic != 0) style |= System.Drawing.FontStyle.Italic;
        float pt = Math.Clamp((tenths > 0 ? tenths : 100) / 10f, 4f, 200f);

        using var initial = new System.Drawing.Font(InstalledFace(lf.FaceName), pt, style, System.Drawing.GraphicsUnit.Point);
        using var dlg = new System.Windows.Forms.FontDialog
        {
            Font = initial,
            ShowEffects = false,
            ShowColor = false,
            ShowApply = false,
            AllowVerticalFonts = false,
            AllowScriptChange = false,
            FontMustExist = true,
            MinSize = 4,
            MaxSize = 200,
        };
        if (Win32Owner.Show(dlg, owner) != System.Windows.Forms.DialogResult.OK) return false;
        var f = dlg.Font;
        s.SetPlotFont(title, f.FontFamily.Name, (int)Math.Round(f.SizeInPoints * 10), f.Bold, f.Italic, s.Settings.ApplyAll);
        return true;
    }
}

/// <summary>Slice Average dialog (FR-XPT-042).</summary>
public sealed class SliceAverageWindow : DialogBase
{
    private sealed record StatRow(string Source, string Parameter, string Min, string Max, string Average, string Range, string Sum, string Points, string Sigma, string CV, string TI);

    /// <summary>Shows the statistics of the slice range (or the visible X range).</summary>
    public static bool Show(Window? owner, Session s)
    {
        var plot = s.ActivePlot;
        if (plot == null) return false;
        double a = s.SliceRange.Start, b = s.SliceRange.Stop;
        if (a == 0 && b == 0) { a = plot.XScale.RMin; b = plot.XScale.RMax; }
        if (a > b) (a, b) = (b, a);
        new SliceAverageWindow(owner, s, plot, a, b).ShowDialog();
        s.SliceRange = (0, 0);
        return true;
    }

    private SliceAverageWindow(Window? owner, Session s, PlotDocument plot, double xmin, double xmax) : base(owner, "Slice Average", 900)
    {
        Row("", new TextBlock { Text = Crt.Sprintf("Range:  %g  to  %g  (%g sec.)", xmin, xmax, xmax - xmin) });
        var prec = Row("Precision:", Combo("General", new[] { "General", "0", "1", "2", "3", "4", "5", "6", "7" }, false));
        prec.SelectedIndex = 0;
        var colAvg = Row("", Check("Show column average", false));
        var list = StretchRow(new ListView(), 260);
        var gv = new GridView();
        foreach (var (h, p) in new[] { ("Source", "Source"), ("Parameter", "Parameter"), ("Minimum", "Min"), ("Maximum", "Max"), ("Average", "Average"), ("Range", "Range"), ("Sum", "Sum"), ("#points", "Points"), ("Sigma", "Sigma"), ("CV", "CV"), ("TI", "TI") })
            gv.Columns.Add(new GridViewColumn { Header = h, DisplayMemberBinding = new System.Windows.Data.Binding(p), Width = 80 });
        list.View = gv;
        List<StatRow> rows = new();
        void Fill()
        {
            int pr = prec.SelectedIndex <= 0 ? TabFormat.General : prec.SelectedIndex - 1;
            string F(double v) => ValueFormat.FormatValue((float)v, pr);
            rows = new List<StatRow>();
            foreach (var c in plot.Curves)
            {
                var st = PlotDocument.ComputeStats(c, xmin, xmax);
                string src = Path.GetFileName(c.Data.Source?.Info.Path ?? "");
                if (st.Points == 0) { rows.Add(new StatRow(src, c.Name, "No Data", "*", "*", "*", "*", "0", "*", "*", "*")); continue; }
                rows.Add(new StatRow(src, c.Name, F(st.Min), F(st.Max), F(st.Avg), F(st.Max - st.Min), F(st.Sum), st.Points.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    st.SigmaValid ? F(st.Sigma) : "n/a", st.Avg != 0 && st.SigmaValid ? F(st.Sigma / st.Avg) : "n/a", F(st.TI)));
            }
            if (colAvg.IsChecked == true && rows.Count > 0)
            {
                string Avg(Func<StatRow, string> sel)
                {
                    var v = rows.Select(sel).Where(x => x != "*" && x != "n/a" && x != "No Data").Select(Crt.Atof).ToList();
                    return v.Count > 0 ? F(v.Average()) : "*";
                }
                rows.Add(new StatRow("All Parameters", "Column Avg.", Avg(r => r.Min), Avg(r => r.Max), Avg(r => r.Average), Avg(r => r.Range), Avg(r => r.Sum), Avg(r => r.Points), Avg(r => r.Sigma), Avg(r => r.CV), Avg(r => r.TI)));
            }
            list.ItemsSource = rows;
        }
        prec.SelectionChanged += (_, _) => Fill();
        colAvg.Click += (_, _) => Fill();
        Fill();
        AddButton("Save", () =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Crt.Sprintf("plot%04d.avg.csv", plot.PlotNo), Filter = "CSV (*.csv)|*.csv|Text (*.txt)|*.txt", InitialDirectory = Directory.Exists(s.Settings.ExportDir) ? s.Settings.ExportDir : "" };
            if (dlg.ShowDialog(this) != true) return;
            char d = dlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? ',' : '\t';
            var sb = new System.Text.StringBuilder(string.Join(d, "Source", "Parameter", "Min", "Max", "Average", "Range", "Sum", "#Points", "Sigma", "CV", "TI") + "\r\n");
            foreach (var r in rows) sb.Append(string.Join(d, r.Source, r.Parameter, r.Min, r.Max, r.Average, r.Range, r.Sum, r.Points, r.Sigma, r.CV, r.TI)).Append("\r\n");
            try { AnsiText.WriteAllText(dlg.FileName, sb.ToString()); } catch (IOException ex) { MessageBox.Show(this, ex.Message); }
        });
        AddButton("Close", () => DialogResult = true, isDefault: true, isCancel: true);
        Height = 460;
    }
}

/// <summary>Data Pick dialog (values of every curve at a time, FR-UI-033).</summary>
public sealed class DataPickWindow : DialogBase
{
    private sealed record PickRow(string Source, string Parameter, string Time, string Value);

    /// <summary>Shows the values at <see cref="Session.PickTime"/>.</summary>
    public static bool Show(Window? owner, Session s)
    {
        var plot = s.ActivePlot;
        if (plot == null) return false;
        new DataPickWindow(owner, s, plot, s.PickTime).ShowDialog();
        return true;
    }

    private DataPickWindow(Window? owner, Session s, PlotDocument plot, double t) : base(owner, Crt.Sprintf("Data Pick - time %g", t), 640)
    {
        var list = StretchRow(new ListView { SelectionMode = SelectionMode.Extended }, 220);
        var gv = new GridView();
        foreach (var (h, p) in new[] { ("Source", "Source"), ("Parameter", "Parameter"), ("Time", "Time"), ("Value", "Value") })
            gv.Columns.Add(new GridViewColumn { Header = h, DisplayMemberBinding = new System.Windows.Data.Binding(p), Width = 140 });
        list.View = gv;
        var rows = new List<PickRow>();
        foreach (var c in plot.Curves)
        {
            if (c.Points == 0) continue;
            int n = TimeMath.Location(t, c.Time, c.Points, 0);
            rows.Add(new PickRow(Path.GetFileName(c.Data.Source?.Info.Path ?? ""), c.Name, Crt.Sprintf("%g", (double)c.Time[n]), Crt.Sprintf("%g", (double)c.Values[n])));
        }
        list.ItemsSource = rows;
        void PlotRows(IEnumerable<PickRow> sel)
        {
            var names = sel.Select(r => r.Parameter).Distinct().ToList();
            if (names.Count == 0) return;
            s.YAxis = string.Join(",", names);
            s.SaveParm(s.YAxis);
            DialogResult = true;
            s.Plot();
        }
        list.MouseDoubleClick += (_, _) => PlotRows(list.SelectedItems.Cast<PickRow>());
        AddButton("Plot Selected", () => PlotRows(list.SelectedItems.Cast<PickRow>()));
        AddButton("Plot All", () => PlotRows(rows));
        AddButton("Close", () => DialogResult = true, isCancel: true);
        Height = 420;
    }
}

/// <summary>Nomenclature dialog (FR-PLT-040).</summary>
public sealed class NomenWindow : DialogBase
{
    private static readonly (int Flag, string Name)[] Items =
    {
        (Nomen.DBNAME, "Database name"), (Nomen.FILENAME, "File name"), (Nomen.NAME, "Name"), (Nomen.UNITS, "Units"), (Nomen.DESC, "Description"),
        (Nomen.REFT, "Reference time"), (Nomen.UDEF, "User defined"), (Nomen.ALTNAME, "Alternate name"), (Nomen.EXTNAME, "Extended name"),
        (Nomen.SEQNUM, "Sequence number"), (Nomen.CURVAL, "Current value"), (Nomen.CURTIME, "Current time"),
    };

    /// <summary>Shows the X or Y nomenclature dialog.</summary>
    public static bool Show(Window? owner, Session s, bool y)
    {
        var plot = s.ActivePlot;
        var cfg = plot?.Config ?? s.PlotConfig;
        int nomen = y ? cfg.YNomen : cfg.XNomen;
        int[] order = (int[])(y ? cfg.YNomOrder : cfg.XNomOrder).Clone();
        int[] len = (int[])(y ? cfg.YNomLen : cfg.XNomLen).Clone();
        string udef = y ? (plot?.YUdef ?? s.Settings.YUdef) : (plot?.XUdef ?? s.Settings.XUdef);
        var d = new NomenWindow(owner, y, nomen, order, len, udef, s.Settings.ApplyAll && plot != null, s.Settings.LogicTxtIdx, s);
        if (d.ShowDialog() != true) return false;
        s.ApplyNomenDialog(y, d.NomenFlags, d.Order, d.Len, d.Udef, d.ApplyAll, d.LogicIdx);
        return true;
    }

    public int NomenFlags { get; private set; }
    public int[] Order { get; }
    public int[] Len { get; }
    public string Udef { get; private set; } = "";
    public bool ApplyAll { get; private set; }
    public int LogicIdx { get; private set; }

    private NomenWindow(Window? owner, bool y, int nomen, int[] order, int[] len, string udef, bool all, int logicIdx, Session s) : base(owner, y ? "Y Axis/Legend Labeling" : "X Axis/Legend Labeling", 520)
    {
        Order = order;
        Len = len;
        var list = StretchRow(new ListBox(), 240);
        var checks = new Dictionary<int, CheckBox>();
        var lens = new Dictionary<int, TextBox>();
        void Build()
        {
            list.Items.Clear();
            foreach (int f in Order)
            {
                var it = Items.First(i => i.Flag == f);
                if (!y && (f == Nomen.SEQNUM || f == Nomen.CURVAL || f == Nomen.CURTIME)) continue;
                var row = new DockPanel { Tag = f };
                var lenBox = lens.TryGetValue(f, out var lb) ? lb : lens[f] = new TextBox { Width = 40, Height = 22, Padding = new Thickness(4, 1, 4, 1), Text = Len[Nomen.IndexOf(f)].ToString(System.Globalization.CultureInfo.InvariantCulture) };
                var cb = checks.TryGetValue(f, out var c) ? c : checks[f] = new CheckBox { Content = it.Name, IsChecked = (nomen & f) != 0, VerticalAlignment = VerticalAlignment.Center };
                (lenBox.Parent as Panel)?.Children.Remove(lenBox);
                (cb.Parent as Panel)?.Children.Remove(cb);
                DockPanel.SetDock(lenBox, Dock.Right);
                row.Children.Add(lenBox);
                row.Children.Add(cb);
                list.Items.Add(row);
            }
        }
        Build();
        var moves = new StackPanel { Orientation = Orientation.Horizontal };
        var up = new Button { Content = "Move Up", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(6, 0, 6, 0) };
        var down = new Button { Content = "Move Down", Padding = new Thickness(6, 0, 6, 0) };
        moves.Children.Add(up);
        moves.Children.Add(down);
        Row("", moves);
        void Move(int d)
        {
            if (list.SelectedItem is not DockPanel p || p.Tag is not int f) return;
            int i = Array.IndexOf(Order, f), j = i + d;
            if (j < 0 || j >= Order.Length) return;
            (Order[i], Order[j]) = (Order[j], Order[i]);
            Build();
            list.SelectedIndex = list.Items.Cast<DockPanel>().ToList().FindIndex(x => (int)x.Tag == f);
        }
        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(1);
        var ud = Row("User defined text:", Text(udef));
        var efmt = Row("", Check("Exponential number format", (nomen & Nomen.EFORMAT) != 0));
        var logic = Row("Logic labels:", Combo("", Enumerable.Range(0, 4).Select(i => (i + 1) + ": " + s.Settings.LogicTxtTrue[i] + "/" + s.Settings.LogicTxtFalse[i]), false));
        logic.SelectedIndex = Math.Clamp(logicIdx, 0, 3);
        logic.IsEnabled = y;
        var applyAll = Row("", Check("Apply to all plots", all));
        OkCancel(() =>
        {
            int n = 0;
            foreach (var (f, cb) in checks) if (cb.IsChecked == true) n |= f;
            if (efmt.IsChecked == true) n |= Nomen.EFORMAT;
            foreach (var (f, tb) in lens) Len[Nomen.IndexOf(f)] = Math.Clamp(Crt.Atoi(tb.Text), 0, 256);
            NomenFlags = n;
            Udef = ud.Text;
            ApplyAll = applyAll.IsChecked == true;
            LogicIdx = Math.Max(0, logic.SelectedIndex);
            return true;
        });
        Height = 520;
    }
}

/// <summary>
/// Event marker dialogs (FR-MRK-005): <i>Add Event Marker</i> (timestamp, text, top/bottom) and the
/// <i>Edit</i> list editor for the markers of the active plot. Changes apply to the active plot, or to all
/// plots with Apply Changes Globally, and to the master set.
/// </summary>
public sealed class MarkerWindow : DialogBase
{
    private const string TimeHelp = "GMT (ddd/hh:mm:ss.mmm or hh:mm:ss), seconds on the time axis, or an [events] name; optional +/-seconds offset.";

    /// <summary>Shows the add dialog (<paramref name="addOnly"/>) or the list editor.</summary>
    public static bool Show(Window? owner, Session s, bool addOnly)
    {
        var d = new MarkerWindow(owner, s, addOnly);
        return d.ShowDialog() == true;
    }

    private static IEnumerable<string> TimeChoices(Session s) =>
        new[] { "START" }.Concat(s.Files.SelectMany(f => f.Events.Select(e => e.Name)).Where(n => !Marker.IsReservedEvent(n)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Display(Marker m) => m.Time + "    " + m.Text + (m.Bottom ? "    (bottom)" : "");

    /// <summary>
    /// Checks a marker before it is accepted: syntax, non-empty text, and (when data files are open and
    /// no real-time data is loaded) that the timestamp resolves; an unresolved timestamp may still be kept.
    /// </summary>
    private static bool Validate(Session s, string time, string text)
    {
        if (!Marker.IsValidTime(time)) { s.Message("Enter a timestamp:\n" + TimeHelp); return false; }
        if (text.Trim().Length == 0) { s.Message("Enter the marker text."); return false; }
        if (s.Files.Count > 0 && !s.RtLoaded)
        {
            var plot = s.ActivePlot;
            var groups = plot != null ? plot.Curves.Select(c => c.GroupNo).Distinct().ToList() : new List<int> { 0 };
            var r = EventMarkers.Resolve(new[] { new Marker(time, text) }, false, s.Files, s.Groups, groups, plot?.TReference ?? "NONE");
            if (r.Count == 0 &&
                s.Message("Timestamp '" + time + "' was not found in the open files.\nKeep the marker anyway?", PromptButtons.YesNo, PromptIcon.Question, PromptResult.No) != PromptResult.Yes)
                return false;
        }
        return true;
    }

    private MarkerWindow(Window? owner, Session s, bool addOnly) : base(owner, addOnly ? "Add Event Marker" : "Event Markers", 520)
    {
        var plot = s.ActivePlot;
        string scope = s.Settings.ApplyAll ? "all plots (Apply Changes Globally is on)" : plot != null ? "the active plot" : "new plots (no plot is open)";
        ListBox? list = null;
        var work = Marker.CopySet(plot?.Markers ?? s.MasterMarkers);
        if (!addOnly)
        {
            list = StretchRow(new ListBox { FontFamily = new FontFamily("Consolas") }, 160);
            foreach (var m in work) list.Items.Add(Display(m));
        }
        var time = Row("Timestamp:", Combo("", TimeChoices(s)));
        time.ToolTip = TimeHelp;
        var text = Row("Text:", Text(""));
        var pos = Row("Text position:", Combo("Top", new[] { "Top", "Bottom" }, false));
        Row("", new TextBlock { Text = "Applies to " + scope + ".", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });

        if (addOnly && plot != null && plot.Config.Style != PlotStyle.Tabular && !plot.Config.PidVsPid && !plot.Config.LogX && !plot.Config.EndToEnd)
            time.Text = Crt.Sprintf("%.3f", (plot.XScale.RMin + plot.XScale.RMax) / 2.0);

        if (addOnly)
        {
            OkCancel(() =>
            {
                if (!Validate(s, time.Text, text.Text)) return false;
                var m = new Marker(time.Text, text.Text, pos.SelectedIndex == 1);
                s.AddMarker(m);
                s.Record(Session.MarkerCommand(m));
                return true;
            });
            return;
        }

        list!.SelectionChanged += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= work.Count) return;
            time.Text = work[i].Time;
            text.Text = work[i].Text;
            pos.SelectedIndex = work[i].Bottom ? 1 : 0;
        };
        var edit = new StackPanel { Orientation = Orientation.Horizontal };
        Button B(string caption, Action a)
        {
            var b = new Button { Content = caption, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += (_, _) => a();
            edit.Children.Add(b);
            return b;
        }
        B("_Add", () =>
        {
            if (!Validate(s, time.Text, text.Text)) return;
            var m = new Marker(time.Text, text.Text, pos.SelectedIndex == 1);
            if (work.Count >= Marker.MaxMarkers) { s.Message("At most " + Marker.MaxMarkers + " markers per plot."); return; }
            if (work.Any(x => x.SameAs(m))) return;
            work.Add(m);
            list.Items.Add(Display(m));
            list.SelectedIndex = work.Count - 1;
        });
        B("_Update", () =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= work.Count || !Validate(s, time.Text, text.Text)) return;
            work[i] = new Marker(time.Text, text.Text, pos.SelectedIndex == 1);
            list.Items[i] = Display(work[i]);
            list.SelectedIndex = i;
        });
        B("_Delete", () =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= work.Count) return;
            work.RemoveAt(i);
            list.Items.RemoveAt(i);
            if (work.Count > 0) list.SelectedIndex = Math.Min(i, work.Count - 1);
        });
        B("Delete A_ll", () => { work.Clear(); list.Items.Clear(); });
        Row("", edit);
        OkCancel(() =>
        {
            s.ReplaceMarkers(work);
            return true;
        });
        Height = 440;
    }
}

/// <summary>
/// Redline marker dialogs (FR-MRK-011): <i>Add Redline Marker</i> (value, text, left/right) and the
/// <i>Edit Redlines</i> list editor for the redlines of the active plot. Changes apply to the active plot, or
/// to all plots with Apply Changes Globally, and to the master set.
/// </summary>
public sealed class RedlineWindow : DialogBase
{
    private const string ValueHelp = "A number on the value (Y) axis, for example 450 or -1.5e3. In multiple-Y mode it is measured on the selected axis.";

    /// <summary>Shows the add dialog (<paramref name="addOnly"/>) or the list editor.</summary>
    public static bool Show(Window? owner, Session s, bool addOnly)
    {
        var d = new RedlineWindow(owner, s, addOnly);
        return d.ShowDialog() == true;
    }

    private static string Display(RedlineMarker r) => r.ValueText + "    " + r.Text + (r.Right ? "    (right)" : "");

    /// <summary>Checks a redline before it is accepted: numeric value and non-empty text.</summary>
    private static RedlineMarker? Build(Session s, string value, string text, bool right)
    {
        if (!RedlineMarker.TryParseValue(value, out double v)) { s.Message("Enter the redline value:\n" + ValueHelp); return null; }
        if (text.Trim().Length == 0) { s.Message("Enter the redline text."); return null; }
        if (text.Contains('\n') || text.Contains('\r')) { s.Message("The redline text must be a single line."); return null; }
        return new RedlineMarker(v, text, right);
    }

    private RedlineWindow(Window? owner, Session s, bool addOnly) : base(owner, addOnly ? "Add Redline Marker" : "Redline Markers", 520)
    {
        var plot = s.ActivePlot;
        string scope = s.Settings.ApplyAll ? "all plots (Apply Changes Globally is on)" : plot != null ? "the active plot" : "new plots (no plot is open)";
        ListBox? list = null;
        var work = RedlineMarker.CopySet(plot?.Redlines ?? s.MasterRedlines);
        if (!addOnly)
        {
            list = StretchRow(new ListBox { FontFamily = new FontFamily("Consolas") }, 160);
            foreach (var r in work) list.Items.Add(Display(r));
        }
        var value = Row("Value:", Text(""));
        value.ToolTip = ValueHelp;
        var text = Row("Text:", Text(""));
        var pos = Row("Text position:", Combo("Left", new[] { "Left", "Right" }, false));
        Row("", new TextBlock { Text = "Applies to " + scope + ".", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });

        if (addOnly && plot != null && plot.YScales.Count > 0 && plot.Config.Style != PlotStyle.Tabular && plot.Config.Style != PlotStyle.Logic)
        {
            // start with the centre of the visible range of the selected value axis
            int axis = plot.Config.Multi && plot.SelectedAxis >= 0 && plot.SelectedAxis < plot.YScales.Count ? plot.SelectedAxis : 0;
            var ys = plot.YScales[axis];
            double mid = (ys.RMin + ys.RMax) / 2.0;
            if (plot.Config.LogY) mid = Math.Exp(mid);
            if (double.IsFinite(mid)) value.Text = mid.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (addOnly)
        {
            OkCancel(() =>
            {
                var r = Build(s, value.Text, text.Text, pos.SelectedIndex == 1);
                if (r == null) return false;
                s.AddRedline(r);
                s.Record(r.ToCommand());
                return true;
            });
            return;
        }

        list!.SelectionChanged += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= work.Count) return;
            value.Text = work[i].ValueText;
            text.Text = work[i].Text;
            pos.SelectedIndex = work[i].Right ? 1 : 0;
        };
        var edit = new StackPanel { Orientation = Orientation.Horizontal };
        void B(string caption, Action a)
        {
            var b = new Button { Content = caption, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += (_, _) => a();
            edit.Children.Add(b);
        }
        B("_Add", () =>
        {
            var r = Build(s, value.Text, text.Text, pos.SelectedIndex == 1);
            if (r == null) return;
            if (work.Count >= RedlineMarker.MaxRedlines) { s.Message("At most " + RedlineMarker.MaxRedlines + " redlines per plot."); return; }
            if (work.Any(x => x.SameAs(r))) return;
            work.Add(r);
            list.Items.Add(Display(r));
            list.SelectedIndex = work.Count - 1;
        });
        B("_Update", () =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= work.Count) return;
            var r = Build(s, value.Text, text.Text, pos.SelectedIndex == 1);
            if (r == null) return;
            work[i] = r;
            list.Items[i] = Display(r);
            list.SelectedIndex = i;
        });
        B("_Delete", () =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || i >= work.Count) return;
            work.RemoveAt(i);
            list.Items.RemoveAt(i);
            if (work.Count > 0) list.SelectedIndex = Math.Min(i, work.Count - 1);
        });
        B("Delete A_ll", () => { work.Clear(); list.Items.Clear(); });
        Row("", edit);
        OkCancel(() =>
        {
            s.ReplaceRedlines(work);
            return true;
        });
        Height = 440;
    }
}

/// <summary>Favorites dialog (FR-LOAD-050).</summary>
public sealed class FavoritesWindow : DialogBase
{
    private static bool s_plot = true, s_caption;

    private sealed record Fav(string Label, string Y, string X)
    {
        public override string ToString() => Label;
    }

    /// <summary>Shows the favourites.</summary>
    public static bool Show(Window? owner, Session s) => new FavoritesWindow(owner, s).ShowDialog() == true;

    private static List<Fav> Load(string file)
    {
        var l = new List<Fav>();
        if (!File.Exists(file)) return l;
        foreach (var raw in AnsiText.ReadAllLines(file))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string Value(string key)
            {
                int i = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return "";
                string rest = line[(i + key.Length)..];
                int next = int.MaxValue;
                foreach (var k in new[] { "name=", "yaxis=", "xaxis=" })
                {
                    int j = rest.IndexOf(k, StringComparison.OrdinalIgnoreCase);
                    if (j >= 0 && j < next) next = j;
                }
                return (next == int.MaxValue ? rest : rest[..next]).Trim();
            }
            string name = Value("name=");
            l.Add(new Fav(name.Length > 0 ? name : line, Value("yaxis="), Value("xaxis=")));
        }
        return l;
    }

    private FavoritesWindow(Window? owner, Session s) : base(owner, "Favorites", 480)
    {
        var file = Row("Favorites file:", Text(s.Settings.FavFile));
        var list = StretchRow(new ListBox(), 220);
        var plot = Row("", Check("Plot", s_plot));
        var caption = Row("", Check("Use label as plot title", s_caption));
        void Fill() { list.ItemsSource = Load(file.Text); }
        Fill();
        AddButton("Browse", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Favorites (*.favorites;*.fav)|*.favorites;*.fav|All files (*.*)|*.*" };
            if (d.ShowDialog(this) == true) { file.Text = d.FileName; s.Settings.FavFile = d.FileName; Fill(); }
        });
        void Accept()
        {
            if (list.SelectedItem is not Fav f) return;
            s_plot = plot.IsChecked == true;
            s_caption = caption.IsChecked == true;
            s.YAxis = f.Y;
            s.XAxis = f.X.Length > 0 ? f.X : "(default)";
            s.SaveParm(s.YAxis);
            DialogResult = true;
            if (s_plot)
            {
                string saveTitle = s.Settings.XTitle;
                if (s_caption) s.Settings.XTitle = "\"" + f.Label + "\"";
                s.Plot();
                if (s_caption) s.Settings.XTitle = saveTitle;
            }
        }
        list.MouseDoubleClick += (_, _) => Accept();
        AddButton("OK", Accept, isDefault: true);
        AddButton("Cancel", () => DialogResult = false, isCancel: true);
        Height = 400;
    }
}

/// <summary>Filter Helper dialog (FR-UI-022).</summary>
public sealed class FilterWindow : DialogBase
{
    private static string s_p1 = "", s_p2 = "", s_op = ">", s_y = "", s_x = "(default)";
    private static readonly List<string> s_filters = new();

    /// <summary>Shows the helper.</summary>
    public static bool Show(Window? owner, Session s) => new FilterWindow(owner, s).ShowDialog() == true;

    private FilterWindow(Window? owner, Session s) : base(owner, "Filter Helper", 520)
    {
        var names = s.Files.SelectMany(f => f.Parameters.Select(p => p.Name)).Distinct().OrderBy(n => n).ToList();
        var p1 = Row("Parameter 1:", Combo(s_p1, names));
        var op = Row("Operator:", Combo(s_op, new[] { ">", "<", ">=", "<=", "==", "!=" }));
        var p2 = Row("Parameter 2 / value:", Combo(s_p2, names));
        var filters = StretchRow(new ListBox(), 120);
        foreach (var f in s_filters) filters.Items.Add(f);
        var addRem = new StackPanel { Orientation = Orientation.Horizontal };
        var add = new Button { Content = "Add Filter", Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(6, 0, 6, 0) };
        var rem = new Button { Content = "Remove Filter", Padding = new Thickness(6, 0, 6, 0) };
        addRem.Children.Add(add);
        addRem.Children.Add(rem);
        Row("", addRem);
        var y = Row("Y axis parameter:", Combo(s_y, names));
        var x = Row("X axis:", Combo(s_x, new[] { "(default)" }.Concat(names)));
        var doPlot = Row("", Check("Plot", true));
        add.Click += (_, _) =>
        {
            string a = p1.Text.Trim(), o = op.Text.Trim(), b = p2.Text.Trim();
            if (a.Length == 0 || o.Length == 0 || b.Length == 0) { s.Message("Missing parameter or operator."); return; }
            string Br(string t) => ExprSyntax.ValidateConst(t) == 0 ? t : "[" + t + "]";
            filters.Items.Add(Br(a) + o + Br(b));
        };
        rem.Click += (_, _) => { if (filters.SelectedIndex >= 0) filters.Items.RemoveAt(filters.SelectedIndex); else s.Message("Select a filter from listbox"); };
        OkCancel(() =>
        {
            if (filters.Items.Count == 0) { s.Message("No filters are defined"); return false; }
            var fl = filters.Items.Cast<string>().ToList();
            string yAxis = string.Join(" && ", fl.Select(f => "(" + f + ")"));
            if (y.Text.Trim().Length > 0) yAxis += " = [" + y.Text.Trim() + "]";
            s_filters.Clear();
            s_filters.AddRange(fl);
            s_p1 = p1.Text; s_p2 = p2.Text; s_op = op.Text; s_y = y.Text; s_x = x.Text;
            s.YAxis = yAxis;
            s.XAxis = x.Text.Trim().Length == 0 ? "(default)" : x.Text.Trim();
            s.SaveParm(yAxis);
            s.Record("xaxis " + s.XAxis);
            s.Record("yaxis " + s.YAxis);
            if (doPlot.IsChecked == true) Dispatcher.BeginInvoke(s.Plot);
            return true;
        });
    }
}

/// <summary>File and parameter property reports (FR-UI-030/031).</summary>
public sealed class TextReportWindow : DialogBase
{
    private TextReportWindow(Window? owner, string title) : base(owner, title, 720) { }

    /// <summary>File Properties.</summary>
    public static bool FileProperties(Window? owner, Session s)
    {
        var d = new TextReportWindow(owner, "File Properties");
        var list = d.Row("File:", Combo("", new List<string>(), false));
        var text = d.StretchRow(new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsReturn = true }, 300);
        void Fill()
        {
            list.Items.Clear();
            list.Items.Add(Crt.Sprintf("Total: %d files in %d groups", s.Files.Count, s.Groups.Count));
            for (int g = 0; g < s.Groups.Count; g++)
                for (int j = 0; j < s.Groups[g].NFiles; j++)
                    list.Items.Add(Crt.Sprintf("%s (group:%d file:%d) : %s", s.Groups[g].GroupName, g + 1, j + 1, Path.GetFileName(s.Files[s.Groups[g].StartPos + j].Info.Path)));
            list.SelectedIndex = s.Files.Count > 0 ? 1 : 0;
        }
        list.SelectionChanged += (_, _) =>
        {
            int i = list.SelectedIndex - 1;
            text.Text = DetailsWriter.FileDetails(s.Files, s.Groups, i, s.Data.GetDescription) ?? "";
        };
        Fill();
        d.AddButton("Copy", () => s.Shell.CopyTextToClipboard(text.Text));
        d.AddButton("Copy All", () => s.Shell.CopyTextToClipboard(string.Join("\r\n", Enumerable.Range(0, s.Files.Count).Select(i => DetailsWriter.FileDetails(s.Files, s.Groups, i, s.Data.GetDescription)))));
        d.AddButton("Save", () =>
        {
            var f = new Microsoft.Win32.SaveFileDialog { FileName = "File Details.txt" };
            if (f.ShowDialog(d) == true) AnsiText.WriteAllText(f.FileName, text.Text);
        });
        d.AddButton("Remove File", () =>
        {
            int i = list.SelectedIndex - 1;
            if (i < 0) return;
            if (s.Plots.Count > 0 && s.Message("Plot Windows will be closed", PromptButtons.OkCancel, PromptIcon.Warning) == PromptResult.Cancel) return;
            s.RemoveFile(i);
            Fill();
        });
        d.AddButton("Close", () => d.DialogResult = true, isCancel: true);
        d.Height = 560;
        d.ShowDialog();
        return true;
    }

    /// <summary>Parameter Properties.</summary>
    public static bool ParameterProperties(Window? owner, Session s)
    {
        var plot = s.ActivePlot;
        if (plot == null) return false;
        var d = new TextReportWindow(owner, "Parameter Properties");
        var list = d.Row("Curve:", Combo("", new List<string>(), false));
        var text = d.StretchRow(new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AcceptsReturn = true }, 300);
        void Fill()
        {
            list.Items.Clear();
            list.Items.Add(Crt.Sprintf("Plot #%d: %d parameter(s)", plot.PlotNo, plot.NCurves));
            for (int i = 0; i < plot.NCurves; i++) list.Items.Add(Crt.Sprintf("Curve %d:  <%s>%s", i + 1, Path.GetFileName(plot.Curves[i].Data.Source?.Info.Path ?? ""), plot.Curves[i].Name));
            list.SelectedIndex = plot.NCurves > 0 ? 1 : 0;
        }
        list.SelectionChanged += (_, _) => text.Text = DetailsWriter.ParmDetails(plot, list.SelectedIndex - 1) ?? "";
        Fill();
        d.AddButton("Move Up", () =>
        {
            int i = list.SelectedIndex - 1;
            if (i < 1) return;
            (plot.Curves[i - 1], plot.Curves[i]) = (plot.Curves[i], plot.Curves[i - 1]);
            plot.ResetMinMax(2, false); s.Invalidate(plot); Fill(); list.SelectedIndex = i;
        });
        d.AddButton("Move Down", () =>
        {
            int i = list.SelectedIndex - 1;
            if (i < 0 || i >= plot.NCurves - 1) return;
            (plot.Curves[i + 1], plot.Curves[i]) = (plot.Curves[i], plot.Curves[i + 1]);
            plot.ResetMinMax(2, false); s.Invalidate(plot); Fill(); list.SelectedIndex = i + 2;
        });
        d.AddButton("Remove", () =>
        {
            int i = list.SelectedIndex - 1;
            if (i < 0) return;
            if (plot.NCurves <= 1) { s.Message("Can't remove last parameter"); return; }
            plot.Curves.RemoveAt(i);
            if (plot.YScales.Count > plot.NCurves) plot.YScales.RemoveAt(plot.YScales.Count - 1);
            plot.ResetMinMax(3, false);
            s.Invalidate(plot);
            Fill();
        });
        d.AddButton("Copy", () => s.Shell.CopyTextToClipboard(text.Text));
        d.AddButton("Save", () =>
        {
            var f = new Microsoft.Win32.SaveFileDialog { FileName = "Parameter Details.txt" };
            if (f.ShowDialog(d) == true) AnsiText.WriteAllText(f.FileName, text.Text);
        });
        d.AddButton("Close", () => d.DialogResult = true, isCancel: true);
        d.Height = 520;
        d.ShowDialog();
        return true;
    }
}

/// <summary>Symbolic Names editor (FR-UI-034).</summary>
public sealed class SymbolsWindow : DialogBase
{
    /// <summary>Shows the editor.</summary>
    public static bool Show(Window? owner, Session s) => new SymbolsWindow(owner, s).ShowDialog() == true;

    private SymbolsWindow(Window? owner, Session s) : base(owner, "Symbolic Names", 520)
    {
        var entries = s.Symbols.Entries.ToList();
        var list = StretchRow(new ListBox { FontFamily = new FontFamily("Consolas") }, 220);
        var name = Row("Name:", Text(""));
        var value = Row("Value:", Text(""));
        void Fill() { list.ItemsSource = entries.Select(e => SymbolTable.NameOf(e) + " = " + SymbolTable.ValueOf(e)).ToList(); }
        Fill();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            name.Text = SymbolTable.NameOf(entries[list.SelectedIndex]);
            value.Text = SymbolTable.ValueOf(entries[list.SelectedIndex]);
        };
        AddButton("Add/Update", () =>
        {
            string n = name.Text.Replace("$", "").Trim().ToUpperInvariant();
            if (n.Length == 0) return;
            int i = entries.FindIndex(e => SymbolTable.NameOf(e).Equals(n, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
            {
                if (MessageBox.Show(this, "Name already exists, replace?", "Symbolic Names", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                entries[i] = n + "\t" + value.Text.Trim();
            }
            else entries.Add(n + "\t" + value.Text.Trim());
            Fill();
        });
        AddButton("Delete", () => { if (list.SelectedIndex >= 0) { entries.RemoveAt(list.SelectedIndex); Fill(); } });
        AddButton("Clear", () => { entries.Clear(); Fill(); });
        OkCancel(() =>
        {
            s.Symbols.Entries.Clear();
            s.Symbols.Entries.AddRange(entries);
            s.Symbols.Sort();
            s.Record("clear $$");
            foreach (var e in s.Symbols.Entries) s.Record("set " + SymbolTable.NameOf(e) + " = " + SymbolTable.ValueOf(e));
            return true;
        });
        Height = 420;
    }
}

/// <summary>Manage Plug-ins dialog (FR-PLG-012).</summary>
public sealed class PluginsWindow : DialogBase
{
    private sealed record PluginRow(string Filename, string Version, string Id, string Enabled, string Description);

    /// <summary>Shows the dialog.</summary>
    public static bool Show(Window? owner, Session s, MainWindow main) => new PluginsWindow(owner, s, main).ShowDialog() == true;

    private PluginsWindow(Window? owner, Session s, MainWindow main) : base(owner, "Manage Plug-ins", 760)
    {
        var rows = new List<PluginRow>
        {
            new("DataAcc.NET", Multiplot.DataAccess.DataAccessService.DataAccVersion, "n/a", "Always", "Data access core"),
            new("SunAcc.NET", Multiplot.Sun.SunProvider.Version, "f0ffffff", "Always", "SUN formats"),
        };
        foreach (var p in s.Data.Plugins)
        {
            string id = p.Plugin is IDataSourceProvider dp ? Crt.Sprintf("%08x", dp.Signature) : "n/a";
            rows.Add(new(p.FileName, p.Plugin.Version, id, p.Plugin is IDataSourceProvider ? "Yes" : "Loaded", p.Plugin.Description));
        }
        foreach (var b in s.Data.BlockedPlugins) rows.Add(new(b, "", "", "No", ""));
        var list = StretchRow(new ListView(), 220);
        var gv = new GridView();
        foreach (var (h, p, w) in new[] { ("Filename", "Filename", 160.0), ("Version", "Version", 200.0), ("ID", "Id", 80.0), ("Enabled", "Enabled", 70.0), ("Description", "Description", 220.0) })
            gv.Columns.Add(new GridViewColumn { Header = h, DisplayMemberBinding = new System.Windows.Data.Binding(p), Width = w });
        list.View = gv;
        list.ItemsSource = rows;
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedItem is not PluginRow r || (r.Enabled != "Yes" && r.Enabled != "No")) return;
            int i = rows.IndexOf(r);
            rows[i] = r with { Enabled = r.Enabled == "Yes" ? "No" : "Yes" };
            list.ItemsSource = null;
            list.ItemsSource = rows;
        };
        var userFolder = Row("", Check("Load plugins from user folder (" + Path.Combine(s.Settings.UserFolder, "plugins") + ")", s.UseUserPluginFolder));
        OkCancel(() =>
        {
            s.Registry.WriteBlockedPlugins(rows.Where(r => r.Enabled == "No").Select(r => r.Filename));
            s.UseUserPluginFolder = userFolder.IsChecked == true;
            s.Registry.WriteUserPluginFolder(s.UseUserPluginFolder);
            s.NoPlugins = false;
            s.SetupPlugins();
            main.RebuildCommands();
            return true;
        });
        Height = 420;
    }
}

/// <summary>Small form dialogs.</summary>
public static class SimpleForms
{
    private sealed class Form : DialogBase
    {
        public Form(Window? owner, string title, double width = 520) : base(owner, title, width) { }
        public T R<T>(string label, T c) where T : FrameworkElement => Row(label, c);
        public T S<T>(T c, double min = 200) where T : FrameworkElement => StretchRow(c, min);
        public void Ok(Func<bool>? v = null) => OkCancel(v);
        public Button B(string t, Action a) => AddButton(t, a);
        public static TextBox T(string v) => Text(v);
        public static CheckBox C(string t, bool v) => Check(t, v);
    }

    /// <summary>Folders of a <c>;</c>-separated search path list, trimmed, empty entries removed.</summary>
    private static List<string> PathEntries(string list) =>
        list.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

    /// <summary>
    /// A path text box with a <i>Browse...</i> button that opens the Windows folder selection dialog. For a
    /// search path list (<paramref name="list"/>) the folders chosen (several can be selected) are added to
    /// the list unless already in it; for a single folder the choice replaces the text.
    /// </summary>
    private static TextBox FolderRow(Form f, string label, string value, bool list)
    {
        var box = Form.T(value);
        var browse = new Button { Content = "Browse...", MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };
        browse.ToolTip = list ? "Add folders to the search path" : "Choose the folder";
        var panel = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        panel.Children.Add(browse);
        panel.Children.Add(box);
        f.R(label, panel);
        browse.Click += (_, _) =>
        {
            var entries = list ? PathEntries(box.Text) : new List<string> { box.Text.Trim() };
            string? start = entries.FirstOrDefault(Directory.Exists);
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = list ? label.Replace("__", "_").TrimEnd(':') + " — add folders" : label.Replace("__", "_").TrimEnd(':'),
                Multiselect = list,
            };
            if (start != null) dlg.InitialDirectory = start;
            if (dlg.ShowDialog(f) != true) return;
            if (!list) { box.Text = dlg.FolderName; return; }
            foreach (var folder in dlg.FolderNames)
                if (!entries.Any(e => string.Equals(e.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                    entries.Add(folder);
            box.Text = string.Join(";", entries);
        };
        return box;
    }

    /// <summary>
    /// File Paths (FR-UI-035): each path has a <i>Browse...</i> button with the Windows folder selection
    /// dialog; the search paths are <c>;</c>-separated folder lists that can still be edited as text.
    /// </summary>
    public static bool Paths(Window? owner, Session s)
    {
        var f = new Form(owner, "File Paths", 640);
        var data = FolderRow(f, "Data file search path:", s.Settings.FileSpec, true);
        var db = FolderRow(f, "DB__DIR search path:", s.Settings.DbFolder, true);
        var script = FolderRow(f, "Script search path:", s.Settings.ScriptDir, true);
        var wpd = FolderRow(f, "WPD search path:", s.Settings.WpdFolder, true);
        var export = FolderRow(f, "Export folder:", s.Settings.ExportDir, false);
        f.R("", new TextBlock { Text = "Search paths are lists of folders separated by ';'. Browse adds folders to the list.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        f.Ok();
        if (f.ShowDialog() != true) return false;
        s.Settings.FileSpec = data.Text; s.Settings.DbFolder = db.Text; s.Settings.ScriptDir = script.Text; s.Settings.WpdFolder = wpd.Text; s.Settings.ExportDir = export.Text;
        if (!Directory.Exists(s.Settings.ExportDir)) s.Message("Export path is invalid and should be reset\nTabulations and Exports may fail");
        s.Record("FilePath " + s.Settings.FileSpec.Replace(";", "\\;"));
        s.Record("DBpath " + s.Settings.DbFolder.Replace(";", "\\;"));
        s.Record("ScriptPath " + s.Settings.ScriptDir.Replace(";", "\\;"));
        s.Record("WPDpath " + s.Settings.WpdFolder.Replace(";", "\\;"));
        s.Record("ExportPath " + s.Settings.ExportDir);
        return true;
    }

    /// <summary>External applications (FR-UI-036).</summary>
    public static bool ExternalApps(Window? owner, Session s)
    {
        var f = new Form(owner, "External Applications");
        var view = f.R("Tab view / edit application:", Form.T(s.Settings.EditApp));
        var export = f.R("Export application:", Form.T(s.Settings.ExportApp));
        var printer = f.R("Text printer command (stored):", Form.T(s.Settings.TextPrinter));
        f.Ok();
        if (f.ShowDialog() != true) return false;
        s.Settings.EditApp = view.Text; s.Settings.ExportApp = export.Text; s.Settings.TextPrinter = printer.Text;
        return true;
    }

    /// <summary>AutoRun list editor (FR-RT-040).</summary>
    public static bool AutoRun(Window? owner, Session s)
    {
        var f = new Form(owner, "Auto Run");
        var entries = s.AutoRunList.ToList();
        var list = f.S(new ListBox(), 160);
        var ev = f.R("Event:", Form.T(""));
        var wps = f.R("Script:", Form.T(""));
        void Fill() { list.ItemsSource = entries.ToList(); }
        Fill();
        f.B("Add", () => { if (ev.Text.Trim().Length > 0 && wps.Text.Trim().Length > 0) { entries.Add(ev.Text.Trim().ToUpperInvariant() + "," + wps.Text.Trim()); Fill(); } });
        f.B("Remove", () => { if (list.SelectedIndex >= 0) { entries.RemoveAt(list.SelectedIndex); Fill(); } });
        f.Ok();
        if (f.ShowDialog() != true) return false;
        s.AutoRunList.Clear();
        s.Record("AUTORUN clear");
        foreach (var e in entries) { s.AutoRunList.Add(e); s.Record("AUTORUN " + e); }
        return true;
    }

    /// <summary>Window list (FR-UI-043).</summary>
    public static bool WindowList(Window? owner, Session s)
    {
        var f = new Form(owner, "Select Window", 420);
        var list = f.S(new ListBox(), 200);
        var plots = s.Plots.OrderBy(p => p.PlotNo).ToList();
        list.ItemsSource = plots.Select(p => Crt.Sprintf("Plot #%d  %s", p.PlotNo, p.Caption)).ToList();
        list.MouseDoubleClick += (_, _) => f.DialogResult = true;
        f.Ok();
        if (f.ShowDialog() != true || list.SelectedIndex < 0) return false;
        s.ActivatePlot(plots[list.SelectedIndex]);
        return true;
    }

    /// <summary>About (FR-UI-045).</summary>
    public static bool About(Window? owner, Session s)
    {
        var f = new Form(owner, "About Multiplot", 460);
        f.R("", new TextBlock { Text = Session.ProductVersion + "\n" + Session.ProductDate + "\n\n" + string.Join("\n", s.Data.VersionLines()), TextWrapping = TextWrapping.Wrap });
        f.B("OK", () => f.DialogResult = true);
        f.ShowDialog();
        return true;
    }

    /// <summary>Time alignment tool (FR-UI-024, simplified: shift per curve).</summary>
    public static bool TimeAlign(Window? owner, Session s)
    {
        var plot = s.ActivePlot;
        if (plot == null) return false;
        var f = new Form(owner, "Time Alignment Tool", 480);
        var curve = f.R("Curve:", new ComboBox { ItemsSource = plot.Curves.Select(c => c.Name).ToList(), SelectedIndex = 0 });
        var shift = f.R("Time shift (seconds):", Form.T(Crt.Sprintf("%.3f", plot.NCurves > 0 ? (double)plot.Curves[0].Data.TimeShift : 0)));
        curve.SelectionChanged += (_, _) => { if (curve.SelectedIndex >= 0) shift.Text = Crt.Sprintf("%.3f", (double)plot.Curves[curve.SelectedIndex].Data.TimeShift); };
        f.Ok();
        if (f.ShowDialog() != true || curve.SelectedIndex < 0) return false;
        var c = plot.Curves[curve.SelectedIndex];
        float newShift = (float)Crt.Atof(shift.Text);
        float delta = newShift - c.Data.TimeShift;
        if (delta != 0)
        {
            for (int i = 0; i < c.Points; i++) c.Time[i] += delta;
            c.Data.TimeShift = newShift;
            plot.ResetMinMax(3, false);
            s.Invalidate(plot);
        }
        return true;
    }

    /// <summary>User button assignment.</summary>
    public static bool UserButtons(Window? owner, Session s, MainWindow main)
    {
        var f = new Form(owner, "User Functions", 520);
        var labels = new List<(TextBox L, TextBox C)>();
        for (int i = 0; i < Core.Model.AppSettings.NUserButtons; i++)
        {
            var l = f.R(Crt.Sprintf("User %d label:", i + 1), Form.T(s.Settings.UserButtons[i].Label));
            var c = f.R("command:", Form.T(s.Settings.UserButtons[i].Command));
            labels.Add((l, c));
        }
        f.Ok();
        if (f.ShowDialog() != true) return false;
        for (int i = 0; i < labels.Count; i++)
        {
            s.Settings.UserButtons[i].Label = labels[i].L.Text.Trim();
            s.Settings.UserButtons[i].Command = labels[i].C.Text.Trim();
            if (labels[i].L.Text.Trim().Length > 0) s.Record(Crt.Sprintf("SET USER%d %s,%s", i + 1, labels[i].L.Text.Trim(), labels[i].C.Text.Trim()));
        }
        main.RebuildCommands();
        return true;
    }
}
