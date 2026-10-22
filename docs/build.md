# Build and release

## Requirements

Use Windows x64 and the .NET 10 SDK selected by `global.json`, plus the .NET 8
runtime for the CLI and tests. The repository keeps its .NET 10 build toolchain
and demos; the distributed tool targets `net8.0` and does not require .NET 10.
Python is needed
only to refresh pinned upstream templates with `scripts/vendor.py`.

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1 -Install
```

The integration script prepares HelloApp for three architectures and the
InstallerDemo payload, then runs real build and per-user installation tests.
Per-machine UAC and ARM64 installation still require separate test environments.

## Publishing a release

1. Update the project version and matching `CHANGELOG.md` section.
2. Synchronize both READMEs, third-party notices, and validation limitations.
   Preserve demos, InstallerDemo names, and local provenance notes.
3. Run unit tests and the real installer suite. Pack and test the NuGet tool as below.
4. Commit the intended source, then create and push the matching `vX.Y.Z` tag
   when ready to publish.
5. The GitHub workflow builds and tests the tag, then publishes the verified
   NuGet package through Trusted Publishing (OIDC) as `gaoshang212`.

All jobs have read-only repository contents permissions. Only the tag-triggered
NuGet job has `id-token: write` to obtain temporary publishing credentials.
Branch pushes, pull requests, and manual runs on
branches produce CI artifacts without publishing.

The repository is [gaoshang212/dotnet-nsis](https://github.com/gaoshang212/dotnet-nsis).
Enable Actions and optionally private vulnerability reporting. Configure branch
protection and required checks in repository settings as appropriate.

## NuGet distribution

The package ID and installed command are both `dotnet-nsis`. This is a
framework-dependent .NET 8 tool; use it on Windows x64. It includes managed
dependencies, original templates, licenses and documentation. HelloApp and
InstallerDemo remain in the source checkout; their build commands use the repository
layout.

```powershell
dotnet pack src/DotnetNsis -c Release -o artifacts/nuget -p:RestoreLockedMode=true
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-tool-package.ps1
```

The result is `artifacts/nuget/dotnet-nsis.0.1.0.nupkg`. The verification script
checks the project version, nonempty changelog entry, package identity/version/type
and .NET 8 runtime configuration, and hashes of templates, licenses and
documents. It installs from a local-only feed with an isolated package cache and
tool directory, then checks `--version`, help, `init`, `validate`, a real NSIS build
and invalid-configuration exit code `2`. It never installs the resulting installer
or changes your global tool installation. Diagnostic files remain under
`artifacts/nuget-smoke/`.

The script restricts runtime roll-forward to patch versions while testing the
tool, so a newer installed runtime cannot hide an absent .NET 8 runtime. CI
installs both the .NET 8 and .NET 10 SDKs for these separate roles.

Pass `-Tag v0.1.0` to check the release tag against the project version; CI passes
the actual tag for tag builds. Mismatched tags and missing changelog entries fail
before the package is installed or uploaded.

For a persistent local installation from the built package:

```powershell
dotnet tool install --tool-path ./artifacts/tools --add-source ./artifacts/nuget --version 0.1.0 dotnet-nsis
./artifacts/tools/dotnet-nsis.exe --help
```

### Configure NuGet.org Trusted Publishing

1. Sign in to NuGet.org as `gaoshang212`. Confirm that the
   `dotnet-nsis` package ID is available or owned by that account.
2. Open **Trusted Publishing** from the account menu and add a GitHub policy.
   Use the following values for `https://github.com/gaoshang212/dotnet-nsis.git`.

   | Field | Value |
   | --- | --- |
   | Policy owner | `gaoshang212` |
   | Repository Owner | `gaoshang212` |
   | Repository | `dotnet-nsis` |
   | Workflow File | `build.yml` (filename only) |
   | Environment | Leave empty; the workflow does not use an environment |
   | Package glob pattern | `dotnet-nsis` |

3. Allow publishing new packages for the first release and new versions for
   subsequent releases. No GitHub Actions API key secret is required.
4. Push a `vX.Y.Z` tag matching the project version and changelog. The build job
   validates the tag, runs tests, and uploads the tested `.nupkg`. The NuGet job
   downloads that artifact, uses the pinned official `NuGet/login` action with
   `user: gaoshang212` to exchange a GitHub OIDC token for a temporary API key,
   and pushes the exact version to
   `https://api.nuget.org/v3/index.json`.

The temporary key comes from `steps.login.outputs.NUGET_API_KEY` immediately
before publishing. Without a matching NuGet trust policy, login fails.
Branch and PR builds never push packages. Existing
versions are skipped to allow retrying a successful push; new content needs a new
version. A failed NuGet job can be rerun with the tested artifact.
NuGet.org validation and indexing must finish before users can install a new release.

Implementation references: Microsoft's [.NET tool packaging tutorial](https://learn.microsoft.com/en-us/dotnet/core/tools/global-tools-how-to-create)
and [NuGet Trusted Publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing),
plus the official [NuGet/login action](https://github.com/NuGet/login).
