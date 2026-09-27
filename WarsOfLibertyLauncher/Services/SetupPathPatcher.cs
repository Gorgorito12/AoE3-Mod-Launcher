using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Thrown when a mod opted into <see cref="Models.ModProfile.PrivateSetupPath"/> but its
/// executable is not one we can patch. Fatal to the install ON PURPOSE: the alternative is
/// a mod that silently reads the BASE GAME's registry key and therefore loads vanilla
/// content from the player's real <c>bin\</c>, which looks like "the mod does nothing".
/// </summary>
public sealed class SetupPathPatchException : Exception
{
    public string ExePath { get; }
    public SetupPathPatchException(string exePath, string message) : base(message) => ExePath = exePath;
}

/// <summary>
/// Thrown when the mod's private registry key can't be written because the process isn't
/// elevated. Kept apart from <see cref="SetupPathPatchException"/> because the two failures ask
/// the user for different things: this one is fixed by relaunching as administrator, the other
/// means the mod's executable isn't supported at all.
/// </summary>
public sealed class SetupPathKeyAccessException : Exception
{
    public SetupPathKeyAccessException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Gives a total conversion that ships the STOCK <c>age3y.exe</c> (no UHC patch) its own
/// private registry key, so it loads content from its own folder instead of the base game's.
///
/// <para><b>The problem.</b> The stock engine resolves its <c>.bar</c>/data through the
/// registry <c>setuppath</c> under <c>…\Age of Empires 3 Expansion Pack 2\1.0</c> — which
/// points at the player's real <c>bin\</c> — and NOT through the launch working directory.
/// A mod cloned into its own folder therefore loads vanilla content. UHC-patched mods (WoL,
/// Improvement Mod) sidestep this because their rebuilt exe reads the working directory; we
/// cannot produce a UHC exe ourselves (it is a different build, not a byte patch).</para>
///
/// <para><b>The fix.</b> The key path is a plain string inside the binary, so we rewrite it
/// in the player's own local copy of the exe to name a key belonging to the mod, and create
/// that key pointing at the mod's folder. Nothing global changes: the base game's key is
/// only ever READ, and vanilla keeps working untouched alongside. This replaces the older
/// <see cref="AoE3SetupPathRedirect"/> junction model, which had to rename the player's
/// <c>bin\</c> while the mod ran.</para>
///
/// <para><b>Measured on the real binaries</b> (stock <c>age3y.exe</c>, 11,598,648 bytes, and
/// Napoleonic Era's <c>age3n.exe</c>, which is the same build with 23 bytes of strings
/// changed — the mod's own authors used this same technique):</para>
/// <list type="bullet">
///   <item>The key occurs at exactly <b>3</b> sites: one ANSI copy and two UTF-16 ones.</item>
///   <item>It is <b>72 characters</b>, which is the hard ceiling on the replacement — a
///     longer name would overwrite whatever follows the string.</item>
///   <item>Three OTHER keys are present (vanilla and WarChiefs) and are deliberately left
///     alone; the engine did not need them for either mod tested. Note the vanilla one is
///     only 54 chars, so patching those too would tighten the ceiling.</item>
///   <item>The private key needs <b>every value</b> of the base key, not just
///     <c>setuppath</c> — see <see cref="EnsurePrivateKey"/>.</item>
///   <item>The engine reads <b>HKLM only</b>. HKCU was tried and the game ignored it (it
///     fell back to demanding the 25-character product key), so creating the key needs
///     elevation. Only the per-user first-run marker lives in HKCU.</item>
/// </list>
///
/// <para><b>Caveat worth carrying into the docs:</b> rewriting bytes in an <c>.exe</c>
/// invalidates its Authenticode signature and is the kind of edit antivirus heuristics
/// watch for. We patch the player's local copy in place and never redistribute a modified
/// binary.</para>
/// </summary>
public static class SetupPathPatcher
{
    /// <summary>The base-game key baked into every stock AoE3:TAD executable.</summary>
    internal const string BaseKey =
        @"Software\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack 2\1.0";

    /// <summary>Where every AoE3 product key lives, private ones included.</summary>
    private const string KeyRoot = @"Software\Microsoft\Microsoft Games";

    /// <summary>
    /// Longest mod name that still fits the replacement key inside the slot the original
    /// occupies: <c>KeyRoot</c> + <c>\</c> + name + <c>\1.0</c> must not exceed
    /// <see cref="BaseKey"/>. Surfaced so callers can say the limit out loud instead of
    /// repeating the arithmetic.
    /// </summary>
    public const int MaxModNameLength = 33;   // 72 - 34 - 1 - 4

    /// <summary>
    /// Product names we must never create, overwrite or delete a key for — they belong to
    /// the player's own games, not to a mod.
    /// </summary>
    private static readonly string[] ReservedNames =
    {
        "Age of Empires 3",
        "Age of Empires 3 Expansion Pack",
        "Age of Empires 3 Expansion Pack 2",
    };

    /// <summary>The per-user values that record "this player accepted the EULA".</summary>
    private static readonly string[] FirstRunValues = { "FIRSTRUN", "SystemInitialization" };

    // ---- pure core --------------------------------------------------------------

    /// <summary>
    /// The private key path for a mod, e.g. <c>Software\Microsoft\Microsoft Games\Struggle
    /// of Indonesia\1.0</c>. Throws when the name cannot be used, rather than silently
    /// producing a key the patcher would then refuse.
    /// </summary>
    internal static string PrivateKeyFor(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
            throw new ArgumentException("Mod name is empty.", nameof(modName));

        var name = modName.Trim();

        if (name.IndexOfAny(new[] { '\\', '/' }) >= 0)
            throw new ArgumentException($"Mod name '{name}' cannot contain a path separator.", nameof(modName));
        if (name.Any(char.IsControl))
            throw new ArgumentException($"Mod name '{name}' contains control characters.", nameof(modName));
        // The ANSI copy of the string is written with Encoding.ASCII, which turns anything
        // outside it into '?' — a key nobody could create. Reject it up front instead.
        if (name.Any(c => c > 0x7E || c < 0x20))
            throw new ArgumentException($"Mod name '{name}' must be plain ASCII.", nameof(modName));

        var key = $@"{KeyRoot}\{name}\1.0";
        if (key.Length > BaseKey.Length)
            throw new ArgumentException(
                $"Mod name '{name}' is too long: the key would be {key.Length} characters and " +
                $"only {BaseKey.Length} fit in the executable.", nameof(modName));
        if (ReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Mod name '{name}' is a base-game product name.", nameof(modName));

        return key;
    }

    /// <summary>
    /// Rewrites every occurrence of <see cref="BaseKey"/> in <paramref name="exe"/> to
    /// <paramref name="privateKey"/>, in both the ANSI and UTF-16 forms the binary carries,
    /// and returns how many sites were changed.
    ///
    /// <para>The replacement is padded with zero bytes out to the original length, which
    /// both keeps every other offset in the file where it was and supplies the string's null
    /// terminator. <b>A return of 0 leaves the buffer byte-for-byte untouched</b> — that is
    /// what lets the caller abort cleanly on an executable it does not recognise instead of
    /// writing a half-patched binary.</para>
    /// </summary>
    internal static int Patch(byte[] exe, string privateKey)
    {
        if (exe == null) throw new ArgumentNullException(nameof(exe));
        if (string.IsNullOrEmpty(privateKey)) throw new ArgumentException("Empty key.", nameof(privateKey));
        if (privateKey.Length > BaseKey.Length)
            throw new ArgumentException(
                $"Key is {privateKey.Length} characters; only {BaseKey.Length} fit.", nameof(privateKey));

        var patched = 0;
        foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.Unicode })
        {
            var needle = enc.GetBytes(BaseKey);
            var replacement = enc.GetBytes(privateKey);

            var i = 0;
            while (i <= exe.Length - needle.Length)
            {
                if (!MatchesAt(exe, i, needle)) { i++; continue; }

                Array.Copy(replacement, 0, exe, i, replacement.Length);
                Array.Clear(exe, i + replacement.Length, needle.Length - replacement.Length);
                patched++;
                i += needle.Length;
            }
        }
        return patched;
    }

    private static bool MatchesAt(byte[] haystack, int offset, byte[] needle)
    {
        for (var j = 0; j < needle.Length; j++)
            if (haystack[offset + j] != needle[j]) return false;
        return true;
    }

    /// <summary>
    /// Whether the buffer already names <paramref name="privateKey"/>, in either encoding.
    /// This is how a re-run tells "already patched" apart from "an executable we do not
    /// recognise" — both of which make <see cref="Patch"/> return 0.
    /// </summary>
    internal static bool ContainsPrivateKey(byte[] exe, string privateKey)
    {
        foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.Unicode })
        {
            var needle = enc.GetBytes(privateKey);
            for (var i = 0; i <= exe.Length - needle.Length; i++)
                if (MatchesAt(exe, i, needle)) return true;
        }
        return false;
    }

    // ---- one key per INSTALL ------------------------------------------------------
    //
    // The key used to be re-derived from the mod's display name at every install, repair and
    // update, which broke three ways. Every COPY of a mod was patched to the same key, so
    // repairing copy 2 re-pointed it at copy 2 and copy 1 silently loaded copy 2's content;
    // uninstalling one copy deleted the key the other still used, and that copy then asked for
    // the 25-character product key. And a catalogue rename changed the derived name, so the next
    // repair found an exe carrying neither the base key nor the "new" one and aborted.
    // The key an install uses is now whatever its own executable already names, kept unless
    // another live install owns it, and a new install gets the first free one.

    /// <summary>What an install's key should be, and whether its exe must move off another one.</summary>
    public sealed record KeyPlan(string Key, string? RepatchFrom);

    /// <summary>
    /// The private key an ALREADY-patched executable names, or null when it names none (an
    /// unpatched exe carries only the base game's keys, which are reserved) or more than one.
    /// Searches both encodings the patch writes, at any byte offset.
    /// </summary>
    internal static string? ReadPrivateKey(byte[] exe)
    {
        if (exe == null) return null;
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.Unicode })
        {
            int charSize = enc is UnicodeEncoding ? 2 : 1;
            var prefix = enc.GetBytes(KeyRoot + @"");
            int maxBytes = BaseKey.Length * charSize;
            for (var i = 0; i <= exe.Length - prefix.Length; i++)
            {
                if (!MatchesAt(exe, i, prefix)) continue;
                var end = i;
                var limit = Math.Min(exe.Length, i + maxBytes);
                while (end + charSize <= limit && !IsZero(exe, end, charSize)) end += charSize;
                // A string running past the slot without a terminator is not one of ours.
                bool terminated = end + charSize <= exe.Length && IsZero(exe, end, charSize);
                if (terminated)
                {
                    var s = enc.GetString(exe, i, end - i);
                    if (IsPrivateKeyShape(s)) found.Add(s);
                }
                i = Math.Max(i, end - 1);
            }
        }
        return found.Count == 1 ? found.First() : null;
    }

    private static bool IsPrivateKeyShape(string key)
    {
        if (key.Length > BaseKey.Length || !key.EndsWith(@"\1.0", StringComparison.Ordinal)) return false;
        var name = ProductNameToRemove(key);
        return name != null
               && string.Equals(key, $@"{KeyRoot}\{name}\1.0", StringComparison.OrdinalIgnoreCase)
               && !name.Any(c => c > 0x7E || c < 0x20);
    }

    private static bool IsZero(byte[] buffer, int offset, int count)
    {
        for (var j = 0; j < count; j++)
            if (buffer[offset + j] != 0) return false;
        return true;
    }

    /// <summary>
    /// Moves an already-patched executable from <paramref name="fromKey"/> to
    /// <paramref name="toKey"/>, and ONLY at sites that are provably the slot <see cref="Patch"/>
    /// wrote: the old key followed by zero padding out to the base key's full length. Anything
    /// else is left alone, so a string that merely happens to match is never overwritten. Returns
    /// the number of sites changed; 0 leaves the buffer untouched.
    /// </summary>
    internal static int Repatch(byte[] exe, string fromKey, string toKey)
    {
        if (exe == null) throw new ArgumentNullException(nameof(exe));
        if (string.IsNullOrEmpty(fromKey) || string.IsNullOrEmpty(toKey))
            throw new ArgumentException("Empty key.");
        if (fromKey.Length > BaseKey.Length || toKey.Length > BaseKey.Length)
            throw new ArgumentException($"Keys must fit in {BaseKey.Length} characters.");

        var patched = 0;
        foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.Unicode })
        {
            int charSize = enc is UnicodeEncoding ? 2 : 1;
            var needle = enc.GetBytes(fromKey);
            var replacement = enc.GetBytes(toKey);
            var slot = BaseKey.Length * charSize;

            var i = 0;
            while (i <= exe.Length - slot)
            {
                if (!MatchesAt(exe, i, needle) || !IsZero(exe, i + needle.Length, slot - needle.Length))
                {
                    i++;
                    continue;
                }
                Array.Copy(replacement, 0, exe, i, replacement.Length);
                Array.Clear(exe, i + replacement.Length, slot - replacement.Length);
                patched++;
                i += slot;
            }
        }
        return patched;
    }

    /// <summary>
    /// The first key for <paramref name="modName"/> no other live install owns: the name itself,
    /// then "<c>name #2</c>" … "<c>#9</c>", the name shortened to keep every candidate inside the
    /// executable's slot. Throws when all are taken rather than sharing one.
    /// </summary>
    internal static string ChooseKey(string modName, Func<string, bool> ownedElsewhere)
    {
        var baseName = (modName ?? "").Trim();
        var first = PrivateKeyFor(baseName);
        if (!ownedElsewhere(first)) return first;
        for (var n = 2; n <= 9; n++)
        {
            var suffix = $" #{n}";
            var stem = baseName.Length + suffix.Length > MaxModNameLength
                ? baseName[..(MaxModNameLength - suffix.Length)].TrimEnd()
                : baseName;
            var key = PrivateKeyFor(stem + suffix);
            if (!ownedElsewhere(key)) return key;
        }
        throw new SetupPathPatchException("",
            $"Every private registry key for '{baseName}' is in use by another install.");
    }

    /// <summary>
    /// Pure: which key an install uses. The key its executable already names wins, then the one
    /// its previous manifest recorded (a re-lay can put an UNPATCHED exe back), each only while no
    /// other live install owns it; otherwise the first free key. <see cref="KeyPlan.RepatchFrom"/>
    /// is set when the exe carries a different key and must be moved off it.
    /// </summary>
    internal static KeyPlan PlanKey(
        string modName, string? keyInExe, string? previousKey, Func<string, bool> ownedElsewhere)
    {
        KeyPlan Plan(string key) => new(key,
            keyInExe != null && !string.Equals(keyInExe, key, StringComparison.OrdinalIgnoreCase) ? keyInExe : null);

        foreach (var candidate in new[] { keyInExe, previousKey })
        {
            if (string.IsNullOrWhiteSpace(candidate) || !IsPrivateKeyShape(candidate)) continue;
            if (!ownedElsewhere(candidate)) return Plan(candidate);
        }
        return Plan(ChooseKey(modName, ownedElsewhere));
    }

    /// <summary>
    /// Pure core of <see cref="IsKeyOwnedElsewhere"/>: a key belongs to ANOTHER install when its
    /// <c>setuppath</c> names a different folder that is still a live install. A key pointing at a
    /// folder that is gone is stale and free to take over.
    /// </summary>
    internal static bool OwnedElsewhere(string? keySetupPath, string installFolder, Func<string, bool> isLiveInstall)
    {
        if (string.IsNullOrWhiteSpace(keySetupPath)) return false;
        if (SamePath(keySetupPath, installFolder)) return false;
        return isLiveInstall(keySetupPath);
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Whether <paramref name="privateKey"/> currently serves another install than
    /// <paramref name="installFolder"/>. Read-only (32-bit HKLM view, like the game), so it costs
    /// no elevation. Unreadable reads as "not owned", the old behaviour.
    /// </summary>
    public static bool IsKeyOwnedElsewhere(string privateKey, string installFolder)
    {
        string? setupPath = null;
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = hklm.OpenSubKey(privateKey, writable: false);
            setupPath = key?.GetValue("setuppath") as string;
        }
        catch { }
        return OwnedElsewhere(setupPath, installFolder,
            dir => Directory.Exists(dir) && File.Exists(Path.Combine(dir, Models.InstallManifest.FileName)));
    }

    /// <summary>The key this install should use; see <see cref="PlanKey"/>.</summary>
    public static KeyPlan PlanKeyForInstall(string modName, string installFolder, string exePath, string? previousKey)
    {
        string? inExe = null;
        try
        {
            if (File.Exists(exePath)) inExe = ReadPrivateKey(File.ReadAllBytes(exePath));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SetupPathPatcher: could not read '{exePath}': {ex.Message}");
        }
        return PlanKey(modName, inExe, previousKey, k => IsKeyOwnedElsewhere(k, installFolder));
    }

    /// <summary>The product name a key is filed under (the per-user EULA marker lives there too).</summary>
    internal static string ProductNameOf(string privateKey)
        => ProductNameToRemove(privateKey)
           ?? throw new ArgumentException($"'{privateKey}' is not a mod key.", nameof(privateKey));

    /// <summary>
    /// Moves an executable off a key another install owns. Same write discipline as
    /// <see cref="PatchExecutable"/>: aside, then swapped in. Throws when no site could be proven.
    /// </summary>
    public static void RepatchExecutable(string exePath, string fromKey, string toKey)
    {
        var bytes = File.ReadAllBytes(exePath);
        var sites = Repatch(bytes, fromKey, toKey);
        if (sites == 0)
            throw new SetupPathPatchException(exePath,
                $"'{Path.GetFileName(exePath)}' names '{fromKey}' in a shape the launcher did not write; " +
                "it cannot be moved to a key of its own.");
        var staging = exePath + ".patching";
        File.WriteAllBytes(staging, bytes);
        try
        {
            File.Move(staging, exePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(staging); } catch { }
            throw;
        }
        DiagnosticLog.Write($"SetupPathPatcher: moved {sites} site(s) in '{exePath}' from '{fromKey}' to '{toKey}'.");
    }

    // ---- file / registry wrappers -----------------------------------------------

    /// <summary>
    /// Patches the mod's executable in place.
    ///
    /// <para><b>Idempotent</b>, which is load-bearing rather than tidy: a Repair or an
    /// Update re-lays only the mod's OVERLAY, and for a mod whose executable comes from the
    /// AoE3 clone (Struggle of Indonesia) that file is never rewritten — so this runs again
    /// over an exe that is already patched. Treating that as a failure would abort every
    /// repair of exactly the mods this feature exists for.</para>
    ///
    /// <para>Throws <see cref="SetupPathPatchException"/> only when the executable contains
    /// neither the base key nor the private one — an unknown build. Deliberately fatal; see
    /// that type's remarks.</para>
    /// </summary>
    public static void PatchExecutable(string exePath, string privateKey)
    {
        if (!File.Exists(exePath))
            throw new SetupPathPatchException(exePath, $"Executable not found: {exePath}");

        var bytes = File.ReadAllBytes(exePath);
        var sites = Patch(bytes, privateKey);
        if (sites == 0)
        {
            if (ContainsPrivateKey(bytes, privateKey))
            {
                DiagnosticLog.Write($"SetupPathPatcher: '{exePath}' is already patched — nothing to do.");
                return;
            }
            throw new SetupPathPatchException(exePath,
                $"'{Path.GetFileName(exePath)}' does not contain the AoE3 registry key, so it cannot be " +
                "pointed at a private one.");
        }

        // Written aside and swapped in, never rewritten in place: a power cut, an antivirus lock
        // or a full disk mid-write would otherwise leave a truncated game executable — and when
        // the exe came from the AoE3 clone rather than the payload (Struggle of Indonesia),
        // Repair does not re-lay it, so the player would have to reinstall.
        var staging = exePath + ".patching";
        File.WriteAllBytes(staging, bytes);
        try
        {
            File.Move(staging, exePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(staging); } catch { /* the original is untouched either way */ }
            throw;
        }
        DiagnosticLog.Write($"SetupPathPatcher: patched {sites} site(s) in '{exePath}' → '{privateKey}'.");
    }

    /// <summary>
    /// Creates the mod's private key with <paramref name="setupPath"/>, cloning every other
    /// value from the base game's key.
    ///
    /// <para><b>Cloning ALL of them is load-bearing.</b> Writing only <c>setuppath</c> was
    /// tried first and the game opened straight into the "enter your 25-character product
    /// key" dialog: the licence check reads <c>pid</c>, <c>digitalproductid</c> and
    /// <c>doublehash</c> from the SAME key. Two of those are <c>REG_BINARY</c>, so each
    /// value is copied with its original kind rather than coerced to a string.</para>
    ///
    /// <para>The 32-bit view is mandatory: the game is a 32-bit process, so its
    /// <c>HKLM\Software\…</c> is redirected to <c>WOW6432Node</c>, while this launcher is
    /// x64 and would otherwise write somewhere the game never looks.</para>
    ///
    /// <para>Writing needs elevation (HKLM), but only the FIRST time: when the key is already
    /// there and correct this returns without touching the registry, which is what keeps a
    /// later Repair or Update from demanding admin for no reason. Returns the sub-key path,
    /// for the install manifest to record so uninstall can remove it.</para>
    /// </summary>
    public static string EnsurePrivateKey(string privateKey, string setupPath)
    {
        if (IsPrivateKeyCurrent(privateKey, setupPath))
        {
            DiagnosticLog.Write($"SetupPathPatcher: HKLM(32-bit)\\{privateKey} is already correct — no write needed.");
            return privateKey;
        }

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var baseKey = hklm.OpenSubKey(BaseKey, writable: false)
            ?? throw new InvalidOperationException(
                $"The base game's registry key is missing ({BaseKey}) — cannot derive the mod's key.");

        try
        {
            using var target = hklm.CreateSubKey(privateKey, writable: true)
                ?? throw new InvalidOperationException($"Could not create {privateKey}.");

            foreach (var name in baseKey.GetValueNames())
            {
                var kind = baseKey.GetValueKind(name);
                var value = name.Equals("setuppath", StringComparison.OrdinalIgnoreCase)
                    ? setupPath
                    : baseKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null) continue;
                target.SetValue(name, value, kind);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            // The elevation guards should have caught this before any download, so reaching
            // here means something changed underneath us. Name the cause anyway — the raw
            // .NET text ("Requested registry access is not allowed") tells the user nothing.
            throw new SetupPathKeyAccessException(
                $"Writing HKLM\\{privateKey} needs administrator rights.", ex);
        }

        DiagnosticLog.Write($"SetupPathPatcher: wrote HKLM(32-bit)\\{privateKey} with setuppath '{setupPath}'.");
        return privateKey;
    }

    /// <summary>
    /// Whether the mod's key already exists and is usable. **Read-only, so it costs no
    /// elevation** — which is the point: this is what the install/repair elevation guard keys
    /// off, so the user is asked for admin the first time and never again. Reads the same
    /// 32-bit view the game does.
    /// </summary>
    public static bool IsPrivateKeyCurrent(string privateKey, string setupPath)
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var baseKey = hklm.OpenSubKey(BaseKey, writable: false);
            using var target = hklm.OpenSubKey(privateKey, writable: false);
            if (baseKey == null || target == null) return false;

            return KeyMatches(baseKey.GetValueNames(), target.GetValueNames(),
                target.GetValue("setuppath") as string, setupPath);
        }
        catch
        {
            // Unreadable for any reason: report "not current" so the caller takes the write
            // path, where a real failure surfaces with its own message instead of being
            // swallowed here.
            return false;
        }
    }

    /// <summary>
    /// Pure core of <see cref="IsPrivateKeyCurrent"/>: the mod's key points where we want AND
    /// carries every value the base game's key has. The value-name check is what catches a
    /// key written by an older build that only set <c>setuppath</c> — the state that makes the
    /// game demand a product key.
    /// </summary>
    internal static bool KeyMatches(
        IEnumerable<string> baseValueNames,
        IEnumerable<string> targetValueNames,
        string? targetSetupPath,
        string expectedSetupPath)
    {
        if (string.IsNullOrWhiteSpace(targetSetupPath)) return false;
        if (!string.Equals(targetSetupPath.TrimEnd('\\', '/'), (expectedSetupPath ?? "").TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
            return false;

        var present = new HashSet<string>(targetValueNames, StringComparer.OrdinalIgnoreCase);
        return baseValueNames.All(present.Contains);
    }

    /// <summary>
    /// Copies the player's existing "I accepted the EULA" marker from the base game onto the
    /// mod's per-user key, so a mod running under its own product key does not re-prompt for
    /// a licence the player already accepted for this same game.
    ///
    /// <para><b>It only ever propagates an acceptance that already exists</b> — if the base
    /// game's marker is missing or not set, nothing is written and the mod shows the EULA
    /// itself. Never fabricate one.</para>
    ///
    /// <para>HKCU, so no elevation, and no WOW redirection applies to
    /// <c>HKCU\Software</c>. Best-effort: a failure here costs one extra click, nothing more.</para>
    /// </summary>
    public static void CarryOverFirstRunMarker(string modName)
    {
        try
        {
            using var source = Registry.CurrentUser.OpenSubKey($@"{BaseKey}", writable: false);
            if (source?.GetValue("FIRSTRUN") is not int firstRun || firstRun != 1)
            {
                DiagnosticLog.Write("SetupPathPatcher: base game shows no EULA acceptance — leaving the mod's prompt in place.");
                return;
            }

            using var target = Registry.CurrentUser.CreateSubKey($@"{KeyRoot}\{modName}\1.0", writable: true);
            if (target == null) return;

            foreach (var name in FirstRunValues)
            {
                var value = source.GetValue(name, null);
                if (value != null) target.SetValue(name, value, source.GetValueKind(name));
            }
            DiagnosticLog.Write($"SetupPathPatcher: carried the first-run marker over to '{modName}'.");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"SetupPathPatcher.CarryOverFirstRunMarker failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The product name an uninstall may remove for <paramref name="privateKey"/>, or null when
    /// the path must not be touched at all.
    ///
    /// <para>Pure, and separated out because it is the whole safety argument for a RECURSIVE
    /// HKLM delete driven by a string read off disk. It refuses anything outside the AoE3
    /// product root, anything naming one of the player's own games, and any segment that isn't a
    /// plain key name — <c>..</c> above all, which Win32 happens to treat as literal, but
    /// "happens to" is not a guarantee worth resting this on.</para>
    /// </summary>
    internal static string? ProductNameToRemove(string? privateKey)
    {
        if (string.IsNullOrWhiteSpace(privateKey)) return null;

        var relative = privateKey.Trim().TrimEnd('\\');
        if (!relative.StartsWith(KeyRoot + @"\", StringComparison.OrdinalIgnoreCase)) return null;

        var productName = relative.Substring(KeyRoot.Length + 1).Split('\\')[0];
        if (string.IsNullOrWhiteSpace(productName)) return null;
        if (productName is "." or "..") return null;
        if (productName.IndexOfAny(new[] { '/', ':' }) >= 0) return null;
        if (ReservedNames.Contains(productName, StringComparer.OrdinalIgnoreCase)) return null;

        return productName;
    }

    /// <summary>
    /// Removes a private key created at install time, and its parent product folder when
    /// that is left empty. Gated by <see cref="ProductNameToRemove"/> — uninstall must never be
    /// able to take the player's own game key with it.
    /// </summary>
    public static void RemovePrivateKey(string privateKey)
    {
        var productName = ProductNameToRemove(privateKey);
        if (productName == null)
        {
            DiagnosticLog.Write($"SetupPathPatcher: refusing to remove '{privateKey}'.");
            return;
        }

        var relative = privateKey.Trim().TrimEnd('\\');
        var productPath = $@"{KeyRoot}\{productName}";
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                hklm.DeleteSubKeyTree(relative, throwOnMissingSubKey: false);

                // Drop the now-empty product folder, but only if the mod left nothing behind.
                using var product = hklm.OpenSubKey(productPath, writable: false);
                if (product is { SubKeyCount: 0, ValueCount: 0 })
                {
                    product.Dispose();
                    hklm.DeleteSubKey(productPath, throwOnMissingSubKey: false);
                }
            }
            catch (UnauthorizedAccessException)
            {
                DiagnosticLog.Write($"SetupPathPatcher: removing '{relative}' needs admin — skipped.");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write($"SetupPathPatcher: removing '{relative}' failed: {ex.Message}");
            }
        }

        // The per-user marker is ours too, and needs no elevation.
        try { Registry.CurrentUser.DeleteSubKeyTree(productPath, throwOnMissingSubKey: false); }
        catch (Exception ex) { DiagnosticLog.Write($"SetupPathPatcher: removing the per-user key failed: {ex.Message}"); }
    }
}
