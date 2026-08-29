# ¿Es un virus el AoE3 Mod Launcher? · Is it a virus?

> **Enlace corto para compartir:** fija esta página en tu servidor de Discord y
> enlázala desde cada release. · *Pin this page and link it from every release.*

---

## Español

### Respuesta corta

**No.** El AoE3 Mod Launcher es una aplicación de **código abierto** (licencia
Apache-2.0). Windows y algunos antivirus muestran una advertencia porque el
`.exe` todavía **no está firmado por un certificado de pago que Windows ya
reconozca** — no porque contenga malware. Es un **falso positivo**, y abajo te
dejamos cómo comprobarlo tú mismo.

### Compruébalo tú mismo (no hace falta confiar en nuestra palabra)

1. **Lee el código.** Todo el launcher es público:
   <https://github.com/Gorgorito12/AoE3-Mod-Launcher>. No hay nada oculto —
   puedes leer exactamente qué hace.

2. **Míralo en VirusTotal** (analiza el archivo con ~70 antivirus a la vez):
   <!-- TODO: sube publish\Aoe3ModLauncher.exe a https://www.virustotal.com y pega aquí el ENLACE PERMANENTE del informe -->
   👉 **[Informe de VirusTotal](https://www.virustotal.com/)** *(pendiente de
   publicar el enlace del informe de la última versión)*.
   Si algún motor lo marca, casi siempre es una detección **heurística/genérica**
   (nombres como `Wacatac`, `Injector`, `ML.Attribute`, `Unsafe`), es decir "esto
   se *parece* a algo", no "esto *es* malware".

3. **Verifica el hash SHA-256.** Cada release publica el hash del `.exe`. Compara
   el de tu descarga:
   ```powershell
   Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256
   ```
   Si coincide con el de las notas del release, tu archivo es **exactamente** el
   que se publicó, sin modificar.

4. **Compílalo tú mismo.** No tienes que descargar nuestro binario. Con el **SDK
   de .NET 8** en Windows:
   ```powershell
   git clone https://github.com/Gorgorito12/AoE3-Mod-Launcher
   cd AoE3-Mod-Launcher/WarsOfLibertyLauncher
   dotnet publish -c Release -r win-x64 --self-contained `
       -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
       -o publish
   ```
   El resultado es el mismo launcher, construido desde el código fuente que
   acabas de leer. Los releases oficiales, además, se construyen en **GitHub
   Actions** (CI pública), así que cualquiera puede ver el registro de compilación.

### ¿Por qué avisa Windows o mi antivirus, si es limpio?

Por cuatro motivos, y ninguno es "tiene un virus":

1. **No está firmado por un certificado de confianza.** Un certificado de firma
   comercial cuesta entre **200 y 700 USD al año**, algo inviable para una
   herramienta gratuita de modding. Sin él, Windows muestra al editor como
   "desconocido" y SmartScreen avisa.
2. **Es un `.exe` autocontenido y grande (~165 MB).** Incluye todo .NET dentro
   para que no tengas que instalar nada. A los heurísticos de "empaquetador" eso
   les resulta sospechoso.
3. **Hace cosas normales para un launcher que, aisladas, parecen sospechosas:**
   descarga archivos, copia archivos del juego, escribe una entrada de inicio y
   pide permisos de administrador solo cuando hace falta. Todo legítimo y
   auditable en el código.
4. **Reputación baja.** Es un binario nuevo con pocas descargas. SmartScreen
   confía en un programa a medida que más gente lo ejecuta; al principio avisa
   aunque sea inofensivo.

### ¿Qué estamos haciendo para que dejen de salir esas advertencias?

- **Firma con certificado de confianza (SignPath Foundation).** Estamos en el
  proceso de firma gratuita para proyectos de código abierto. Cuando se apruebe,
  cada release llevará una firma que Windows ya reconoce y **las advertencias
  desaparecen**.
- **Reporte de falsos positivos a Microsoft y a los antivirus** que lo marquen,
  para que ajusten su detección y suban la reputación.
- **Compresión desactivada** en el `.exe` (por eso pesa ~165 MB y no ~120 MB):
  era lo que más disparaba el heurístico de Defender.

### Cómo ejecutarlo con seguridad

Si confías en las pruebas de arriba, la guía paso a paso para pasar el aviso de
SmartScreen / Smart App Control está en
**[INSTALL.md](../WarsOfLibertyLauncher/INSTALL.md)**.

### Si aún así no te convence

Está bien desconfiar de un `.exe` de internet — es lo sano. Tienes tres caminos:
compílalo tú mismo (arriba), espera a la versión firmada, o simplemente no lo
uses. Y si crees que encontraste algo malicioso de verdad en el código,
**[abre un issue](https://github.com/Gorgorito12/AoE3-Mod-Launcher/issues)**: lo
revisamos en público.

---

## English

### Short answer

**No.** The AoE3 Mod Launcher is **open-source** software (Apache-2.0). Windows
and some antivirus tools show a warning because the `.exe` **isn't yet signed by
a paid certificate that Windows already trusts** — not because it contains
malware. It's a **false positive**, and here's how to confirm that yourself.

### Verify it yourself (don't take our word for it)

1. **Read the code.** The entire launcher is public:
   <https://github.com/Gorgorito12/AoE3-Mod-Launcher>. Nothing is hidden — you
   can read exactly what it does.

2. **Check it on VirusTotal** (scans the file with ~70 antivirus engines at once):
   <!-- TODO: upload publish\Aoe3ModLauncher.exe to https://www.virustotal.com and paste the PERMANENT report LINK here -->
   👉 **[VirusTotal report](https://www.virustotal.com/)** *(report link for the
   latest build pending)*.
   If any engine flags it, it's almost always a **heuristic/generic** detection
   (names like `Wacatac`, `Injector`, `ML.Attribute`, `Unsafe`) — meaning "this
   *looks like* something", not "this *is* malware".

3. **Verify the SHA-256 hash.** Every release publishes the `.exe`'s hash. Compare
   your download's:
   ```powershell
   Get-FileHash Aoe3ModLauncher.exe -Algorithm SHA256
   ```
   If it matches the one in the release notes, your file is **exactly** the one
   that was published, untampered.

4. **Build it yourself.** You don't have to download our binary. With the **.NET 8
   SDK** on Windows:
   ```powershell
   git clone https://github.com/Gorgorito12/AoE3-Mod-Launcher
   cd AoE3-Mod-Launcher/WarsOfLibertyLauncher
   dotnet publish -c Release -r win-x64 --self-contained `
       -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
       -o publish
   ```
   You get the same launcher, built from the source you just read. Official
   releases are also built in **GitHub Actions** (public CI), so anyone can
   inspect the build log.

### Why does Windows / my antivirus warn me if it's clean?

Four reasons, and none of them is "it has a virus":

1. **It isn't signed by a trusted certificate.** A commercial signing certificate
   costs **$200–700 per year**, which isn't feasible for a free modding tool.
   Without it, Windows shows the publisher as "unknown" and SmartScreen warns.
2. **It's a large, self-contained `.exe` (~165 MB).** It bundles all of .NET so
   you don't have to install anything. "Packer" heuristics find that suspicious.
3. **It does normal launcher things that look suspicious in isolation:** it
   downloads files, copies game files, writes a startup entry, and requests
   admin rights only when needed. All legitimate and auditable in the code.
4. **Low reputation.** It's a new binary with few downloads. SmartScreen trusts a
   program as more people run it; early on it warns even when harmless.

### What are we doing to make the warnings go away?

- **Trusted code signing (SignPath Foundation).** We're going through their free
  code-signing program for open-source projects. Once approved, every release
  carries a signature Windows already trusts and **the warnings disappear**.
- **Reporting false positives to Microsoft** and to any antivirus that flags it,
  so they correct their detection and reputation improves.
- **Compression disabled** in the `.exe` (that's why it's ~165 MB, not ~120 MB):
  it was the biggest trigger for Defender's packer heuristic.

### How to run it safely

If you trust the evidence above, the step-by-step guide to get past the
SmartScreen / Smart App Control prompt is in
**[INSTALL.md](../WarsOfLibertyLauncher/INSTALL.md)**.

### Still not convinced?

Being wary of an `.exe` from the internet is healthy. You have three options:
build it yourself (above), wait for the signed release, or simply don't use it.
And if you believe you found something genuinely malicious in the code, please
**[open an issue](https://github.com/Gorgorito12/AoE3-Mod-Launcher/issues)** —
we review it in the open.

If you would rather just ask someone, **[the Discord](https://discord.gg/WVarbzzzmc)** is the place —
plenty of people there have already been through the same warning.

---

*See also: the full code-cited **[Transparency & Security Audit](AUDIT.md)** ·
[CODE_SIGNING_POLICY.md](../CODE_SIGNING_POLICY.md) ·
[PRIVACY.md](../PRIVACY.md) · [SECURITY.md](../SECURITY.md)*
