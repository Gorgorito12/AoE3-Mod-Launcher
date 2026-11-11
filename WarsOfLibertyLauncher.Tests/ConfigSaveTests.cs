using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// The config used to be a plain overwrite that the UI thread and background saves could run at
/// the same moment; a <c>launcher-config.json.clobbered-…</c> in a player's data folder is what
/// that left. It is written to a scratch copy and swapped in now, one writer at a time.
/// </summary>
public class ConfigSaveTests
{
    [Fact]
    public void THE_ONE_THAT_MATTERS_ManyWritersLeaveOneWholeFileAndNoScratch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aoe3ml-config-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "launcher-config.json");
            var lockObj = new object();
            Parallel.For(0, 40, i =>
            {
                var body = Encoding.UTF8.GetBytes("{\"writer\":" + i + ",\"pad\":\"" + new string('x', 4000) + "\"}");
                // Save() takes its lock around this; the test takes the same shape.
                lock (lockObj) LauncherConfig.WriteWhole(path, body);
            });

            var text = File.ReadAllText(path);
            Assert.StartsWith("{\"writer\":", text);
            Assert.EndsWith("\"}", text);
            Assert.Empty(Directory.GetFiles(dir).Where(f => f.EndsWith(".tmp", StringComparison.Ordinal)));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AReaderHoldingTheFileDoesNotStopTheSave()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aoe3ml-config-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "launcher-config.json");
            LauncherConfig.WriteWhole(path, Encoding.UTF8.GetBytes("{\"a\":1}"));
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                LauncherConfig.WriteWhole(path, Encoding.UTF8.GetBytes("{\"a\":2}"));
            Assert.Equal("{\"a\":2}", File.ReadAllText(path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
