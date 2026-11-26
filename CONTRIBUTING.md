# Contributing to ScreenTime

Use four spaces and keep C# formatted with the repository's pinned CSharpier tool. The .NET 10 SDK is needed for formatting. The application builds and runs on the .NET Framework 4.8 toolchain.

```powershell
dotnet tool restore
$sources = (Get-ChildItem -Path src, tests -Recurse -File -Filter '*.cs').FullName
dotnet csharpier format --no-msbuild-check @sources
dotnet csharpier check --no-msbuild-check @sources
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

Enable the repository's local Git hooks once per checkout.

```powershell
git config core.hooksPath .githooks
```

Use [Conventional Commits 1.0.0](https://www.conventionalcommits.org/en/v1.0.0/) for commits and pull request titles. Examples include `fix(tracker): retain qualified visits`, `style: format C# sources`, and `feat(storage)!: change the export format`. Leave a blank line before an optional body or footer. For squash merges, keep the pull request title as the commit subject.

GitHub Actions checks new non-merge commits on pushes and pull requests, as well as pull request titles. It also checks C# formatting, scans for accidentally committed private files or credentials, builds with warnings treated as errors, runs regression tests, and packages the Windows and source archives.
