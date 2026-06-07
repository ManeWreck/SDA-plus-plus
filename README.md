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

## Download

Download the latest portable version from [GitHub Releases](https://github.com/ManeWreck/SDApp-GitHub-Publish/releases).

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
  `SHA-256: 064F95DE9FE7354008D92F6B715B629F5BC53FD1DE45217C2352C00547D49178`

## Repository Layout

```text
.
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
