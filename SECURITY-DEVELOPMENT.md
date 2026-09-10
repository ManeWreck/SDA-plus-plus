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
- Invitation creation requires an active Admin identity. Invitation parsing rejects duplicate parameters, unsupported protocol versions, ambiguous relay URLs, invalid 32-byte Base64URL tokens and oversized QR payloads before dispatch.
- UI distinguishes server acceptance from confirmed device execution; revocation explicitly does not promise deletion of copied secrets or termination of Steam sessions.

## Not completed or verified

- No live enrollment verification or visual QA on the new wizard yet.
- Password-encrypted .maFile storage still uses the legacy format; its migration and KDF upgrade remain pending.
- Credentials v3 support is implemented in both platforms with shared test vectors. Avalonia offers explicit v2-to-v3 migration with a retained encrypted backup, staged read-back verification and a compatibility acknowledgment. Default creation remains v2 until the rollout is ready.
- Trusted-device completion remains pending.
- Core now has a command synchronization coordinator joining the signed client to the journal/processor. Tests exercise lost acknowledgments across processor restart, identity changes after fetch, expired commands and unknown outcomes. Failed acknowledgments leave the result incomplete, and redelivery retries acknowledgment without repeating execution. This coordinator is not enabled for live side effects in Avalonia.
- The checked-in Mobile pairing-relay source is the earlier pairing-only implementation. The deployed Worker does contain the registry; its bundle was recovered separately for inspection. That unfinished recovery work is excluded from this release. No deployed Worker was changed.
- A local command processor and Windows DPAPI journal are implemented and tested: write-ahead Started receipts, duplicate suppression, expiry/target checks, and OutcomeUnknown after interruption. A single processor must own the journal directory. Missing/deleted receipts are not rollback-protected; an authenticated server command lifecycle is still required.
- This processor is not wired to Avalonia polling or destructive actions yet. Server acknowledgments/status UI, Mobile enforcement, and keeping journal/identity outside WipeLocalData scope remain integration work. No real device commands were executed.
- Avalonia now offers manual read-only incoming-command queue inspection. Core supports signed GET /commands and POST /commands/{id}/ack using the legacy Completed/Failed contract; Started, Expired and OutcomeUnknown cannot be mislabeled as final legacy acknowledgments. Malformed lists fail closed. Automatic execution/acknowledgment and expanded server status support remain disabled/pending; these endpoints have only been tested with synthetic HTTP responses.
- Automatic reconciliation of unknown Steam outcomes remains pending. Resume is supported for saved prepared checkpoints only; submitted/legacy/unknown checkpoints show recovery information without sending activation. Preserve the .maFile and check Steam state.
- No release published from these changes.

## Verification

## Desktop update checks

- Avalonia checks public GitHub release metadata once on startup by default; this can be disabled in Settings and saved with the existing Save button.
- Settings also provides a manual check and a release-page button. A sidebar button appears when an update is available.
- Stable builds exclude prereleases; experimental builds include them. Only newer versions with the expected Windows x64 Desktop ZIP are offered. Versions at or below 1.7.0 are excluded to avoid the legacy WinForms line.
- Checks are limited to one attempt per five minutes in the current process, with a 15-second timeout. The first 100 releases are inspected. Failures do not block vault use.
- Only public release metadata is requested, without GitHub authentication or vault/account data. GitHub still receives normal network metadata such as the IP address.
- Release links are constructed for the fixed repository, not trusted from response URLs. API contract: https://docs.github.com/en/rest/releases/releases
- Installation remains manual. No automatic executable replacement, signed update manifest, rollback helper, or Mobile updater is implemented in this step.
- Automated selection tests cover channel filtering, version ordering, draft exclusion, missing packages and safe release links. No visual UI or end-to-end installation testing performed.

- Core tests cover serialized checkpoint restart, failed write-ahead persistence, original device identity, and device-action approval binding.
- Avalonia Desktop builds successfully. No live Steam enrollment or visual UI verification performed.
- A pre-existing CS0649 warning in SteamAuth SessionData appeared when rebuilding that library.
