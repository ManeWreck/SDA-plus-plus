namespace SDAPlusPlus.Core.Devices;

public interface IPairingRelayService
{
    Task<PairingRelayHealthResult> CheckHealthAsync(string endpoint, CancellationToken cancellationToken = default);
}
