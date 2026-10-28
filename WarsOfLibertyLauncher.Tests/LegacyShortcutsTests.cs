using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="LegacyShortcuts"/> — the cleanup of shortcuts an older build wrote as NTFS
/// STREAMS. A display name with a colon made <c>Desktop\Age of Empires 1: HD Edition.lnk</c> a
/// stream on an empty file called "Age of Empires 1": no shortcut on the Desktop, an empty file
/// instead, and an uninstall that removed only the stream.
///
/// <para>The rule is deliberately narrow (the maintainer's call): only a recorded <c>.lnk</c>
/// whose name holds a colon, and its base file only while it is still EMPTY. The refusals — a
/// base with content, a neighbouring file, an ordinary shortcut — are the point. The disk tests
/// create real streams under a temp folder, exactly as the old code did; on a volume that has no
/// streams they return early.</para>
/// </summary>
public class LegacyShortcutsTests
{
    [Theory]
    [InlineData(@"C:\Users\X\Desktop\Age of Empires 1: HD Edition.lnk", true)]
    [InlineData(@"C:\Users\X\Desktop\Foo : Bar.lnk", true)]
    [InlineData(@"C:\Users\X\Desktop\Wars of Liberty.lnk", false)]      // an ordinary shortcut
    [InlineData(@"C:\Users\X\Desktop\Foo: Bar.txt", false)]             // not a shortcut
    [InlineData(@"Foo: Bar.lnk", false)]                                // not a full path
    [InlineData(@"C:\Users\X\Desktop\:Bar.lnk", false)]                 // no base name before the colon
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyARecordedLnkWithAColonInItsNameIsAStream(string? path, bool expected)
        => Assert.Equal(expected, LegacyShortcuts.IsStreamPath(path));

    [Theory]
    [InlineData(@"C:\D\Age of Empires 1: HD Edition.lnk", @"C:\D\Age of Empires 1")]
    [InlineData(@"C:\D\Foo : Bar.lnk", @"C:\D\Foo ")]
    public void TheBaseFileIsTheNameUpToTheColon(string streamPath, string expected)
        => Assert.Equal(expected, LegacyShortcuts.BaseFileOf(streamPath));

    // ------------------------------------------------------------------ on a real volume

    private static readonly byte[] FakeLink = { 0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00 };

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aoe3ml-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes the stream the way the old CreateShortcuts did; false when the volume
    /// has no alternate data streams (then there is nothing to test).</summary>
    private static bool TryWriteStream(string streamPath)
    {
        try
        {
            File.WriteAllBytes(streamPath, FakeLink);
            return File.Exists(streamPath);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ExistsExactly(string path) => File.Exists(@"\\?\" + path);

    [Fact]
    public void THE_ONE_THAT_MATTERS_RemovesTheStreamAndItsEmptyBaseFile()
    {
        var dir = NewTempDir();
        try
        {
            var stream = Path.Combine(dir, "Age of Empires 1: HD Edition.lnk");
            if (!TryWriteStream(stream)) return;
            var baseFile = Path.Combine(dir, "Age of Empires 1");
            Assert.True(File.Exists(baseFile));
            Assert.Equal(0, new FileInfo(baseFile).Length);   // the empty file the player saw

            Assert.True(LegacyShortcuts.TryRemove(stream, out var error), error);

            Assert.False(File.Exists(stream));
            Assert.False(File.Exists(baseFile));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ABaseFileThatHasContentIsKept()
    {
        var dir = NewTempDir();
        try
        {
            var baseFile = Path.Combine(dir, "Age of Empires 1");
            File.WriteAllText(baseFile, "something the player owns");
            var stream = Path.Combine(dir, "Age of Empires 1: HD Edition.lnk");
            if (!TryWriteStream(stream)) return;

            Assert.True(LegacyShortcuts.TryRemove(stream, out _));

            Assert.False(File.Exists(stream));
            Assert.Equal("something the player owns", File.ReadAllText(baseFile));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// "Foo : Bar.lnk" hangs off a base named "Foo " — WITH the space. Win32 would strip that
    /// space, so without the \\?\ form the checks and the delete would land on the neighbouring
    /// "Foo", a different file.
    /// </summary>
    [Fact]
    public void NeverTouchesTheNeighbourOfABaseEndingInASpace()
    {
        var dir = NewTempDir();
        try
        {
            var neighbour = Path.Combine(dir, "Foo");
            File.WriteAllText(neighbour, "the neighbour");
            var stream = Path.Combine(dir, "Foo : Bar.lnk");
            if (!TryWriteStream(stream)) return;
            Assert.True(ExistsExactly(Path.Combine(dir, "Foo ")));

            Assert.True(LegacyShortcuts.TryRemove(stream, out _));

            Assert.False(ExistsExactly(Path.Combine(dir, "Foo ")));
            Assert.Equal("the neighbour", File.ReadAllText(neighbour));
        }
        finally
        {
            // A leftover "Foo " can only be removed through \\?\ as well.
            try { File.Delete(@"\\?\" + Path.Combine(dir, "Foo ")); } catch { }
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RemoveRecordedIgnoresEverythingButStreams()
    {
        var dir = NewTempDir();
        try
        {
            var ordinary = Path.Combine(dir, "Wars of Liberty.lnk");
            File.WriteAllBytes(ordinary, FakeLink);
            var stream = Path.Combine(dir, "Age of Empires 1: HD Edition.lnk");
            if (!TryWriteStream(stream)) return;

            var stillThere = LegacyShortcuts.RemoveRecorded(new[] { ordinary, stream });

            Assert.Empty(stillThere);
            Assert.True(File.Exists(ordinary));
            Assert.False(File.Exists(stream));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>A stream that cannot be removed right now stays on the list the caller keeps
    /// recording — dropped from the record, nothing would ever find it again.</summary>
    [Fact]
    public void AStreamThatCannotBeRemovedIsReportedForAnotherTry()
    {
        var dir = NewTempDir();
        try
        {
            var stream = Path.Combine(dir, "Age of Empires 1: HD Edition.lnk");
            if (!TryWriteStream(stream)) return;

            using (new FileStream(stream, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(new[] { stream }, LegacyShortcuts.RemoveRecorded(new[] { stream }));

            // The next try finds it unlocked and finishes the job, empty base file included.
            Assert.Empty(LegacyShortcuts.RemoveRecorded(new[] { stream }));
            Assert.False(File.Exists(stream));
            Assert.False(File.Exists(Path.Combine(dir, "Age of Empires 1")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// A stream on ANOTHER Windows account's Desktop (the launcher was once run elevated as that
    /// account): every check answers "not there" and every delete "access denied". The path stays
    /// recorded — only that account can ever clean it — and the uninstall skips it the way it
    /// skips an ordinary shortcut it cannot see, instead of failing and keeping the install
    /// registered for ever. The other account is stood in for by a deny rule on a temp folder.
    /// </summary>
    [Fact]
    public void AStreamThisAccountCannotSeeStaysRecordedAndNeverFailsTheUninstall()
    {
        var dir = NewTempDir();
        var desktop = Path.Combine(dir, "Desktop");
        var install = Path.Combine(dir, "install");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(install);
        var stream = Path.Combine(desktop, "Age of Empires 1: HD Edition.lnk");
        var baseFile = Path.Combine(desktop, "Age of Empires 1");
        var me = WindowsIdentity.GetCurrent().User!;
        const FileSystemRights unseen = FileSystemRights.ReadData | FileSystemRights.ReadAttributes
                                        | FileSystemRights.ReadExtendedAttributes | FileSystemRights.Delete;
        var folderDeny = new FileSystemAccessRule(me, unseen | FileSystemRights.DeleteSubdirectoriesAndFiles,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Deny);
        var fileDeny = new FileSystemAccessRule(me, unseen, AccessControlType.Deny);
        bool fileDenied = false, folderDenied = false;
        try
        {
            if (!TryWriteStream(stream)) return;
            new InstallManifest
            {
                InstallPath = install, ModId = "aoe1-hd-edition", Shortcuts = new List<string> { stream },
            }.Save();

            // On the file itself too, so the test does not hang on ACL inheritance reaching a
            // file that already existed.
            SetDeny(new FileInfo(baseFile), fileDeny, add: true);
            fileDenied = true;
            SetDeny(new DirectoryInfo(desktop), folderDeny, add: true);
            folderDenied = true;
            if (LegacyShortcuts.IsStillThere(stream)) return;   // the rules did not take; nothing to show

            Assert.Equal(new[] { stream }, LegacyShortcuts.RemoveRecorded(new[] { stream }));

            var errors = new List<string>();
            var aoe1 = new ModProfile { Id = "aoe1-hd-edition", DisplayName = "Age of Empires 1: HD Edition" };
            UninstallService.DeleteShortcuts(aoe1, install, allowFallback: false, errors);
            Assert.Empty(errors);
        }
        finally
        {
            if (folderDenied) SetDeny(new DirectoryInfo(desktop), folderDeny, add: false);
            if (fileDenied) SetDeny(new FileInfo(baseFile), fileDeny, add: false);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void SetDeny(DirectoryInfo folder, FileSystemAccessRule rule, bool add)
    {
        var acl = folder.GetAccessControl();
        if (add) acl.AddAccessRule(rule);
        else acl.RemoveAccessRule(rule);
        folder.SetAccessControl(acl);
    }

    private static void SetDeny(FileInfo file, FileSystemAccessRule rule, bool add)
    {
        var acl = file.GetAccessControl();
        if (add) acl.AddAccessRule(rule);
        else acl.RemoveAccessRule(rule);
        file.SetAccessControl(acl);
    }

    /// <summary>A Desktop that moved, a drive that is gone: nothing is left to remove, so it is
    /// not an error (an uninstall would otherwise report a partial failure and keep the copy).</summary>
    [Fact]
    public void AStreamWhoseFolderIsGoneCountsAsRemoved()
    {
        var dir = NewTempDir();
        try
        {
            var stream = Path.Combine(dir, "Moved Desktop", "Age of Empires 1: HD Edition.lnk");

            Assert.True(LegacyShortcuts.TryRemove(stream, out var error), error);
            Assert.Empty(LegacyShortcuts.RemoveRecorded(new[] { stream }));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
