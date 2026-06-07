using System.Collections.Generic;
using System.Threading.Tasks;

namespace Steam_Desktop_Authenticator
{
    internal interface ICloudStorageProvider
    {
        Task<string> TestConnectionAsync();
        Task EnsureContainerAsync();
        Task<string> DownloadTextAsync(string fileName, bool optional = false);
        Task UploadTextAsync(string fileName, string content);
    }

    internal interface ICloudStorageFileListProvider
    {
        Task<IReadOnlyCollection<string>> ListFileNamesAsync(string extensionFilter = null);
    }
}
