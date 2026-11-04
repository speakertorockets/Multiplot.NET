using System.Reflection;
using System.Runtime.Loader;
using Multiplot.Core;
using Multiplot.Plugins;
using Multiplot.Sun;

namespace Multiplot.DataAccess;

/// <summary>A loaded plugin with its origin (Manage Plugins listing, FR-PLG-012).</summary>
public sealed class LoadedPlugin
{
    /// <summary>Assembly file name (registry block key).</summary>
    public required string FileName { get; init; }
    /// <summary>Full path.</summary>
    public required string Path { get; init; }
    /// <summary>Plugin instance.</summary>
    public required IMultiplotPlugin Plugin { get; init; }
    /// <summary>Load context (collectible).</summary>
    public required AssemblyLoadContext Context { get; init; }
}

/// <summary>Result of a plugin scan.</summary>
public sealed class PluginScanResult
{
    /// <summary>Loaded plugins in load order.</summary>
    public List<LoadedPlugin> Loaded { get; } = new();
    /// <summary>Blocked plugin file names found.</summary>
    public List<string> Blocked { get; } = new();
    /// <summary>Warnings (rejected signatures, load failures).</summary>
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Data-access façade (FR-ACC-003..006): the built-in SUN provider followed by .NET plugins; detects a
/// file's provider, opens files, broadcasts library options and library commands, reports versions.
/// </summary>
public sealed class DataAccessService : IDisposable
{
    /// <summary>Data access core version line.</summary>
    public const string DataAccVersion = "DataAcc.NET(x64) v1.0.0";
    /// <summary>Signature of PC GOAL stream sources.</summary>
    public const uint PcGoalRealtime = 0xFFF0FFFF;

    private readonly List<LoadedPlugin> _plugins = new();
    private int _status;

    /// <summary>Built-in SUN library.</summary>
    public SunProvider Sun { get; } = new();

    /// <summary>Loaded plugins.</summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>Blocked plugin names found in the last scan.</summary>
    public List<string> BlockedPlugins { get; } = new();

    /// <summary>Data source plugins in load order.</summary>
    public IEnumerable<IDataSourceProvider> DataProviders => _plugins.Select(p => p.Plugin).OfType<IDataSourceProvider>();

    /// <summary>Expression function plugins.</summary>
    public IReadOnlyList<IExpressionFunctionProvider> FunctionProviders => _plugins.Select(p => p.Plugin).OfType<IExpressionFunctionProvider>().ToList();

    /// <summary>Diagnostic sink.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>Status of the last open.</summary>
    public int LastStatus => _status;

    /// <summary>SUN signature first, then plugins; 0 when unknown (status set).</summary>
    public uint IsSupportedType(string fileName)
    {
        _status = 0;
        uint sig = Sun.IsSupportedType(fileName);
        if (sig != 0) return sig;
        if (!fileName.Contains(']') && !CanOpen(fileName))
        {
            _status = (int)SunError.FileOpen;
            return 0;
        }
        foreach (var p in DataProviders)
        {
            try
            {
                if (p.IsSupported(fileName)) return p.Signature;
            }
            catch (Exception ex) { Log?.Invoke($"Plugin {p.Name} IsSupported failed: {ex.Message}"); }
        }
        _status = (int)SunError.UnknownFormat;
        return 0;
    }

    private static bool CanOpen(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Returns null on failure with <see cref="LastStatus"/> set.</summary>
    public IDataFile? Open(string fileName)
    {
        _status = 0;
        if (string.IsNullOrEmpty(fileName)) { _status = (int)SunError.FileOpen; return null; }
        uint type = IsSupportedType(fileName);
        if (type == 0) return null;
        if (SunSignatures.IsSun(type))
        {
            try
            {
                var f = Sun.Open(fileName);
                _status = (int)Sun.LastStatus;
                return f;
            }
            catch (DataAccessException ex) { _status = ex.Code; return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _status = (int)SunError.FileOpen;
                return null;
            }
        }
        foreach (var p in DataProviders)
        {
            if (p.Signature != type) continue;
            try
            {
                var f = p.Open(fileName);
                _status = p.LastStatus;
                return f;
            }
            catch (DataAccessException ex) { _status = ex.Code; return null; }
            catch (Exception ex)
            {
                Log?.Invoke($"Plugin {p.Name} Open failed: {ex}");
                _status = p.LastStatus != 0 ? p.LastStatus : (int)SunError.FileOpen;
                return null;
            }
        }
        _status = (int)SunError.UnknownFormat;
        return null;
    }

    /// <summary>Library description.</summary>
    public string GetDescription(uint sig)
    {
        if (SunSignatures.IsSun(sig)) return SunSignatures.Describe(sig);
        foreach (var p in DataProviders) if (p.Signature == sig) return p.FormatDescription;
        return "Unknown";
    }

    /// <summary>Version lines: data access core, SUN library, each plugin.</summary>
    public IReadOnlyList<string> VersionLines()
    {
        var l = new List<string> { DataAccVersion, SunProvider.Version };
        foreach (var p in _plugins) l.Add($"{p.Plugin.Name} {p.Plugin.Version}");
        return l;
    }

    /// <summary>Broadcasts a generic option id (1001..1012) to the SUN library and all data plugins (FR-ACC-006).</summary>
    public void SetOption(int id)
    {
        Sun.SetOption(id);
        foreach (var p in DataProviders)
        {
            try { p.SetOption(id); }
            catch (Exception ex) { Log?.Invoke($"Plugin {p.Name} SetOption({id}) failed: {ex.Message}"); }
        }
    }

    /// <summary>Sends text to every plugin (sig 0) or the one whose signature matches.</summary>
    public int ExecuteCommand(string command, uint sig)
    {
        int result = 0;
        foreach (var p in DataProviders)
        {
            if (sig != 0 && p.Signature != sig) continue;
            try { result = p.ExecuteCommand(command); }
            catch (Exception ex) { Log?.Invoke($"Plugin {p.Name} command failed: {ex.Message}"); }
        }
        return result;
    }

    /// <summary>Sends a library command to the built-in SUN library (text = option id).</summary>
    public int ExecuteSunCommand(string command) => Sun.ExecuteCommand(command);

    /// <summary>
    /// Loads plugins from the given folders (FR-PLG-010/011): every *.dll with a public non-abstract
    /// <see cref="IMultiplotPlugin"/> type, skipping blocked file names; each assembly in its own collectible context.
    /// </summary>
    public PluginScanResult LoadPlugins(IEnumerable<string> folders, ISet<string> blocked, IPluginHost host)
    {
        UnloadPlugins();
        var result = new PluginScanResult();
        BlockedPlugins.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (string dll in Directory.GetFiles(folder, "*.dll").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string name = System.IO.Path.GetFileName(dll);
                if (name.Equals("Multiplot.Plugins.Abstractions.dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(name)) continue;
                if (blocked.Contains(name))
                {
                    result.Blocked.Add(name);
                    BlockedPlugins.Add(name);
                    continue;
                }
                LoadAssembly(dll, name, host, result);
            }
        }
        _plugins.AddRange(result.Loaded);
        foreach (var w in result.Warnings) Log?.Invoke(w);
        return result;
    }

    private void LoadAssembly(string dll, string name, IPluginHost host, PluginScanResult result)
    {
        var ctx = new PluginLoadContext(dll);
        try
        {
            var asm = ctx.LoadFromAssemblyPath(System.IO.Path.GetFullPath(dll));
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            bool any = false;
            foreach (var t in types)
            {
                if (t is null || t.IsAbstract || !t.IsPublic || !typeof(IMultiplotPlugin).IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                var plugin = (IMultiplotPlugin)Activator.CreateInstance(t)!;
                if (plugin is IDataSourceProvider dp)
                {
                    if (SunSignatures.IsSun(dp.Signature) || DataProviders.Concat(result.Loaded.Select(l => l.Plugin).OfType<IDataSourceProvider>()).Any(o => o.Signature == dp.Signature))
                    {
                        result.Warnings.Add(Crt.Sprintf("Plugin %s rejected: signature %08x already registered", name, dp.Signature));
                        continue;
                    }
                }
                bool ok;
                try { ok = plugin.Initialize(host); }
                catch (Exception ex) { result.Warnings.Add($"Plugin {name} Initialize failed: {ex.Message}"); ok = false; }
                if (!ok) continue;
                result.Loaded.Add(new LoadedPlugin { FileName = name, Path = dll, Plugin = plugin, Context = ctx });
                any = true;
            }
            if (!any) ctx.Unload();
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Plugin {name} failed to load: {ex.Message}");
            try { ctx.Unload(); } catch (InvalidOperationException) { }
        }
    }

    /// <summary>Shuts the plugins down.</summary>
    public void UnloadPlugins()
    {
        var contexts = new HashSet<AssemblyLoadContext>();
        foreach (var p in _plugins)
        {
            try { p.Plugin.Shutdown(); }
            catch (Exception ex) { Log?.Invoke($"Plugin {p.FileName} Shutdown failed: {ex.Message}"); }
            contexts.Add(p.Context);
        }
        _plugins.Clear();
        foreach (var c in contexts)
        {
            try { c.Unload(); } catch (InvalidOperationException) { }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => UnloadPlugins();

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string path) : base(System.IO.Path.GetFileNameWithoutExtension(path), isCollectible: true)
            => _resolver = new AssemblyDependencyResolver(System.IO.Path.GetFullPath(path));

        protected override Assembly? Load(AssemblyName name)
        {
            // share the contracts assembly with the host so the interfaces match
            if (name.Name == typeof(IMultiplotPlugin).Assembly.GetName().Name) return null;
            string? p = _resolver.ResolveAssemblyToPath(name);
            return p != null ? LoadFromAssemblyPath(p) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string? p = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return p != null ? LoadUnmanagedDllFromPath(p) : IntPtr.Zero;
        }
    }
}
