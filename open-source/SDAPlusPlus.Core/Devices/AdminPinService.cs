using System.Security.Cryptography;
using System.Text;

namespace SDAPlusPlus.Core.Devices;

public enum AdminPinState
{
    Success,
    NotConfigured,
    InvalidFormat,
    WrongPin,
    Locked,
    Damaged
}

public sealed record AdminPinResult(AdminPinState State, int RetryAfterSeconds = 0);

public sealed class AdminPinService(IDeviceRegistryStore registryStore)
{
    public const int Iterations = 210_000;

    public AdminPinResult Configure(DeviceRegistrySettings settings, string pin, string confirmation)
    {
        if (!IsValidPin(pin) || !string.Equals(pin, confirmation, StringComparison.Ordinal))
            return new AdminPinResult(AdminPinState.InvalidFormat);

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Derive(pin, salt, Iterations);
        try
        {
            settings.AdminPinSalt = Convert.ToBase64String(salt);
            settings.AdminPinHash = Convert.ToBase64String(hash);
            settings.AdminPinIterations = Iterations;
            settings.AdminPinFailedAttempts = 0;
            settings.AdminPinLockedUntil = null;
            registryStore.Save(settings);
            return new AdminPinResult(AdminPinState.Success);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(hash);
        }
    }

    public AdminPinResult Verify(DeviceRegistrySettings settings, string pin)
    {
        if (!IsConfigured(settings))
            return new AdminPinResult(AdminPinState.NotConfigured);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (settings.AdminPinLockedUntil is long lockedUntil && lockedUntil > now)
            return new AdminPinResult(AdminPinState.Locked, (int)Math.Max(1, (lockedUntil - now + 999) / 1000));

        try
        {
            byte[] salt = Convert.FromBase64String(settings.AdminPinSalt);
            byte[] expected = Convert.FromBase64String(settings.AdminPinHash);
            byte[] candidate = Derive(pin, salt, settings.AdminPinIterations);
            try
            {
                if (CryptographicOperations.FixedTimeEquals(candidate, expected))
                {
                    settings.AdminPinFailedAttempts = 0;
                    settings.AdminPinLockedUntil = null;
                    registryStore.Save(settings);
                    return new AdminPinResult(AdminPinState.Success);
                }

                settings.AdminPinFailedAttempts++;
                if (settings.AdminPinFailedAttempts >= 5)
                {
                    settings.AdminPinFailedAttempts = 0;
                    settings.AdminPinLockedUntil = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds();
                }
                registryStore.Save(settings);
                return new AdminPinResult(AdminPinState.WrongPin);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(candidate);
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentOutOfRangeException)
        {
            return new AdminPinResult(AdminPinState.Damaged);
        }
    }

    public static bool IsConfigured(DeviceRegistrySettings settings) =>
        !string.IsNullOrWhiteSpace(settings.AdminPinSalt) &&
        !string.IsNullOrWhiteSpace(settings.AdminPinHash) &&
        settings.AdminPinIterations > 0;

    private static bool IsValidPin(string pin) =>
        pin.Length is >= 6 and <= 12 && pin.All(character => character is >= '0' and <= '9');

    private static byte[] Derive(string pin, byte[] salt, int iterations)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(pin);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(bytes, salt, iterations, HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
