# Third-party notices

## FFmpeg

DiffVideo invokes the separate `ffmpeg.exe` and `ffprobe.exe` programs. It does
not link against FFmpeg libraries.

The portable package uses the LGPL build of FFmpeg from BtbN/FFmpeg-Builds:

- FFmpeg revision: `n9.0.1-11-ge47273f4d9` (build date 2026-09-02)
- Build release: `autobuild-2026-09-02-13-13`
- Archive: `ffmpeg-n9.0.1-11-ge47273f4d9-win64-lgpl-9.0.zip`
- SHA-256: `14CE996102BCACCDC8DE62E404DD96C9E6EB4C7AE28A25EB3537817F1E4D60FD`
- Binary source: https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-09-02-13-13
- FFmpeg source revision: https://github.com/FFmpeg/FFmpeg/commit/e47273f4d9
- Build scripts: https://github.com/BtbN/FFmpeg-Builds

The selected build disables `libx264` and `libx265` and is published as the
LGPL variant. Its complete license text is distributed in
`licenses/FFmpeg-LICENSE.txt` in the portable package.

FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.
This notice is informational and is not legal advice.

## NAudio

Timeline audio preview uses `NAudio.WinMM` 3.0.1 and its transitive NAudio
components. NAudio is Copyright 2008-2026 Mark Heath and is distributed under
the MIT License. The complete license is included at
`licenses/NAudio-LICENSE.txt`.

- Package: https://www.nuget.org/packages/NAudio.WinMM/3.0.1
- Source revision: https://github.com/naudio/NAudio/commit/0f6b856dd16396fe0fa5bbe58d140b47b048306f

## Google Material Icons

The UI embeds a subset of Google's Material Icons Round (24px) as WPF vector
geometries. These icons are distributed under the Apache License, Version 2.0.
The complete license is included in `licenses/MaterialIcons-LICENSE.txt`.

- Source: https://github.com/google/material-design-icons
- Pinned revision: `84ccef280841abfac506afc4ad4a2782f6d0a1d0`
- Original SVGs and per-icon source URLs: `src/DiffVideo.App/Assets/MaterialIcons/`
- Adaptation: SVG path data is preserved; transparent bounding shapes are omitted,
  circles/polygons are represented by WPF geometries, and color is inherited from
  the containing control. The original 24×24 viewport is retained.

No icon font installation or network connection is required at runtime.

## D2Coding

The application embeds the D2Coding regular and bold TrueType fonts for consistent
Korean and Latin text rendering across Windows installations. D2Coding is
copyright NAVER Corporation and is distributed under the SIL Open Font License,
Version 1.1. The complete license is included in
`licenses/D2Coding-LICENSE.txt`.

- Version: `1.3.3-20260725`
- Source: https://github.com/naver/d2codingfont
