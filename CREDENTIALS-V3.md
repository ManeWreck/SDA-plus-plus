# Credentials v3 (staged compatibility support)

JSON envelope: `version: 3`, `encrypted: true`, `algorithm: "aes-256-gcm"`, `iv`, `payload`.

- Key: exactly 32 raw random bytes, standard Base64 encoded for transport. This is NOT a human password; no password hashing fallback is allowed for v3.
- IV: a freshly generated 12-byte cryptographic nonce, standard Base64 encoded.
- Payload: standard Base64 of ciphertext followed by a 16-byte authentication tag.
- AAD: exact UTF-8 bytes `SDA++/credentials/v3`, no newline.
- Plaintext: existing JSON array of credential entries, maximum 4 MiB.
- Unsupported version/algorithm or failed authentication is rejected, with no CBC fallback.
- Optional outer timestamps and other extra fields are not authenticated and must not be used for authorization or conflict resolution. Account credentials must be read only from the authenticated payload.
- This format alone does not prevent replay of an older valid file.

Compatibility rollout: both Desktop and Mobile can read v3 and preserve v3 on subsequent writes. Existing v2 files still use v2 until explicit migration; automatic migration and default-v3 creation are NOT enabled. Older released clients cannot read v3. No production cloud files were converted.

Avalonia's Credentials page now offers an explicit upgrade action. Confirm that every device supports v3 first. The migration reads and authenticates a staged v3 file, checks that the source has not changed, then atomically replaces it while retaining the previous encrypted bytes in `credentials.secure.json.pre-v3-<id>.bak`. The key does not change. An already-v3 file is not rewritten. Cloud upload remains a separate user action.

Keep the backup and original key until all devices have been checked. To recover its entries without touching the upgraded file, use encrypted import into a separate vault. For exact file rollback, close all writers and restore the backup as `credentials.secure.json`; retain a copy of the current v3 file first. Changes made after the backup are not included in it.

Shared deterministic decryption vector (tests only, never encrypt with these fixed inputs in production):

Key: 32 zero bytes. IV: 12 zero bytes. Plaintext: `[]`.
Payload Base64: `lfqDNbw6Ustnux9+d98CZumx`.

Implementations use the platform [Microsoft AesGcm](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-8.0) and [Java Cipher GCM](https://docs.oracle.com/en/java/javase/22/docs/api/java.base/javax/crypto/Cipher.html) APIs. Key buffers are cleared after use; managed strings cannot be reliably zeroized.

This does not yet migrate password-encrypted .maFile storage or change its KDF.
