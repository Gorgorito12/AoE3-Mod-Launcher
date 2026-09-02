using System;
using System.IO;
using System.Runtime.CompilerServices;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Points every runtime file the launcher writes at a throwaway directory, before a
/// single test runs.
///
/// <para><b>Why this is not optional.</b> The launcher settings dialog applies and
/// persists each setting the moment it is touched, so a test that constructs the dialog
/// and flips one control reaches <c>LauncherConfig.Save()</c>. Without this redirect that
/// write lands on <c>%LocalAppData%\AoE3ModLauncher\launcher-config.json</c> — the real
/// one — and since the test's config is a fresh <c>LauncherConfig</c>, it replaces the
/// developer's installed mods, active mod and multiplayer sign-in with defaults. That is
/// not a hypothetical: it is what prompted this file.</para>
///
/// <para><b>A <c>ModuleInitializer</c>, not a fixture.</b> <see cref="AppPaths.DataDir"/>
/// is resolved once into a static property, so the variable has to be set before ANY code
/// in the assembly reads it. A collection fixture runs too late for a test that touches
/// paths during construction, and an xUnit assembly fixture is not guaranteed to precede
/// a static initializer either. A module initializer runs before the first member of this
/// assembly is used, which is the only point early enough.</para>
///
/// <para>The directory is per-process (the pid is in the name) so parallel runs cannot
/// collide, and it is left on disk under %TEMP%: deleting it at exit would race the
/// background work some tests start, and %TEMP% is the operating system's problem.</para>
/// </summary>
internal static class TestDataDirectory
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        // Respect an override that is already set: a CI job may point the whole run at a
        // workspace path of its own, and clobbering that would defeat the purpose.
        if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(AppPaths.DataDirOverrideVariable)))
            return;

        var dir = Path.Combine(
            Path.GetTempPath(),
            "aoe3ml-tests",
            Environment.ProcessId.ToString());
        try { Directory.CreateDirectory(dir); } catch { /* best-effort; the setter matters */ }
        Environment.SetEnvironmentVariable(AppPaths.DataDirOverrideVariable, dir);
    }
}
