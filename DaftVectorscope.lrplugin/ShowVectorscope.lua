-- Daft Vectorscope floating dialog:
-- Displays a 512x512 Rec.709 vectorscope (exported once on startup, updated from cache
-- when target points change) and drives Lightroom Develop HSL + RGB Tone Curves in real time.

local LrApplication = import 'LrApplication'
local LrDialogs = import 'LrDialogs'
local LrView = import 'LrView'
local LrBinding = import 'LrBinding'
local LrFunctionContext = import 'LrFunctionContext'
local LrTasks = import 'LrTasks'
local LrPathUtils = import 'LrPathUtils'
local LrFileUtils = import 'LrFileUtils'

local VectorscopeRenderer = require 'VectorscopeRenderer'
local ColorSeparation = require 'ColorSeparation'

-- Prevent duplicate dialog instances
if _G.DaftVectorscopeDialogActive then
    return
end
_G.DaftVectorscopeDialogActive = true

local RENDER_TIMEOUT_SECONDS = 30

LrTasks.startAsyncTask(function()
    LrFunctionContext.callWithContext("DaftVectorscopeSession", function(context)
        context:addCleanupHandler(function()
            _G.DaftVectorscopeDialogActive = false
        end)

        local f = LrView.osFactory()
        local bind = LrView.bind
        local defaultScopePath = LrPathUtils.child(_PLUGIN.path, 'default_scope.png')

        local props = LrBinding.makePropertyTable(context)
        props.isOpen = true
        props.scopeImagePath = defaultScopePath
        props.photoTitle = "No photo selected"
        props.photoDetails = "Select a photo in Lightroom Classic"
        props.statusText = "Ready"

        -- Target Color Points: (Hue 0..359 deg, Saturation 0..100%)
        props.targetHue1 = 132.0
        props.targetSat1 = 50.0
        props.enableTarget2 = true
        props.targetHue2 = 312.0
        props.targetSat2 = 50.0
        props.enableTarget3 = false
        props.targetHue3 = 220.0
        props.targetSat3 = 50.0

        -- Unified Color Separation Shaping Controls
        props.strength = 0.0      -- Overall Strength (0..100%, default 0)
        props.density = 0.0       -- Luminance Color Density impact (0..100%, default 0)
        props.bandwidth = 40.0    -- Hue Selection Tolerance (15..60 deg)
        props.pinBlackWhite = 100.0 -- Pin Black (0,0) & White (255,255) Tone Curve endpoints (0..100%, default 100)

        local state = {
            isRendering = false,
            renderStartTime = 0,
            generation = 0,
            cachedPhotoId = nil,
            lastRenderedKey = nil,
            lastFailedKey = nil,
            refreshPending = false,
            sliderDebouncePending = false,
            isPushingHsl = false,
            hslPushPending = false,
            baseBandSat = { 25, 25, 25, 25, 25, 25, 25, 25 },
        }

        local function getActiveTargets()
            local targets = {
                { hue = props.targetHue1 or 132.0, sat = props.targetSat1 or 50.0 },
            }
            if props.enableTarget2 then
                table.insert(targets, { hue = props.targetHue2 or 312.0, sat = props.targetSat2 or 50.0 })
            end
            if props.enableTarget3 then
                table.insert(targets, { hue = props.targetHue3 or 220.0, sat = props.targetSat3 or 50.0 })
            end
            return targets
        end

        local function getTargetsArg()
            local parts = {}
            for _, pt in ipairs(getActiveTargets()) do
                table.insert(parts, string.format("%.0f:%.0f", pt.hue, pt.sat))
            end
            return table.concat(parts, ",")
        end

        local function settingsKey(photoId)
            return string.format("%s_t%s", tostring(photoId), getTargetsArg())
        end

        local function currentScopePath()
            local p = props.scopeImagePath
            if p ~= defaultScopePath then return p end
            return nil
        end

        -- Serialize Develop writes so rapid slider drags coalesce without overlapping catalog transactions
        local function pushHslToLightroom()
            if state.isPushingHsl then
                state.hslPushPending = true
                return
            end
            state.isPushingHsl = true
            local photo = LrApplication.activeCatalog():getTargetPhoto()
            if photo then
                local hslAdj, curveAdj = ColorSeparation.calculateHslAdjustments(
                    getActiveTargets(),
                    state.baseBandSat,
                    props.strength,
                    props.density,
                    props.bandwidth,
                    props.pinBlackWhite
                )
                ColorSeparation.applyRealtimeToLightroom(hslAdj, curveAdj, photo)
            end
            state.isPushingHsl = false
            if state.hslPushPending and props.isOpen then
                state.hslPushPending = false
                pushHslToLightroom()
            end
        end

        local function updateVectorscope(force)
            if state.isRendering then
                if (os.time() - state.renderStartTime) > RENDER_TIMEOUT_SECONDS then
                    VectorscopeRenderer.log("Render watchdog expired; releasing lock")
                    state.isRendering = false
                else
                    state.refreshPending = true
                    return
                end
            end

            local photo = LrApplication.activeCatalog():getTargetPhoto()
            if not photo then
                props.photoTitle = "No photo selected"
                props.photoDetails = "Select a photo in Lightroom Classic"
                props.statusText = "Waiting for selection..."
                return
            end

            local photoId = photo.localIdentifier
            local key = settingsKey(photoId)

            if not force then
                if key == state.lastRenderedKey then return end
                if key == state.lastFailedKey then return end
            end

            local options = {
                targetsArg = getTargetsArg(),
            }

            state.generation = state.generation + 1
            local myGeneration = state.generation
            state.isRendering = true
            state.renderStartTime = os.time()

            local useCache = (not force)
                and (photoId == state.cachedPhotoId)
                and LrFileUtils.exists(VectorscopeRenderer.getThumbPath())

            local function onRenderComplete(outPath, bandSat, renderErr)
                if myGeneration ~= state.generation then
                    return
                end
                state.isRendering = false
                if not props.isOpen then return end

                if outPath and LrFileUtils.exists(outPath) then
                    props.scopeImagePath = outPath
                    if bandSat and #bandSat == 8 then
                        state.baseBandSat = bandSat
                    end
                    props.statusText = "Live"
                    state.cachedPhotoId = photoId
                    state.lastRenderedKey = key
                    state.lastFailedKey = nil
                else
                    if not useCache then
                        state.cachedPhotoId = nil
                    end
                    state.lastFailedKey = key
                    local msg = tostring(renderErr or "Unknown error")
                    if #msg > 40 then msg = string.sub(msg, 1, 37) .. "..." end
                    props.statusText = "Error: " .. msg
                    VectorscopeRenderer.log("Render failed: " .. tostring(renderErr))
                end

                if state.refreshPending then
                    state.refreshPending = false
                    updateVectorscope(false)
                end
            end

            if useCache then
                props.statusText = "Updating..."
                VectorscopeRenderer.renderCached(options, onRenderComplete, currentScopePath())
                return
            end

            props.statusText = "Analyzing photo..."
            local fileName = photo:getFormattedMetadata('fileName') or "Active Photo"
            local dimensions = photo:getFormattedMetadata('dimensions') or ""
            props.photoTitle = fileName
            props.photoDetails = (dimensions ~= "") and ("Resolution: " .. dimensions) or ""

            VectorscopeRenderer.exportAndRender(photo, options, onRenderComplete, currentScopePath())
        end

        local function scheduleTargetRedraw()
            if not state.cachedPhotoId or not LrFileUtils.exists(VectorscopeRenderer.getThumbPath()) then
                return
            end
            if state.sliderDebouncePending then return end
            state.sliderDebouncePending = true
            LrTasks.startAsyncTask(function()
                LrTasks.sleep(0.08)
                state.sliderDebouncePending = false
                if props.isOpen and state.cachedPhotoId and LrFileUtils.exists(VectorscopeRenderer.getThumbPath()) then
                    updateVectorscope(false)
                end
            end)
        end

        local targetKeys = {
            targetHue1 = true, targetSat1 = true,
            enableTarget2 = true, targetHue2 = true, targetSat2 = true,
            enableTarget3 = true, targetHue3 = true, targetSat3 = true,
        }

        local function onControlChanged(_, key)
            LrTasks.startAsyncTask(pushHslToLightroom)
            if targetKeys[key] then
                scheduleTargetRedraw()
            end
        end

        local observedKeys = {
            'targetHue1', 'targetSat1',
            'enableTarget2', 'targetHue2', 'targetSat2',
            'enableTarget3', 'targetHue3', 'targetSat3',
            'strength', 'density', 'bandwidth', 'pinBlackWhite',
        }
        for _, k in ipairs(observedKeys) do
            props:addObserver(k, onControlChanged)
        end

        local contents = f:column {
            spacing = f:control_spacing(),
            margin_horizontal = 10,
            margin_vertical = 8,
            bind_to_object = props,

            -- Header Bar
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:static_text {
                    title = bind 'photoTitle',
                    font = '<system/bold>',
                },
                f:static_text {
                    title = bind {
                        key = 'photoDetails',
                        transform = function(v)
                            return (v and v ~= "") and ("• " .. v) or ""
                        end
                    },
                    font = '<system/small>',
                },
                f:spacer { fill_horizontal = 1 },
                f:static_text {
                    title = bind 'statusText',
                    font = '<system/small>',
                    alignment = 'right',
                },
            },

            -- 512x512 Vectorscope Display
            f:picture {
                value = bind 'scopeImagePath',
                width = 512,
                height = 512,
            },

            f:separator { fill_horizontal = 1 },

            -- Target 1 (Always Active)
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:static_text { title = "Target 1:", width = 75, font = '<system/bold>' },
                f:static_text { title = "Hue", width = 26, alignment = 'right' },
                f:slider { value = bind 'targetHue1', min = 0, max = 359, width = 145 },
                f:static_text {
                    title = bind { key = 'targetHue1', transform = function(v) return string.format("%3.0f°", v or 0) end },
                    width = 36,
                },
                f:static_text { title = "Sat", width = 24, alignment = 'right' },
                f:slider { value = bind 'targetSat1', min = 0, max = 100, width = 115 },
                f:static_text {
                    title = bind { key = 'targetSat1', transform = function(v) return string.format("%3.0f%%", v or 50) end },
                    width = 38,
                },
            },

            -- Target 2 (Optional Checkbox)
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:checkbox { title = "Target 2:", value = bind 'enableTarget2', width = 75 },
                f:static_text { title = "Hue", width = 26, alignment = 'right' },
                f:slider { value = bind 'targetHue2', min = 0, max = 359, width = 145, enabled = bind 'enableTarget2' },
                f:static_text {
                    title = bind { key = 'targetHue2', transform = function(v) return string.format("%3.0f°", v or 0) end },
                    width = 36,
                },
                f:static_text { title = "Sat", width = 24, alignment = 'right' },
                f:slider { value = bind 'targetSat2', min = 0, max = 100, width = 115, enabled = bind 'enableTarget2' },
                f:static_text {
                    title = bind { key = 'targetSat2', transform = function(v) return string.format("%3.0f%%", v or 50) end },
                    width = 38,
                },
            },

            -- Target 3 (Optional Checkbox)
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:checkbox { title = "Target 3:", value = bind 'enableTarget3', width = 75 },
                f:static_text { title = "Hue", width = 26, alignment = 'right' },
                f:slider { value = bind 'targetHue3', min = 0, max = 359, width = 145, enabled = bind 'enableTarget3' },
                f:static_text {
                    title = bind { key = 'targetHue3', transform = function(v) return string.format("%3.0f°", v or 0) end },
                    width = 36,
                },
                f:static_text { title = "Sat", width = 24, alignment = 'right' },
                f:slider { value = bind 'targetSat3', min = 0, max = 100, width = 115, enabled = bind 'enableTarget3' },
                f:static_text {
                    title = bind { key = 'targetSat3', transform = function(v) return string.format("%3.0f%%", v or 50) end },
                    width = 38,
                },
            },

            f:separator { fill_horizontal = 1 },

            -- Overall Strength (Default 0%)
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:static_text { title = "Strength:", width = 115, alignment = 'right', font = '<system/bold>' },
                f:slider { value = bind 'strength', min = 0, max = 100, width = 335 },
                f:static_text {
                    title = bind { key = 'strength', transform = function(v) return string.format("%.0f%%", v or 0) end },
                    width = 45,
                },
            },

            -- Luminance Color Density (Default 0%)
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:static_text { title = "Luminance Density:", width = 115, alignment = 'right' },
                f:slider { value = bind 'density', min = 0, max = 100, width = 335 },
                f:static_text {
                    title = bind { key = 'density', transform = function(v) return string.format("%.0f%%", v or 0) end },
                    width = 45,
                },
            },

            -- Hue Tolerance
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:static_text { title = "Tolerance:", width = 115, alignment = 'right' },
                f:slider { value = bind 'bandwidth', min = 15, max = 60, width = 335 },
                f:static_text {
                    title = bind { key = 'bandwidth', transform = function(v) return string.format("±%.0f°", v or 40) end },
                    width = 45,
                },
            },

            -- Pin Black & White Endpoints (Default 100%)
            f:row {
                fill_horizontal = 1,
                spacing = f:label_spacing(),
                f:static_text { title = "Pin Black & White:", width = 115, alignment = 'right' },
                f:slider { value = bind 'pinBlackWhite', min = 0, max = 100, width = 335 },
                f:static_text {
                    title = bind { key = 'pinBlackWhite', transform = function(v) return string.format("%.0f%%", v or 100) end },
                    width = 45,
                },
            },
        }

        LrDialogs.presentFloatingDialog(_PLUGIN, {
            title = "Daft Vectorscope (Color Separation)",
            contents = contents,
            resizable = false,
            save_frame = "DaftVectorscopeColorSepDialogPos",
            windowWillClose = function()
                props.isOpen = false
            end,
        })

        updateVectorscope(true)

        while props.isOpen do
            LrTasks.sleep(0.3)
        end
    end)
end)
