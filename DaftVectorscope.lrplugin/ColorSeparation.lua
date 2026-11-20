local LrApplication = import 'LrApplication'
local LrApplicationView = import 'LrApplicationView'
local LrDevelopController = import 'LrDevelopController'

local ColorSeparation = {}

-- 8 Native Lightroom Classic HSL Channels with their Rec.709 Cb/Cr center angles
-- and calibrated subtractive density coupling weights (rho).
-- Hue shaping is handled via RGB Point Tone Curves rather than HSL Hue sliders.
ColorSeparation.CHANNELS = {
    { name = "Red",     angle = 103, satParam = "SaturationAdjustmentRed",     lumParam = "LuminanceAdjustmentRed",     rho = 0.48 },
    { name = "Orange",  angle = 138, satParam = "SaturationAdjustmentOrange",  lumParam = "LuminanceAdjustmentOrange",  rho = 0.38 },
    { name = "Yellow",  angle = 175, satParam = "SaturationAdjustmentYellow",  lumParam = "LuminanceAdjustmentYellow",  rho = 0.30 },
    { name = "Green",   angle = 220, satParam = "SaturationAdjustmentGreen",   lumParam = "LuminanceAdjustmentGreen",   rho = 0.55 },
    { name = "Aqua",    angle = 283, satParam = "SaturationAdjustmentAqua",    lumParam = "LuminanceAdjustmentAqua",    rho = 0.52 },
    { name = "Blue",    angle = 355, satParam = "SaturationAdjustmentBlue",    lumParam = "LuminanceAdjustmentBlue",    rho = 0.45 },
    { name = "Purple",  angle = 25,  satParam = "SaturationAdjustmentPurple",  lumParam = "LuminanceAdjustmentPurple",  rho = 0.42 },
    { name = "Magenta", angle = 50,  satParam = "SaturationAdjustmentMagenta", lumParam = "LuminanceAdjustmentMagenta", rho = 0.40 },
}

-- Primary Rec.709 RGB channel angles mapped to Lightroom Point Tone Curve parameters
ColorSeparation.PRIMARY_POLES = {
    { name = "Red",   param = "ToneCurvePV2012Red",   angle = 103.0 },
    { name = "Green", param = "ToneCurvePV2012Green", angle = 220.0 },
    { name = "Blue",  param = "ToneCurvePV2012Blue",  angle = 355.0 },
}

-- Calculate shortest circular distance between two angles (0-180 deg)
local function angleDistance(a, b)
    local diff = math.abs(a - b) % 360
    if diff > 180 then
        diff = 360 - diff
    end
    return diff
end

local function clampByte(val)
    local rounded = math.floor(val + 0.5)
    if rounded > 255 then return 255 end
    if rounded < 0 then return 0 end
    return rounded
end

-- Smooth Sine-Attractor Field: anchors target poles, pulls intermediate hues toward
-- nearest target pole and pushes them away from the gaps between targets.
local function computeHueAttractorShift(angleDeg, targetHues, strengthNorm)
    if not targetHues or #targetHues == 0 or math.abs(strengthNorm) < 1e-6 then
        return 0.0
    end

    local sorted = {}
    for _, th in ipairs(targetHues) do
        local norm = th % 360
        if norm < 0 then norm = norm + 360 end
        local dup = false
        for _, s in ipairs(sorted) do
            if math.abs(s - norm) < 0.5 then dup = true break end
        end
        if not dup then table.insert(sorted, norm) end
    end
    table.sort(sorted)

    local a = angleDeg % 360
    if a < 0 then a = a + 360 end

    local nearestIdx = 1
    local bestAbs = 999.0
    local signedDelta = 0.0
    for i, t in ipairs(sorted) do
        local d = t - a
        while d > 180.0 do d = d - 360.0 end
        while d <= -180.0 do d = d + 360.0 end
        if math.abs(d) < bestAbs then
            bestAbs = math.abs(d)
            signedDelta = d
            nearestIdx = i
        end
    end

    if bestAbs < 1e-4 then return 0.0 end

    local halfGap = 180.0
    if #sorted >= 2 then
        local adjIdx
        if signedDelta > 0 then
            adjIdx = ((nearestIdx - 2) % #sorted) + 1
        else
            adjIdx = (nearestIdx % #sorted) + 1
        end
        local span = math.abs(sorted[nearestIdx] - sorted[adjIdx])
        if span > 180.0 then span = 360.0 - span end
        if #sorted == 2 then span = math.min(span, 360.0 - span) end
        halfGap = math.max(15.0, span * 0.5)
    end

    local u = math.max(-1.0, math.min(1.0, signedDelta / halfGap))
    return strengthNorm * math.sin(math.pi * u)
end

-- Translate the sine-attractor hue shift math into 5-knot RGB Point Tone Curves
-- (x = 0, 64, 128, 192, 255), leaving the Master Luminance curve untouched.
function ColorSeparation.calculateToneCurves(targets, strength, bandwidth, pinBlackWhite)
    if not targets or #targets == 0 or (strength or 0) <= 0.0001 then
        return {
            ToneCurvePV2012Red   = { 0, 0, 64, 64, 128, 128, 192, 192, 255, 255 },
            ToneCurvePV2012Green = { 0, 0, 64, 64, 128, 128, 192, 192, 255, 255 },
            ToneCurvePV2012Blue  = { 0, 0, 64, 64, 128, 128, 192, 192, 255, 255 },
        }
    end

    local sNorm = math.max(0.0, math.min(1.0, (strength or 0.0) / 100.0))
    local sigma = bandwidth or 40.0
    local twoSigmaSq = 2.0 * sigma * sigma
    local unpin = 1.0 - math.max(0.0, math.min(1.0, (pinBlackWhite or 100.0) / 100.0))

    local targetHues = {}
    for _, pt in ipairs(targets) do
        table.insert(targetHues, pt.hue)
    end

    local bandDelta = {}
    for idx, ch in ipairs(ColorSeparation.CHANNELS) do
        local rad0 = ch.angle * (math.pi / 180.0)
        local shift = computeHueAttractorShift(ch.angle, targetHues, sNorm)
        local rad1 = (ch.angle + shift * 30.0) * (math.pi / 180.0)

        local maxAff = 0.0
        local sumAff = 0.0
        local sumCb = 0.0
        local sumCr = 0.0
        for _, pt in ipairs(targets) do
            local dist = angleDistance(ch.angle, pt.hue)
            local aff = math.exp(-(dist * dist) / twoSigmaSq)
            if aff > maxAff then maxAff = aff end
            local ptRad = pt.hue * (math.pi / 180.0)
            local ptSat = (pt.sat or 50.0) / 100.0
            sumCb = sumCb + aff * (ptSat * math.cos(ptRad))
            sumCr = sumCr + aff * (ptSat * math.sin(ptRad))
            sumAff = sumAff + aff
        end

        local tCb = (sumAff > 1e-6) and (sumCb / sumAff) or math.cos(rad0)
        local tCr = (sumAff > 1e-6) and (sumCr / sumAff) or math.sin(rad0)

        local dCb = (math.cos(rad1) - math.cos(rad0)) + 0.35 * sNorm * maxAff * (tCb - math.cos(rad0))
        local dCr = (math.sin(rad1) - math.sin(rad0)) + 0.35 * sNorm * maxAff * (tCr - math.sin(rad0))

        bandDelta[idx] = {
            Red   = 1.5748 * dCr,
            Green = -0.4681 * dCr - 0.1873 * dCb,
            Blue  = 1.8556 * dCb,
        }
    end

    local K_CURVE = 28.0
    local delta64 = {}
    local delta128 = {}
    local delta192 = {}

    for _, p in ipairs(ColorSeparation.PRIMARY_POLES) do
        local compAngle = (p.angle + 180.0) % 360.0
        local sumWHi, sumHi = 0.0, 0.0
        local sumWLo, sumLo = 0.0, 0.0

        for idx, ch in ipairs(ColorSeparation.CHANNELS) do
            local diffHi = angleDistance(ch.angle, p.angle)
            if diffHi < 90.0 then
                local c = math.cos(diffHi * math.pi / 180.0)
                local w = c * c
                sumWHi = sumWHi + w
                sumHi = sumHi + w * bandDelta[idx][p.name]
            end

            local diffLo = angleDistance(ch.angle, compAngle)
            if diffLo < 90.0 then
                local c = math.cos(diffLo * math.pi / 180.0)
                local w = c * c
                sumWLo = sumWLo + w
                sumLo = sumLo + w * bandDelta[idx][p.name]
            end
        end

        delta192[p.name] = (sumWHi > 1e-6) and (K_CURVE * (sumHi / sumWHi)) or 0.0
        delta64[p.name]  = (sumWLo > 1e-6) and (K_CURVE * (sumLo / sumWLo)) or 0.0
        delta128[p.name] = 0.5 * (delta64[p.name] + delta192[p.name])
    end

    local minDelta64 = math.min(0.0, delta64.Red, delta64.Green, delta64.Blue)
    local maxDelta192 = math.max(0.0, delta192.Red, delta192.Green, delta192.Blue)

    local curves = {}
    for _, p in ipairs(ColorSeparation.PRIMARY_POLES) do
        local y0   = clampByte(0.0 + unpin * (delta64[p.name] - minDelta64))
        local y64  = clampByte(64.0 + delta64[p.name])
        local y128 = clampByte(128.0 + delta128[p.name])
        local y192 = clampByte(192.0 + delta192[p.name])
        local y255 = clampByte(255.0 + unpin * (delta192[p.name] - maxDelta192))
        curves[p.param] = { 0, y0, 64, y64, 128, y128, 192, y192, 255, y255 }
    end

    return curves
end

-- Unified Luminance + Saturation HSL adjustments + RGB Tone Curve Hue adjustments
-- targets: array of { hue = 0..359, sat = 0..100 }
-- bandSat: 8-element array of measured baseline band saturations (0..100)
-- strength: 0..100 (default 0 -> all zeros / linear curves)
-- density: 0..100 (default 0 -> 0 luminance change)
-- bandwidth: 15..60 deg (default 40)
-- pinBlackWhite: 0..100 (default 100 -> endpoints strictly pinned)
function ColorSeparation.calculateHslAdjustments(targets, bandSat, strength, density, bandwidth, pinBlackWhite)
    local adjustments = {}
    for _, ch in ipairs(ColorSeparation.CHANNELS) do
        adjustments[ch.satParam] = 0
        adjustments[ch.lumParam] = 0
    end

    local curves = ColorSeparation.calculateToneCurves(targets, strength, bandwidth, pinBlackWhite)

    if not targets or #targets == 0 or (strength or 0) <= 0.0001 then
        return adjustments, curves
    end

    local sigma = bandwidth or 40.0
    local twoSigmaSq = 2.0 * sigma * sigma
    local sNorm = math.max(0.0, math.min(1.0, (strength or 0.0) / 100.0))
    local dNorm = math.max(0.0, math.min(1.0, (density or 0.0) / 100.0))

    for idx, ch in ipairs(ColorSeparation.CHANNELS) do
        local maxAffinity = 0.0
        local weightedTargetSat = 0.0
        local affinitySum = 0.0

        for _, pt in ipairs(targets) do
            local dist = angleDistance(ch.angle, pt.hue)
            local affinity = math.exp(-(dist * dist) / twoSigmaSq)
            if affinity > maxAffinity then
                maxAffinity = affinity
            end
            weightedTargetSat = weightedTargetSat + affinity * (pt.sat or 50.0)
            affinitySum = affinitySum + affinity
        end

        local targetSat = (affinitySum > 1e-6) and (weightedTargetSat / affinitySum) or 50.0
        local measuredSat = (bandSat and bandSat[idx]) or 25.0
        local gap = targetSat - measuredSat
        local penalty = 1.0 - maxAffinity

        local deltaSat = sNorm * ((gap * maxAffinity) - (65.0 * penalty))
        local satValue = math.floor(deltaSat + 0.5)
        if satValue > 100 then satValue = 100 end
        if satValue < -100 then satValue = -100 end

        local lumValue = 0
        if dNorm > 0.0001 then
            local softKnee = 1.0 - (math.abs(satValue) / 250.0)
            local deltaLum = -dNorm * ch.rho * satValue * softKnee
            lumValue = math.floor(deltaLum + 0.5)
            if lumValue > 100 then lumValue = 100 end
            if lumValue < -100 then lumValue = -100 end
        end

        adjustments[ch.satParam] = satValue
        adjustments[ch.lumParam] = lumValue
    end

    return adjustments, curves
end

-- Apply HSL Saturation/Luminance and RGB Point Tone Curves to Lightroom Classic in real time,
-- leaving HSL Hue sliders and the Master Luminance curve untouched.
function ColorSeparation.applyRealtimeToLightroom(hslAdjustments, curveAdjustments, photo)
    local ok, err = pcall(function()
        local switched = false
        for param, val in pairs(hslAdjustments or {}) do
            local setOk = pcall(LrDevelopController.setValue, param, val)
            if not setOk and not switched then
                switched = true
                pcall(LrApplicationView.switchToModule, 'develop')
                pcall(LrDevelopController.setValue, param, val)
            end
        end

        if curveAdjustments then
            for param, curve in pairs(curveAdjustments) do
                pcall(LrDevelopController.setValue, param, curve)
            end

            if photo then
                local curveSettings = {
                    ToneCurvePV2012Red   = curveAdjustments.ToneCurvePV2012Red,
                    ToneCurvePV2012Green = curveAdjustments.ToneCurvePV2012Green,
                    ToneCurvePV2012Blue  = curveAdjustments.ToneCurvePV2012Blue,
                    ToneCurveName2012    = "Custom",
                    EnableToneCurve      = true,
                }
                local catalog = photo.catalog or LrApplication.activeCatalog()
                pcall(function()
                    catalog:withWriteAccessDo("Daft Tone Curve Hue", function()
                        photo:applyDevelopSettings(curveSettings, nil, true)
                    end, { timeout = 2, asynchronous = true })
                end)
            end
        end
    end)
    return ok, err
end

return ColorSeparation
