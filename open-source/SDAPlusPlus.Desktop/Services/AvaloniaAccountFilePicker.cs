using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace SDAPlusPlus.Desktop.Services;

public sealed class AvaloniaAccountFilePicker : IAccountFilePicker
{
    public async Task<string?> PickVaultFolderAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null) return null;
        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Import existing SDA / SDA++ folder", AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    public async Task<string?> PickMaFileAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import Steam Guard .maFile",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Steam Guard account")
                    {
                        Patterns = ["*.maFile"]
                    }
                ]
            });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickCredentialsFileAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import encrypted SDA++ credentials",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("SDA++ encrypted credentials")
                    {
                        Patterns = ["credentials.secure.json", "*.secure.json"]
                    }
                ]
            });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickPlainCredentialsFileAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.StorageProvider is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import login:password or JSON credentials",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Supported credential files")
                    {
                        Patterns = ["*.txt", "*.list", "*.json"]
                    },
                    new FilePickerFileType("login:password list")
                    {
                        Patterns = ["*.txt", "*.list"]
                    },
                    new FilePickerFileType("JSON credentials")
                    {
                        Patterns = ["*.json"]
                    }
                ]
            });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
