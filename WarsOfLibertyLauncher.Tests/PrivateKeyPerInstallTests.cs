using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins one private AoE3 key per INSTALL (<see cref="SetupPathPatcher.PlanKey"/>). Every copy of a
/// stock-exe mod used to share one key derived from the mod's name: repairing copy 2 re-pointed it,
/// so copy 1 loaded copy 2's content; uninstalling a copy deleted the key the other still used; and
/// a catalogue rename made the next repair abort. The cases that matter are the ones where the
/// patcher must NOT write: a key another live install owns, and bytes it did not provably write.
/// </summary>
public class PrivateKeyPerInstallTests
{
    private const string Root = @"Software\Microsoft\Microsoft Games\";
    private static string Key(string name) => Root + name + @"\1.0";

    /// <summary>The fake exe of <see cref="SetupPathPatcherTests"/>, plus the vanilla and
    /// WarChiefs keys a real exe also carries (reserved, and never to be read as ours).</summary>
    private static byte[] FakeExe(string? patchedTo = null)
    {
        var parts = new[]
        {
            Encoding.ASCII.GetBytes("MZ\0\0before\0"),
            Encoding.ASCII.GetBytes(Key("Age of Empires 3")), new byte[] { 0 },
            Encoding.ASCII.GetBytes(SetupPathPatcher.BaseKey), new byte[] { 0 },
            Encoding.ASCII.GetBytes("x\0"),
            Encoding.Unicode.GetBytes(SetupPathPatcher.BaseKey), new byte[] { 0, 0 },
            Encoding.ASCII.GetBytes("odd\0"),
            Encoding.Unicode.GetBytes(SetupPathPatcher.BaseKey), new byte[] { 0, 0 },
            Encoding.Unicode.GetBytes(Key("Age of Empires 3 Expansion Pack")), new byte[] { 0, 0 },
            Encoding.ASCII.GetBytes("tail\0"),
        };
        var exe = parts.SelectMany(p => p).ToArray();
        if (patchedTo != null) Assert.Equal(3, SetupPathPatcher.Patch(exe, patchedTo));
        return exe;
    }

    [Fact]
    public void AnUnpatchedExeNamesNoPrivateKey()
        => Assert.Null(SetupPathPatcher.ReadPrivateKey(FakeExe()));

    [Fact]
    public void APatchedExeNamesTheKeyItWasPatchedWith()
        => Assert.Equal(Key("Napoleonic Era"), SetupPathPatcher.ReadPrivateKey(FakeExe(Key("Napoleonic Era"))));

    [Fact]
    public void RepatchMovesEverySlotAndKeepsTheLengthAndTheSurroundings()
    {
        var exe = FakeExe(Key("Napoleonic Era"));
        var before = exe.Length;

        Assert.Equal(3, SetupPathPatcher.Repatch(exe, Key("Napoleonic Era"), Key("Napoleonic Era #2")));

        Assert.Equal(before, exe.Length);
        Assert.Equal(Key("Napoleonic Era #2"), SetupPathPatcher.ReadPrivateKey(exe));
        Assert.Contains("tail", Encoding.ASCII.GetString(exe));
        // The reserved keys are untouched.
        Assert.Contains(Key("Age of Empires 3"), Encoding.ASCII.GetString(exe));
    }

    /// <summary>THE REFUSAL THAT MATTERS: a match not followed by the zero padding Patch writes is
    /// not a slot we own, and is never overwritten.</summary>
    [Fact]
    public void RepatchRefusesBytesItDidNotProvablyWrite()
    {
        var text = Key("Napoleonic Era") + "and-more-text-that-is-not-padding-at-all-xxxxxxxxxxxxxxx";
        var exe = Encoding.ASCII.GetBytes(text + "\0");
        var copy = (byte[])exe.Clone();
        Assert.Equal(0, SetupPathPatcher.Repatch(exe, Key("Napoleonic Era"), Key("Other")));
        Assert.Equal(copy, exe);
    }

    [Fact]
    public void TheFirstInstallGetsTheNameAndACopyGetsTheNextFreeKey()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Key("Struggle of Indonesia"), SetupPathPatcher.ChooseKey("Struggle of Indonesia", owned.Contains));

        owned.Add(Key("Struggle of Indonesia"));
        Assert.Equal(Key("Struggle of Indonesia #2"), SetupPathPatcher.ChooseKey("Struggle of Indonesia", owned.Contains));

        owned.Add(Key("Struggle of Indonesia #2"));
        Assert.Equal(Key("Struggle of Indonesia #3"), SetupPathPatcher.ChooseKey("Struggle of Indonesia", owned.Contains));
    }

    [Fact]
    public void ALongNameIsShortenedSoEveryCandidateStillFits()
    {
        var name = new string('N', SetupPathPatcher.MaxModNameLength);
        var key = SetupPathPatcher.ChooseKey(name, k => k == Key(name));
        Assert.True(key.Length <= SetupPathPatcher.BaseKey.Length);
        Assert.EndsWith(@" #2\1.0", key);
    }

    [Fact]
    public void WhenEveryKeyIsTakenItRefusesRatherThanShares()
        => Assert.Throws<SetupPathPatchException>(() => SetupPathPatcher.ChooseKey("Mod", _ => true));

    [Fact]
    public void TheExesOwnKeyIsKept_EvenAfterTheModWasRenamed()
    {
        var plan = SetupPathPatcher.PlanKey("New Name", Key("Old Name"), previousKey: null, _ => false);
        Assert.Equal(Key("Old Name"), plan.Key);
        Assert.Null(plan.RepatchFrom);
    }

    /// <summary>
    /// A copy made before keys were per-install shares its key with the copy the key points at.
    /// Repairing it must move IT to a key of its own, never re-point the other copy's key.
    /// </summary>
    [Fact]
    public void ACopySharingAnotherInstallsKeyMovesToItsOwn()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Key("Napoleonic Era") };
        var plan = SetupPathPatcher.PlanKey("Napoleonic Era", Key("Napoleonic Era"), Key("Napoleonic Era"), owned.Contains);
        Assert.Equal(Key("Napoleonic Era #2"), plan.Key);
        Assert.Equal(Key("Napoleonic Era"), plan.RepatchFrom);
    }

    /// <summary>A re-lay can put an UNPATCHED exe back: the previous manifest's key is reused.</summary>
    [Fact]
    public void AReLaidUnpatchedExeReusesThePreviousKey()
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Key("Napoleonic Era") };
        var plan = SetupPathPatcher.PlanKey("Napoleonic Era", keyInExe: null, Key("Napoleonic Era #2"), owned.Contains);
        Assert.Equal(Key("Napoleonic Era #2"), plan.Key);
        Assert.Null(plan.RepatchFrom);
    }

    [Fact]
    public void AKeyIsOwnedElsewhereOnlyByAnotherLiveFolder()
    {
        Assert.False(SetupPathPatcher.OwnedElsewhere(null, @"C:\Mods\A", _ => true));
        Assert.False(SetupPathPatcher.OwnedElsewhere(@"C:\Mods\A\", @"C:\Mods\A", _ => true));   // itself
        Assert.False(SetupPathPatcher.OwnedElsewhere(@"C:\Mods\Gone", @"C:\Mods\A", _ => false)); // stale
        Assert.True(SetupPathPatcher.OwnedElsewhere(@"C:\Mods\B", @"C:\Mods\A", _ => true));
    }

    [Fact]
    public void AReservedKeyIsNeverACandidate()
    {
        // A tampered manifest naming the player's own game key must not be kept.
        var plan = SetupPathPatcher.PlanKey("Mod", keyInExe: null, previousKey: SetupPathPatcher.BaseKey, _ => false);
        Assert.Equal(Key("Mod"), plan.Key);
    }
}
