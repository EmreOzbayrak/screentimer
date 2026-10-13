# ScreenTime for Windows

A native Windows tray app for screen time, calendar history and app resource sessions. Click its clock beside sound and Bluetooth to open a compact dark panel with a circular activity chart. No browser dashboard or account is required.

## Install and run

Extract the Windows release ZIP and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -EnableStartup
```

This installs to `%LOCALAPPDATA%\ScreenTime\app`, adds a Start menu shortcut and starts in the tray. Omit `-EnableStartup` to keep your existing startup preference (off on a fresh install). You can also run `ScreenTime.exe` directly, then enable startup in Settings; enabling it creates a stable installed copy so moving the download or development folder does not break startup. Windows may place the icon in its **^ hidden icons** menu. Startup is at Windows sign-in, and Windows may delay it. If Task Manager disables the startup entry, re-enable it there or save the enabled option in ScreenTime settings.

Click outside the panel, press Escape or × to hide it while tracking continues. Right-click the icon to pause or quit. The panel resembles Quick Settings but is its own tray popup, not part of Windows' Bluetooth controls. Settings and details open inside the same panel.

Requires Windows 10/11 with .NET Framework 4.8. Private RAM measurements require a Windows build supporting `PROCESS_MEMORY_COUNTERS_EX2` (Windows 10/11 22H2 with the September 2023 cumulative update or newer). Unsupported measurements say **Unavailable**. No administrator access, NuGet packages or sensor driver is needed. Initial releases are unsigned; see [SECURITY.md](SECURITY.md).

## Calendar history

Choose **Day**, **Week** or **Month**, then use the arrows or calendar to revisit history. Weeks run Monday–Sunday; months follow the calendar. **Today** returns to the current period. Changing the period never deletes records, and there is no rolling 24-hour/7-day expiration. History survives app updates and Windows restarts because it lives outside the installed app folder.

The ring and legend show active time with matching colors for Work, Education, Fun, Social and Other. Switch between Categories, Apps and Pages. Click an app to see ranked websites, resource averages and its sessions. Category overrides apply to future and existing matching activity. The goal card estimates when a project could finish given a daily time commitment; dates are projections, not promises.

Foreground apps and browser visits need **10 continuous observed seconds** before anything is saved. Qualifying visits include their first ten seconds. Separate short visits do not add up to pass the threshold. App changes, browser-page changes, pause, lock and idle reset pending visits. Switching folders within File Explorer does not reset an app run. Foreground activity is sampled once a second and stops after the configured idle timeout; watching a video without input can count as idle.

## Sessions and resources

Open **Background apps & sessions**, then choose an app. Each expandable session represents an observed continuous run of the app, including its background time. A three-hour Overwatch run can appear as one session; switching to another app does not end that running session. Closing/restarting the process, restarting ScreenTime, pause, lock, sleep, or a long sampling gap starts a new observation session. Apps with the same process name are grouped, including subprocesses. These are observed app-running sessions, not inferred game matches. Sessions crossing midnight retain one identity with daily portions, so calendar totals are not duplicated.

Measurements are sampled approximately every five seconds on a background worker, with a ten-second minimum observed run. Session details include average and sampled peak app CPU, private RAM and GPU, plus whole-PC CPU, physical RAM in use and GPU during that session. Firmware-reported RAM speed is recorded as configuration information. **Fan RPM is unavailable**: the standard Windows fan class reports a requested target, not a measured tachometer value. ScreenTime does not install privileged hardware drivers.

- **CPU** is process execution time divided by elapsed observed time and all logical processors. CPU time sums across subprocesses; runtime is counted once. Task Manager may use frequency-adjusted CPU utility, so its live number is not expected to equal a session average.
- **RAM** is private working set, excluding shared pages. Whole-PC RAM is total physical memory minus available memory. Legacy full working-set records remain separately labelled and are never reinterpreted as private RAM. Values use MiB/GiB (1024-based).
- **GPU** uses Windows GPU Engine utilization counters. Subprocesses are summed within each engine; the busiest engine across adapters determines the app total. Engines are not all added together. Missing/first/changed counters are unavailable until a complete interval can be measured. Drivers or localized performance-counter names can make GPU counters unavailable.
- **Averages** are weighted by measured duration; missing samples do not become zero. Session details show CPU/RAM/GPU measurement coverage. Peaks are the highest sampled values, not instantaneous hardware maxima. Background time requires another app to have focus at both sample endpoints; focus transitions may be unclassified.

Protected processes, subprocess churn and work between samples can be missed. Resource sampling continues through ordinary idle time but stops on pause, lock, sleep and quit. Earlier aggregate history cannot be reconstructed into sessions or corrected resource readings; new measurements start with this version. Whole-PC metrics include other apps and cannot be attributed solely to a game. These are resource estimates, not electricity or money costs.

Definitions follow Microsoft's [memory counters](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex2), [GPU aggregation explanation](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/), [system CPU times](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), and [fan properties](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-fan).

## Privacy and saving

**Private/incognito recording is off by default.** Unverified browser windows are skipped as well. Page-title/hostname recording and browser resource collection are separate opt-ins, also off by default. Browser resource totals include private subprocesses if enabled. Normal-window verification currently supports recognized English Chrome/Edge profile controls; other browsers or layouts may be skipped. See [PRIVACY.md](PRIVACY.md) for exact behavior and limitations.

History is encrypted for your Windows account with DPAPI, saved every 15 seconds and at normal exit, with atomic replacement and a rolling backup. It is stored in `%LOCALAPPDATA%\ScreenTime\activity.json` (an encrypted file despite its old extension). Readable old data is migrated. If both main and backup are unreadable, the app preserves them and stops instead of wiping history. A sudden power failure can lose the time since the last save. History is retained until you choose **Delete recorded history**, which also clears sessions and backups.

## Build and verify

From the source folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -SourceDirectory dist -EnableStartup
```

The build uses Windows' bundled .NET Framework C# compiler, embeds the tray UI and runs regression tests. Use `-OutputDirectory dist\preview-build` to build beside a running version. `dist\ScreenTime.Tests.exe --live` briefly allocates 128 MiB and generates a CPU load to compare native measurements with Windows counters, without saving real history.

`ScreenTime.exe --background` starts hidden in the tray. `--quit` asks the running instance to save and exit. `--smoke-test` uses a separate temporary store and sample data, checks tray controls/history navigation, and renders UI previews beside the test binary. It never modifies real history or startup preferences. Do not publish development preview images or the test executable as runtime files.

Run `package.ps1` to create allowlisted source/runtime ZIPs with checksums. See [PUBLISHING.md](PUBLISHING.md) for GitHub upload instructions and optional certificate signing. Licensed under [MIT](LICENSE).
