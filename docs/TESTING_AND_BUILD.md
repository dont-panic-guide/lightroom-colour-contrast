<!-- doc-type: product-features -->
# Testing & Build Pipeline (`tests/run_tests.ps1`)

## 1. Automated Build, Test & Bundle Script ([run_tests.ps1](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/tests/run_tests.ps1))
Run from the repository root:
```powershell
powershell -ExecutionPolicy Bypass -File .\tests\run_tests.ps1
```

### Pipeline Stages
1. **Compiler Discovery**: Locates Roslyn `csc.exe` (VS2022/VS2026 MSBuild or .NET Framework fallback).
2. **Engine Compilation**: Compiles [Program.cs](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/vectorscope_engine/Program.cs) with `/unsafe /optimize+ /target:winexe` into `vectorscope_engine\vectorscope_engine.exe`.
3. **C# Test Execution**: Compiles [VectorscopeTests.cs](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/tests/VectorscopeTests.cs) + [GenerateTestPatterns.cs](file:///c:/Users/pan_y/Documents/Daft%20Lightroom/tests/GenerateTestPatterns.cs) + `Program.cs` into `tests\test_runner.exe` and runs all **54** tests (`tests\test_output\`):
   - Rec.709 `Cb/Cr` math accuracy (Red, Green, Blue) and 132° Skin Tone angle.
   - Synthetic pattern rendering: grayscale collapse, 75% SMPTE color bar alignment, skin tone cluster alignment, true-color trace preservation, and gain scaling.
   - Performance benchmark: 20 iterations of `512×512` `RenderVectorscope` (`< 50 ms` threshold; `~2.1 ms` typical).
   - Color separation & filmic density math (`CalculateChannelSat`, `CalculateChannelLum`, `CalculateAdjustments`).
   - RGB Tone Curve hue math (`CalculateToneCurves`): linear identity at `Strength = 0`, interior knot shifts at `Strength > 0`, strict `(0,0)`/`(255,255)` pinning at `Pin = 100%`, endpoint shifts at `Pin = 0%`, and proportional scaling at `Pin = 50%`.
   - Sine-attractor hue shifts, `MeasureBandSaturations`, pixel simulation (`Apply`), target CLI parsing/dot rendering, and end-to-end CLI execution.
4. **Lua Validation**: Checks block balance (`function`/`if`/`for`/`while`/`do` vs `end`), bracket matching, and `Info.lua` SDK menu keys (`LrLibraryMenuItems`, `LrExportMenuItems`) across all 5 `.lua` files.
5. **Plugin Bundling**: Copies `vectorscope_engine.exe` into `DaftVectorscope.lrplugin\bin\vectorscope_engine.exe` and generates `default_scope.png` if missing.

## 2. Manual Lightroom Classic Verification
1. Reload `DaftVectorscope.lrplugin` in **File > Plug-in Manager**.
2. Open a photo in the **Develop** module and launch **File > Plug-in Extras > Open Vectorscope...**.
3. Set a manual **HSL Hue** slider in Lightroom (e.g., `Hue Red = +25`).
4. Drag **Strength** above `0%`:
   - Confirm Lightroom's **HSL Saturation** sliders update in real time and manual **HSL Hue** (`+25`) stays untouched.
   - Confirm **Tone Curve > Red / Green / Blue** point curves update in real time while the **Master RGB** curve stays untouched.
5. Drag **Luminance Density** above `0%` and confirm **HSL Luminance** sliders respond.
6. Drag **Pin Black & White** from `100%` toward `0%` and confirm black (`x = 0`) and white (`x = 255`) endpoints unpin proportionally.
7. Toggle **Target 2** / **Target 3** and adjust **Hue** / **Sat** sliders to confirm target dots update on the vectorscope without re-exporting from Lightroom.
