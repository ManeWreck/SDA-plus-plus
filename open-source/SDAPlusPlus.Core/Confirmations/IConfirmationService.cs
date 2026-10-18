using SDAPlusPlus.Core.Vault;

namespace SDAPlusPlus.Core.Confirmations;

public interface IConfirmationService
{
    Task<ConfirmationLoadResult> LoadAsync(
        IEnumerable<VaultAccount> accounts,
        CancellationToken cancellationToken = default,
        string? preferredSteamId = null,
        bool forceRefresh = false);

    Task<ConfirmationActionResult> ApplyAsync(
        AccountConfirmation confirmation,
        ConfirmationDecision decision,
        CancellationToken cancellationToken = default);

    Task<ConfirmationDetailsResult> LoadDetailsAsync(
        AccountConfirmation confirmation,
        CancellationToken cancellationToken = default);
}
