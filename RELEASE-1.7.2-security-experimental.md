# SDA++ Desktop 1.7.2 Security Experimental (Avalonia)

Experimental, NOT stable. Windows x64 portable, self-contained .NET. Extract separately and back up your vault before testing. WinForms is not included.

## Included

- GitHub update checks on startup and manually in Settings, channel-aware selection, and a release-page button. Installation remains manual.
- Stricter device invitation validation and active-Admin requirements.
- Read-only incoming command queue for this PC. Internal durable command journal and retry coordinator; automatic remote execution remains disabled.
- Includes earlier security experimental work: credentials v3 AES-GCM with explicit migration/backup, safer account import and enrollment recovery checkpoints.

## Test this release

Start with launch/restart, vault preservation, logins on a test copy, WebDAV transfer to Mobile, then update checks and read-only device views. Use the attached TESTING-EXPERIMENTAL-20260910.md for detailed Russian instructions, expected results, stop conditions and recovery guidance.

176 Desktop Core tests passed before packaging. No current physical-device, visual UI, live Steam enrollment or full cloud integration test. Do not test destructive device commands against real devices.

## Limitations

Full trusted-device enforcement, unknown Steam outcome reconciliation, .maFile cryptography modernization and sync conflict handling remain incomplete. The deployed Cloudflare Worker was NOT changed. Unfinished recovered Worker code is excluded.

EXE is unsigned. SHA256SUMS.txt verifies package integrity, not publisher authenticity. No vaults, credentials, signing keys or personal screenshots are included. Existing stable releases are unchanged.

Companion Android: https://github.com/ManeWreck/SDA-plus-plus-mobile/releases/tag/v0.6.19-security-experimental
