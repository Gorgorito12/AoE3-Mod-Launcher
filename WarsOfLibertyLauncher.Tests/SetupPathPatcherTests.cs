using System;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Tests for <see cref="SetupPathPatcher"/>, which rewrites the AoE3 registry-key string
/// inside a mod's own copy of the stock executable so it loads content from the mod's
/// folder instead of the base game's.
///
/// The core is byte-level and pure, so these run on synthetic buffers rather than a real
/// 11 MB executable. The case that matters most is the ABSENT one: an exe we do not
/// recognise must come back untouched, because that is what lets the installer abort
/// cleanly instead of writing a half-patched binary.
/// </summary>
public class SetupPathPatcherTests
{
    private const string PrivateKey = @"Software\Microsoft\Microsoft Games\Struggle of Indonesia\1.0";

    /// <summary>A buffer shaped like the real exe: one ANSI copy of the key and two UTF-16 ones,
    /// each surrounded by unrelated bytes that must survive.</summary>
    private static byte[] BuildFakeExe()
    {
        var parts = new[]
        {
            Encoding.ASCII.GetBytes("MZ\0\0padding-before\0"),
            Encoding.ASCII.GetBytes(SetupPathPatcher.BaseKey), new byte[] { 0 },
            Encoding.ASCII.GetBytes("GetPidx\0"),
            Encoding.Unicode.GetBytes(SetupPathPatcher.BaseKey), new byte[] { 0, 0 },
            Encoding.ASCII.GetBytes("shfolder.dll\0"),
            Encoding.Unicode.GetBytes(SetupPathPatcher.BaseKey), new byte[] { 0, 0 },
            Encoding.ASCII.GetBytes("tail\0"),
        };
        return parts.SelectMany(p => p).ToArray();
    }

    [Fact]
    public void Patch_RewritesEveryAnsiAndUtf16Site()
    {
        var exe = BuildFakeExe();

        var sites = SetupPathPatcher.Patch(exe, PrivateKey);

        Assert.Equal(3, sites);
        // Counted over the raw bytes, not a decoded string: a UTF-16 run can start at an odd
        // offset, where decoding the whole buffer as UTF-16 would step straight past it.
        Assert.Equal(1, CountOf(exe, Encoding.ASCII, PrivateKey));
        Assert.Equal(2, CountOf(exe, Encoding.Unicode, PrivateKey));
        Assert.Equal(0, CountOf(exe, Encoding.ASCII, SetupPathPatcher.BaseKey));
        Assert.Equal(0, CountOf(exe, Encoding.Unicode, SetupPathPatcher.BaseKey));
    }

    [Fact]
    public void Patch_KeepsTheFileLengthAndTheSurroundingBytes()
    {
        // Every other offset in a PE has to stay where it was, so the shorter replacement is
        // zero-padded rather than the buffer resized.
        var exe = BuildFakeExe();
        var originalLength = exe.Length;

        SetupPathPatcher.Patch(exe, PrivateKey);

        Assert.Equal(originalLength, exe.Length);
        var ansi = Encoding.ASCII.GetString(exe);
        Assert.Contains("padding-before", ansi);
        Assert.Contains("GetPidx", ansi);
        Assert.Contains("shfolder.dll", ansi);
        Assert.Contains("tail", ansi);
    }

    [Fact]
    public void Patch_PadsWithZerosSoTheStringIsTerminated()
    {
        var exe = BuildFakeExe();
        SetupPathPatcher.Patch(exe, PrivateKey);

        // The ANSI site starts right after the leading padding; the bytes between the end of
        // the shorter key and the end of the original slot must all be zero.
        var start = Encoding.ASCII.GetString(exe).IndexOf(PrivateKey, StringComparison.Ordinal);
        Assert.True(start > 0);
        for (var i = start + PrivateKey.Length; i < start + SetupPathPatcher.BaseKey.Length; i++)
            Assert.Equal(0, exe[i]);
    }

    [Fact]
    public void Patch_WhenTheKeyIsAbsent_ReturnsZeroAndChangesNothing()
    {
        // The one that guards real installs: an unknown build must not be half-written.
        var exe = Encoding.ASCII.GetBytes("MZ\0this executable has no AoE3 registry key at all\0");
        var before = (byte[])exe.Clone();

        var sites = SetupPathPatcher.Patch(exe, PrivateKey);

        Assert.Equal(0, sites);
        Assert.Equal(before, exe);
    }

    [Fact]
    public void Patch_RejectsAKeyLongerThanTheSlot()
    {
        var exe = BuildFakeExe();
        var tooLong = SetupPathPatcher.BaseKey + "X";

        Assert.Throws<ArgumentException>(() => SetupPathPatcher.Patch(exe, tooLong));
    }

    [Theory]
    [InlineData("Struggle of Indonesia")]
    [InlineData("Napoleonic Era")]
    public void PrivateKeyFor_BuildsAKeyThatFits(string modName)
    {
        var key = SetupPathPatcher.PrivateKeyFor(modName);

        Assert.Equal($@"Software\Microsoft\Microsoft Games\{modName}\1.0", key);
        Assert.True(key.Length <= SetupPathPatcher.BaseKey.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"has\a\separator")]
    [InlineData("has/a/separator")]
    [InlineData("Conversión total")]                                  // non-ASCII: the ANSI copy would mangle it
    [InlineData("A mod with an unreasonably long name that will not fit")]
    [InlineData("Age of Empires 3")]                                  // the player's own game
    [InlineData("Age of Empires 3 Expansion Pack 2")]
    public void PrivateKeyFor_RejectsNamesItCannotUse(string modName)
    {
        Assert.Throws<ArgumentException>(() => SetupPathPatcher.PrivateKeyFor(modName));
    }

    // ---------------- ProductNameToRemove (gates a recursive HKLM delete) ----------------

    [Fact]
    public void ProductNameToRemove_AcceptsAKeyWeCreated()
    {
        Assert.Equal("Struggle of Indonesia", SetupPathPatcher.ProductNameToRemove(PrivateKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{guid}")]  // outside the AoE3 root
    [InlineData(@"Software\Microsoft\Microsoft Games")]                          // the root itself
    [InlineData(@"Software\Microsoft\Microsoft Games\..\..\Windows\1.0")]        // traversal
    [InlineData(@"Software\Microsoft\Microsoft Games\.\1.0")]
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Empires 3\1.0")]     // the player's own game
    [InlineData(@"Software\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack 2\1.0")]
    public void ProductNameToRemove_RefusesAnythingElse(string? key)
    {
        // This value is read off a manifest on disk and drives DeleteSubKeyTree in HKLM, so the
        // rejections are the whole point of the method.
        Assert.Null(SetupPathPatcher.ProductNameToRemove(key));
    }

    // ---------------- KeyMatches (decides whether admin is needed) ----------------

    private static readonly string[] BaseValues =
        { "langid", "version", "setuppath", "pid", "digitalproductid", "doublehash" };

    private const string ModFolder = @"C:\Games\Struggle of Indonesia";

    [Fact]
    public void KeyMatches_WhenTheKeyIsCompleteAndPointsHere_IsCurrent()
    {
        // The common case after the first install: nothing to write, so no admin prompt on
        // every later Repair or Update.
        Assert.True(SetupPathPatcher.KeyMatches(BaseValues, BaseValues, ModFolder, ModFolder));
    }

    [Theory]
    [InlineData(@"C:\Games\Struggle of Indonesia\")]   // trailing separator
    [InlineData(@"c:\games\struggle of indonesia")]     // casing
    public void KeyMatches_IgnoresTrailingSeparatorAndCase(string stored)
    {
        Assert.True(SetupPathPatcher.KeyMatches(BaseValues, BaseValues, stored, ModFolder));
    }

    [Fact]
    public void KeyMatches_WhenSetupPathPointsElsewhere_NeedsRewriting()
    {
        // e.g. the player moved or reinstalled the mod into another folder.
        Assert.False(SetupPathPatcher.KeyMatches(BaseValues, BaseValues, @"D:\Old Location", ModFolder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void KeyMatches_WithNoSetupPath_NeedsRewriting(string? stored)
    {
        Assert.False(SetupPathPatcher.KeyMatches(BaseValues, BaseValues, stored, ModFolder));
    }

    [Fact]
    public void KeyMatches_WhenTheLicenceValuesAreMissing_NeedsRewriting()
    {
        // The state that makes the game demand the 25-character product key: a key written
        // with setuppath alone. It must not read as "already fine".
        var partial = new[] { "setuppath" };
        Assert.False(SetupPathPatcher.KeyMatches(BaseValues, partial, ModFolder, ModFolder));
    }

    /// <summary>How many times a string appears in the buffer in a given encoding.</summary>
    private static int CountOf(byte[] haystack, Encoding encoding, string text)
    {
        var needle = encoding.GetBytes(text);
        var n = 0;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { hit = false; break; }
            if (hit) { n++; i += needle.Length - 1; }
        }
        return n;
    }
}
