# Guía de integración para modders

> Cómo conseguir que **tu mod de AoE3** aparezca en el launcher, se instale,
> se actualice y se desinstale sin que nadie tenga que tocar el código del
> launcher.

Esta guía describe el contrato completo entre un mod y el launcher.
Está pensada para que un modder con una build funcional —archivos en una
carpeta, idealmente un `.zip` publicado en algún sitio— pueda en una tarde
tener su mod listado oficialmente.

---

## 1. Visión general en una imagen

```
   tu_mod (tu repo / tu CDN)            aoe3-mods-catalog (repo central)
   ┌──────────────────────────┐         ┌──────────────────────────────┐
   │ payload .zip / releases  │◀────────│ mods/<tu-id>/                │
   │ UpdateInfo.xml (opcional)│         │   ├─ mod.json   ← manifest   │
   │                          │         │   ├─ icon.png                │
   └──────────────────────────┘         │   └─ banner.png              │
                                        └────────────┬─────────────────┘
                                                     │
                                          24 h cache │ raw.githubusercontent
                                                     ▼
                                        ┌──────────────────────────────┐
                                        │ Launcher (Aoe3ModLauncher)   │
                                        │  · ModCatalogService fetch   │
                                        │  · ModRegistry merge         │
                                        │  · UI: mod card + install    │
                                        └──────────────────────────────┘
```

Lo importante:

- **No tocas el código del launcher.** Tu mod entra al ecosistema por un
  pull request al repo central `Gorgorito12/aoe3-mods-catalog`, NO al
  repo `Updater`. El launcher hace fetch del catálogo cada 24 h y muestra
  lo nuevo automáticamente.
- **Un único archivo decisivo: `mod.json`.** Ese manifest describe tu mod,
  dónde se instala, cómo se actualiza, cómo se ejecuta. El launcher lee
  ese archivo y todo el resto del flujo lo deriva.
- **El binario del mod vive donde tú quieras** (GitHub Releases, tu CDN,
  SourceForge, etc.) — el catálogo sólo guarda metadatos + URLs.
- **CI en el catálogo valida tu PR** contra el schema y verifica el icono
  y banner. Los cambios cosméticos hacen auto-merge; los críticos pasan
  por revisión humana (sistema de "tiers", §7).

---

## 2. Las tres rutas para publicar

Elige la que más cómoda te resulte; el resultado es el mismo PR.

### 2.1. Asistente in-app — la ruta recomendada

En el launcher: pestaña **Mods → "Publish my mod"** (botón). Un wizard de
6 pasos te pide todos los campos del schema con validación inline:

1. **Identidad** — `id`, `displayName`.
2. **Look & feel** — `accentColor`, `icon`, `banner`.
3. **Install** — `type`, `defaultFolder`, `probeFile`, `executable`.
4. **Updates** — `mechanism` y sus campos dependientes.
5. **Descripción & website** — `description.en`, `description.es`,
   `officialWebsite`.
6. **Revisión** — preview del `mod.json` generado. Dos botones:
   **Copy JSON** (copia al portapapeles) y **Open PR on GitHub** (abre
   `https://github.com/Gorgorito12/aoe3-mods-catalog/new/main` con la
   ruta `mods/<tu-id>/mod.json` y el contenido pre-rellenado).

Ventaja: imposible inventarse un campo o equivocarse con un regex —
el wizard usa las mismas expresiones que el schema y avisa antes de
enviar.

### 2.2. Pull request directo

Si prefieres trabajar con tu editor:

```
git clone https://github.com/Gorgorito12/aoe3-mods-catalog
cd aoe3-mods-catalog
mkdir -p mods/<tu-id>
$EDITOR mods/<tu-id>/mod.json
cp ~/tu-mod/icon.png    mods/<tu-id>/icon.png
cp ~/tu-mod/banner.png  mods/<tu-id>/banner.png
git checkout -b add-<tu-id>
git add mods/<tu-id>
git commit -s -m "Add <tu-id> to catalog"
git push origin add-<tu-id>
```

Apunta el `$schema` de tu `mod.json` al schema del repo para tener
autocompletado en VS Code:

```json
"$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json"
```

### 2.3. Fork + edición desde GitHub web

Botón **Fork** en `Gorgorito12/aoe3-mods-catalog`, abres tu fork,
**Add file → Create new file**, pones la ruta `mods/<tu-id>/mod.json`,
pegas tu JSON y abres PR. Sube los assets en commits siguientes
(**Add file → Upload files**, target `mods/<tu-id>/`).

---

## 3. Anatomía de `mod.json`

Schema completo en
[`aoe3-mods-catalog-template/schema/mod.schema.json`](../aoe3-mods-catalog-template/schema/mod.schema.json).
Lo que sigue son los campos en el orden en el que suelen rellenarse, con
las restricciones reales que aplica el schema.

### 3.1. Identidad

| Campo | Obligatorio | Restricciones | Notas |
|---|---|---|---|
| `id` | sí | `^[a-z][a-z0-9-]{1,30}$` | Debe coincidir con el nombre de la carpeta bajo `/mods/`. Es la *clave primaria* — cambiarlo después rompe instalaciones existentes. Elígelo bien. |
| `displayName` | sí | 1–50 caracteres | El que aparece en la card del launcher. Mayúsculas, espacios y acentos permitidos. |
| `subtitle` | no | ≤ 50 caracteres | Línea pequeña debajo del título (ej. *"AoE3:TAD overhaul"*). |
| `author` | no | ≤ 100 caracteres | Nombre del equipo o autor. |
| `officialWebsite` | no | `^https?://` | Se abre en el navegador del usuario, no se descarga nada. HTTP permitido para sitios legacy; HTTPS preferido. |

### 3.2. Look & feel

| Campo | Restricciones | Specs físicas |
|---|---|---|
| `accentColor` | `^#[0-9a-fA-F]{6}$` | Color del borde de la card, de los badges y del banner sintético si no incluyes uno. |
| `icon` | nombre de archivo `.png` | **256 × 256 px, PNG con alfa, ≤ 100 KB.** Se valida en CI; un 257×257 lo rechaza. |
| `banner` | nombre `.png/.jpg/.jpeg` | **1200 × 300 px, ≤ 500 KB.** |

Los archivos viven en `mods/<tu-id>/` junto al `mod.json`. El launcher
resuelve `icon: "icon.png"` a
`https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/mods/<tu-id>/icon.png`
y los cachea en disco (`ModAssetCacheService`).

### 3.3. Descripciones multilingües

```json
"description": {
  "en": "Total-conversion mod for AoE3, set in 19th-century colonial wars.",
  "es": "Mod de conversión total para AoE3, ambientado en guerras coloniales del siglo XIX."
}
```

Las claves son códigos ISO 639-1. El launcher elige según el idioma de
la UI y cae a `en` si no encuentra el del usuario. Máximo 500 caracteres
por idioma.

### 3.4. `install` — cómo se instala el mod

```json
"install": {
  "type": "IsolatedFolder",
  "defaultFolder": "C:\\Program Files (x86)\\Mi Mod",
  "probeFile": "data\\stringtable.xml",
  "executable": "age3m.exe",
  "arguments": "",
  "payloadUrls": ["https://..."],
  "payloadSha256": ["aabbcc..."]
}
```

| Campo | Descripción |
|---|---|
| `type` | `IsolatedFolder` o `InPlaceOverlay`. Detalle en §4. |
| `defaultFolder` | Sugerencia para el dialog de instalación. Vacío para `InPlaceOverlay` — el launcher usa la ruta de AoE3 detectada. |
| `probeFile` | Ruta relativa que el launcher comprueba para confirmar que el mod está instalado en una carpeta (`File.Exists(install + probeFile)`). Pon algo que **sólo** exista en tu mod, no `age3y.exe` (eso existe en AoE3 vanilla). |
| `executable` | Nombre del .exe que arranca el juego (`age3y.exe` para WoL, `age3m.exe` para Improvement Mod). El launcher lo busca dentro de la carpeta de instalación. |
| `arguments` | Argumentos extra que el launcher añade al ejecutar. Casi siempre vacío. |
| `payloadUrls` | Array de URLs HTTPS con el zip inicial. Si el mod se distribuye en partes (`.zip.001`, `.002`, …) lístalas en orden: el launcher las concatena y luego descomprime. |
| `payloadSha256` | **Muy recomendado.** Array paralelo a `payloadUrls` con el SHA-256 de cada parte. Si lo declaras y un download no matchea, el launcher aborta — protege frente a manipulación del payload. |

### 3.5. `update` — cómo se actualizan los archivos

```json
"update": {
  "mechanism": "GitHubReleases",
  "github": { "externalAssetUrlTemplate": "...", "externalAssetSha256": "..." }
}
```

`mechanism` es un enum con cuatro valores. Detalle de cada uno en §5.

### 3.6. Campos opcionales avanzados

| Campo | Cuándo usarlo |
|---|---|
| `sourceRepo` | `owner/repo` de tu repositorio GitHub. **Requerido** si `update.mechanism = GitHubReleases`. Para otros mecanismos es informativo. |
| `approvedReleaseTag` | Tag aprobado para `GitHubReleases`. Bumpear este campo es el flujo normal para sacar versión (auto-merge, §7). |
| `installProductGuid` | Clave estable de Add/Remove Programs (`HKLM\…\Uninstall\<aqui>`). Si tienes un instalador previo con su propio GUID, ponlo aquí para mantener compatibilidad. Si no, omítelo y el launcher genera `<id>_launcher`. |
| `userDataFolder` | Nombre de la carpeta en `Documents\My Games\<aquí>\` donde tu mod guarda saves/replays. Si lo defines, el launcher activa el alert pre-instalación que ofrece backup, y expone "Open / Create backup / Restore backup" en el menú del engranaje. Omítelo si tu mod reutiliza la carpeta vanilla de AoE3. |
| `translations` | Define `{ "repo": "owner/repo", "coveredFiles": [...] }` para que el launcher liste traducciones de la comunidad publicadas como releases en ese repo. Sólo tiene sentido si tu mod usa el mismo esquema de overlay que WoL (archivos en `data\`). |

---

## 4. Tipos de instalación (`install.type`)

### 4.1. `IsolatedFolder` — la opción por defecto

El launcher **clona AoE3 entero** en una carpeta nueva y aplica tu mod
encima. Resultado: el AoE3 original del usuario queda intacto, tu mod
vive aislado, y los dos pueden coexistir.

Pasos internos:

1. Detecta AoE3 (Steam / GOG / retail).
2. Clona la carpeta de AoE3 a `defaultFolder`.
3. Aplana `bin\` a la raíz (layout Steam) y borra el `bin\` redundante.
4. Extrae tu payload encima.
5. Escribe shortcuts, registry entry y `<id>-manifest.json`.

Úsalo cuando:

- Tu mod es una **conversión total** (WoL, Napoleonic Era, …).
- Quieres que el usuario no perciba al instalar que va a tocar su AoE3.
- Tu ejecutable es distinto al vanilla (ej. `age3y.exe`, `age3m.exe`).

Ejemplo real: `aoe3-mods-catalog-template/mods/wol/mod.json`.

### 4.2. `InPlaceOverlay` — encima de AoE3

Los archivos se extraen **directamente sobre la instalación de AoE3
existente**. No hay clonación; el mod y AoE3 comparten carpeta.

Pasos internos:

1. Detecta AoE3.
2. Hace backup de cada archivo a sobreescribir antes de extraer.
3. Extrae tu payload encima.
4. Escribe `<id>-manifest.json` para poder revertir.

Úsalo cuando:

- Tu mod es un **patch/overhaul ligero** que toca pocos archivos.
- Te basta con que AoE3 vanilla quede modificado (el usuario sabe que
  para volver al vanilla tendrá que desinstalar).

**Cuidado:** este modo modifica el AoE3 del usuario. Asegúrate de
declarar `payloadSha256` y de listar bien qué archivos toca tu mod,
porque el desinstalador lo usará para limpiar.

---

## 5. Mecanismos de actualización (`update.mechanism`)

Árbol de decisión:

```
¿Tu mod publica versiones como GitHub Releases?
├─ sí ────────────────────────────────▶ GitHubReleases
└─ no
   ├─ ¿Tienes un UpdateInfo.xml + parches .tar.xz incrementales?
   │   └─ sí ─────────────────────────▶ WolPatcher
   ├─ ¿Tienes tu propio updater externo que arranca con el juego?
   │   └─ sí ─────────────────────────▶ DelegatedExternal
   └─ ninguno ─────────────────────────▶ Manual
```

### 5.1. `GitHubReleases` — recomendado para mods nuevos

El launcher pinea una versión a un **tag de release** en tu repo
(`sourceRepo`). Cuando quieres publicar v1.1, abres un PR al catálogo
que **sólo** cambia `approvedReleaseTag: "v1.0"` → `"v1.1"`. Eso es un
"Tier 2" y se auto-mergea (§7).

```json
"sourceRepo": "tuusuario/tu-mod",
"approvedReleaseTag": "v1.0",
"update": { "mechanism": "GitHubReleases" }
```

Por defecto el launcher descarga **el primer asset .zip** del release
tag. Si quieres hostear el payload fuera de GitHub Releases (CDN propio,
S3, etc.) pero mantener el tag como marcador de versión, declara:

```json
"update": {
  "mechanism": "GitHubReleases",
  "github": {
    "externalAssetUrlTemplate": "https://tu-cdn.com/tu-mod-{tag}.zip",
    "externalAssetSha256": "aabbcc...64-hex"
  }
}
```

El literal `{tag}` se reemplaza por `approvedReleaseTag` al descargar.
**`externalAssetSha256` es obligatorio** si declaras la plantilla — el
launcher se niega a instalar desde un host externo sin hash, porque
GitHub ya no garantiza la autenticidad.

### 5.2. `WolPatcher` — para mods que ya tienen el pipeline legacy

Es el sistema que usa Wars of Liberty: un `UpdateInfo.xml` en el server
del mod lista versiones, cada una con un `.tar.xz` incremental.

```json
"update": {
  "mechanism": "WolPatcher",
  "wol": {
    "updateInfoUrl": "http://tu-mod.com/updates/UpdateInfo.xml",
    "updateInfoUrlAlt": "http://mirror.example.com/UpdateInfo.xml",
    "payloadZipUrls": ["https://github.com/.../payload.zip.001", "...002"],
    "payloadSha256": ["...", "..."]
  }
}
```

El launcher:

1. Hashea `data\protoy.xml`, `data\techtreey.xml`,
   `data\stringtabley.xml` para identificar la versión actualmente
   instalada.
2. Aplica todos los parches pendientes desde `minreqdownload` hacia
   arriba.
3. Verifica CRC32 de cada parche antes de aplicar.
4. Hace backup de cada archivo antes de sobreescribir.

Documentación del formato `UpdateInfo.xml`: ver
`WarsOfLibertyLauncher/Models/UpdateInfo.cs`.

### 5.3. `DelegatedExternal` — tu mod tiene su propio updater

El launcher se desentiende: instala el payload inicial y luego cada vez
que el usuario juega, lanza tu .exe — si tu mod arranca su propio
updater (estilo `age3m.exe` de Improvement Mod), allá tú.

```json
"update": { "mechanism": "DelegatedExternal" }
```

### 5.4. `Manual` — sin actualizaciones automáticas

El launcher lista tu mod, deja al usuario instalarlo (si declaras
`install.payloadUrls`) y no intenta actualizarlo. Útil para demos,
prototipos o mods cuyo flujo de updates aún no está decidido.

```json
"update": { "mechanism": "Manual" }
```

---

## 6. Modelo de seguridad

Tres capas:

### 6.1. Schema validation (CI)

`ajv validate` corre en cada PR contra `schema/mod.schema.json`. Rechaza
manifests con campos desconocidos (`additionalProperties: false`),
regexes que no matchean, longitudes excedidas, URLs sin esquema, etc.

### 6.2. Hashes SHA-256

| Campo | Cuándo es obligatorio | Cuándo es muy recomendado |
|---|---|---|
| `install.payloadSha256` | nunca | siempre que declares `payloadUrls` |
| `update.wol.payloadSha256` | nunca | siempre que declares `payloadZipUrls` |
| `update.github.externalAssetSha256` | **siempre** que declares `externalAssetUrlTemplate` | n/a |

El launcher verifica el hash tras descargar y aborta si no coincide. Sin
hash, el launcher confía en el host (GitHub Releases, sitio del mod).
**Con** hash, el launcher detecta manipulación incluso si el host fue
comprometido después de aprobado el PR.

### 6.3. Sistema de Tiers — qué auto-mergea y qué no

El script `classify_pr.py` clasifica cada PR según qué campos toca:

| Tier | Campos modificados | Acción |
|---|---|---|
| **invalid** | Archivos fuera de `/mods/`, varios mods a la vez, JSON malformado, nombres de archivo desconocidos | PR bloqueado con un comentario explicativo |
| **tier1** | Sólo: `displayName`, `subtitle`, `description`, `accentColor`, `author`, `officialWebsite`, `icon`, `banner` | **Auto-merge** tras validación |
| **tier2** | Sólo: `approvedReleaseTag` (bump de versión) | **Auto-merge** tras validación |
| **tier3** | Cualquiera de: `id`, `sourceRepo`, `install.*`, `update.*`, `translations`, o primera submission del mod | Etiqueta `needs-manual-review` + comentario; el maintainer revisa a mano |

Lo que esto significa para ti como modder:

- **Tu primera submission siempre es tier 3.** Toca esperar review.
- **Cambiar icono / banner / texto** después: auto-merge en minutos.
- **Sacar versión nueva** (cambio de `approvedReleaseTag`): auto-merge.
- **Cambiar URLs, hashes o `install.*`**: review humano, siempre. Esto
  es deliberado — controla qué descarga el launcher.

---

## 7. Flujo completo: de cero a publicado

```
                                      ┌────────────────────────────┐
                                      │ Tu repo / tu CDN tiene el  │
                                      │ payload (.zip / release)   │
                                      └─────────────┬──────────────┘
                                                    │
                                                    ▼
┌─────────────────────────┐    ┌────────────────────────────────────┐
│ Asistente in-app o      │    │ Calcula SHA-256 de cada payload    │
│ editor manual           │───▶│   certutil -hashfile payload.zip   │
│ (escribes mod.json)     │    │   SHA256                           │
└─────────────────────────┘    └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ PR a Gorgorito12/aoe3-mods-catalog │
                               │   mods/<tu-id>/mod.json            │
                               │   mods/<tu-id>/icon.png            │
                               │   mods/<tu-id>/banner.png          │
                               └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ CI: classify → validate            │
                               │   1ª submission → tier 3           │
                               └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ Maintainer revisa y mergea         │
                               └─────────────┬──────────────────────┘
                                             │
                                             ▼
                               ┌────────────────────────────────────┐
                               │ Próximo refresh del catálogo en    │
                               │ los launchers (≤24 h por cache).   │
                               │ Tu mod aparece en la UI.           │
                               └────────────────────────────────────┘
```

Una vez merged, no necesitas hacer nada más: los launchers existentes
verán tu mod automáticamente cuando expire su cache de 24h
(`ModCatalogService.CacheTtl`).

---

## 8. Ejemplos reales

### 8.1. Conversión total con WolPatcher (WoL)

```json
{
  "$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json",
  "id": "wol",
  "displayName": "Wars of Liberty",
  "subtitle": "Launcher",
  "accentColor": "#c8102e",
  "author": "Wars of Liberty Team",
  "officialWebsite": "http://aoe3wol.com/",
  "description": {
    "en": "Total-conversion mod for AoE3, set in 19th-century colonial wars.",
    "es": "Mod de conversión total para AoE3, ambientado en las guerras coloniales del siglo XIX."
  },
  "install": {
    "type": "IsolatedFolder",
    "defaultFolder": "C:\\Program Files (x86)\\Wars of Liberty",
    "probeFile": "data\\stringtabley.xml",
    "executable": "age3y.exe",
    "arguments": ""
  },
  "update": {
    "mechanism": "WolPatcher",
    "wol": {
      "updateInfoUrl": "http://aoe3wol.com/updates/UpdateInfo.xml",
      "updateInfoUrlAlt": "http://master.dl.sourceforge.net/project/wars-of-liberty/Patches/UpdateInfo.xml",
      "payloadZipUrls": [
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.001",
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.002",
        "https://github.com/papillo12/Updater/releases/download/updater/WolPayload.zip.003"
      ]
    }
  },
  "translations": {
    "repo": "papillo12/translations",
    "coveredFiles": [
      "data\\stringtabley.xml",
      "data\\unithelpstringsy.xml"
    ]
  }
}
```

### 8.2. Overhaul con GitHubReleases (Improvement Mod)

```json
{
  "$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json",
  "id": "improvement-mod",
  "displayName": "Improvement Mod",
  "subtitle": "AoE3:TAD overhaul",
  "accentColor": "#3a8cd9",
  "description": {
    "en": "Overhaul mod for AoE3:TAD. The launcher clones your AoE3 install into a separate folder and overlays the latest release on top — your original AoE3 stays untouched.",
    "es": "Mod de mejora para AoE3:TAD. El launcher clona AoE3 en una carpeta separada y aplica encima la última release — tu AoE3 original queda intacto."
  },
  "sourceRepo": "papillo12/Improvement-Mod",
  "approvedReleaseTag": "Improvement-Mod",
  "install": {
    "type": "IsolatedFolder",
    "defaultFolder": "C:\\Program Files (x86)\\Improvement Mod",
    "probeFile": "age3m.exe",
    "executable": "age3m.exe",
    "arguments": ""
  },
  "update": { "mechanism": "GitHubReleases" }
}
```

### 8.3. Mod nuevo en GitHub con hashes y CDN externo

```json
{
  "$schema": "https://raw.githubusercontent.com/Gorgorito12/aoe3-mods-catalog/main/schema/mod.schema.json",
  "id": "napoleonic-era",
  "displayName": "Napoleonic Era",
  "author": "Napoleonic Era Team",
  "accentColor": "#1f4e79",
  "icon": "icon.png",
  "banner": "banner.png",
  "description": {
    "en": "Napoleonic-era total conversion for AoE3.",
    "es": "Conversión total ambientada en la era napoleónica para AoE3."
  },
  "sourceRepo": "napo-team/napoleonic-era",
  "approvedReleaseTag": "v2.3.0",
  "userDataFolder": "Napoleonic Era",
  "install": {
    "type": "IsolatedFolder",
    "defaultFolder": "C:\\Program Files (x86)\\Napoleonic Era",
    "probeFile": "data\\protonapoleonic.xml",
    "executable": "age3y.exe"
  },
  "update": {
    "mechanism": "GitHubReleases",
    "github": {
      "externalAssetUrlTemplate": "https://cdn.napoleonic-era.com/builds/napoleonic-{tag}.zip",
      "externalAssetSha256": "5d41402abc4b2a76b9719d911017c592aa8f1b9c2b4e8a3f1e0c9b8a7f6e5d4c"
    }
  }
}
```

---

## 9. Errores comunes (y cómo el CI los cazaría primero)

| Síntoma del PR | Causa | Cómo arreglar |
|---|---|---|
| `ajv: id should match pattern "^[a-z]…"` | `id` con mayúsculas, espacios o caracteres raros | Usa sólo `a-z0-9-`, empieza por letra |
| `ajv: install.type should be one of …` | Typo en el enum (ej. `"isolated"`) | `IsolatedFolder` o `InPlaceOverlay`, case-sensitive |
| `ajv: additionalProperties` | Añadiste un campo que no está en el schema | Quita el campo o pide que se añada al schema en un PR aparte |
| `validate_images: icon 257x257` | Tu icon no es exactamente 256×256 | Redimensiona; el CI no tolera ±1 px |
| `validate_images: banner.png > 500 KB` | Banner pesa más de 500 KB | Comprime con TinyPNG o equivalente |
| PR marcado **invalid** | Tocaste algo fuera de `/mods/<tu-id>/`, o más de un mod a la vez | Una PR por mod. Si necesitas modificar el schema, sepáralo en otro PR |
| PR sin auto-merge aunque sólo tocaste `displayName` | Es tu primera submission — siempre tier 3 por seguridad | Esperar al maintainer; las siguientes serán auto-merge |
| El launcher no muestra mi mod después del merge | Cache de 24h aún no expiró | Borra `%LocalAppData%\AoE3ModLauncher\catalog-cache.json` para forzar refresh |

---

## 10. Lo que **NO** tienes que hacer (y se ve a menudo)

- ❌ **No edites `WarsOfLibertyLauncher/Services/ModRegistry.cs`.** Esa
  clase tiene una lista hardcoded sólo para WoL (offline fallback). Tu
  mod va en el catálogo. Editar el `ModRegistry` directamente significa
  que tu mod requeriría un nuevo release del launcher para aparecer —
  lo opuesto a lo que el sistema busca.
- ❌ **No subas el payload del mod al catálogo.** El repo del catálogo
  guarda solo metadatos (`mod.json` + assets pequeños). El binario va
  en GitHub Releases / tu CDN.
- ❌ **No declares `payloadSha256` sin haber calculado realmente el
  hash.** Si pones un placeholder, el launcher rechaza la instalación
  para todos los usuarios.
- ❌ **No reuses un `id` ajeno.** Aunque el schema no lo prohíbe a nivel
  regex, el CI rechaza el PR si la carpeta `mods/<id>` ya existe y no
  eres su CODEOWNER.
- ❌ **No metas `<id>` con mayúsculas o espacios** "porque luce mejor".
  Para texto visible usa `displayName`; `id` es identificador técnico.

---

## 11. Referencias del código

Si quieres entender qué hace el launcher con tu `mod.json`:

| Archivo | Qué hace |
|---|---|
| [`WarsOfLibertyLauncher/Models/ModCatalogManifest.cs`](../WarsOfLibertyLauncher/Models/ModCatalogManifest.cs) | DTO que mapea 1:1 con `mod.json` |
| [`WarsOfLibertyLauncher/Services/ModCatalogService.cs`](../WarsOfLibertyLauncher/Services/ModCatalogService.cs) | Fetch + cache del catálogo |
| [`WarsOfLibertyLauncher/Services/ModRegistry.cs`](../WarsOfLibertyLauncher/Services/ModRegistry.cs) | Proyección a `ModProfile` y merge con built-in |
| [`WarsOfLibertyLauncher/Models/ModProfile.cs`](../WarsOfLibertyLauncher/Models/ModProfile.cs) | Modelo runtime que consume el resto del launcher |
| [`WarsOfLibertyLauncher/Services/NativeInstallService.cs`](../WarsOfLibertyLauncher/Services/NativeInstallService.cs) | Pipeline de instalación inicial |
| [`WarsOfLibertyLauncher/Services/UpdateService.cs`](../WarsOfLibertyLauncher/Services/UpdateService.cs) | Flujo de actualización (WolPatcher) |
| [`WarsOfLibertyLauncher/Services/GitHubReleasesInstallService.cs`](../WarsOfLibertyLauncher/Services/GitHubReleasesInstallService.cs) | Flujo de actualización (GitHubReleases) |
| [`aoe3-mods-catalog-template/schema/mod.schema.json`](../aoe3-mods-catalog-template/schema/mod.schema.json) | Schema autoritativo |
| [`aoe3-mods-catalog-template/.github/scripts/classify_pr.py`](../aoe3-mods-catalog-template/.github/scripts/classify_pr.py) | Clasificador de tiers |

---

## 12. ¿Y si necesito algo que el schema no soporta?

Abre un issue en el repo del launcher describiendo el caso de uso. El
schema está versionado deliberadamente: añadir un campo nuevo es un
cambio coordinado entre el launcher, el schema y el clasificador de
tiers. Lo bueno: una vez aceptado, queda disponible para todos los
mods.

Casos típicos que han salido en el roadmap:
- `StandardModsFolder` install type para mods que se montan en
  `Documents\My Games\Age of Empires 3\Mods\` (target v0.9).
- Soporte para AoE3: Definitive Edition (target v0.9 — detección y
  lanzamiento; mods de DE quedan para más adelante).
- `assetNamePattern` para `GitHubReleases` cuando el primer .zip del
  release no es el correcto.

---

**Resumen de un párrafo**: tu mod entra al launcher por un PR al
catálogo central con un `mod.json` que describe identidad, instalación
y mecanismo de actualización; el CI valida el JSON contra un schema y
auto-mergea cambios cosméticos y bumps de versión, dejando los críticos
(URLs, hashes, instalación) bajo revisión humana; los launchers
existentes ven tu mod automáticamente al expirar el cache de 24h, sin
que tengas que tocar ni una línea del launcher.
