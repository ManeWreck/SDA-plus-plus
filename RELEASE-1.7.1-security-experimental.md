# SDA++ Desktop 1.7.1 Security Experimental (Avalonia)

Pre-release, not a replacement for stable. Windows x64; this is the Avalonia UI, not WinForms.

## Included

- New .maFile enrollment wizard with recovery-code re-entry and encrypted persistence before activation.
- Resume a saved prepared enrollment without requesting a new authenticator. Ambiguous submitted states block replay; automatic Steam reconciliation is not implemented.
- Import no longer deletes unlisted account files on collisions and refuses partially readable destinations.
- Credentials v3 AES-GCM support and explicit migration with an encrypted backup. No automatic cloud migration.
- Device management confirmation binds the target/action; checks role and expiry; server acceptance is not represented as confirmed device execution.

## Before testing

Back up your whole existing vault and retain its original keys separately. Do not overwrite a working installation: extract this package to a separate folder. Test with copies first.

Before upgrading credentials to v3, install SDA++ Mobile 0.6.18 security experimental (or a later compatible build) on every paired phone. Older stable builds cannot read v3. The encryption password for .maFiles is not the generated 32-byte credentials key. Cloud upload remains a separate explicit action.

The migration retains `credentials.secure.json.pre-v3-*.bak`. For rollback, close all writers, preserve the current v3 file, and restore the backup under the original filename. Later changes are not in that backup. Keep the original encryption key.

## Verification and limitations

Automated Core tests and Release build are checked during packaging. No physical-device, visual UI, real Steam enrollment, or end-to-end WebDAV/Google Drive test was possible for this release. Do not treat this as a security audit.

Password-encrypted .maFiles still use the legacy encryption/KDF; that migration is pending. Trusted-device execution acknowledgments, broader synchronization conflict handling, and full recovery from unknown Steam outcomes remain pending. Revocation cannot erase secrets already copied to a device and does not terminate Steam sessions.

No personal screenshots or runtime vault files are included. Desktop executable is not code-signed. SHA256SUMS.txt accompanies the package.
