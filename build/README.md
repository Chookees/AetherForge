# Aether Forge release publish

## Publish

From repo root (PowerShell):

```powershell
.\build\publish-release.ps1
```

Output lands in `release\`:

| File | Role |
|------|------|
| `Backend+API.exe` | Standalone ASP.NET Core API |
| `GUI.exe` | Avalonia desktop app (embeds local API) |

Full publish folders: `release\API\` and `release\GUI\`.

## Options

```powershell
.\build\publish-release.ps1 -Configuration Release -Runtime win-x64
.\build\publish-release.ps1 -SkipClean
```

Requires .NET 10 SDK. Builds are self-contained single-file for Windows x64.
