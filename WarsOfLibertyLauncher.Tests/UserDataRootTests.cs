using System;
using System.Collections.Generic;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins the dual-root user-data selection rule behind the "backup went to a
/// totally different path" report: the system Documents folder
/// (GetFolderPath(MyDocuments)) follows Windows redirections — with OneDrive
/// Known Folder Move on a German system the REAL path is
/// "...\OneDrive\Dokumente" — while a 2007 game's saves may still live in the
/// physical %USERPROFILE%\Documents from before the redirection. The rule:
/// first candidate whose folder EXISTS wins; none exists → the first
/// (redirected) candidate, so newly-created data follows the system
/// convention. The existence probe is injected so the rule stays pure.
/// </summary>
public class UserDataRootTests
{
    private const string Redirected = @"C:\Users\x\OneDrive\Dokumente\My Games\Wars of Liberty";
    private const string Physical = @"C:\Users\x\Documents\My Games\Wars of Liberty";
    private static readonly List<string> Both = new() { Redirected, Physical };

    [Fact]
    public void RedirectedExists_WinsRegardlessOfPhysical()
        => Assert.Equal(Redirected, UserDataService.PickUserDataFolder(
            Both, p => true));

    [Fact]
    public void OnlyPhysicalExists_PhysicalWins()
        // The reported bug's shape: saves written before the OneDrive
        // redirection — the launcher must operate where the data IS.
        => Assert.Equal(Physical, UserDataService.PickUserDataFolder(
            Both, p => p == Physical));

    [Fact]
    public void OnlyRedirectedExists_RedirectedWins()
        => Assert.Equal(Redirected, UserDataService.PickUserDataFolder(
            Both, p => p == Redirected));

    [Fact]
    public void NeitherExists_FallsBackToFirstCandidate_ForCreation()
        => Assert.Equal(Redirected, UserDataService.PickUserDataFolder(
            Both, p => false));

    [Fact]
    public void SingleCandidate_NoRedirection_ReturnsIt()
        => Assert.Equal(Physical, UserDataService.PickUserDataFolder(
            new List<string> { Physical }, p => false));

    [Fact]
    public void EmptyCandidates_ReturnsNull()
        => Assert.Null(UserDataService.PickUserDataFolder(
            new List<string>(), p => true));

    [Fact]
    public void ThrowingProbe_TreatedAsAbsent_NotFatal()
        => Assert.Equal(Physical, UserDataService.PickUserDataFolder(
            Both,
            p => p == Redirected ? throw new UnauthorizedAccessException() : p == Physical));

    [Fact]
    public void GetCandidateUserDataFolders_EmptyName_ReturnsEmpty()
        => Assert.Empty(UserDataService.GetCandidateUserDataFolders(""));

    [Fact]
    public void GetCandidateUserDataFolders_EndInFolderName_AndDeduped()
    {
        // On a machine with no redirection both roots collapse to one entry;
        // with redirection there are two. Either way: non-empty, deduped
        // case-insensitively, and every entry ends with "My Games\<folder>".
        var list = UserDataService.GetCandidateUserDataFolders("Wars of Liberty");
        Assert.NotEmpty(list);
        Assert.All(list, p => Assert.EndsWith(@"My Games\Wars of Liberty", p));
        Assert.Equal(list.Count,
            new HashSet<string>(list, StringComparer.OrdinalIgnoreCase).Count);
    }
}
