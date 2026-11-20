<!-- doc-type: changelog -->
# Changelog

## [Unreleased]

<!-- FROZEN_PUSHED_HISTORY -->

## [2026-11-20]
- Moved all color-separation hue adjustments from Lightroom HSL `HueAdjustment*` sliders to 5-knot RGB Point Tone Curves (`ToneCurvePV2012Red`, `ToneCurvePV2012Green`, `ToneCurvePV2012Blue`), leaving manual HSL Hue sliders and the Master Luminance curve (`ToneCurvePV2012`) untouched.
- Added the `Pin Black & White` (`0–100%`, default `100%`) slider to control endpoint pinning at `(0,0)` and `(255,255)` in both Lua (`ColorSeparation.lua`, `ShowVectorscope.lua`, `Help.lua`) and C# (`Program.cs`, `VectorscopeTests.cs`).
- Streamlined the plugin to a single-column real-time Color Separation Vectorscope with startup-only photo export, cached target point redraws, and codebase cleanup.
- Updated code comments for release readiness, removed legacy `.agents/` and empty `logs/` directories, and structured project-level documentation (`PRODUCT_INTENT.md`, `PRODUCT_FEATURES.md`, `docs/*.md`, `CHANGELOG.md`).
- Recorded GNU GPLv3 licensing intent in `PRODUCT_INTENT.md`.
