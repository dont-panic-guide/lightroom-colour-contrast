<!-- doc-type: product-features -->
# Color Separation & Tone Curve Math (`ColorSeparation.lua`)

Implemented in [ColorSeparation.lua](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/DaftVectorscope.lrplugin/ColorSeparation.lua) and mirrored 1:1 in `ColorSeparationSimulator` in [Program.cs](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/vectorscope_engine/Program.cs).

## 1. Lightroom HSL Channels & Primary Poles
`ColorSeparation.CHANNELS` defines 8 Lightroom Classic HSL bands with Rec.709 `Cb/Cr` center angles and subtractive density coupling weights (`rho`):

| Channel | Angle | `rho` | Saturation Param | Luminance Param |
|---|---|---|---|---|
| Red | `103°` | `0.48` | `SaturationAdjustmentRed` | `LuminanceAdjustmentRed` |
| Orange | `138°` | `0.38` | `SaturationAdjustmentOrange` | `LuminanceAdjustmentOrange` |
| Yellow | `175°` | `0.30` | `SaturationAdjustmentYellow` | `LuminanceAdjustmentYellow` |
| Green | `220°` | `0.55` | `SaturationAdjustmentGreen` | `LuminanceAdjustmentGreen` |
| Aqua | `283°` | `0.52` | `SaturationAdjustmentAqua` | `LuminanceAdjustmentAqua` |
| Blue | `355°` | `0.45` | `SaturationAdjustmentBlue` | `LuminanceAdjustmentBlue` |
| Purple | `25°` | `0.42` | `SaturationAdjustmentPurple` | `LuminanceAdjustmentPurple` |
| Magenta | `50°` | `0.40` | `SaturationAdjustmentMagenta` | `LuminanceAdjustmentMagenta` |

`ColorSeparation.PRIMARY_POLES` defines the 3 RGB Tone Curve poles:
- `Red` (`103.0°`) -> `ToneCurvePV2012Red`
- `Green` (`220.0°`) -> `ToneCurvePV2012Green`
- `Blue` (`355.0°`) -> `ToneCurvePV2012Blue`

## 2. HSL Saturation & Luminance (`calculateHslAdjustments`)
When `targets` is empty or `strength <= 0.0001`, all 16 HSL outputs are `0` and all 3 tone curves are linear `{ 0, 0, 64, 64, 128, 128, 192, 192, 255, 255 }`.

Otherwise, for each channel $i$ at angle $\theta_i$ with `sNorm = clamp(strength / 100, 0, 1)`, `dNorm = clamp(density / 100, 0, 1)`, and `sigma = bandwidth` (default `40`):
1. For each active target $T_k = (\text{hue}_k, \text{sat}_k)$:
   $$A_{i,k} = \exp\left(-\frac{\text{angleDistance}(\theta_i, \text{hue}_k)^2}{2\sigma^2}\right)$$
   Track `maxAffinity` $= \max_k A_{i,k}$ and affinity-weighted `targetSat` $= \frac{\sum_k A_{i,k} \cdot \text{sat}_k}{\sum_k A_{i,k}}$.
2. Gap-driven saturation delta against the photo's measured baseline `bandSat[i]` (fallback `25.0`):
   $$\Delta S_i = s_{\text{norm}} \cdot \left((\text{targetSat} - \text{bandSat}[i]) \cdot \text{maxAffinity} - 65.0 \cdot (1 - \text{maxAffinity})\right)$$
   Rounded and clamped to `[-100, 100]`.
3. Subtractive luminance density (`0` when `dNorm <= 0.0001`):
   $$\Delta L_i = -d_{\text{norm}} \cdot \rho_i \cdot \text{satValue}_i \cdot \left(1 - \frac{|\text{satValue}_i|}{250.0}\right)$$
   Rounded and clamped to `[-100, 100]`.

## 3. Sine-Attractor Hue Shaping & RGB Tone Curves (`calculateToneCurves`)
1. **Smooth Sine-Attractor (`computeHueAttractorShift`)**:
   - Deduplicates (`< 0.5°`) and sorts active target hues on `[0, 360)`.
   - Finds nearest target and signed shortest delta $\Delta\theta \in (-180^\circ, 180^\circ]$.
   - Computes `halfGap = max(15.0, span * 0.5)` (`180.0` for a single target).
   - Returns $\delta = s_{\text{norm}} \cdot \sin(\pi \cdot \text{clamp}(\Delta\theta / \text{halfGap}, -1, 1))$.
2. **Per-Band Rec.709 RGB Deltas**:
   - Rotates $\theta_i$ by $\delta_i \times 30^\circ$ to $\theta'_i$, and adds a `0.35 * sNorm * maxAff` pull toward the affinity-weighted target chroma vector $(t_{Cb}, t_{Cr})$.
   - Converts $(\Delta Cb_i, \Delta Cr_i)$ to RGB deltas at constant luminance:
     $$\Delta R_i = 1.5748\,\Delta Cr_i, \quad \Delta B_i = 1.8556\,\Delta Cb_i, \quad \Delta G_i = -0.4681\,\Delta Cr_i - 0.1873\,\Delta Cb_i$$
3. **5-Knot Curve Projection (`x = 0, 64, 128, 192, 255`)**:
   - For each primary pole $p$ at angle $\theta_{p^+}$ and complement $\theta_{p^-} = (\theta_{p^+} + 180^\circ) \bmod 360^\circ$, accumulates $\cos^2$ hemisphere weights (for angular distance $< 90^\circ$) with amplitude `K_CURVE = 28.0`:
     - $\Delta_{192}[p]$ from the primary hemisphere, $\Delta_{64}[p]$ from the complementary hemisphere, and $\Delta_{128}[p] = 0.5(\Delta_{64}[p] + \Delta_{192}[p])$.
   - Endpoint pinning with `unpin = 1.0 - clamp(pinBlackWhite / 100.0, 0.0, 1.0)`:
     - $y_0 = \text{clampByte}(\text{unpin} \cdot (\Delta_{64}[p] - \min(0, \Delta_{64}^R, \Delta_{64}^G, \Delta_{64}^B)))$
     - $y_{255} = \text{clampByte}(255 + \text{unpin} \cdot (\Delta_{192}[p] - \max(0, \Delta_{192}^R, \Delta_{192}^G, \Delta_{192}^B)))$

## 4. Lightroom Develop Sync (`applyRealtimeToLightroom`)
- Pushes all 16 HSL `SaturationAdjustment*` / `LuminanceAdjustment*` keys via `LrDevelopController.setValue` (switching to `'develop'` module once if needed).
- Pushes `ToneCurvePV2012Red`, `ToneCurvePV2012Green`, `ToneCurvePV2012Blue` via `LrDevelopController.setValue` and `catalog:withWriteAccessDo("Daft Tone Curve Hue", ...)` calling `photo:applyDevelopSettings` with `ToneCurveName2012 = "Custom"` and `EnableToneCurve = true`.
- Never writes `HueAdjustment*` or `ToneCurvePV2012`.
