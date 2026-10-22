# Security Policy

## Supported versions

Security fixes target the latest published release. Until the first release,
report issues against the current development branch. Older versions have no
separate maintenance commitment.

## Reporting a vulnerability

Use **Security → Advisories → Report a vulnerability** on the project's GitHub
repository when private vulnerability reporting is enabled. Include the version
or commit, Windows and .NET versions, target architecture, installation scope,
a minimal configuration or script, and the expected impact.

If that option is unavailable, open an issue requesting a private contact
without including exploit details or sensitive data. Wait for a private channel.
Ordinary bugs belong in the bug report template. No fixed response time is promised.

## Security boundary

The CLI packages application files and compiles trusted NSIS scripts. Custom
includes can execute commands during compilation and installation, including
with administrator privileges for per-machine installers. Configuration
validation does not sandbox custom scripts or the packaged application.

Downloaded tool archives and cached executables are checked against pinned
hashes. Do not replace these checks with unverified downloads.

The generic installer removes files inside the installation directory on
upgrade or uninstall. Store persistent data outside that directory. InstallerDemo
has its own preservation and permission rules, documented in its
[README](examples/InstallerDemo/README.md); it does not provide complete transactional
rollback or migration from an existing SetupFactory installation.

The bundled nsQuickJs example executes trusted JavaScript with explicitly
enabled file access. Its path restrictions are not an operating-system sandbox.
The CLI does not sign installers or provide automatic updates.
