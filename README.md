# Snapr

Snapr is a free, open-source screen capture and annotation tool for Windows.
It is a single portable `.exe`: no installer and no admin rights.

## Features

- **Capture** a region, a window or every screen, with a magnifier, size readout and optional delay.
- **Annotate** with arrows, text, callouts, shapes, pen, highlighter, numbered steps, blur or pixelate, magnifier, crop and eraser. Every object stays editable; undo goes back 100 steps.
- **Work with several objects**: select with a box, Shift+click or Ctrl+A; move, nudge with the arrow keys, copy, delete, group (Ctrl+Shift+G), align and distribute them. Paste (Ctrl+V) or drop pictures onto a capture to add them as movable images.
- **Effects**: border, drop shadow and torn edge.
- **Grab Text** (OCR) from any part of the screen, using the OCR engine built into Windows.
- **Scrolling capture** of long pages.
- **Record video** of a region to MP4 with system audio and microphone.
- Every capture is saved automatically as a `.snapr` file and shown in the recents strip.
- **Open Snagit captures**: `.snagx` files open as editable Snapr captures (Open image…, drag onto the editor, or `Snapr.exe --open file.snagx`). The `.snagx` is never changed; anything Snapr has to approximate is listed in a banner.
- **Export** to PNG, JPG, GIF, BMP or PDF, and **print** fitted to the page or at actual size.

## Requirements

- Windows 10 version 1903 or later, or Windows 11 (x64).
- For video recording: [ffmpeg](https://ffmpeg.org/download.html) on your `PATH`, or its location set in Settings. Snapr does not ship ffmpeg.
- For Grab Text: at least one Windows language with optical character recognition (most display languages include it).

## Use

| Action | Default hotkey |
|---|---|
| Capture image | Print Screen |
| Record video (press again to stop) | Shift + Print Screen |
| Scrolling capture | Ctrl + Print Screen |
| Repeat last capture | Ctrl + Alt + Print Screen |
| Start, pause or resume a recording | Ctrl + Shift + Print Screen |

While selecting: drag for a region, click a window, Space for all screens, C to copy without opening the editor, T to grab text, Esc to cancel.

Editor shortcuts: Ctrl+Z / Alt+Backspace undo, Ctrl+Y redo, Ctrl+C copy (the selected object, or the whole image),
Ctrl+X cut, Ctrl+V paste (a Snapr object, or a clipboard picture as a new capture), Delete delete, Ctrl+D or Esc
deselect, Ctrl+S save, Ctrl+Shift+S save as image, Ctrl+O open, Ctrl+Shift+N new from clipboard, Ctrl+W close,
Ctrl+P print, Ctrl+= / Ctrl+− zoom, Ctrl+0 fit. Tool keys: V select, A arrow, T text, K callout, S shape, P pen,
H highlighter, N step, B blur, M magnify, C crop, E eraser.

If Windows reports Print Screen as in use, turn off Settings → Accessibility → Keyboard → "Use the Print screen key to open screen capture", or choose another hotkey in Snapr's Settings.

Files:

- Captures: `Pictures\Snapr\yyyy-MM\*.snapr`
- Videos: `Videos\Snapr\*.mp4`
- If Windows Security's Controlled Folder Access blocks Snapr from Pictures or Videos, Snapr saves to `%LOCALAPPDATA%\Snapr\Captures` and `%LOCALAPPDATA%\Snapr\Videos` instead.
- Settings: `%APPDATA%\Snapr\settings.json`
- Logs: `%APPDATA%\Snapr\logs\`

A `.snapr` file is a ZIP containing `document.json`, `base.png` (the original pixels) and `thumbnail.png`.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet test
dotnet run --project src/Snapr.App
dotnet publish src/Snapr.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Contributing

Snapr is written independently. Do not contribute code, images, icons, templates or text copied from any commercial product.

## Licence

MIT. See [LICENSE](LICENSE). NAudio is MIT-licensed. ffmpeg is not distributed with Snapr.
