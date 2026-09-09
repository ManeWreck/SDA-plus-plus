# Security development status

## Implemented locally

- Avalonia Create .maFile wizard, for accounts with a verified phone number and no existing authenticator.
- Requires an encrypted destination; an empty new vault can be initialized with a password.
- Pending authenticator secrets saved before activation, followed by read-back verification.
- Recovery-code re-entry required before activation; SMS failures allow explicit retry.
- Ambiguous activation responses do not report success or retry automatically.
- Failed final persistence can be retried without repeating Steam activation.
- Saved pending files are never deleted by wizard cancellation.
- Resume saved enrollment from the Avalonia wizard without requesting a new authenticator; re-enter the vault password and recovery code.
- A write-ahead checkpoint inside the encrypted account is persisted and read back before activation. Submitted/unknown/legacy checkpoints block replay after restart.
- Resume retains the original device identifier and authenticator secrets.
- Enrollment persistence no longer reads Avalonia controls from continuation threads; inputs are disabled during active operations.
- Import preserves unlisted files on filename collisions and refuses partially readable destinations.
- Regression tests use synthetic accounts only; no production Steam requests made.
- Device action confirmation captures its target, action and registry identity. Changed permissions, expiry, revocation or registry identity invalidate approval; changing list selection cannot retarget a confirmed action.
- Registry client rejects management commands from non-admin/expired identities and self-targeting requests before network dispatch.
- UI distinguishes server acceptance from confirmed device execution; revocation explicitly does not promise deletion of copied secrets or termination of Steam sessions.

## Not completed or verified

- No live enrollment verification or visual QA on the new wizard yet.
- Password-encrypted .maFile storage still uses the legacy format; its migration and KDF upgrade remain pending.
- Credentials v3 support is implemented in both platforms with shared test vectors. Avalonia offers explicit v2-to-v3 migration with a retained encrypted backup, staged read-back verification and a compatibility acknowledgment. Default creation remains v2 until the rollout is ready.
- Trusted-device completion remains pending.
- Automatic reconciliation of unknown Steam outcomes remains pending. Resume is supported for saved prepared checkpoints only; submitted/legacy/unknown checkpoints show recovery information without sending activation. Preserve the .maFile and check Steam state.
- No release published from these changes.

## Verification

- Core tests cover serialized checkpoint restart, failed write-ahead persistence, original device identity, and device-action approval binding.
- Avalonia Desktop builds successfully. No live Steam enrollment or visual UI verification performed.
- A pre-existing CS0649 warning in SteamAuth SessionData appeared when rebuilding that library.
