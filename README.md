<p align="left">
  <a href="README.md"><img src="https://img.shields.io/badge/English-1D1D1F?style=for-the-badge" alt="English" /></a>
  <a href="README.zh-CN.md"><img src="https://img.shields.io/badge/中文-E8E8E8?style=for-the-badge&labelColor=E8E8E8&color=6E6E73" alt="中文" /></a>
</p>

<img width="1355" height="858" alt="FolderSail" src="https://github.com/user-attachments/assets/e9b74122-0c73-4aa5-aad1-8dbd468a1f9f" />

# FolderSail

A lightweight multi-pane file manager for Windows. The UI is close to Finder: color tags, split views, and a unified top bar for everyday file work.

When you send it to someone else, they only need `FolderSail.exe`. **No .NET install, and no Ollama.**

## Features

- **Split views**: 1 / 2 / 4 / 6 pane layouts; each pane browses independently with its own tabs
- **Color tags**: red / orange / yellow / green / blue / purple / gray. Drop folders onto a tag to save them; right-click to rename, recolor, or clear
- **Disks sidebar**: drives such as C / D with capacity bars; click to open the drive **root**
- **Search**: substring match on file names (typing `youyu` finds items whose names contain youyu)
- **Natural-language search** (rule-based, no model), for example:  
  `help me find files named youyu that are Excel spreadsheets`  
  `folders with 原理图 in the name`  
  Keywords and types (Excel / Word / PDF / images / folders, etc.) are extracted, then searched
- Copy, cut, paste, delete to Recycle Bin, new folder, F2 rename
- Right-click uses the system Explorer context menu
- Drag between panes: copy by default, hold Shift to move

## Shipping to customers

1. Quit FolderSail if it is running
2. Publish a self-contained single file (about 70MB):

```powershell
dotnet publish src\FolderSail\FolderSail.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist\win-x64
```

3. Send `dist\win-x64\FolderSail.exe`

Customer PCs: **64-bit** Windows 10 / 11. The first launch can be slower (self-extract), which is expected.

## Development

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
dotnet restore
dotnet build
dotnet run --project src\FolderSail\FolderSail.csproj
```

## Layout

```
FolderSail.sln
src/FolderSail.Core/   file services, search, tags, and settings
src/FolderSail/        WPF UI (MVVM)
```

## Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+F / Ctrl+K | Focus the top search box |
| Ctrl+T / Ctrl+W | New / close tab |
| Ctrl+C / X / V | Copy / cut / paste |
| Delete | Delete to Recycle Bin |
| F2 | Rename |
| F5 | Refresh |

## License

Use and redistribution follow the license in this repository. If none is stated separately, it is for the project author and authorized customers only.
