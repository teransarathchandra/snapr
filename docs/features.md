# Features

[Capture](#capture) · [Edit](#edit) · [Video](#video) · [Library and files](#library-and-files) · [Shortcuts](#shortcuts) · [Troubleshooting](#troubleshooting)

## Capture

- **Region, window or every screen**, with a magnifier, size readout and optional delay. While selecting: drag for a region, click a window, <kbd>Space</kbd> for all screens, <kbd>C</kbd> to copy without opening the editor, <kbd>T</kbd> to grab text, <kbd>Esc</kbd> to cancel.
- **Scrolling capture** of long pages.
- **Panoramic capture**: choose an area, then scroll, drag or pan the content in any direction; Snapr stitches it into one image (tray menu or <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Shift</kbd>+<kbd>Print Screen</kbd>).
- **Grab Text** (OCR) from any part of the screen, using the OCR engine built into Windows.
- **Capture presets**: save a target (area, screen under the mouse, all screens or a fixed area), delay, action (edit, copy, grab text, save to a folder) and an optional hotkey. Run them from Settings, the tray or the hotkey.

## Edit

- **Annotate** with arrows, text, callouts, shapes, pen, highlighter, numbered steps, blur or pixelate, magnifier, crop and eraser. Every object stays editable; undo goes back 100 steps.
- **Several objects**: select with a box, <kbd>Shift</kbd>+click or <kbd>Ctrl</kbd>+<kbd>A</kbd>; move, nudge, copy, delete, group (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>G</kbd>), align and distribute. Paste or drop pictures onto a capture as movable images.
- **Combine images**: <kbd>Ctrl</kbd>+click two or more captures in the recents strip, then **Combine** to place them side by side, stacked or in a grid.
- **Templates**: step by step with numbered badges, before / after, a grid with captions, or one picture with a title and description.
- **Cut out** a horizontal or vertical strip with a straight or torn join (<kbd>U</kbd>).
- **Select pixels** (<kbd>R</kbd>): move, clear, copy, cut or crop to a rectangle, or **Cover selection** to hide text Snapr can't read.
- **Spotlight** (<kbd>L</kbd>): dim and optionally blur everything outside a rectangle or ellipse.
- **Stamps** (<kbd>G</kbd>): 25 built-in recolourable stamps plus your own PNGs.
- **Edit the image**: rotate, flip, resize, add margins (Image ▾) and fill areas with colour (<kbd>F</kbd>). The original pixels are always kept, and **Revert to original image** brings them back.
- **Redact** emails, web addresses, card numbers, phone numbers or words you choose, with solid blocks or pixelation. Covers are applied when you export, copy or print; the `.snapr` file keeps the original, so share an exported image.
- **Simplify** (Image ▾): turn text into rounded bars and UI blocks into flat boxes, as one editable group, optionally over a plain background.
- **Effects**: border, drop shadow and torn edge.
- **Open Snagit captures**: `.snagx` files open as editable Snapr captures. The `.snagx` is never changed; anything Snapr approximates is listed in a banner.
- **Export** to PNG, JPG, GIF, BMP or PDF, and **print** fitted to the page or at actual size.
- **Share** to a share folder (link or path copied), your own upload endpoint (link copied), or the Windows Share dialog. Set it up in Settings → Sharing.

## Video

- **Record** a region to MP4 with system audio and microphone. Needs [ffmpeg](https://ffmpeg.org/download.html).
- **Webcam bubble**: turn on "Show webcam while recording" in Settings; drag it anywhere inside the area, or hide it from the recording bar.
- **Trim and make GIFs**: trim the start and end, remove parts from the middle, and save a new MP4 or an animated GIF.
- **Annotate videos**: pause, click **Annotate** and draw on the frame. Each annotation shows for 3 s; drag its bar on the Objects track to retime it. Edits are kept next to the video (`clip.mp4.snapr-edit.json`) and drawn into the saved MP4 or GIF.

## Library and files

**Library** (toolbar, tray or <kbd>Ctrl</kbd>+<kbd>L</kbd>) shows every capture and video by month, with search (names, window titles, tags), kind and tag filters.

| What | Where |
|---|---|
| Captures | `Pictures\Snapr\yyyy-MM\*.snapr` |
| Videos | `Videos\Snapr\*.mp4` |
| Settings | `%APPDATA%\Snapr\settings.json` |
| Logs | `%APPDATA%\Snapr\logs\` |

A `.snapr` file is a ZIP with `document.json`, `base.png` (the original pixels) and `thumbnail.png`. Open one from the command line with `Snapr.exe --open file.snapr` (works for `.snagx` too).

## Shortcuts

| Hotkey | Action |
|---|---|
| <kbd>Print Screen</kbd> | Capture image |
| <kbd>Shift</kbd>+<kbd>Print Screen</kbd> | Record video (press again to stop) |
| <kbd>Ctrl</kbd>+<kbd>Print Screen</kbd> | Scrolling capture |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>Print Screen</kbd> | Repeat last capture |
| <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>Print Screen</kbd> | Start, pause or resume a recording |

**Editor:** <kbd>Ctrl</kbd>+<kbd>Z</kbd> undo · <kbd>Ctrl</kbd>+<kbd>Y</kbd> redo · <kbd>Ctrl</kbd>+<kbd>C</kbd> / <kbd>X</kbd> / <kbd>V</kbd> copy, cut, paste · <kbd>Delete</kbd> delete · <kbd>Ctrl</kbd>+<kbd>D</kbd> deselect · <kbd>Ctrl</kbd>+<kbd>S</kbd> save · <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> save as image · <kbd>Ctrl</kbd>+<kbd>O</kbd> open · <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>N</kbd> new from clipboard · <kbd>Ctrl</kbd>+<kbd>W</kbd> close · <kbd>Ctrl</kbd>+<kbd>P</kbd> print · <kbd>Ctrl</kbd>+<kbd>=</kbd> / <kbd>−</kbd> zoom · <kbd>Ctrl</kbd>+<kbd>0</kbd> fit

**Tools:** <kbd>V</kbd> select · <kbd>A</kbd> arrow · <kbd>T</kbd> text · <kbd>K</kbd> callout · <kbd>S</kbd> shape · <kbd>P</kbd> pen · <kbd>H</kbd> highlighter · <kbd>N</kbd> step · <kbd>B</kbd> blur · <kbd>M</kbd> magnify · <kbd>C</kbd> crop · <kbd>E</kbd> eraser · <kbd>R</kbd> selection · <kbd>U</kbd> cut out · <kbd>L</kbd> spotlight · <kbd>G</kbd> stamp · <kbd>F</kbd> fill

## Troubleshooting

- **Print Screen opens the Windows Snipping Tool.** Turn off *Settings → Accessibility → Keyboard → Use the Print screen key to open screen capture*, or pick another hotkey in Snapr's Settings.
- **Captures land in `%LOCALAPPDATA%\Snapr`.** Windows Security's Controlled Folder Access blocked Pictures or Videos, so Snapr saved to `%LOCALAPPDATA%\Snapr\Captures` and `%LOCALAPPDATA%\Snapr\Videos` instead.
- **Grab Text or Redact finds nothing.** Add a Windows language that includes optical character recognition (most display languages do).
- **Recording does nothing.** Install [ffmpeg](https://ffmpeg.org/download.html) and put it on your `PATH`, or set its location in Settings.
