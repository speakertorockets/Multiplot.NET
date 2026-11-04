using Microsoft.Win32;
using Multiplot.Core.Model;
using Multiplot.Settings;

namespace Multiplot.Tests.Settings;

/// <summary>Registry part of the WinPlot settings import (FR-CFG-050).</summary>
public class RegistryImportTests
{
    [Fact]
    public void CopyFrom_CopiesValuesAndSubKeys_SkipsListedKeys()
    {
        string baseKey = "Software\\MultiplotNetTests\\" + Guid.NewGuid().ToString("N");
        string src = baseKey + "\\src", dst = baseKey + "\\dst";
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(src + "\\Recent Files"))
            {
                k.SetValue("file1", "C:\\data\\a.sun");
                k.SetValue("file2", "C:\\data\\b.sun");
            }
            using (var k = Registry.CurrentUser.CreateSubKey(src + "\\Toolbars"))
                k.SetValue("File", new[] { "0", "57600 0", "57601 1" }, RegistryValueKind.MultiString);
            using (var k = Registry.CurrentUser.CreateSubKey(src + "\\Plugins\\Blocked"))
                k.SetValue("Old.dll", new byte[] { 1 }, RegistryValueKind.Binary);
            using (var k = Registry.CurrentUser.CreateSubKey(src + "\\ParmSelect461"))
                k.SetValue("Current", new byte[8], RegistryValueKind.Binary);

            var store = new RegistryStore(dst);
            int n = store.CopyFrom(src, "ParmSelect461");

            Assert.Equal(4, n);
            Assert.Equal(new[] { "C:\\data\\b.sun", "C:\\data\\a.sun" }, store.ReadRecentFiles());
            Assert.Contains("Old.dll", store.ReadBlockedPlugins());
            var s = new AppSettings();
            Assert.True(store.ReadToolbars(s));
            Assert.False(s.ToolbarEnabled[0]);
            using var skipped = Registry.CurrentUser.OpenSubKey(dst + "\\ParmSelect461");
            Assert.Null(skipped);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(baseKey, false);
        }
    }

    [Fact]
    public void CopyFrom_MissingSource_CopiesNothing()
    {
        var store = new RegistryStore("Software\\MultiplotNetTests\\" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, store.CopyFrom("Software\\MultiplotNetTests\\does-not-exist-" + Guid.NewGuid().ToString("N")));
    }
}
