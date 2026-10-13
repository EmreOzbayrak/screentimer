# Publishing ScreenTime

The repository uses the MIT license. App version: 0.2.0. This guide covers publishing the source and Windows release, including creating a new remote when needed.

## Upload through GitHub's website

Extract `artifacts/ScreenTime-0.2.0-source.zip` into a clean folder. Upload its contents at the repository root, including `.github`, `.gitignore` and `.gitattributes`. Do not upload the entire development folder or any file from your Windows ScreenTime data folder. The source archive contains no `.git` history or Git author email.

After the Windows build workflow passes, create a GitHub release for `v0.2.0` and attach `ScreenTime-0.2.0-windows.zip` and `SHA256SUMS.txt` from `artifacts`. Describe an unsigned release as unsigned. End users need the Windows archive; the source archive is for developers.

## Publish using Git

Choose a public Git author name and GitHub's exact no-reply email from your GitHub email settings before committing; a normal Git commit can expose your configured email. No identity is invented by this project. Then, from this folder:

```powershell
git config --local user.name YOUR_PUBLIC_NAME
git config --local user.email YOUR_GITHUB_NOREPLY_EMAIL
git config --local core.hooksPath .githooks
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Check-Secrets.ps1
git add .gitignore .gitattributes .github .githooks README.md LICENSE PRIVACY.md SECURITY.md PUBLISHING.md build.ps1 install.ps1 package.ps1 scripts src tests dist/.gitkeep
git diff --cached --stat
git commit -m "Add ScreenTime Windows tray app"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPOSITORY.git
git push -u origin main
```

Replace the uppercase placeholders before running. No push, account authorization or public release is performed by the local scripts. Enable GitHub secret scanning/push protection and private vulnerability reporting in your new repository where available.

## Rebuild the archives

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

To produce a signed release, obtain a trusted code-signing certificate and install it in `Cert:\CurrentUser\My`. Keep private keys out of this folder. Then pass its thumbprint:

```powershell
.\package.ps1 -CertificateThumbprint YOUR_CERTIFICATE_THUMBPRINT
```

Signing contacts the configured timestamp server. The script requires a valid code-signing certificate with a private key and a valid timestamped result. Signing was not performed for the initial unsigned package because no signing certificate was supplied. GitHub CI produces unsigned artifacts and needs no signing secret. Review signed artifacts separately before publishing.
