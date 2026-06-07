# SDA++

<p align="center">
  <img src="./icon.png" width="128" alt="SDA++ icon" />
</p>

<p align="center">
  A desktop Steam Guard authenticator focused on modern account management, QR login, cloud synchronization, and session recovery.
</p>

<p align="center">
  <img alt="Windows" src="https://img.shields.io/badge/Platform-Windows%2010%2B-2d6cdf" />
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-0f172a" />
  <img alt="Build" src="https://img.shields.io/badge/Build-Release-1f7a4f" />
</p>

![Selected account](./docs/selected-account.png)

Originally based on Steam Desktop Authenticator.  
Now developed as the independent **SDA++** project.

## What's new in SDA++

Compared to the original SDA:

- QR-code login from desktop screenshots
- Hotkey-based account switching
- Automatic session recovery
- Cloud synchronization improvements
- Separate credential vault support
- Enhanced account management tools
- Updated dark UI

## Features

- QR-code login directly from the desktop
- Automatic account switching with hotkeys
- Session recovery utilities
- Saved credentials manager for automatic re-login
- Separate cloud synchronization for `.maFile` data and saved credentials
- WebDAV cloud storage support
- Improved Russian localization
- Updated dark-themed interface
- Portable build with no installation required

## Interface Tour

### Steam Guard code

![Steam Guard code](./docs/steam-guard-code.png)

Shows the current rotating Steam Guard code and the progress bar until the next refresh.

### Selected account

![Selected account](./docs/selected-account.png)

Displays the current account status, session health, and quick actions for confirmations, re-login, and ending sessions.

### Pin account

![Pin account](./docs/pin-account.png)

Pinned accounts stay at the top of the account list so frequently used accounts are easier to reach.

### Account tools

![Account tools](./docs/account-tools.png)

The account tools menu gives quick access to re-login, login credential management, session termination, manifest cleanup, and authenticator deactivation.

### Manage login credentials

![Manage login credentials](./docs/manage-login-credentials.png)

This window stores encrypted login credentials used for automatic re-login and batch session recovery.

### Trade confirmations

![Trade confirmations](./docs/trade-confirmations.png)

Lets you approve or revoke trade and market confirmations directly from the desktop client.

### Settings, cloud sync, language, and hotkeys

![Settings, cloud sync, language, and hotkeys](./docs/settings-cloud-hotkeys.png)

Settings includes language selection, confirmation polling, QR hotkeys, account switching shortcuts, WebDAV cloud sync, and separate credential vault sync options.

### Steam QR hotkeys overlay

![Steam QR hotkeys overlay](./docs/steam-qr-hotkeys-overlay.png)

This floating overlay appears when the QR hotkey system changes state. It confirms whether QR hotkeys are enabled or disabled and shows the result of the last hotkey action.

### Account switching hotkeys

SDA++ supports fast account navigation with keyboard shortcuts:

- `Ctrl+Shift+Left` switches to the previous account
- `Ctrl+Shift+Right` switches to the next account

These shortcuts make it easy to move through multiple accounts without touching the mouse.

## Download

Download the latest portable version from [GitHub Releases](https://github.com/ManeWreck/SDA-plus-plus/releases).

Current release asset:

- `SDA++-portable.zip`

## Quick Start

1. Download `SDA++-portable.zip` from the Releases section.
2. Extract the archive.
3. Run `SDA++.exe`.
4. Create or import your Steam Guard account.
5. Configure cloud synchronization if desired.

## Security

SDA++ stores Steam Guard data locally.

This executable is currently unsigned, so Windows may show an `Unknown publisher` warning on first launch.

The public release package does not contain:

- personal Steam Guard files (`.maFile`)
- saved account credentials
- cloud synchronization credentials
- backup archives
- user-generated runtime data

Always keep encrypted backups of your Steam Guard files.

Release checksum:

- `SDA++-portable.zip`
  `SHA-256: 5033D9DD79C688F4D2AABEA8E9CF5790EC1F4770710D4199741BFB1FABB5CCFD`

## Repository Layout

```text
.
|-- docs/
|-- open-source/
|   |-- SteamDesktopAuthenticator.sln
|   |-- Steam Desktop Authenticator/
|   `-- lib/
|-- portable-exe/
|   |-- SDA++.exe
|   |-- maFiles/
|   `-- ...
|-- LICENSE
`-- README.md
```

## Building From Source

Requirements:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 or another compatible .NET build environment

Build:

```powershell
dotnet build .\open-source\SteamDesktopAuthenticator.sln -c Release
```

## Disclaimer

SDA++ is an unofficial Steam Guard desktop application inspired by the original Steam Desktop Authenticator project and is not affiliated with Valve or Steam.

Use it at your own risk and always keep secure backups of your authentication files.

## Credits

- Based on Steam Desktop Authenticator
- Extended and developed as the independent SDA++ project
