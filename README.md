# Xbox Steam Cover Art Fixer (WPF)

Replace the **Xbox App for PC**’s Steam game cache icons with proper **cover art** from **SteamGridDB**. Keeps filenames the same so art shows up in Xbox immediately.

> Close the Xbox app before replacing to avoid file locks.

---
<img width="1096" height="720" alt="image" src="https://github.com/user-attachments/assets/90a2ce57-9869-453b-951d-b0deecd7c5b4" />

## Features

* **Scan / Rescan** the Xbox Steam cache:
  `C:\Users\<you>\AppData\Local\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries\Steam`
* **Game titles** auto-resolved from `Steam-<AppID>.png` via SteamGridDB.
* **Download Cover Art**: pick from SteamGridDB icons, auto-convert to **PNG**, overwrite in place, create a one-time `.bak`.
* **Fast UI**: async thumbnail loading; picker buttons always visible.

<img width="1071" height="705" alt="image" src="https://github.com/user-attachments/assets/3e6f2d76-f91a-443d-af88-fb28742e3d5c" />
---
<img width="607" height="233" alt="image" src="https://github.com/user-attachments/assets/4fb10f05-ca16-4ee9-a844-7efaf3a67e9e" />

## Requirements

* Windows 10/11, .NET 8 SDK
* SteamGridDB API key (free)

---

## Setup

```bash
git clone (https://github.com/tetraguy/Xbox-PC-Library-Art.git)
cd xbox-steam-cover-art-fixer
dotnet build -c Release
```

API key (choose one):

* **Embedded**: set in `Services/Config.cs`
* **Env var**: `setx STEAMGRIDDB_API_KEY "YOUR_API_KEY"`

Project uses WPF + WinForms:

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
```

---

## Run & Use

1. Close the Xbox app.
2. `dotnet run --project XboxSteamCoverArtFixer`
3. **Scan Folder** → select an entry → **Download Cover Art** → pick icon → **Use Selected**.
4. Reopen Xbox to see the cover.

---

## Notes / Troubleshooting

* **Download failed**: the app uses a separate no-auth HTTP client for CDN; pull latest.
* **Blank titles**: names resolve async; they appear once SGDB returns.
* **Restore**: copy `Steam-XXXXX.png.bak` over the `.png` (Xbox closed).

---

## License & Credits

MIT. Uses the SteamGridDB API; all artwork © their owners. Not affiliated with Microsoft, Steam, or SteamGridDB.
