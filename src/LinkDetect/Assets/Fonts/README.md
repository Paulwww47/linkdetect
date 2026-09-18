# Maple Mono NF CN

Unmodified static TrueType fonts from the official **Maple Mono NF CN v7.9**
release. These retain the requested NF CN variant, including its rounded CJK
glyphs and Nerd Font symbols. They require no font installation or network
connection at runtime.

| File | OpenType weight | Bytes |
| --- | --- | ---: |
| `MapleMono-NF-CN-Regular.ttf` | Regular / 400 | 20,649,812 |
| `MapleMono-NF-CN-Medium.ttf` | Medium / 500 | 20,640,072 |

The typographic family is **Maple Mono NF CN** for both files. Use the WPF
resource family `pack://application:,,,/Assets/Fonts/#Maple Mono NF CN` with
`Normal` or `Medium` weight. Medium's legacy family is `Maple Mono NF CN Medium`.
The fonts are monospaced: Latin advances are 600 units and CJK advances are
1,200 units at 1,000 units per em.

## Exact source

- Downloaded 2026-09-15 from the official project
  [subframe7536/maple-font](https://github.com/subframe7536/maple-font).
- [Release v7.9](https://github.com/subframe7536/maple-font/releases/tag/v7.9),
  tag commit `4d847729cbfc5b5d8cbae5795fd2348c70112648`.
- [Original MapleMono-NF-CN.zip](https://github.com/subframe7536/maple-font/releases/download/v7.9/MapleMono-NF-CN.zip),
  159,498,447 bytes. Its SHA-256 matches the checksum published on the release
  asset page. This is the default hinted NF CN archive, not Normal, NL,
  unhinted, or Variable.
- Font version strings: `Version 7.900`. The bundled upstream configuration
  identifies Nerd Fonts 3.4.0 and keeps the default ligatures. The official
  project documents the CN glyph source as
  [Resource Han Rounded](https://github.com/CyanoHao/Resource-Han-Rounded).
- Both font files and `LICENSE.txt` were copied byte-for-byte from the archive.
  No conversion, renaming, subsetting, or glyph modification was performed.

## Validation

Each font has 33,091 Unicode mappings and 33,637 glyphs, including 20,976 CJK
Unified Ideograph mappings and 10,379 private-use mappings. Simplified Chinese,
Traditional Chinese, Japanese, Latin, and representative Nerd Font symbols
were checked. No characters from the release files were removed.

fontTools 4.65.0 loaded all tables with checksum validation. Both fonts are
static, contain TrueType hint tables, and have OS/2 embedding restrictions set
to zero. Windows WPF resolved the shared family to the requested physical
400/500 files without simulated bold or italic.

## License

Copyright 2022 The Maple Mono Project Authors. The original release contains
the **SIL Open Font License 1.1** in `LICENSE.txt`; that notice and license are
included unchanged and must accompany redistributed copies. The font files'
embedded copyright and license records match that license. The license permits
bundling and embedding with applications; the fonts must not be sold on their
own or relicensed under the application's license.

## SHA-256

| File | SHA-256 |
| --- | --- |
| Original `MapleMono-NF-CN.zip` | `af913b6322905348b3f50e4397fedc35b3a880db5effcce7969003051dcd3e94` |
| `MapleMono-NF-CN-Regular.ttf` | `bb8e8e8c263896f42555107202f1847f7a42c340a3e532df9c4d585c9794411c` |
| `MapleMono-NF-CN-Medium.ttf` | `6ffdd917c83f397ea021c96f23a54f23f5da835d75f8ddf77f8252ccb35d914f` |
| `LICENSE.txt` | `eb2d28d2e565a0757e3d64e34ebb452e75a0cad87c0ab3faf4e08ba7596de902` |
