using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SDAPlusPlus.Core.Cloud;

internal sealed class ObjectStoreVaultProvider(Func<CloudConnectionSettings, ICloudObjectStore> factory) : ICloudVaultProvider
{
    private const int MaximumAccountCount = 500;
    private const int MaximumDocumentCharacters = 2 * 1024 * 1024;

    public async Task<CloudConnectionResult> TestConnectionAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            using ICloudObjectStore store = factory(settings);
            return new CloudConnectionResult(true, await store.TestAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Amazon.S3.AmazonS3Exception or TaskCanceledException)
        {
            return new CloudConnectionResult(false, ex.Message);
        }
    }

    public async Task<CloudPullPreview> PreviewPullAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            using ICloudObjectStore store = factory(settings);
            await store.TestAsync(cancellationToken);
            string? manifest = await store.DownloadAsync("manifest.json", optional: true, cancellationToken);
            if (!string.IsNullOrWhiteSpace(manifest))
                return ParseManifest(manifest);

            IReadOnlyList<string> files = (await store.ListAsync(cancellationToken))
                .Where(IsSafeMaFileName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            return files.Count == 0
                ? new CloudPullPreview(false, "The cloud folder is reachable, but no .maFile accounts were found.")
                : new CloudPullPreview(true, "Cloud account files found through provider listing.", files.Count, FileNames: files);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Amazon.S3.AmazonS3Exception or TaskCanceledException or JsonException)
        {
            return new CloudPullPreview(false, ex.Message);
        }
    }

    public async Task<CloudPullPackageResult> PreparePullAsync(CloudConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        CloudPullPreview preview = await PreviewPullAsync(settings, cancellationToken);
        if (!preview.Success) return new CloudPullPackageResult(false, preview.Message);
        if ((preview.FileNames?.Count ?? 0) > MaximumAccountCount)
            return new CloudPullPackageResult(false, $"Cloud backup contains more than {MaximumAccountCount} accounts.");

        try
        {
            using ICloudObjectStore store = factory(settings);
            string? manifest = preview.ManifestFound
                ? await store.DownloadAsync("manifest.json", optional: false, cancellationToken)
                : null;
            string manifestJson = manifest ?? CreateFallbackManifest(preview.FileNames ?? []);
            Dictionary<string, string> accounts = new(StringComparer.OrdinalIgnoreCase);
            foreach (string fileName in preview.FileNames ?? [])
            {
                if (!IsSafeMaFileName(fileName))
                    return new CloudPullPackageResult(false, "Cloud backup contains an unsafe account filename.");
                string contents = await store.DownloadAsync(fileName, optional: false, cancellationToken) ?? string.Empty;
                if (contents.Length == 0 || contents.Length > MaximumDocumentCharacters)
                    return new CloudPullPackageResult(false, fileName + " is empty or exceeds the allowed size.");
                accounts[fileName] = contents;
            }
            return new CloudPullPackageResult(true, "Cloud backup downloaded and validated in memory.",
                new CloudPullPackage(manifestJson, accounts, preview.IsEncrypted));
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Amazon.S3.AmazonS3Exception or TaskCanceledException)
        {
            return new CloudPullPackageResult(false, ex.Message);
        }
    }

    public async Task<CloudPushResult> PushAsync(CloudConnectionSettings settings, CloudPushPackage package, CancellationToken cancellationToken = default)
    {
        if (!package.IsEncrypted) return new CloudPushResult(false, "Cloud upload requires an encrypted vault.");
        try
        {
            using ICloudObjectStore store = factory(settings);
            await store.EnsureContainerAsync(cancellationToken);
            foreach ((string name, string contents) in package.AccountFiles)
                await store.UploadAsync(name, contents, cancellationToken);
            await store.UploadAsync("manifest.json", package.ManifestJson, cancellationToken);
            int accountCount = package.AccountFiles.Keys.Count(IsSafeMaFileName);
            return new CloudPushResult(true, $"Encrypted vault uploaded. Accounts: {accountCount}.", accountCount);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Amazon.S3.AmazonS3Exception or TaskCanceledException)
        { return new CloudPushResult(false, ex.Message); }
    }

    private static CloudPullPreview ParseManifest(string json)
    {
        if (json.Length == 0 || json.Length > MaximumDocumentCharacters)
            return new CloudPullPreview(false, "Cloud manifest is empty or too large.");
        JObject manifest = JObject.Parse(json);
        string[] files = (manifest["entries"] as JArray ?? [])
            .Select(entry => entry?["filename"]?.Value<string>()).Where(IsSafeMaFileName).Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        return new CloudPullPreview(true, files.Length == 0 ? "Cloud manifest is valid but contains no accounts." : "Cloud manifest is valid.",
            files.Length, manifest.Value<bool?>("encrypted") ?? false, true, files);
    }

    private static string CreateFallbackManifest(IReadOnlyList<string> files) => new JObject
    {
        ["encrypted"] = false,
        ["first_run"] = false,
        ["entries"] = new JArray(files.Select(file => new JObject
        {
            ["filename"] = file,
            ["steamid"] = ulong.TryParse(Path.GetFileNameWithoutExtension(file), out ulong id) ? id : 0,
            ["favorite"] = false
        }))
    }.ToString(Formatting.Indented);

    private static bool IsSafeMaFileName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.EndsWith(".maFile", StringComparison.OrdinalIgnoreCase) && value == Path.GetFileName(value) &&
        !value.Contains("..", StringComparison.Ordinal);
}
