# SDA++ Clean Publish

<p align="center">
  <img src="./icon.png" width="96" alt="SDA++ icon" />
</p>

<p align="center">
  Clean public package of a custom <b>SDA++</b> fork with full source code and a ready-to-run Windows build.
</p>

<p align="center">
  <img alt="Windows" src="https://img.shields.io/badge/Platform-Windows%2010%2B-2d6cdf" />
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-0f172a" />
  <img alt="Build" src="https://img.shields.io/badge/Build-Release-1f7a4f" />
</p>

## What is included

- `open-source/` contains the cleaned source tree for the project.
- `portable-exe/` contains a clean portable build that can be launched immediately on Windows.
- All personal runtime data was removed before packaging.

## Highlights

- QR login and session recovery utilities
- Saved credentials manager for automatic re-login
- Separate cloud sync options for `.maFile` data and saved credentials
- WebDAV pull fallback through `PROPFIND` when cloud `manifest.json` is empty or damaged
- Fixed Russian localization in settings and popup windows
- Clean dark-theme WinForms UI based on the current SDA++ fork

## Privacy cleanup

This package was prepared for public publishing and does **not** include:

- personal `maFiles`
- WebDAV credentials or cloud secrets
- cloud sync cache and backup snapshots
- saved login/password vault files
- runtime `manifest.json` from the working profile

The `portable-exe/maFiles` folder is intentionally empty except for a placeholder note.

## Repository layout

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

## Quick start

1. Open `portable-exe/`.
2. Run `SDA++.exe`.
3. On first launch, configure a fresh profile. The app will create its own local `maFiles` data.

## Security note

This executable is currently unsigned, so Windows may show an `Unknown publisher` warning on first launch.

You can either:

- build the app from source using the .NET 8 SDK
- or download the portable build from GitHub Releases

SHA-256 checksums are provided for release files so you can verify the downloaded archive before running it.

## Build from source

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Open `open-source/SteamDesktopAuthenticator.sln`.
3. Build the `Release` configuration.

## Important note

This is an unofficial community fork of Steam Desktop Authenticator and is **not** affiliated with Steam. Use it carefully, keep secure backups, and do not publish generated runtime data from your own installation.

## Credits

- Original Steam Desktop Authenticator project by community contributors
- This package preserves the customized SDA++ fork and publishes it in a clean, shareable form
