using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>Runs only when asked: it downloads the published launcher, ~180 MB.</summary>
public sealed class NetworkFactAttribute : FactAttribute
{
    public NetworkFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AOE3ML_SELFUPDATE_TEST") != "1")
            Skip = "Downloads the published launcher. Set AOE3ML_SELFUPDATE_TEST=1 to run it.";
    }
}

/// <summary>
/// The real download, verification and swap, against the latest release, on a disposable copy.
/// The restart is the one step this does not reach: nothing here starts the new program.
/// </summary>
public class SelfUpdateIntegrationTests
{
    private static string FindRepoFile(string relative)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, relative)))
            folder = folder.Parent;
        Assert.NotNull(folder);
        return Path.Combine(folder!.FullName, relative);
    }

    private static string Sha(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Signer(string path)
    {
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        return certificate.Subject;
    }

    [NetworkFact]
    public async Task TheLatestReleaseDownloadsVerifiesAndSwapsIn()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Aoe3ModLauncher-tests");
        var release = JsonDocument.Parse(await http.GetStringAsync(
            "https://api.github.com/repos/Gorgorito12/AoE3-Mod-Launcher/releases/latest")).RootElement;
        var asset = release.GetProperty("assets").EnumerateArray()
            .First(a => a.GetProperty("name").GetString() == "Aoe3ModLauncher.exe");
        var url = asset.GetProperty("browser_download_url").GetString()!;
        var published = asset.GetProperty("digest").GetString()!.Replace("sha256:", "");

        var folder = Path.Combine(Path.GetTempPath(), "aoe3ml-selfupdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var current = Path.Combine(folder, "Aoe3ModLauncher.exe");
            File.Copy(FindRepoFile(@"WarsOfLibertyLauncher\bin\Release\net8.0-windows\Aoe3ModLauncher.exe"), current);
            var original = Sha(current);
            var staged = LauncherUpdateService.GetPendingUpdatePath(current);

            // A wrong hash: nothing stays staged, and the running program is not touched.
            await Assert.ThrowsAsync<UpdateVerificationException>(() => LauncherUpdateService.DownloadUpdateForAsync(
                current, url, new string('0', 64), null, CancellationToken.None, null));
            Assert.False(File.Exists(staged));
            Assert.Equal(original, Sha(current));

            // The real thing: its published hash and the same publisher as this build.
            await LauncherUpdateService.DownloadUpdateForAsync(current, url, published, null, CancellationToken.None, null);
            Assert.Equal(published, Sha(staged));
            Assert.Contains("CN=Gorgorito", Signer(staged));

            // This build is unsigned, so the publisher check needs a signed "running" program.
            var signed = Path.Combine(folder, "signed.exe");
            File.Copy(staged, signed);
            await LauncherUpdateService.VerifyDownloadAsync(staged, signed, published, CancellationToken.None);
            var stranger = Path.Combine(Path.GetDirectoryName(FindRepoFile("tools/testbed/testbed.py"))!, "out", "cache", "radmin", "installer.bin");
            if (File.Exists(stranger))
            {
                await Assert.ThrowsAsync<UpdateVerificationException>(() =>
                    LauncherUpdateService.VerifyDownloadAsync(stranger, signed, null, CancellationToken.None));
            }
            File.Delete(signed);

            // One byte changed after the download is refused.
            var tampered = staged + ".tampered";
            File.Copy(staged, tampered);
            using (var stream = new FileStream(tampered, FileMode.Open, FileAccess.ReadWrite))
            {
                stream.Position = stream.Length / 2;
                var value = stream.ReadByte();
                stream.Position = stream.Length / 2;
                stream.WriteByte((byte)(value ^ 0xFF));
            }
            await Assert.ThrowsAsync<UpdateVerificationException>(() => LauncherUpdateService.VerifyDownloadAsync(
                tampered, current, published, CancellationToken.None));
            File.Delete(tampered);

            // The second move fails: the original goes back to its own path.
            using (new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<InvalidOperationException>(() => LauncherUpdateService.SwapIn(current));
            }
            Assert.Equal(original, Sha(current));

            // And the swap: the new program in place, the old one aside.
            LauncherUpdateService.SwapIn(current);
            Assert.Equal(published, Sha(current));
            Assert.Equal(original, Sha(current + ".old"));
            Assert.False(File.Exists(staged));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
