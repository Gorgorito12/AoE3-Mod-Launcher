namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// A pretend release for <c>--preview-dialog=launcherupdate:*</c>, so the update window's states
/// can be looked at with no release to download. The notes are written the way real ones are,
/// which also makes them a fixture for <see cref="ReleaseNotes"/>.
/// </summary>
internal static class LauncherUpdateDemo
{
    public const string Link = "https://github.com/Gorgorito12/AoE3-Mod-Launcher/blob/main/releases/v1.0.16e.md";

    public const string Notes = """
        # AoE3 Mod Launcher v1.0.16e

        > **[Download Aoe3ModLauncher.exe](https://github.com/Gorgorito12/AoE3-Mod-Launcher/releases/tag/v1.0.16e)**
        > - the launcher installs this on its own when you open it.

        ## Team tournaments
        - **2v2 and 3v3 tournaments can now be played.** Until now you could create one, but nobody
          could enter it.
        - **New "My teams" button** under the tournament list. Create a team of up to 6 players, so it
          can have substitutes, and invite players even when they're offline or by searching their
          name. When an invitation reaches you, a notice with a "See" button lets you accept or decline
          it.
        - **A team bracket can't start while a team is short of players**, and the launcher says why.
        - **Tournament games are labelled TOURNAMENT** in Rooms › Community matches and in Ranking, so
          they no longer look like an ordinary competitive match.

        ## Installing mods
        - **Settings copied at install are there the first time you open the mod.** The graphics, sound
          and hotkeys you copy from another mod no longer wait for the second launch.
        - **Properties › User data › Import also works on a mod you haven't opened yet.**

        ## Fixes
        - **The administrator message names the mod you're installing and the real reason.** It used to
          always say Wars of Liberty and "to apply updates".
        - **Settings you import by hand are no longer replaced the next time you play** by the mod you
          chose when installing.

        ---

        # AoE3 Mod Launcher v1.0.16e (Español)

        > **[Descargar Aoe3ModLauncher.exe](https://github.com/Gorgorito12/AoE3-Mod-Launcher/releases/tag/v1.0.16e)**
        > - el launcher la instala solo al abrirlo.

        ## Torneos por equipos
        - **Ya se pueden jugar torneos 2v2 y 3v3.** Hasta ahora se podían crear, pero nadie podía
          inscribirse.
        - **Nuevo botón «Mis equipos»** debajo de la lista de torneos. Crea un equipo de hasta 6
          jugadores, así puede tener suplentes, e invita a jugadores aunque no estén conectados o
          buscándolos por su nombre. Cuando te llega una invitación, un aviso con el botón «Ver» te deja
          aceptarla o rechazarla.
        - **Un cuadro por equipos no puede empezar mientras a un equipo le falten jugadores**, y el
          launcher dice por qué.
        - **Las partidas de torneo dicen TORNEO** en Salas › Partidas de la comunidad y en la
          Clasificación, para que no parezcan una partida competitiva cualquiera.

        ## Instalar mods
        - **Los ajustes que copias al instalar ya están la primera vez que abres el mod.** Los gráficos,
          el sonido y los atajos que copias de otro mod ya no esperan al segundo arranque.
        - **Propiedades › Datos › Importar también funciona con un mod que aún no has abierto.**

        ## Arreglos
        - **El mensaje de administrador nombra el mod que estás instalando y el motivo real.** Antes
          decía siempre Wars of Liberty y «para aplicar actualizaciones».
        - **Los ajustes que importas a mano ya no se reemplazan la próxima vez que juegas** por los del
          mod que elegiste al instalar.
        """;

    public static LauncherUpdateService.UpdateCheckResult Result(bool linkOnly) => new(
        UpdateAvailable: true,
        CurrentVersion: "v1.0.16b",
        LatestVersion: "v1.0.16e",
        DownloadUrl: "https://example.invalid/Aoe3ModLauncher.exe",
        DownloadSize: 170L * 1024 * 1024,
        RemoteTag: "v1.0.16e",
        ReleaseNotes: linkOnly ? Link : Notes);
}
