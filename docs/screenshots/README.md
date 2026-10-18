# Screenshot provenance

These PNGs render the real MainWindow and MainViewModel using Avalonia Headless with Skia, not image mockups.

The standalone tool injects fail-closed in-memory services. It does not instantiate the normal application view model, load user preferences, read account files, open Windows identity keys, register hotkeys, scan the screen, or access Steam/cloud services. Names, IDs, counters and profiles are fictional. Guard codes use a public all-zero demonstration secret belonging to no account.

Reproduce from the repository root:

```powershell
dotnet run --project tools/SDAPlusPlus.Screenshots -c Release
```

Screenshots show the client area without OS decorations, including English pages and Russian wide/compact layouts. They demonstrate appearance, not successful live cloud/Steam operations.
