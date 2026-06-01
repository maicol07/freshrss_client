# FreshRSS Client

A native Windows desktop client for [FreshRSS](https://freshrss.org), built with WinUI 3 and the Windows App SDK.

<p align="center">
  <img src="FreshRssClient/Assets/AppIcon.ico" width="64" height="64" alt="App Icon" />
</p>

## Features

- **System tray integration** — minimize to tray with a dynamic icon showing unread count
- **Windows toast notifications** — get notified of new articles even when the app is in the tray
- **Taskbar badge** — see your total unread count at a glance
- **Single instance** — only one instance runs at a time; clicking a notification reopens the existing window
- **Full FreshRSS API sync** — browse categories, feeds, and articles with read/unread tracking
- **Multi-select** — mark multiple articles as read or open them all in your browser at once
- **Offline cache** — articles and read status persist locally for offline browsing
- **OpenGraph support** — optionally fetch article thumbnails and metadata
- **Grid and list layouts** — switch between compact grid and full article reader
- **Localized UI** — Italian and English
- **Auto-start** — optionally launch at Windows startup, hidden in the tray

## Screenshots

> TODO

## Requirements

- **Windows 10** version 19041 (20H1) or later
- **Windows 11** fully supported
- Architecture: x64, x86, or ARM64

## Installation

### Download (recommended)

Download the latest build from the [Releases](https://github.com/maicol07/freshrss_client/releases) page:

1. Download `FreshRssClient_win-x64.zip` (or `win-arm64`)
2. Extract the folder anywhere (e.g., `%LocalAppData%\FreshRssClient`)
3. Run `FreshRssClient.exe`

No installer required — the app is fully self-contained.

### Build from source

```bash
git clone https://github.com/maicol07/freshrss_client.git
cd freshrss_client
dotnet publish FreshRssClient -c Release -r win-x64 --self-contained
```

## Configuration

On first launch, open **Settings** and enter your FreshRSS server credentials:

- **Server URL** — your FreshRSS instance (e.g., `https://rss.example.com`)
- **Username** — your FreshRSS username
- **API Password** — your FreshRSS API password (found in FreshRSS → Settings → Profile)

The app will authenticate and begin syncing immediately.

## Tech Stack

| Area | Technology |
|------|-----------|
| UI framework | WinUI 3 (Windows App SDK 2.1) |
| MVVM toolkit | CommunityToolkit.Mvvm 8.4 |
| Target framework | .NET 10 / Windows 10 19041+ |
| Notifications | UWP Toast + Badge APIs |
| System tray | Win32 `Shell_NotifyIconW` + GDI+ |
| Localization | Custom `LocalizationManager` |

## Project Structure

```
FreshRssClient/
├── App.xaml.cs              # Entry point, single-instance enforcement
├── MainWindow.xaml.cs       # Navigation shell with tray & title bar
├── Views/
│   ├── ArticlesPage.xaml    # Article list & reader
│   └── SettingsPage.xaml    # Settings form
├── ViewModels/
│   └── MainViewModel.cs     # All app logic & state
├── Services/
│   ├── FreshRssService.cs   # FreshRSS API client
│   ├── NotificationService.cs
│   ├── Localization.cs
│   └── OpenGraphService.cs
├── Helpers/
│   ├── TrayIconHelper.cs    # System tray with dynamic badge
│   └── StartupHelper.cs     # Auto-start registration
└── Assets/                  # Icons and splash screen
```

## License

MIT
