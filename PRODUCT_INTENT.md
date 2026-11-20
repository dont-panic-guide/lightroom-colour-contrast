<!-- doc-type: product-intent -->
# Product Intent: Daft Vectorscope

## 1. Vision & Scope
**Daft Vectorscope** (`com.daft.lightroom.vectorscope`) is a focused, single-column Adobe Lightroom Classic plugin for **real-time color separation** visualized on a Rec.709 vectorscope.

## 2. Explicit User Intents & Design Decisions
1. **Color Separation Only**:
   - The plugin exists solely to control color separation. Legacy features—live image preview, 3-column layout, film emulation, halation, optical glow, film grain, exposure control, enable/disable checkboxes, chroma gain, sample region crop, auto-complement (`+180°`), presets, and neutral protect—were intentionally removed.
2. **Real-Time Lightroom Sync (No Apply/Reset Buttons)**:
   - Every slider and target checkbox change drives Lightroom Classic's Develop settings immediately in real time via `LrDevelopController` and `photo:applyDevelopSettings`, with no "Apply" or "Reset" button step.
3. **Target-Point Driven Color Contrast**:
   - Users define 1 to 3 color targets by `(Hue, Saturation)`: Target 1 is always active; Target 2 and Target 3 are toggled via checkboxes.
   - Active targets are plotted as colored points (`T1`, `T2`, `T3`) directly on the `512×512` vectorscope so the user can see each target's hue angle and saturation radius against the photo's chrominance trace.
4. **Unified Baseline Algorithm**:
   - Hue, Saturation, and Luminance are all part of the baseline calculation:
     - **Strength** (`0–100%`, default `0%`): Single master intensity slider. At `0%`, no color separation changes are applied.
     - **Luminance Density** (`0–100%`, default `0%`): Controls subtractive color density darkening/bleaching; `0%` leaves luminance untouched.
     - **Tolerance** (`15–60°`, default `40°`): Controls Gaussian hue bandwidth ($\sigma$).
   - **No Neutral Protection**: Low-chroma / near-neutral pixels are not masked out by a neutral-protect slider.
5. **Hue Adjustments via RGB Tone Curves (Preserve Manual HSL Hue)**:
   - All hue shaping is applied to Lightroom's per-channel RGB Point Tone Curves (`ToneCurvePV2012Red`, `ToneCurvePV2012Green`, `ToneCurvePV2012Blue`) using the sine-attractor hue math.
   - Lightroom's 8-channel HSL `HueAdjustment*` sliders are **never touched** so any manual HSL Hue edits made by the user in Lightroom remain intact.
   - Lightroom's Master Luminance curve (`ToneCurvePV2012`) is **never touched**.
   - **Pin Black & White** (`0–100%`, default `100%`): Controls whether black `(0,0)` and white `(255,255)` RGB curve endpoints stay pinned (`100%`) or shift proportionally (`1 - Pin%`) with the shadow/highlight hue offsets (`< 100%`).
6. **Startup-Only Photo Export**:
   - Lightroom does not expose a native vectorscope API to plugins, so the vectorscope relies on a startup export of the active photo.
   - The plugin exports the photo **once** when the dialog opens (`updateVectorscope(true)`) and does not re-export on slider drags or provide a manual Refresh button. Adjusting target `Hue`/`Sat` or Target 2/3 checkboxes re-renders target markers from the cached thumbnail.

## 3. Non-Goals
- No standalone film lab / TIFF export pipeline.
- No spatial 2D filters (halation, bloom, grain, Laplacian contrast).
- No modification of Lightroom's Master Tone Curve (`ToneCurvePV2012`) or HSL Hue (`HueAdjustment*`) sliders.

## 4. Licensing
- **License**: This project is to be released under the **GNU General Public License v3.0 (GNU GPLv3)**.

