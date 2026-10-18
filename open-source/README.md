# SDA++ source

The default desktop is **SDAPlusPlus.Desktop (Avalonia)**.

| Directory | Purpose |
| --- | --- |
| SDAPlusPlus.Desktop | Avalonia UI and Windows integrations |
| SDAPlusPlus.Core | Vault, cloud, credentials, sessions and device logic |
| SDAPlusPlus.Core.Tests | Automated tests |
| lib/SteamAuth | Steam authentication library |
| Steam Desktop Authenticator | Legacy WinForms; not the default release |
| ../tools/SDAPlusPlus.Screenshots | Offline real-view rendering with fictional data |

Install the .NET 8 SDK. From the repository root:

```powershell
dotnet test open-source/SDAPlusPlus.Core.Tests/SDAPlusPlus.Core.Tests.csproj -c Release
dotnet run --project open-source/SDAPlusPlus.Desktop/SDAPlusPlus.Desktop.csproj -c Release
dotnet publish open-source/SDAPlusPlus.Desktop/SDAPlusPlus.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/avalonia -p:DebugType=None -p:DebugSymbols=false
```

Never add runtime account data to this repository. Legacy WinForms can still be built explicitly from its csproj.
