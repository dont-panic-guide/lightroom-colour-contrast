local LrPathUtils = import 'LrPathUtils'
local LrFileUtils = import 'LrFileUtils'
local LrTasks = import 'LrTasks'
local LrExportSession = import 'LrExportSession'

local VectorscopeRenderer = {}

-- Monotonic counter used to give every render unique output / batch / log files.
-- Unique names avoid (a) Lightroom's f:picture caching a path and showing a stale image,
-- (b) the engine failing to overwrite a PNG that Lightroom currently holds open, and
-- (c) overlapping renders clobbering each other's batch or log file.
local renderCounter = 0

function VectorscopeRenderer.getTempDirectory()
    local tempDir = LrPathUtils.child(LrPathUtils.getStandardFilePath('temp'), 'DaftVectorscope')
    if not LrFileUtils.exists(tempDir) then
        LrFileUtils.createAllDirectories(tempDir)
    end
    return tempDir
end

-- Dedicated folder for LrExportSession output so scans never pick up unrelated files
local function getExportDirectory()
    local exportDir = LrPathUtils.child(VectorscopeRenderer.getTempDirectory(), 'export')
    if not LrFileUtils.exists(exportDir) then
        LrFileUtils.createAllDirectories(exportDir)
    end
    return exportDir
end

function VectorscopeRenderer.getThumbPath()
    return LrPathUtils.child(VectorscopeRenderer.getTempDirectory(), 'thumb_input.jpg')
end

function VectorscopeRenderer.getEnginePath()
    local binPath = LrPathUtils.child(_PLUGIN.path, 'bin')
    return LrPathUtils.child(binPath, 'vectorscope_engine.exe')
end

local function logDebug(msg)
    pcall(function()
        local logPath = LrPathUtils.child(VectorscopeRenderer.getTempDirectory(), 'plugin_debug.log')
        local f = io.open(logPath, 'a')
        if f then
            f:write(os.date('%Y-%m-%d %H:%M:%S') .. " " .. tostring(msg) .. "\n")
            f:close()
        end
    end)
end
VectorscopeRenderer.log = logDebug

local function isJpeg(path)
    local ext = string.lower(LrPathUtils.extension(path) or '')
    return ext == 'jpg' or ext == 'jpeg'
end

-- Remove old scope PNGs / batch / log / stats files, keeping any path in `keep` (a set)
local function cleanupOldOutputs(tempDir, keep)
    for file in LrFileUtils.files(tempDir) do
        local leaf = LrPathUtils.leafName(file)
        if not keep[file] and (
            string.find(leaf, '^scope_%d+%.png$') or
            string.find(leaf, '^run_scope_%d+%.bat$') or
            string.find(leaf, '^engine_log_%d+%.txt$') or
            string.find(leaf, '^stats_%d+%.txt$')
        ) then
            pcall(function() LrFileUtils.delete(file) end)
        end
    end
end

-- Call onComplete exactly once, even if the callback itself errors
local function safeComplete(onComplete, outPath, bandSat, err)
    if onComplete then
        local ok, cbErr = pcall(onComplete, outPath, bandSat, err)
        if not ok then logDebug("onComplete callback error: " .. tostring(cbErr)) end
    end
end

local function parseStatsFile(path)
    if not path or not LrFileUtils.exists(path) then return nil end
    local f = io.open(path, 'r')
    if not f then return nil end
    local bandSat = {}
    for line in f:lines() do
        line = string.gsub(line, "^\239\187\191", "")
        line = string.gsub(line, "\r$", "")
        local idx, val = string.match(line, "^band_sat_(%d+)=(.+)$")
        if idx and val then
            local i = tonumber(idx)
            if i and i >= 1 and i <= 8 then
                bandSat[i] = tonumber(val) or 25.0
            end
        end
    end
    f:close()
    for i = 1, 8 do
        if type(bandSat[i]) ~= 'number' then
            return nil
        end
    end
    return bandSat
end

-- Synchronous engine run. Must be called from inside an async task.
-- Returns outPngPath, bandSat, errorMessage
local function runEngine(options, inJpgPath, previousOutPath, measureStats)
    local tempDir = VectorscopeRenderer.getTempDirectory()

    if not inJpgPath or not LrFileUtils.exists(inJpgPath) then
        return nil, nil, "No preview image for this photo"
    end

    local exePath = VectorscopeRenderer.getEnginePath()
    if not LrFileUtils.exists(exePath) then
        return nil, nil, "Engine missing: bin\\vectorscope_engine.exe"
    end

    renderCounter = renderCounter + 1
    local id = tostring(renderCounter)
    local outPngPath = LrPathUtils.child(tempDir, 'scope_' .. id .. '.png')
    local batPath = LrPathUtils.child(tempDir, 'run_scope_' .. id .. '.bat')
    local logPath = LrPathUtils.child(tempDir, 'engine_log_' .. id .. '.txt')
    local statsPath = measureStats and LrPathUtils.child(tempDir, 'stats_' .. id .. '.txt') or nil

    local targetsArg = ""
    if options.targetsArg and options.targetsArg ~= "" then
        targetsArg = ' --targets "' .. tostring(options.targetsArg) .. '"'
    end

    local statsArg = ""
    if statsPath then
        statsArg = string.format(
            ' --stats-output "%s"',
            statsPath
        )
    end

    local batContent = string.format(
        '@echo off\r\n"%s" --input "%s" --output "%s" --size 512 --skinline true --targets true --rings true --axes true%s%s > "%s" 2>&1\r\nexit /b %%ERRORLEVEL%%\r\n',
        exePath, inJpgPath, outPngPath,
        targetsArg,
        statsArg,
        logPath
    )

    -- Binary mode so Lua doesn't turn \r\n into \r\r\n on Windows
    local batFile = io.open(batPath, 'wb')
    if not batFile then
        return nil, nil, "Cannot write batch file in temp folder"
    end
    batFile:write(batContent)
    batFile:close()

    local cmd = '"cmd.exe /c "' .. batPath .. '""'
    local exitCode = LrTasks.execute(cmd)

    local detail = nil
    if exitCode ~= 0 or not LrFileUtils.exists(outPngPath) then
        local lf = io.open(logPath, 'r')
        if lf then
            detail = lf:read('*all')
            lf:close()
        end
        if not detail or detail == "" then
            detail = "Engine exit code " .. tostring(exitCode)
        end
        detail = string.match(detail, "^[^\r\n]+") or detail
    end

    local bandSat = nil
    if not detail and statsPath then
        bandSat = parseStatsFile(statsPath)
    end
    if statsPath then pcall(function() LrFileUtils.delete(statsPath) end) end
    pcall(function() LrFileUtils.delete(batPath) end)
    pcall(function() LrFileUtils.delete(logPath) end)

    if detail then
        logDebug("runEngine FAILED: " .. detail)
        return nil, nil, detail
    end

    cleanupOldOutputs(tempDir, {
        [outPngPath] = true,
        [previousOutPath or ''] = true,
    })
    return outPngPath, bandSat, nil
end

-- Fast path: re-render from the cached preview of the current photo (~2 ms engine time)
function VectorscopeRenderer.renderCached(options, onComplete, previousOutPath)
    LrTasks.startAsyncTask(function()
        local ok, outPath, bandSat, err = LrTasks.pcall(runEngine, options, VectorscopeRenderer.getThumbPath(), previousOutPath, false)
        if not ok then
            logDebug("renderCached error: " .. tostring(outPath))
            safeComplete(onComplete, nil, nil, tostring(outPath))
            return
        end
        safeComplete(onComplete, outPath, bandSat, err)
    end)
end

-- Pipeline 1: LrExportSession (applies all Develop edits, sRGB, 1024 px)
local function exportViaSession(photo)
    local exportDir = getExportDirectory()

    -- Empty the export folder so the only JPEG left afterwards is ours
    for file in LrFileUtils.files(exportDir) do
        pcall(function() LrFileUtils.delete(file) end)
    end

    local exportSettings = {
        LR_exportServiceProvider = "com.adobe.ag.export.file",
        LR_format = "JPEG",
        LR_jpeg_quality = 0.75,
        LR_export_colorSpace = "sRGB",
        LR_export_bitDepth = 8,
        LR_size_doConstrain = true,
        LR_size_maxHeight = 1024,
        LR_size_maxWidth = 1024,
        LR_size_units = "pixels",
        LR_size_resizeType = "wh",
        LR_size_doNotEnlarge = true,
        LR_export_destinationType = "specificFolder",
        LR_export_destinationPathPrefix = exportDir,
        LR_export_useSubfolder = false,
        LR_collisionHandling = "overwrite",
        LR_reimportExportedPhoto = false,
        LR_outputSharpeningOn = false,
        LR_useWatermark = false,
        LR_minimizeEmbeddedMetadata = true,
        LR_removeLocationMetadata = true,
        LR_renamingTokensOn = false,
    }

    local exportSession = LrExportSession({
        photosToExport = { photo },
        exportSettings = exportSettings,
    })
    exportSession:doExportOnCurrentTask()

    for file in LrFileUtils.files(exportDir) do
        if isJpeg(file) then
            return file
        end
    end
    return nil
end

-- Pipeline 2: Lightroom preview thumbnail (used if the original file is offline etc.)
local function exportViaThumbnail(photo, thumbPath)
    local thumbData = nil
    local done = false

    -- The returned request object MUST stay referenced, or it can be garbage
    -- collected before the callback fires.
    local request = photo:requestJpegThumbnail(1024, 1024, function(jpegData, err)
        thumbData = jpegData
        done = true
    end)

    local waited = 0
    while not done and waited < 50 do -- up to 5 s
        LrTasks.sleep(0.1)
        waited = waited + 1
    end
    request = nil -- release

    if thumbData and #thumbData > 1000 then
        local f = io.open(thumbPath, 'wb')
        if f then
            f:write(thumbData)
            f:close()
            return thumbPath
        end
    end
    return nil
end

-- Full path: extract a fresh preview for `photo`, then render it
function VectorscopeRenderer.exportAndRender(photo, options, onComplete, previousOutPath)
    LrTasks.startAsyncTask(function()
        local ok, err = LrTasks.pcall(function()
            local thumbPath = VectorscopeRenderer.getThumbPath()

            -- Delete the old preview first so a failed extraction can never
            -- render the previous photo under the new photo's name.
            if LrFileUtils.exists(thumbPath) then
                LrFileUtils.delete(thumbPath)
            end

            local source = nil
            local okExport, exportResult = LrTasks.pcall(exportViaSession, photo)
            if okExport then
                source = exportResult
            else
                logDebug("LrExportSession failed: " .. tostring(exportResult))
            end

            if source and LrFileUtils.exists(source) then
                LrFileUtils.copy(source, thumbPath)
                pcall(function() LrFileUtils.delete(source) end)
            else
                logDebug("Falling back to requestJpegThumbnail")
                local okThumb, thumbResult = LrTasks.pcall(exportViaThumbnail, photo, thumbPath)
                if not okThumb then
                    logDebug("requestJpegThumbnail failed: " .. tostring(thumbResult))
                end
            end

            if not LrFileUtils.exists(thumbPath) then
                local reason = okExport and "Export produced no file" or tostring(exportResult)
                safeComplete(onComplete, nil, nil, reason)
                return
            end

            local outPath, bandSat, renderErr = runEngine(options, thumbPath, previousOutPath, true)
            safeComplete(onComplete, outPath, bandSat, renderErr)
        end)

        if not ok then
            logDebug("exportAndRender error: " .. tostring(err))
            safeComplete(onComplete, nil, nil, tostring(err))
        end
    end)
end

return VectorscopeRenderer
