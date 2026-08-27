<p align="center">
  <img src="FreshRssClient/Assets/Square150x150Logo.scale-200.png" width="120" alt="FreshRSS Client" />
</p>

<h1 align="center">FreshRSS Client</h1>

<p align="center">
  A native Windows desktop client for <a href="https://freshrss.org">FreshRSS</a>, built with WinUI 3 and the Windows App SDK.
</p>

<p align="center">
  <a href="https://github.com/maicol07/freshrss_client/actions/workflows/build.yml"><img src="https://github.com/maicol07/freshrss_client/actions/workflows/build.yml/badge.svg" alt="Build status" /></a>
</p>

## Features

- **System tray integration** — minimize to tray with a dynamic icon showing the unread count
- **Windows toast notifications** — get notified of new articles even when the app sits in the tray
- **Taskbar badge** — see your total unread count at a glance
- **Single instance** — only one instance runs at a time; clicking a notification reopens the existing window
- **Full FreshRSS API sync** — browse categories, feeds, and articles with read/unread tracking
- **Multi-select** — mark several articles as read, or open them all in your browser at once
- **Offline cache** — articles, read status, and pending read-marks persist locally and replay on reconnect
- **Article thumbnails** — the first image in the feed body is extracted and reused in the list and in toasts
- **Grid and list layouts** — switch between a compact grid and the full article reader
- **Localized UI** — Italian and English
- **Auto-start** — optionally launch at Windows startup, hidden in the tray
- **Credentials in the Windows Credential Locker** — never written to the settings file

## Screenshots

> TODO

## Requirements

**To run**

- Windows 10 version 19041 (20H1) or later, or Windows 11
- Architecture: x64, x86, or ARM64

**To build**

- .NET SDK 10.0.100, or a later 10.0.x feature band — pinned in [`global.json`](global.json) with `rollForward: latestFeature`
- MSBuild, or Visual Studio with the Windows App SDK workload

## Installation

No release has been published yet. Until then, either grab the CI build or compile locally.

**CI build** — open the latest green run on the [Build workflow](https://github.com/maicol07/freshrss_client/actions/workflows/build.yml), download the `FreshRssClient-win-x64` artifact, extract it anywhere (e.g. `%LocalAppData%\FreshRssClient`) and run `FreshRssClient.exe`. The publish is self-contained, so no runtime install is required.

**From source**

```bash
git clone https://github.com/maicol07/freshrss_client.git
cd freshrss_client
dotnet publish FreshRssClient -c Release -r win-x64 --self-contained
```

Swap `win-x64` for `win-arm64` or `win-x86` as needed.

## Configuration

On first launch, open **Settings** and enter your FreshRSS server credentials:

| Setting | Notes |
|---------|-------|
| Server URL | Your FreshRSS instance, e.g. `https://rss.example.com` |
| Username | Your FreshRSS username |
| API password | Set it in FreshRSS under Settings → Profile |

The app authenticates and starts syncing immediately. The remaining settings — sync interval (default 15 min), unread-only filter, max cached read articles (default 50), language, grid layout, open links in the external browser, auto-start, start minimized in tray — are also on that page.

### Where data is stored

Everything lives in `%LocalAppData%\FreshRssClient`:

| File | Contents |
|------|----------|
| `settings.json` | All settings except the password |
| `cache.json` | Cached articles and read state for offline browsing |
| `pending_reads.json` | Read-marks made offline, replayed on the next sync |
| `sent_notifications.json` | Article IDs already notified, so toasts are not repeated |

The API password goes to the Windows Credential Locker under the `FreshRssClient` resource, not to `settings.json`.

## Tests

```bash
dotnet test FreshRssClient.Tests/FreshRssClient.Tests.csproj -c Debug -p:Platform=x64
```

That is the exact command CI runs. The suite uses Microsoft.Testing.Platform (configured in `global.json`) and covers the API client, the view model, notifications, and URL handling.

## Tech Stack

| Area | Technology |
|------|-----------|
| UI framework | WinUI 3 / Windows App SDK 2.4 |
| MVVM toolkit | CommunityToolkit.Mvvm 8.4 |
| Settings UI | CommunityToolkit.WinUI.Controls.SettingsControls |
| Target framework | .NET 10, Windows 10 19041+ |
| Notifications | UWP Toast + Badge APIs |
| System tray | Win32 `Shell_NotifyIconW` + GDI+ (`System.Drawing.Common`) |
| Localization | Custom `LocalizationManager` over `.resx` |

## Project Structure

```
FreshRssClient/
├── App.xaml.cs                  # Entry point, single-instance enforcement
├── MainWindow.xaml.cs           # Navigation shell with tray & title bar
├── Views/
│   ├── ArticlesPage.xaml        # Article list, grid layout, multi-select
│   ├── ArticleDetailPage.xaml   # Full article reader
│   └── SettingsPage.xaml        # Settings form
├── ViewModels/
│   └── MainViewModel.cs         # App logic, state, persistence
├── Services/
│   ├── FreshRssService.cs       # FreshRSS API client
│   ├── NotificationService.cs   # Toast + badge
│   └── Localization.cs          # LocalizationManager
├── Helpers/
│   ├── TrayIconHelper.cs        # Tray icon with dynamic unread badge
│   ├── StartupHelper.cs         # Auto-start registration
│   ├── SafeFireAndForget.cs     # Exception-safe async void
│   └── WebUri.cs                # http/https URL validation
├── Resources/                   # Strings.resx, Strings.it.resx
└── Assets/                      # Icons and splash screen

FreshRssClient.Tests/            # Microsoft.Testing.Platform suite
tools/generate-icons.ps1         # Icon set generator
```

## Branding

The whole icon set is generated from a single definition rather than hand-drawn, so a recolor or a shape tweak never leaves the sizes out of sync:

```powershell
pwsh -File tools/generate-icons.ps1
```

It overwrites every file in `FreshRssClient/Assets` in place — the MSIX tile and unplated variants, the splash screen, and a multi-resolution `AppIcon.ico` (16 → 256 px). The palette lives in three constants at the top of the script. The tray icon is drawn at runtime by `TrayIconHelper` using the same proportions, so the badge can be composited over it; its palette constants are kept in sync manually.

## License

[MIT](LICENSE)
