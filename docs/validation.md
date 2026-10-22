# Validation

The following describes reproducible checks and their scope. A checked-in
workflow is not evidence that a hosted CI run or a release has succeeded.

| Check | Command | Scope |
| --- | --- | --- |
| Locked dependencies | `dotnet restore --locked-mode` | Resolve committed package locks |
| Unit tests | `dotnet test --no-restore` | Runs on .NET 8; CLI, configuration, templates, cache and demo helpers; real installers skipped |
| Build matrix | `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1` | Three application architectures, two installer modes and two scopes, including the nsQuickJs example |
| Installer lifecycle | Same script with `-Install` | x86/x64 per-user install, upgrade and uninstall; InstallerDemo repair, upgrade, downgrade/process/configuration checks |
| NuGet tool | `dotnet pack src/DotnetNsis -c Release -o artifacts/nuget -p:RestoreLockedMode=true`, then `scripts/test-tool-package.ps1` | Project/tag version and changelog checks, .NET 8 runtime requirement, package metadata and file hashes, isolated local installation, commands, real NSIS compilation and invalid-configuration exit code |

InstallerDemo uses a separate application ID and generated demo executables. Its
tests do not install the original product or require the local source paths
mentioned in the example's provenance notes.

## Remaining manual checks

- Native ARM64 installation and application execution.
- Per-machine UAC interactions and actual access from different standard users.
- Wizard layout, language selection, icons, and interactive directory selection.
- A clean Windows machine without development tools.
- System-font installation and migration from the original SetupFactory
  application, which are not implemented by InstallerDemo.

See the [InstallerDemo behavior comparison](../examples/InstallerDemo/README.md) for its
preservation rules and remaining differences. Do not infer support for full
rollback, signing, or automatic updates from successful installer tests.
