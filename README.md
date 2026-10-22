# dotnet-nsis

**English** | [简体中文](README.zh-CN.md)

An open-source Windows installer CLI written in C#. Read a YAML configuration
and package a published application directory into an NSIS `.exe` installer.
Released under the [MIT license](LICENSE); dependencies retain their own
[licenses](THIRD-PARTY-NOTICES.md).

The CLI reuses pinned electron-builder NSIS templates. Node.js is not required,
and NSIS/7-Zip are downloaded and verified automatically on first use, then
reused from the local cache.

## Install from NuGet

Once a version has been published to NuGet.org, install it as a .NET tool on
Windows x64 with the .NET 8 SDK:

```powershell
dotnet tool install --global dotnet-nsis
dotnet-nsis --help
# Update an existing installation.
dotnet tool update --global dotnet-nsis
```

The NuGet package targets .NET 8 and includes templates and licenses.
The package is a CLI tool,
not a library for `PackageReference`. For local packaging and publishing setup,
see [NuGet distribution](docs/build.md#nuget-distribution).

## Quick start

Develop with the .NET 10 SDK on Windows x64 and install the .NET 8 runtime to
run the CLI and tests. The SDK requirement applies to building this repository;
users of the NuGet tool only need .NET 8. Each installer targets one
application architecture: x64, x86, or ARM64.

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet run --project src/DotnetNsis -- init
# Edit nsis.yml with the published application directory and entry executable.
dotnet run --project src/DotnetNsis -- validate
dotnet run --project src/DotnetNsis -- build
```

The CLI is distributed through NuGet. It does not compile the application being packaged or
install its .NET runtime. Publish that application as self-contained or prepare
its runtime on the target machine.

## Command line

Argument parsing, help and version output use `System.CommandLine 2.0.13`.
`--config` accepts the `-c` alias and equals syntax; the default is `nsis.yml`
in the current directory.

```powershell
dotnet-nsis --help
dotnet-nsis build --help
dotnet-nsis --version
dotnet-nsis init -c ./nsis.yml
dotnet-nsis validate --config=./nsis.yml
dotnet-nsis build -c "./configuration directory/application.yml"
```

No arguments displays help. Unknown commands/options and missing option values
return `2` without writing files or building. The configuration option may
precede the subcommand: `dotnet-nsis -c ./nsis.yml build`.

## Configuration

```yaml
appId: com.example.myapp
productName: My Application
version: 1.0.0
publisher: Example Company
appDirectory: ./publish
executable: bin/MyApp.exe
outputDirectory: ./dist
arch: x64
nsis:
  installerLanguages: [zh_CN, en_US]
  displayLanguageSelector: false
  # include: ./build/installer.nsh
  # installerIcon: ./build/installer.ico
  # uninstallerIcon: ./build/uninstaller.ico
  oneClick: false
  perMachine: false
  allowToChangeInstallationDirectory: true
  createDesktopShortcut: true
  createStartMenuShortcut: true
  runAfterFinish: false
```

| Field | Rules or default |
| --- | --- |
| `appId` | Required stable identifier. Letters, digits, dots, underscores and hyphens; keep unchanged for upgrades. |
| `productName` | Required display name. Invalid Windows filename characters are replaced in filenames, shortcuts and default directory names. |
| `version` | Required three- or four-part numeric version, each part 0–65535. Windows resources append `.0` to three-part versions. |
| `publisher` | Optional; empty by default. |
| `appDirectory` | Required directory containing the complete published application. |
| `executable` | Required `.exe` path relative to the application directory; subdirectories are supported. |
| `outputDirectory` | Defaults to `./dist`; cannot equal or be inside the application directory. |
| `arch` | `x64`, `x86`, or `arm64`; defaults to `x64` and must match the application. |
| `nsis.installerLanguages` | Defaults to `[zh_CN]`. Ordered list; the first language is the fallback. |
| `nsis.displayLanguageSelector` | Defaults to `false`. With multiple languages, `true` displays a selector during interactive installation. |
| `nsis.oneClick` | Defaults to `false` for a wizard; `true` enables one-click installation. |
| `nsis.perMachine` | Defaults to `false` for fixed per-user installation; `true` requires administrator privileges for fixed per-machine installation. |
| `nsis.allowToChangeInstallationDirectory` | Defaults to `true`; ignored in one-click mode. |
| `nsis.createDesktopShortcut` | Defaults to `true`. |
| `nsis.createStartMenuShortcut` | Defaults to `true`. |
| `nsis.runAfterFinish` | Defaults to `false`; `true` allows launching the application after installation. |
| `nsis.include` | Optional single custom `.nsh` file; omitted or `null` disables it. |
| `nsis.installerIcon` | Optional installer `.ico`; omitted or `null` uses the NSIS default. |
| `nsis.uninstallerIcon` | Optional uninstaller and Windows uninstall-list `.ico`; omitted or `null` retains default behavior. |

Paths are resolved relative to the **YAML file**, independently of the working
directory. All files and empty directories in the input are packaged without
exclusion rules, so use a clean publish directory. Symbolic links, junctions,
unknown fields and duplicate fields are rejected. Configuration text rejects
control characters and `${` preprocessor expressions.

The default installer language is Simplified Chinese. The wizard retains the
upstream behavior of appending the application directory name when the selected
path does not contain it. Per-user installation defaults to the user's Programs
directory; per-machine installation uses the appropriate Program Files directory.

Output is `{safe-name}-{version}-{arch}-Setup.exe`. An existing file is replaced
only after a successful build.

## Installer languages

```yaml
nsis:
  installerLanguages: [zh_CN, en_US, zh_TW]
  displayLanguageSelector: true
```

The installer and uninstaller include the selected resources, match the system
language, and fall back to the first entry. The selector applies to the installer;
silent installation and build-time uninstaller generation do not show it.
Omitting the list keeps Simplified Chinese only; `init` explicitly enables Chinese
and English in its generated example.

The pinned electron-builder list contains 27 language codes:

```text
en_US de_DE fr_FR es_ES zh_CN zh_TW ja_JP ko_KR it_IT nl_NL
da_DK sv_SE nb_NO fi_FI ru_RU pt_PT pt_BR pl_PL uk_UA cs_CZ
sk_SK hu_HU ar_SA he_IL tr_TR th_TH vi_VN
```

Codes are case-insensitive and accept hyphens such as `zh-CN`. Short codes select
the first matching region, for example `en` → `en_US`, `zh` → `zh_CN`, and
`pt` → `pt_PT`. Empty/null lists, unknown codes and duplicates are rejected.

Upstream messages fall back to English when a translation is unavailable.
Project-specific ARM64 messages support Simplified/Traditional Chinese and
English fallback. Application names and custom script messages are not translated.

## Application GUID

The original, case-sensitive `appId` and namespace
`50e065bc-3134-11e6-9bab-38c9862bdaf3` produce a UUID v5, matching electron-builder
when `nsis.guid` is omitted. Versions and display names do not affect it.

Earlier SHA-256 installation identifiers differ and are not migrated
automatically: uninstall those older packages before installing a new one.
Matching GUIDs do not establish complete layout or upgrade compatibility with
electron-builder packages; validate that migration separately.

## Custom icons

```yaml
nsis:
  installerIcon: ./build/installer.ico
  uninstallerIcon: ./build/uninstaller.ico
```

Paths are relative to the YAML file or absolute. Each field is independent and
may refer to the same file. Only `.ico` is accepted; there is no PNG/SVG conversion
or automatic discovery. `validate` checks existence and the ICO directory
structure; NSIS checks the actual image data during compilation.

`installerIcon` controls the installer EXE icon. `uninstallerIcon` controls the
uninstaller EXE, copies `uninstallerIcon.ico` to the installation directory, and
uses it in the Windows uninstall list. Without it, the uninstaller uses the NSIS
default and the uninstall list uses the entry EXE icon. Neither option changes
application or shortcut icons.

Icon definitions precede the custom include. Remove duplicate `MUI_ICON`,
`MUI_UNICON`, or `UNINSTALLER_ICON` definitions, or explicitly override with
`!define /redef`.

## Custom NSIS scripts

`nsis.include` extends the default template using electron-builder's
[include macros](https://www.electron.build/nsis/). The file must exist; paths
are relative to YAML or absolute. There is no automatic `build/installer.nsh`
discovery.

```yaml
nsis:
  include: ./build/installer.nsh
```

Define the hooks you need:

```nsis
; Run after extracting application files.
!macro customInstall
  DetailPrint "Running custom installation steps."
!macroend

; Run before the default uninstall file cleanup.
!macro customUnInstall
  DetailPrint "Running custom uninstall steps."
!macroend
```

Supported upstream hooks include `customHeader`, `preInit`, `customInit`,
`customUnInit`, `customInstall`, `customUnInstall`, `customRemoveFiles`, and
`customUnInstallSection`. Wizard mode also supports `customWelcomePage`,
`customPageAfterChangeDir`, `customFinishPage`, `customUnWelcomePage`, and
`customUninstallPage`. YAML fixes the installation scope, so `customInstallMode`
is not connected. Whole-script replacement through `nsis.script` is not supported.

The include's directory is added to the header search path. Use
`!include "hooks\actions.nsh"` and Windows backslashes in scripts.
`${BUILD_RESOURCES_DIR}` points to that directory, so bundled files can use
`File "${BUILD_RESOURCES_DIR}\extra.txt"`. The compiler's working directory
remains the temporary template directory; ordinary relative `File` paths are
not automatically relative to the include. Use UTF-8 scripts and resolve nested
resources from the top-level include directory.

Both installer and uninstaller compilation load the include.
`BUILD_UNINSTALLER` is defined only during uninstaller generation. `preInit`
also runs in the build-time uninstaller generator; put install-only actions in
`customInit` or `customInstall`. Directives such as `!system` execute at compile
time, and `customRemoveFiles` replaces default cleanup. Use trusted scripts and
handle their upgrade/uninstall behavior. Compilation errors remain in the
failed build directory's `build.log`.

The CLI also provides `customBeforeInstall`, a project-specific extension that
runs before old-version uninstall and new-file extraction in wizard, silent and
one-click modes. Use it for backup or preflight validation. On failure, set a
nonzero exit code and `Quit` to prevent removal of the old installation.
`customInstall` still runs after extraction. Shortcut working directories match
the entry EXE's directory, including subdirectory entries.

### Data-directory example

[installer.nsh](examples/build/installer.nsh) demonstrates a welcome page,
data-directory page and additional resources. The data directory defaults to
`Data` under the final installation directory. The page accepts typed or browsed
paths. Silent and one-click installs reuse existing configuration or the default.

[data-directory.js](examples/build/data-directory.js) validates paths and uses
nsQuickJs `fs.writeText` to write `data-directory.json`; before upgrades it checks
the old file size, reads it through `fs.readText`, and parses the JSON. HelloApp
does not consume this demonstration configuration.

The example bundles the `0.1.0` x86 Unicode plugin with `fs` support and licenses
under `examples/build/nsQuickJs`. The installer process is x86 for all three
application architectures. No PowerShell or Node.js is needed during installation.
Keep the complete `build` directory when copying the example. Plugin failures and
corrupt configuration abort installation.

JSON is limited to 16 KiB and directory paths to 1023 UTF-16 code units. New JSON
is UTF-8 without a BOM and preserves Chinese characters; older BOM/Unicode-escaped
files remain readable. NSIS sends the page value in chunks instead of performing
JSON byte I/O.

Each call sets `rootDir` to the current installation directory and explicitly
enables `allowFileAccess`. A unique temporary trusted script is extracted there,
loaded and deleted; the context is destroyed after use. Changing the installation
directory creates a context with the new root. NSIS creates external data
directories; plugin file access stays within its root.

```json
{
  "dataDirectory": "D:\\应用数据"
}
```

This example does not change directory permissions. Data inside the installation
directory is removed on upgrade/uninstall. For persistent data, select an external
directory writable by the application user.

## Examples

The independent [InstallerDemo](examples/InstallerDemo/README.md) reproduces the
original SetupFactory installation flow, including subdirectory entry points, four-part
repair/upgrade versions, five-process blocking, configuration backup/restore,
DataPath selection, and writable Conf/Data/Log directories.

The demo uses `InstallerDemo` names while preserving the directory structure and
local provenance, including the original script directory
`D:\workspace\csharp\NeuracleEMG\SetupFactoryScript`.
It has its own application ID and generated executables; it
does not include the real product or require those external source directories.
Its README records behavior differences and remaining work.

Build the smaller HelloApp example:

```powershell
dotnet publish examples/HelloApp -r win-x64 --self-contained true -o examples/publish
dotnet run --project src/DotnetNsis -- build --config examples/nsis.yml
```

The result is `artifacts/installers/NSIS 示例应用-1.0.0-x64-Setup.exe`.
NSIS supports `/S` for silent installation and `/D=` for the destination.
`/D=` must be last and its value unquoted; quote the executable path as required
by the shell.

```powershell
& './artifacts/installers/NSIS 示例应用-1.0.0-x64-Setup.exe' /S
```

Uninstall through Windows Installed apps or
`Uninstall {safe-name}.exe` in the installation directory. Upgrades with the
same `appId` use upstream old-version removal and replacement. The supported
initial scope is same-architecture, same-installation-scope upgrades. Keep
application data outside the installation directory.

Signing and automatic updates are not enabled. Upstream update arguments and
application-data deletion branches remain, but the CLI does not generate update
configuration, enable default data deletion, or expose corresponding YAML switches.

## Cache and troubleshooting

Tools are cached at `%LOCALAPPDATA%/dotnet-nsis/cache`:

- NSIS tool bundle `1.2.1`, containing NSIS `3.12` and plugins.
- 7-Zip tool bundle `1.0.1`, using Windows x64 executables.
- Fixed SHA-256 checks for downloads and cached files; damaged cache entries are
  repaired from verified archives.
- Offline builds once the cache is complete; dependency locks coordinate
  concurrent processes.

Failed builds retain the application copy, generated scripts and `build.log`
under `.dotnet-nsis-{random-id}` in the output directory. Successful builds remove
that temporary directory. Standard output prints the resulting absolute path;
progress goes to standard error.

Exit codes: `0` success, `2` configuration error, `1` download/build error,
`130` cancellation. Compressed applications at or above 2 GB are rejected to
avoid NSIS single-package limits.

## Tests

```powershell
dotnet test
# Download real tools and verify three architectures, two modes and two scopes.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1
# Also run per-user x86/x64 installation, upgrade, launch and uninstall.
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1 -Install
```

Installer tests use random application IDs and temporary paths, cleaning up their
shortcuts, registry entries and data. Run them in a Windows test environment.
Checks include subdirectory entry points, file integrity, empty directories,
shortcut targets, upgrades and external user-data preservation.

Ordinary `dotnet test` explicitly skips real toolchain and installation tests.
Per-machine UAC, wizard interactions and actual ARM64 installation require
separate environments. See [validation scope](docs/validation.md).

## Template sources

Installer templates come from [electron-builder's NSIS template directory](https://github.com/electron-userland/electron-builder/tree/8eff005219db1d17363add3ef4902e5b14798241/packages/app-builder-lib/templates/nsis)
(`packages/app-builder-lib/templates/nsis`). The link points to the pinned commit
used by this project so the template version can be traced.
Original templates ship with the CLI; adaptations affect temporary build copies
only. See [third-party notices](THIRD-PARTY-NOTICES.md) for the full commit hash
and adaptation details.

Developers can refresh that pinned snapshot with `python scripts/vendor.py`.
Python is not a CLI runtime dependency.

## Contributing and releases

See [contributing](CONTRIBUTING.md), [security reports](SECURITY.md),
[changelog](CHANGELOG.md), and [build/release instructions](docs/build.md).

GitHub Actions publishes the tested `dotnet-nsis` tool package to NuGet.org
after a matching `vX.Y.Z` tag passes builds and real installer tests, using
Trusted Publishing (OIDC) for the `gaoshang212` NuGet account. Configure the NuGet
trust policy described in the release guide; no long-lived API key secret is needed. Every build
packs and installs the tool from a local-only feed and checks a real NSIS build
before uploading its `.nupkg` artifact. See the release guide for setup.

HelloApp, InstallerDemo and local provenance notes remain in the source repository.
The NuGet package contains the CLI, templates, licenses and documentation;
use the complete source checkout for example build commands and linked example files.
