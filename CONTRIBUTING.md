# Contributing

Build on Windows x64 with the .NET 10 SDK and install the .NET 8 runtime for the
CLI and tests. The NuGet tool targets .NET 8; preserve that runtime baseline when
adding APIs or dependencies. Start with:

```powershell
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-restore
```

For template, dependency, or installer behavior changes, also run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-integration.ps1 -Install
```

This runs real installers and modifies temporary registry entries and shortcuts.
Use a Windows test environment. State which architectures, installation scopes,
and interactive flows you actually tested. See [build and release](docs/build.md)
and [validation](docs/validation.md).

## Code and documentation

- Keep responsibilities in `CommandLineApplication`, `Configuration`, `ToolCache`,
  `TemplateAdapter`, `PackageBuilder`, and `ProcessRunner` separate.
- Use four-space C# indentation, file-scoped namespaces, nullable references,
  `PascalCase` members, and `camelCase` parameters. Use two-space YAML indentation.
- Add concise Chinese XML documentation with `<summary>` to every C# type and
  member, including private members and tests. Keep implementation comments and
  user-facing CLI messages in Chinese.
- Pass cancellation tokens through asynchronous calls. Use `ArgumentList` for
  external processes.
- Cover behavior changes with success, invalid-input, and regression cases.
  Build warnings are errors.
- Keep the English [README](README.md) and [Chinese README](README.zh-CN.md)
  synchronized. New contributor and release documentation uses English.
- Preserve HelloApp, InstallerDemo and existing local provenance notes. Use
  `InstallerDemo` for the installer example's project and module names, keeping
  script, configuration, test and documentation references synchronized.
- Keep upstream templates unchanged. Apply adaptations to temporary copies in
  `TemplateAdapter`. Dependency updates must include pinned revisions, hashes,
  license notices, and real build-matrix validation.

## Pull requests

Describe the problem, resulting behavior, validation, and remaining limitations.
Use the issue and pull request templates. Record user-visible changes in
[CHANGELOG.md](CHANGELOG.md). Write new commit subjects and bodies in English,
using Conventional Commits, for example `docs: add release instructions`.

For vulnerabilities, follow [SECURITY.md](SECURITY.md). Third-party components
retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
