# SDA++ Desktop UI migration

The Avalonia client is developed next to the stable WinForms application. The stable executable remains the default until feature parity and security review are complete.

## Implemented

- Responsive XAML dashboard without absolute control coordinates.
- Reusable navigation, card, button, input, and account-list styles.
- Read-only loading of encrypted and unencrypted legacy `.maFile` accounts through `SDAPlusPlus.Core`.
- In-window vault unlock state with password clearing after every attempt.
- Live Steam Guard code generation and countdown.
- Account selection and search by login or SteamID.
- Initial dashboard and settings navigation.
- Typed English/Russian localization and persisted non-secret UI preferences.
- WebDAV connection testing and read-only pull preview with `PROPFIND` fallback.
- Explicit two-step cloud import with package validation, local backup, and rollback.
- Windows virtual-desktop QR capture without writing screenshots to disk.
- Configurable QR capture for the full desktop, monitor under cursor, or bounded cursor area.
- Configurable system-wide QR and confirmation hotkeys using `RegisterHotKey` with no keyboard hook.
- Hotkey conflict detection, previous-binding rollback, and clean unregister on application exit.
- Strict parsing and approval of modern Steam `https://s.team/q/...` sign-in codes.
- Dedicated QR page with account selection, progress, localized status, and user-initiated scanning only.
- Unified pending-confirmation list across all locally available accounts.
- Bounded parallel confirmation refresh with expired/unavailable account reporting.
- Explicit two-step Accept/Reject actions; no confirmation is processed automatically.

## Next boundaries

1. Port remaining account actions with explicit security boundaries.
2. Port selected-account login/session actions and import workflow.
3. Add Windows and Linux packaging after parity tests pass.

Set `SDAPP_MAFILES` to an existing `maFiles` directory when running the new client during development. Normal loading, code generation, QR scanning, and cloud preview are read-only. Only the final, explicit cloud-import confirmation writes account files, and it creates a backup first.
