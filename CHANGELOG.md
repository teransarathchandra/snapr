# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-20

### Fixed

- Recording falls back to the default microphone when the chosen one is gone, and the recording bar shows "Default mic".
- The webcam opens during the countdown, so recording starts right at 0, and the bubble stays until the screen grab stops.
- Video annotations: a separate Text size slider for text, callouts and steps.
- Sharing: header names with spaces are refused; an empty header name drops the saved value; old share temp folders are cleaned up at startup.
- The Library scans in the background, so large folders no longer freeze the window.
- Tags cut at 40 characters never split an emoji or accented letter.
- Template starting texts that don't fit are shortened with "…" inside the canvas.
- Windows get the dark title bar before they first appear, so they no longer flash light.
- Simplify says so when the view changed while it was working.
- A blank first panorama frame no longer causes the frames after it to be dropped.

### Changed

- README: a note on Windows SmartScreen for the unsigned download.
- CI uses actions/checkout v7 and actions/setup-dotnet v6.

## [0.1.0] - 2026-10-20

First public release.

- Capture a region, window, all screens, scrolling pages and panoramas.
- Annotate with arrows, text, callouts, shapes, steps, stamps, blur, spotlight and more; every object stays editable.
- Redact emails, phone numbers, card numbers and web addresses found with Windows OCR.
- Simplify, templates, combine images, cut out and pixel selection.
- Record MP4 with audio and a webcam bubble; trim, annotate and save as MP4 or GIF.
- Library with search and tags; export to PNG, JPG, GIF, BMP and PDF; open Snagit `.snagx` files.
- Share to a share folder, your own upload endpoint or the Windows Share dialog, with the link copied.

[0.2.0]: https://github.com/teransarathchandra/snapr/releases/tag/v0.2.0
[0.1.0]: https://github.com/teransarathchandra/snapr/releases/tag/v0.1.0
