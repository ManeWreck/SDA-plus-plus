using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Cloud;

public static class CloudCredentialDocument
{
    public static CloudFileDownloadResult ValidateDownload(string? contents)
    {
        if (contents is null) return new(false, "credentials.secure.json was not found in the selected cloud folder.");
        if (string.IsNullOrWhiteSpace(contents) || contents.Length > 2 * 1024 * 1024)
            return new(false, "The encrypted credentials file is empty or too large.");
        try
        {
            JObject document = JObject.Parse(contents, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (document["encrypted"]?.Type != JTokenType.Boolean || document.Value<bool>("encrypted") != true ||
                document["payload"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(document.Value<string>("payload")))
                return new(false, "Only encrypted credentials files can be downloaded for import.");
            // Authentication/decryption and version checks happen in the vault importer before any replacement.
            return new(true, "Encrypted credentials downloaded. Enter their original encryption key.", contents);
        }
        catch (JsonException)
        {
            return new(false, "The cloud credentials document is invalid.");
        }
    }
}
