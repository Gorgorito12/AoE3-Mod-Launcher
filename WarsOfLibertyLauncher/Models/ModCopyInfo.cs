using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Models;

/// <summary>
/// One selectable install copy of a mod, for the multiplayer create-room copy
/// picker. <see cref="InstallId"/> is the <see cref="ModState.ActiveInstallId"/>
/// / <see cref="ModInstall.Id"/> passed to the active-copy switch (empty = the
/// legacy active install with no minted id — a no-op switch). <see cref="Label"/>
/// is already disambiguated for display (see <c>Services.PathDisplay</c>).
/// </summary>
public sealed record ModCopyChoice(string InstallId, string Label, bool IsActive);

/// <summary>
/// Copy-awareness for a single mod, surfaced in the multiplayer create-room
/// dialog so the host can SEE (and, for the active dashboard mod, CHOOSE) which
/// installed copy the room will use. Multiplayer always launches / fingerprints
/// the ACTIVE copy, so "choosing" a copy here rotates the active copy — a single
/// source of truth (see MainWindow.SwitchActiveInstallAsync).
/// </summary>
/// <param name="HasMultiple">The mod has more than one registered copy.</param>
/// <param name="CanSwitch">This mod is the active dashboard mod, so its active
/// copy can be switched from here; otherwise the picker is display-only.</param>
/// <param name="Copies">Active + inactive copies, active first, labels disambiguated.</param>
public sealed record ModCopyInfo(bool HasMultiple, bool CanSwitch, IReadOnlyList<ModCopyChoice> Copies);
