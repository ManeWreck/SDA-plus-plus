# SDA++ 1.4.1

SDA++ 1.4.1 adds safe automatic update checks.

- Checks the latest GitHub Release after startup and every six hours.
- Shows a footer link and Windows notification only when a newer version exists.
- Opens the exact release page when the update link is clicked.
- Never downloads, installs, or launches an update automatically.
- Silently ignores network and GitHub API failures so Steam Guard remains uninterrupted.
- Fixes the GitHub footer link to open the desktop SDA++ repository directly.

# SDA++ 1.4.0

SDA++ 1.4.0 improves multi-account operation, session recovery, confirmations, and account visibility.

## Account monitoring

- Added a dedicated **Monitor** entry in the main navigation.
- Added an all-account dashboard for VAC status, Steam level, game count, session health, and tradable CS2 inventory.
- Added search by account name or SteamID.
- Added filters for accounts that need attention, active sessions, and private profiles.
- Added sorting by account, level, inventory size, and VAC status.
- Added a five-minute response cache to reduce repeated Steam Community requests.
- Inventory loading now follows pagination and excludes medals and every other untradeable item.

## Sessions and auto-login

- Persistent Steam sessions are requested during manual and stored-credential login.
- Added **Auto login all accounts** to Account tools.
- Manual re-login offers to save missing credentials securely for later recovery.
- Added configurable hotkeys for batch auto-login and confirmations.

## Confirmations

- The confirmations window now combines confirmations from all loaded accounts.
- Each confirmation keeps its originating account attached, preventing actions from being sent through the wrong account.
- Popup confirmation actions use the same account-safe mapping.

## Interface

- The application version is visible in the title and footer.
- Updated localization and compact hotkey controls.
- Updated application icon.

## Security

Release archives and source commits exclude `.maFile`, manifest, saved credentials, cookies, tokens, cloud secrets, and backup data.
