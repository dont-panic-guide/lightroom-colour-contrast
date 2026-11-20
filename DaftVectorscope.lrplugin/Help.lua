local LrDialogs = import 'LrDialogs'
local LrView = import 'LrView'

local f = LrView.osFactory()

local guideText = [[
Welcome to Daft Vectorscope for Adobe Lightroom Classic!

This plugin provides a real-time Rec.709 vectorscope and unified Hue/Saturation/Luminance color separation shaper for Adobe Lightroom Classic.

Key Features & Guide:

1. Color Mode Vectorscope:
   Every pixel in the active photo is plotted in polar chrominance space (Rec.709 Cb/Cr). Distance from the center represents Saturation and angle represents Hue.

2. Standard Color Targets & Skin Tone Line (I-Line):
   The six boxes (R, Mg, B, Cy, G, Yl) show 75% and 100% Rec.709 reference positions. The dashed amber line at ~132° is the universal skin tone axis.

3. Target Color Points (T1, T2, T3):
   - Target 1 is always active; enable Target 2 and Target 3 with their checkboxes.
   - Each target's Hue (0–359°) and Saturation (0–100%) sliders plot a colored point directly on the Vectorscope.
   - The plugin measures your photo's baseline saturation in each hue band and boosts/pulls more strongly the further the photo's saturation is from your target point.

4. Real-Time Color Separation Shaping:
   - Strength (0–100%, default 0%): Overall intensity scaling Saturation, RGB Tone Curve Hue shaping, and Luminance density together.
   - Luminance Density (0–100%, default 0%): Controls subtractive color density darkening/bleaching (0% leaves luminance untouched).
   - Tolerance (±15° to ±60°): Controls how broadly each target influences neighboring hues.
   - Pin Black & White (0–100%, default 100%): Controls how strongly the black (0,0) and white (255,255) endpoints of the RGB Tone Curves are pinned (below 100%, shadow and highlight hue shifts extend to the black and white endpoints).
   - Saturation and Luminance drive Lightroom Classic's HSL sliders in real time (leaving HSL Hue sliders untouched), while all Hue adjustments are applied to Lightroom's RGB Point Tone Curves (Red, Green, Blue), leaving the Master Luminance curve untouched.
]]

LrDialogs.presentModalDialog {
    title = "Daft Vectorscope - User Guide",
    contents = f:column {
        spacing = f:control_spacing(),
        f:static_text {
            title = guideText,
            width_in_chars = 70,
            height_in_lines = 45,
        },
    },
    actionVerb = "Close",
    cancelVerb = "< exclude >",
}
