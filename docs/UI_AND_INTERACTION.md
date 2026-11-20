<!-- doc-type: product-features -->
# UI & Interaction (`Info.lua`, `ShowVectorscope.lua`, `Help.lua`)

## 1. Plugin Manifest & Menu Entry Points
Defined in [Info.lua](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/DaftVectorscope.lrplugin/Info.lua):
- `LrSdkVersion = 11.0`, `LrToolkitIdentifier = 'com.daft.lightroom.vectorscope'`, `LrPluginName = 'Daft Vectorscope'`
- `LrExportMenuItems`: `"Open Vectorscope..."` -> `ShowVectorscope.lua` (`File > Plug-in Extras`)
- `LrLibraryMenuItems`: `"Open Vectorscope..."` -> `ShowVectorscope.lua` (`Library > Plug-in Extras`)
- `LrHelpMenuItems`: `"Vectorscope User Guide"` -> `Help.lua` (`Help > Plug-in Extras`)

## 2. Floating Dialog & Lifecycle ([ShowVectorscope.lua](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/DaftVectorscope.lrplugin/ShowVectorscope.lua))
- **Single-Instance Guard**: `_G.DaftVectorscopeDialogActive` prevents duplicate dialogs and is cleared via `context:addCleanupHandler`.
- **Dialog Configuration**: Non-resizable floating dialog (`LrDialogs.presentFloatingDialog`), frame persistence key `"DaftVectorscopeColorSepDialogPos"`.
- **Header Bar**: Displays `photoTitle` (`fileName`), `photoDetails` (`Resolution: <dimensions>`), and `statusText` (`Ready`, `Analyzing photo...`, `Updating...`, `Live`, or `Error: ...`).
- **Vectorscope Display**: `512×512` `f:picture` bound to `props.scopeImagePath` (defaults to `default_scope.png`).

## 3. Controls & Observable Properties
| Property | Control | Range | Default | Behavior |
|---|---|---|---|---|
| `targetHue1` | Slider + `°` readout | `0–359` | `132.0` | Target 1 hue angle (always active) |
| `targetSat1` | Slider + `%` readout | `0–100` | `50.0` | Target 1 saturation radius |
| `enableTarget2` | Checkbox | `bool` | `true` | Enables/disables Target 2 sliders |
| `targetHue2` | Slider + `°` readout | `0–359` | `312.0` | Target 2 hue angle |
| `targetSat2` | Slider + `%` readout | `0–100` | `50.0` | Target 2 saturation radius |
| `enableTarget3` | Checkbox | `bool` | `false` | Enables/disables Target 3 sliders |
| `targetHue3` | Slider + `°` readout | `0–359` | `220.0` | Target 3 hue angle |
| `targetSat3` | Slider + `%` readout | `0–100` | `50.0` | Target 3 saturation radius |
| `strength` | Slider + `%` readout | `0–100` | `0.0` | Master color separation intensity |
| `density` | Slider + `%` readout | `0–100` | `0.0` | Luminance density impact |
| `bandwidth` | Slider + `±°` readout | `15–60` | `40.0` | Gaussian hue tolerance ($\sigma$) |
| `pinBlackWhite` | Slider + `%` readout | `0–100` | `100.0` | RGB curve black/white endpoint pinning |

## 4. Event & Render Flow
1. **Startup**: Calls `updateVectorscope(true)` once after presenting the dialog, exporting the active photo via `VectorscopeRenderer.exportAndRender` and storing the 8-band baseline saturation in `state.baseBandSat`.
2. **Control Changes (`onControlChanged`)**:
   - Every observed property change dispatches `pushHslToLightroom()` asynchronously.
   - `pushHslToLightroom()` serializes writes via `state.isPushingHsl` / `state.hslPushPending` so rapid slider drags coalesce without overlapping `withWriteAccessDo` transactions.
   - Changes to `targetKeys` (`targetHue1..3`, `targetSat1..3`, `enableTarget2..3`) also call `scheduleTargetRedraw()` (80 ms debounce), which invokes `updateVectorscope(false)` using `VectorscopeRenderer.renderCached` against `thumb_input.jpg`.
   - `strength`, `density`, `bandwidth`, and `pinBlackWhite` do **not** trigger vectorscope re-renders.
3. **Render Watchdog**: `RENDER_TIMEOUT_SECONDS = 30` releases `state.isRendering` if a render stalls; `state.generation` discards stale completions.

## 5. User Guide ([Help.lua](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/DaftVectorscope.lrplugin/Help.lua))
Presents a modal dialog (`Daft Vectorscope - User Guide`) documenting Rec.709 vectorscope interpretation, 75%/100% targets, the ~132° Skin Tone Line, Target 1–3 points, and real-time HSL + RGB Tone Curve shaping.
