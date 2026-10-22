# Changelog

Version sections describe release content, not publication status. Published
package versions are listed on NuGet.org.

## [0.1.0]

### Features

- Package a published application directory into a Windows NSIS executable from
  YAML, with `init`, `validate`, and `build` commands.
- Build x64, x86, and ARM64 application packages on Windows x64, with wizard or
  one-click installers and fixed per-user or per-machine scope.
- Reuse pinned electron-builder templates and verified NSIS/7-Zip downloads.
- Support 27 installer languages, language selection, custom icons, custom NSIS
  includes, subdirectory entry points, and three- or four-part versions.
- Derive application GUIDs using electron-builder's default UUID v5 algorithm.
- Provide a data-directory example using the bundled nsQuickJs plugin.
- Include InstallerDemo with repair/upgrade flows, downgrade rejection, five-process
  checks, configuration backup/restore, writable data directories, and preserved
  configuration/data/log files on uninstall.
- Include English and Chinese READMEs, MIT licensing, contribution and security
  policies and issue templates.
- Distribute `dotnet-nsis` as a .NET 8 tool on NuGet, with local package installation
  checks and tag-triggered Trusted Publishing (OIDC) for the `gaoshang212` account.

### Compatibility and limitations

- The CLI is distributed as a NuGet tool requiring .NET 8. Installer builds
  use a Windows x64 host.
- Keep `appId`, architecture, and installation scope stable for supported upgrades.
  Earlier SHA-256 installation identifiers are not migrated automatically.
- No signing, automatic-update configuration, or full transactional rollback.
- InstallerDemo uses generic module names and preserves the original directory
  structure and local provenance notes.
  System-font installation and migration from the real SetupFactory product are
  not implemented.
- ARM64 installation, per-machine UAC, and interactive wizard behavior require
  separate validation. See [validation](docs/validation.md).
