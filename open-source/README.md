# SDA++ Source Tree

This folder contains the cleaned source code for the SDA++ fork included in this package.

## Build requirements

- Windows 10 or newer
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

1. Open `SteamDesktopAuthenticator.sln`.
2. Build the `Release` configuration.
3. The main application project is `Steam Desktop Authenticator`.

## Included changes

- Auto-login support built around a separate credentials vault
- Separate sync settings for saved credentials
- WebDAV pull fallback through `PROPFIND`
- Russian localization fixes in settings and popup dialogs

## Cleanup

This source tree was copied without `bin/`, `obj/`, user profiles, cloud cache, or other machine-specific runtime data.
