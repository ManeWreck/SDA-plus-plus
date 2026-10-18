using SDAPlusPlus.Core.Vault;
using SteamAuth;

namespace SDAPlusPlus.Core.Confirmations;

public sealed class AccountConfirmation
{
    internal AccountConfirmation(VaultAccount account, Confirmation confirmation)
    {
        Account = account;
        Confirmation = confirmation;
        AccountName = account.Name;
        SteamId = account.SteamId;
        Headline = confirmation.Headline ?? string.Empty;
        Summary = confirmation.Summary?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        AcceptLabel = string.IsNullOrWhiteSpace(confirmation.Accept) ? "Accept" : confirmation.Accept;
        RejectLabel = string.IsNullOrWhiteSpace(confirmation.Cancel) ? "Reject" : confirmation.Cancel;
        Type = confirmation.ConfType;
        CreatorId = confirmation.Creator;
        SenderAvatarUrl = NormalizeUrl(confirmation.Icon);
    }

    internal VaultAccount Account { get; }
    internal Confirmation Confirmation { get; }
    public string AccountName { get; }
    public string SteamId { get; }
    public string Headline { get; }
    public IReadOnlyList<string> Summary { get; }
    public string AcceptLabel { get; }
    public string RejectLabel { get; }
    public Confirmation.EMobileConfirmationType Type { get; }
    public ulong CreatorId { get; }
    public ulong ConfirmationId => Confirmation.ID;
    public string? SenderAvatarUrl { get; }
    public string? ReceiverAvatarUrl { get; internal set; }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.StartsWith("//", StringComparison.Ordinal) ? "https:" + value : value;
    }
}
