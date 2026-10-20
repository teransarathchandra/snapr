<p align="center">
  <img src="docs/assets/banner.svg" alt="Snapr: capture, annotate, redact and record your screen. Free, open source, one portable .exe for Windows." width="100%">
</p>

<p align="center">
  <a href="https://github.com/teransarathchandra/snapr/releases/latest"><img src="https://img.shields.io/github/v/release/teransarathchandra/snapr?label=release&color=2563eb" alt="Latest release"></a>
  <a href="https://github.com/teransarathchandra/snapr/actions/workflows/ci.yml"><img src="https://github.com/teransarathchandra/snapr/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white" alt="Windows 10 and 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT license"></a>
</p>

<p align="center">
  A free screen capture and annotation tool for Windows.<br>
  No installer, no account, no admin rights. Download one <code>.exe</code> and press <kbd>Print Screen</kbd>.
</p>

<p align="center">
  <a href="https://github.com/teransarathchandra/snapr/releases/latest/download/Snapr.exe"><img src="https://img.shields.io/badge/Download-Snapr.exe-2563EB?style=for-the-badge&logo=windows&logoColor=white" alt="Download Snapr.exe"></a>
  <a href="#build-from-source"><img src="https://img.shields.io/badge/Build-from_source-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt="Build from source"></a>
</p>

## See it

### 1 · Install in 10 seconds

Download one file and run it. No installer, no admin rights. Snapr waits in the tray.

<img src="docs/assets/install.gif" alt="PowerShell downloads Snapr.exe with curl, runs it, then Print Screen captures part of the screen and the editor opens" width="100%">

### 2 · Capture and annotate

Press <kbd>Print Screen</kbd>, drag an area, then add arrows, text and numbered steps. Everything stays editable.

<img src="docs/assets/annotate.gif" alt="A region is captured, then an arrow, a typed text label and two numbered steps are added" width="100%">

### 3 · Redact in one click

Snapr reads the capture and finds emails, phone numbers, card numbers and web addresses. Type any other word to hide it too.

<img src="docs/assets/redact.gif" alt="Redact lists four emails and four phone numbers, the word Sofia is typed, and all of them are covered with black blocks" width="100%">

### 4 · Simplify

Turn a busy screenshot into clean wireframe shapes, ready for docs and slides.

<img src="docs/assets/simplify.gif" alt="Simplify turns every line of text into a rounded bar on a plain background" width="100%">

## What's inside

|  |  |
|---|---|
| 📸 **Capture** | Region, window, all screens, scrolling pages and panoramas |
| ✏️ **Annotate** | Arrows, text, callouts, shapes, steps, stamps, blur, spotlight, magnifier |
| 🔒 **Redact** | Find and cover private text automatically, with Windows OCR |
| 🔤 **Grab Text** | Copy the text from any part of the screen |
| 🎬 **Record** | MP4 with system audio, microphone and a webcam bubble |
| ✂️ **Trim and GIF** | Cut videos, annotate them and save MP4 or animated GIF |
| 🗂️ **Library** | Every capture saved, searchable and taggable |
| 📤 **Export** | PNG, JPG, GIF, BMP, PDF or print. Opens Snagit `.snagx` files too |
| 🔗 **Share** | Copy to a share folder, upload to your own endpoint, or use Windows Share |

The full list is in [docs/features.md](docs/features.md).

## Get started

1. **[Download Snapr.exe](https://github.com/teransarathchandra/snapr/releases/latest/download/Snapr.exe)** and run it, or in PowerShell:
   ```powershell
   curl.exe -LO https://github.com/teransarathchandra/snapr/releases/latest/download/Snapr.exe
   .\Snapr.exe
   ```
   > [!NOTE]
   > Snapr.exe isn't code-signed yet, so on first run Windows SmartScreen may say "Windows protected your PC". Choose **More info → Run anyway**. You can check the download against `Snapr.exe.sha256` on the release page.
2. Snapr starts in the tray. Press <kbd>Print Screen</kbd> and drag an area.

**Needs:** Windows 10 1903+ or Windows 11 (x64). Video recording also needs [ffmpeg](https://ffmpeg.org/download.html) on your `PATH` (or set its location in Settings).

| Hotkey | Action |
|---|---|
| <kbd>Print Screen</kbd> | Capture image |
| <kbd>Shift</kbd> + <kbd>Print Screen</kbd> | Record video (press again to stop) |
| <kbd>Ctrl</kbd> + <kbd>Print Screen</kbd> | Scrolling capture |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>Print Screen</kbd> | Repeat last capture |

Every hotkey can be changed in Settings. Editor shortcuts and file locations: [docs/features.md](docs/features.md#shortcuts).

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet test
dotnet run --project src/Snapr.App
```

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md). Security reports: [SECURITY.md](SECURITY.md).

Released under the [MIT licence](LICENSE). ffmpeg is not bundled with Snapr.
