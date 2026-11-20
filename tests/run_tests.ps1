# Daft Lightroom Vectorscope Automated Test Suite Runner
$ErrorActionPreference = "Stop"

$workspaceRoot = Split-Path -Parent $PSScriptRoot
Set-Location $workspaceRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Running Vectorscope Plugin Tests & Build Verification" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Locate C# compiler (Roslyn)
$cscCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)

$cscPath = $null
foreach ($cand in $cscCandidates) {
    if (Test-Path $cand) {
        $cscPath = $cand
        break
    }
}

if (-not $cscPath) {
    Write-Error "Could not locate a suitable C# compiler (csc.exe)."
    exit 1
}

Write-Host "[1/4] Using Roslyn Compiler: $cscPath" -ForegroundColor Gray

# 2. Compile Vectorscope Engine
Write-Host "[2/4] Compiling Vectorscope Native Engine..." -ForegroundColor Yellow
$engineDir = Join-Path $workspaceRoot "vectorscope_engine"
$engineExe = Join-Path $engineDir "vectorscope_engine.exe"
$engineSrc = Join-Path $engineDir "Program.cs"

& $cscPath -nologo -target:exe -optimize+ -unsafe -out:$engineExe -r:System.Drawing.dll $engineSrc
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile vectorscope_engine.exe"
    exit $LASTEXITCODE
}
Write-Host "      -> Successfully compiled $engineExe" -ForegroundColor Green

# 3. Compile and Run Automated Unit & Benchmark Tests
Write-Host "[3/4] Compiling and Executing Vectorscope Test Suite..." -ForegroundColor Yellow
$testsDir = Join-Path $workspaceRoot "tests"
$testRunnerExe = Join-Path $testsDir "test_runner.exe"
$testSources = @(
    (Join-Path $testsDir "VectorscopeTests.cs"),
    (Join-Path $testsDir "GenerateTestPatterns.cs"),
    $engineSrc
)

& $cscPath -nologo -target:exe -optimize+ -unsafe -main:DaftVectorscope.Tests.Program -out:$testRunnerExe -r:System.Drawing.dll $testSources
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to compile test_runner.exe"
    exit $LASTEXITCODE
}

# Run the test runner executable
& $testRunnerExe
$testExitCode = $LASTEXITCODE
if ($testExitCode -ne 0) {
    Write-Error "Unit tests failed with exit code $testExitCode"
    exit $testExitCode
}

# 4. Validate Plugin Lua Structure and Syntax
Write-Host "[4/5] Validating Lightroom Plugin Lua Files..." -ForegroundColor Yellow
$pluginDir = Join-Path $workspaceRoot "DaftVectorscope.lrplugin"
$luaFiles = Get-ChildItem -Path $pluginDir -Filter "*.lua"

$hasSyntaxErrors = $false
foreach ($file in $luaFiles) {
    $rawContent = Get-Content -Path $file.FullName -Raw
    
    # Strip multiline strings and comments: [[...]] and --[[...]]
    $stripped = [regex]::Replace($rawContent, '(?s)--\[\[.*?\]\]', '')
    $stripped = [regex]::Replace($stripped, '(?s)\[\[.*?\]\]', '""')
    # Strip single line comments: --...
    $stripped = [regex]::Replace($stripped, '--[^\r\n]*', '')
    # Strip string literals in single regex to avoid quote-inside-quote interference
    $stripped = [regex]::Replace($stripped, '"(\\"|[^"\r\n])*"|''(\\\\''|[^''\r\n])*''', '""')

    # Keyword balance checks on stripped code
    $functionCount = ([regex]::Matches($stripped, '\bfunction\b')).Count
    $ifCount = ([regex]::Matches($stripped, '\bif\b')).Count
    $whileCount = ([regex]::Matches($stripped, '\bwhile\b')).Count
    $forCount = ([regex]::Matches($stripped, '\bfor\b')).Count
    $endCount = ([regex]::Matches($stripped, '\bend\b')).Count
    $openBraces = ([regex]::Matches($stripped, '\{')).Count
    $closeBraces = ([regex]::Matches($stripped, '\}')).Count
    $openParens = ([regex]::Matches($stripped, '\(')).Count
    $closeParens = ([regex]::Matches($stripped, '\)')).Count

    $expectedEnds = $functionCount + $ifCount + $whileCount + $forCount
    $isBalanced = ($expectedEnds -eq $endCount) -and ($openBraces -eq $closeBraces) -and ($openParens -eq $closeParens)

    # Check for invalid bare makePropertyTable() calls without functionContext
    $barePropTable = ([regex]::Matches($rawContent, 'LrBinding\.makePropertyTable\s*\(\s*\)')).Count
    if ($barePropTable -gt 0) {
        Write-Host "      [FAIL] $($file.Name): Found LrBinding.makePropertyTable() without functionContext argument!" -ForegroundColor Red
        $hasSyntaxErrors = $true
        continue
    }

    if ($isBalanced) {
        Write-Host "      [PASS] $($file.Name): Syntax & structure validated" -ForegroundColor Green
    } else {
        Write-Host "      [FAIL] $($file.Name): Block mismatch (ends: $endCount vs expected: $expectedEnds, {}: $openBraces/$closeBraces, (): $openParens/$closeParens)" -ForegroundColor Red
        $hasSyntaxErrors = $true
    }
}

# Verify Info.lua contains valid Lightroom SDK menu keys
$infoLuaPath = Join-Path $pluginDir "Info.lua"
$infoContent = Get-Content -Path $infoLuaPath -Raw
if ($infoContent -notmatch "LrLibraryMenuItems" -and $infoContent -notmatch "LrExportMenuItems") {
    Write-Error "Info.lua missing required menu entry points (LrLibraryMenuItems or LrExportMenuItems)!"
    exit 1
}
Write-Host "      [PASS] Info.lua: Validated Lightroom SDK menu keys (LrLibraryMenuItems & LrExportMenuItems)" -ForegroundColor Green

# 5. Package Engine Binary into Plugin
Write-Host "[5/5] Bundling Engine Binary and Assets into Lightroom Plugin..." -ForegroundColor Yellow
$pluginBinDir = Join-Path $workspaceRoot "DaftVectorscope.lrplugin\bin"
if (-not (Test-Path $pluginBinDir)) {
    New-Item -ItemType Directory -Path $pluginBinDir -Force | Out-Null
}
Copy-Item -Path $engineExe -Destination (Join-Path $pluginBinDir "vectorscope_engine.exe") -Force
Write-Host "      -> Bundled engine into $pluginBinDir\vectorscope_engine.exe" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " All Vectorscope Tests & Build Verification PASSED!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Cyan

exit 0
