<!-- doc-type: product-features -->
# Product Features & Architecture Index

**Daft Vectorscope** consists of a Lua plugin (`DaftVectorscope.lrplugin/`), a compiled C# GDI+ vectorscope engine (`vectorscope_engine/Program.cs`), and an automated build/test suite (`tests/`).

## Subsystem Documentation
1. **[UI & Interaction](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/docs/UI_AND_INTERACTION.md)** — Lightroom SDK entry points (`Info.lua`), single-column floating dialog layout (`ShowVectorscope.lua`), Help dialog (`Help.lua`), startup-only export, 80 ms debounced target redraws, and serialized Develop sync.
2. **[Color Separation & Tone Curve Math](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/docs/COLOR_SEPARATION_MATH.md)** — 8-channel Rec.709 HSL Saturation/Luminance math, Smooth Sine-Attractor hue field, 5-knot RGB Point Tone Curve projection, `Pin Black & White` endpoint scaling, and Lightroom Develop sync (`ColorSeparation.lua`).
3. **[Native Engine & Rendering](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/docs/NATIVE_ENGINE_AND_RENDERING.md)** — `VectorscopeRenderer.lua` export/cache bridge, `vectorscope_engine.exe` CLI flags, Rec.709 `Cb/Cr` accumulation, graticule & target point overlay, `MeasureBandSaturations`, and `ColorSeparationSimulator` (`Program.cs`).
4. **[Testing & Build Pipeline](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/docs/TESTING_AND_BUILD.md)** — `run_tests.ps1` build/bundle workflow, Roslyn compilation, 54 C# unit/CLI/benchmark tests (`VectorscopeTests.cs`, `GenerateTestPatterns.cs`), and Lua validation.

## Repository Layout
```text
Daft Lightroom/
├── PRODUCT_INTENT.md
├── PRODUCT_FEATURES.md
├── CHANGELOG.md
├── docs/
│   ├── UI_AND_INTERACTION.md
│   ├── COLOR_SEPARATION_MATH.md
│   ├── NATIVE_ENGINE_AND_RENDERING.md
│   └── TESTING_AND_BUILD.md
├── DaftVectorscope.lrplugin/
│   ├── Info.lua
│   ├── ShowVectorscope.lua
│   ├── ColorSeparation.lua
│   ├── VectorscopeRenderer.lua
│   ├── Help.lua
│   ├── default_scope.png
│   └── bin/vectorscope_engine.exe
├── vectorscope_engine/
│   ├── Program.cs
│   └── vectorscope_engine.exe
└── tests/
    ├── run_tests.ps1
    ├── VectorscopeTests.cs
    ├── GenerateTestPatterns.cs
    ├── test_runner.exe
    └── test_output/
```
