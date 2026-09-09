using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Qr;

public interface ISteamQrApprovalService
{
    Task<SteamQrApprovalResult> ApproveAsync(
        VaultAccount? account,
        string qrText,
        CancellationToken cancellationToken = default);
}
