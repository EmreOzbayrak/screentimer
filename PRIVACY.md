# Privacy

ScreenTime runs locally. It has no analytics, account, server, advertising SDK, automatic updater, or app network requests. Signing and GitHub Actions are separate developer tools, not app traffic.

## What is recorded

- Foreground app names, dates, categories and active duration. Apps and browser visits must last at least 10 continuous observed seconds; separate short visits do not combine.
- Browser page titles and hostnames only if enabled. Complete URLs, URL paths, query strings, page contents, keystrokes and screenshots are not saved. A title can still contain sensitive information.
- Accessible processes in the current Windows session: observed runtime, CPU execution time, private and total working-set RAM, GPU utilization when available, and app-running session boundaries. Whole-PC CPU, used physical RAM, GPU, and firmware-reported RAM speed accompany sessions. No hardware serial numbers are read.
- Preferences, app exclusions, category rules and goal estimates.

## Private browsing

**Record incognito / private browser windows is off by default.** When off, ScreenTime skips both private windows and windows whose privacy status cannot be verified. It checks native browser toolbar controls before reading a title or address. Currently normal-window verification recognizes supported English Chrome and Edge profile controls. Other browsers, translations, hidden/custom toolbars, browser updates and Firefox always-private mode may be unverified and skipped. This is conservative, best-effort accessibility detection, not a browser-provided security boundary. Exclude a browser entirely when you require it never to be recorded.

Page title/hostname recording also defaults off. Enabling private recording permits unverified browser windows; the separate title setting still controls page metadata. These settings affect future records. Existing history is retained until deleted.

**Browser resource collection defaults off separately.** Browser subprocesses can serve both normal and private windows, so enabling this option records their combined CPU/RAM/GPU and runtime, including private processes. It never extracts page names from background processes. Whole-PC usage naturally includes every workload but does not identify private pages.

## Storage and retention

History stays in `%LOCALAPPDATA%\ScreenTime\activity.json` with a rolling `.bak`. Despite the legacy filename, the new format is encrypted with Windows DPAPI for the current Windows account. The app migrates readable old JSON and backups in place. Save uses an encrypted temporary file, flushes it, then atomically replaces the main file. Saves occur every 15 seconds and at normal exit, lock and suspend. A sudden power loss can omit activity since the last completed save; measurement gaps are never filled in later.

If the main file cannot be read, ScreenTime attempts the backup and preserves an encrypted unreadable copy. If neither is recoverable, it stops with an error instead of replacing history with an empty file. Do not delete the originals while troubleshooting. A missing main file with an intact backup is recoverable.

Days, weeks, months and sessions are retained indefinitely. Selecting a period only filters the display. Delete recorded history removes all activity, sessions, resource records and rolling/recovery backups while retaining preferences and goals. Logical deletion is not a guarantee of forensic erasure from an SSD or independent backups.

DPAPI protects files at rest; software already running as your Windows account can still decrypt them. Copying the encrypted file to a different account or reinstalling Windows may make it unreadable. Keep normal Windows/user-profile backups if long-term recovery is important. ScreenTime does not have a cloud copy.

## Controls

Pause, exclude an app by process name, disable browser options, or quit via the tray icon. Foreground recording stops on idle; resource sampling continues through ordinary inactivity and stops on pause, lock, suspend or quit. Exclusions apply to both app histories. Changes to settings discard pending samples.
