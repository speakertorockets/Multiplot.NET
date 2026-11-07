// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

using System;using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;       // <- for RenderOptions
using System.Windows.Interop;     // <- for RenderMode
using System.Runtime.InteropServices;
using Multiplot.Application;
using Multiplot.Settings;
using AppStartup = Multiplot.Application.Startup;

namespace Multiplot.App;

/// <summary>Entry point.</summary>
public static class Program
{
    /// <summary>Main.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        bool useSoftware = args?.Any(a => a == "--software-rendering") == true;
        if (useSoftware)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        GlobalExceptionHandler.Install("Multiplot");

        string raw = string.Join(" ", args.Select(a => a.Contains(' ') && !a.StartsWith('"') ? "\"" + a + "\"" : a));
        var app = new App(raw);
        try
        {
            return app.Run();
        }
        catch (COMException ex) when (App.IsRenderThreadFailure(ex))
        {
            // Raised again from MediaContext.Dispose during dispatcher shutdown once the render thread has died;
            // DispatcherUnhandledException cannot see it there.
            Console.Error.WriteLine("Multiplot render thread failure: " + ex);
            return 1;
        }
    }

    internal static class GlobalExceptionHandler
    {
        // called as GlobalExceptionHandler.Install("Multiplot", args);
        public static void Install(string appName)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    Console.Error.WriteLine($"{appName} Unhandled exception: {ex}");
                }
                catch { /* swallow to avoid secondary failures */ }
            };
        }
    }
}

/// <summary>The WPF application.</summary>
public sealed class App : System.Windows.Application
{
    private readonly string _rawCommandLine;
    private Session? _session;
    private MainWindow? _main;
    private WpfShell? _shell;

    /// <summary>Creates the application with the raw command line.</summary>
    public App(string rawCommandLine) => _rawCommandLine = rawCommandLine;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandled;
        var cl = CommandLine.Parse(_rawCommandLine);
        _main = new MainWindow();
        _shell = new WpfShell(_main, cl.Hide, ExitApplication);
        _session = AppStartup.CreateSession(_shell, cl);
        _main.Attach(_session, _shell);
        if (!cl.Hide)
        {
            var wp = _session.Registry.ReadWindowPosition();
            if (wp.IsSet)
            {
                _main.WindowStartupLocation = WindowStartupLocation.Manual;
                _main.Left = wp.Left;
                _main.Top = wp.Top;
                _main.Width = Math.Max(400, wp.Right - wp.Left);
                _main.Height = Math.Max(300, wp.Bottom - wp.Top);
            }
            else _main.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _main.Show();
            if (wp.Maximized) _main.WindowState = WindowState.Maximized;
            if (Environment.GetEnvironmentVariable("MULTIPLOTNET_SNAPSHOT") is { Length: > 0 } snap) ScheduleSnapshot(snap);
        }
        AppStartup.LoadInitialSettings(_session, cl);
        _shell.SetAppearance(_session.Settings.DarkMode); // also when no INI was found (FR-UI-046)
        _main.RebuildCommands();
        Dispatcher.InvokeAsync(async () =>
        {
            try { await AppStartup.ProcessArgumentsAsync(_session, cl); }
            catch (Exception ex) { ReportUnhandled(ex); }
        }, DispatcherPriority.Background);
    }

    private bool _exiting;

    /// <summary>Normal shutdown path (menu Exit, EXIT command, window close).</summary>
    public void ExitApplication()
    {
        if (_exiting || _session == null || _main == null) return;
        if (!AppStartup.QueryExit(_session)) return;
        _exiting = true;
        WindowPlacement? placement = null;
        if (_main.IsVisible)
        {
            var b = _main.RestoreBounds;
            if (!b.IsEmpty) placement = new WindowPlacement((int)b.Left, (int)b.Top, (int)b.Right, (int)b.Bottom, _main.WindowState == WindowState.Maximized);
        }
        try { AppStartup.Shutdown(_session, placement); }
        catch (Exception ex) { _session.DiagLog("Shutdown: " + ex); }
        _main.AllowClose = true;
        _main.Close();
        Shutdown(0);
    }

    /// <summary>Diagnostics: renders the main window to a PNG a few seconds after start-up.</summary>
    private void ScheduleSnapshot(string path)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_main?.Content is not FrameworkElement fe) return;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(fe);
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(fe.ActualWidth * dpi.DpiScaleX), (int)(fe.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(fe);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var f = File.Create(path);
            enc.Save(f);
        };
        t.Start();
    }

    private const int UceerrRenderThreadFailure = unchecked((int)0x88980406);
    private bool _renderThreadFailed;

    /// <summary>True for the WPF composition (render thread) failure, after which nothing can be drawn again.</summary>
    internal static bool IsRenderThreadFailure(Exception ex) => ex is COMException { HResult: UceerrRenderThreadFailure };

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (IsRenderThreadFailure(e.Exception))
        {
            // Exception to NFR-REL-001: with a dead render thread WPF cannot recover and every window message
            // re-throws, so save what can be saved and terminate instead of continuing.
            FatalRenderFailure(e.Exception);
            return;
        }
        ReportUnhandled(e.Exception); // NFR-REL-001: never terminate on an unhandled exception
    }

    private void FatalRenderFailure(Exception ex)
    {
        if (_renderThreadFailed) return;
        _renderThreadFailed = true;
        _session?.DiagLog("Fatal render thread failure: " + ex);
        try { if (_session != null) AppStartup.Shutdown(_session, null); }
        catch (Exception sx) { _session?.DiagLog("Shutdown after render failure: " + sx); }
        // Native message box without a WPF owner: WPF windows can no longer render.
        if (_shell != null && !_shell.Hidden)
            MessageBox.Show("The display system failed (" + ex.Message + ").\n\nMultiplot must close. Details were written to the Multiplot log.",
            "Multiplot", MessageBoxButton.OK, MessageBoxImage.Error);
        Environment.Exit(1);
    }

    private void ReportUnhandled(Exception ex)
    {
        _session?.DiagLog("Unhandled: " + ex);
        if (_shell != null && !_shell.Hidden)
            MessageBox.Show(_main!, ex.Message, "Multiplot", MessageBoxButton.OK, MessageBoxImage.Error);
    }


}

