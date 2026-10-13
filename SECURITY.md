# Security

ScreenTime is a local activity recorder. Review [PRIVACY.md](PRIVACY.md) before enabling browser metadata or browser resource collection.

The application requires no API keys, service credentials, administrator privileges or downloaded kernel drivers. History and encryption keys must never be added to this repository. `.gitignore`, the source scanner and explicit release file lists reduce accidental inclusion; they do not prove that every possible secret is absent.

The Windows executable is **unsigned unless the publisher explicitly signs it**. An unsigned build may display Windows reputation warnings. A SHA-256 checksum verifies that two files match; it does not establish publisher trust. No certificate or private key is bundled. The packaging script optionally signs using a code-signing certificate already in the publisher's Windows certificate store and verifies the signature and timestamp.

For a potential vulnerability, use the repository's private **Report a vulnerability** option when the owner has enabled it. If unavailable, ask the maintainer for a private reporting channel without disclosing sensitive details publicly. Do not post activity files, private page titles, credentials, memory dumps or personal screenshots in issues.

Only the current source version is maintained. There is no automatic updater: obtain updates from the repository owner, verify the release, quit the app and run the included installer. The installer keeps history outside the application directory.

## Developer checks

`scripts/Check-Secrets.ps1` scans candidate Git source files and prints redacted findings. `build.ps1` compiles without NuGet and runs regression checks. `package.ps1` creates allowlisted runtime/source archives, excluding `.git`, local history, preview screenshots, test executables, certificates and development output. The GitHub workflow uses pinned actions, a read-only repository token and no configured secrets. It builds artifacts; it does not publish a release automatically.
