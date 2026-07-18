# Bundled PDF fonts

These fonts make Unicode PDF export deterministic on Windows and macOS; the writer never consults installed system fonts.

## Pathstitch CJK sans

- Runtime asset: `NotoSansCJKsc-Regular-Canonical.otf`
- Upstream: `notofonts/noto-cjk`, commit `f8d157532fbfaeda587e826d4cd5b21a49186f7c`, `Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf`
- Upstream SHA-256: `2C76254F6FC379FDDFCE0A7E84FB5385BB135D3E399294F6EEB6680D0365B74B`
- Runtime SHA-256: `1845917783692E590A79A6EBA8688F7B0EE07E6C644FD2453E5933CEF52B6B28`
- License: `OFL-NotoSansSC.txt` (SIL Open Font License 1.1)

The runtime asset was generated with fonttools 4.59.2 by removing 1,390 Unicode cmap aliases in the CJK Radicals Supplement/Kangxi Radicals (`U+2E80-U+2FDF`) and CJK Compatibility Ideographs (`U+F900-U+FAFF`). Canonical CJK mappings and glyph outlines are unchanged. This prevents PDF glyph reverse-mapping from replacing source characters such as `文`, `二`, and `行` with compatibility code points in ToUnicode maps.

## Noto Emoji

- Runtime asset: `NotoEmoji-Variable.ttf`
- Upstream: `google/fonts`, commit `389b770410cc0b7c21c85673bfa2077420fe7f65`, `ofl/notoemoji/NotoEmoji[wght].ttf`
- Runtime SHA-256: `DE6C18832938AFC99CAF132B39D6A30A19BAC7F2E812E28DB2535B4608D27551`
- License: `OFL-NotoEmoji.txt` (SIL Open Font License 1.1)