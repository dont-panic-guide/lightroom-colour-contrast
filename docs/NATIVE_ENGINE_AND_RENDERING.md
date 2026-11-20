<!-- doc-type: product-features -->
# Native Engine & Rendering (`VectorscopeRenderer.lua`, `Program.cs`)

## 1. Lightroom Export & Cache Bridge ([VectorscopeRenderer.lua](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/DaftVectorscope.lrplugin/VectorscopeRenderer.lua))
- **Temp Directory**: `%TEMP%\DaftVectorscope\` (exports isolated in `%TEMP%\DaftVectorscope\export\`).
- **Two-Stage Photo Extraction (`exportAndRender`)**:
  1. Primary: `LrExportSession` exports a `1024 px` max-dimension sRGB 8-bit JPEG (`quality = 0.75`) with Develop edits applied.
  2. Fallback: `photo:requestJpegThumbnail(1024, 1024, ...)` (up to 5 s wait) if `LrExportSession` fails (e.g., offline original).
  3. Copies result to `thumb_input.jpg`, invokes `bin\vectorscope_engine.exe` with `--stats-output`, and parses `band_sat_1..8`.
- **Cached Re-Render (`renderCached`)**: Runs `vectorscope_engine.exe` directly against `thumb_input.jpg` without re-exporting or re-measuring stats (`~2.1 ms` average engine render latency).
- **File Versioning & Cleanup**: Uses a monotonic `renderCounter` (`scope_<id>.png`, `run_scope_<id>.bat`, `engine_log_<id>.txt`, `stats_<id>.txt`) to avoid `f:picture` path caching and Windows file locks; `cleanupOldOutputs` deletes stale artifacts.

## 2. Native C# Engine ([Program.cs](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/vectorscope_engine/Program.cs))
Compiled as a standalone .NET WinExe (`vectorscope_engine.exe`) using `System.Drawing` with unsafe `LockBits` pointer access.

### CLI Options (`VectorscopeCore.ParseArgs`)
| Flag | Default | Description |
|---|---|---|
| `--input`, `-i` | *(required)* | Input JPEG/PNG path |
| `--output`, `-o` | *(required)* | Output vectorscope PNG path |
| `--size`, `-s` | `512` | Square output dimension (`128–2048`) |
| `--gain`, `-g` | `1.0` | Chroma gain (`0.1–10.0`) |
| `--intensity` | `2.5` | Trace density factor (`0.1–10.0`) |
| `--skinline` | `true` | Draw ~132° Skin Tone Line |
| `--targets` | `true` / `""` | `true`/`false` for Rec.709 boxes, or CSV `hue:sat` pairs (e.g., `132:50,312:50`) |
| `--rings` | `true` | Draw 20/40/60/75/100% saturation rings |
| `--axes` | `true` | Draw `Cb`/`Cr` crosshairs and ticks |
| `--stats-output` | `null` | Write `band_sat_1..8` baseline saturations |
| `--strength` | `0.0` | Simulation strength (`0–100`) |
| `--density` | `0.0` | Simulation luminance density (`0–100`) |
| `--bandwidth` | `40.0` | Simulation tolerance (`10–90`) |
| `--pin-bw` | `100.0` | Simulation black/white endpoint pinning (`0–100`) |
| `--band-sat` | `null` | Optional 8-float CSV overriding measured band saturations |

### Vectorscope Rendering (`VectorscopeCore.RenderVectorscope`)
- Precomputes 256-entry Rec.709 LUTs (`Cb_R/G/B`, `Cr_R/G/B`):
  - `Cb = -0.1146 R - 0.3854 G + 0.5000 B`
  - `Cr =  0.5000 R - 0.4542 G - 0.0458 B`
- Bilinearly splats sampled pixels into a float `[R, G, B, Weight]` buffer using dynamic stride (`1`, `2`, or `3` based on pixel count) and tone-maps density via `1 - exp(-w * densityFactor)` over `#121418`.
- `DrawGraticule` overlays concentric rings (`maxR = 0.596`), axes, ~132° Skin Tone Line, 75%/100% Rec.709 boxes (`R, Mg, B, Cy, G, Yl`), and target `(Hue, Sat)` markers (`T1`, `T2`, `T3`).

### Pixel Simulation (`ColorSeparationSimulator`)
Used for CLI simulation (`--strength > 0`) and automated engine/Lua parity testing (`CalculateAdjustments`, `CalculateToneCurves`, `BuildCurveByteLut`, `Apply`). Preserves pure neutral pixels (`chromaSq < 0.000004`) and applies HSL saturation/luminance followed by `rLut`, `gLut`, `bLut` curve mapping.
