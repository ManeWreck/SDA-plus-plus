using SDAPlusPlus.Core.Credentials;

namespace SDAPlusPlus.Desktop.ViewModels;

public sealed class CredentialItemViewModel
{
    public CredentialItemViewModel(StoredCredential credential)
    {
        SteamIdValue = credential.SteamId;
        AccountName = credential.AccountName;
        Username = credential.Username;
        Updated = credential.UpdatedUtc.ToLocalTime().ToString("g");
    }

    public ulong SteamIdValue { get; }
    public string AccountName { get; }
    public string Username { get; }
    public string SteamId => SteamIdValue == 0 ? "-" : SteamIdValue.ToString();
    public string Updated { get; }
    public string PasswordMask => "••••••••••••";

    public StoredCredential CreateIdentity() => new(SteamIdValue, AccountName, Username, string.Empty, default);
}
