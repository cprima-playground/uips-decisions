<#
.SYNOPSIS
    Pack the UiPath project and execute a workflow entry point.

.DESCRIPTION
    1. Locates uipcli under %LOCALAPPDATA%\cpmf\tools\uipcli-*
    2. Locates UiRobot.exe across enterprise (%ProgramFiles%, %LOCALAPPDATA%)
       and community (%ProgramFiles%\UiPathPlatform\Studio\<version>) installs.
    3. Packs project/project.json into <OutputDir>\Decisions.<Version>.nupkg
    4. Executes the package with the supplied entry point.

.PARAMETER EntryPoint
    Relative path to the entry point XAML inside the package, e.g.
        Tests\RoutingPipeline\Workflow_RoutingPipeline_Pipeline.xaml
    Ignored when -PackOnly is set.

.PARAMETER PackOnly
    Pack the project without executing. UiRobot detection is skipped.

.PARAMETER Version
    Package version to build.
    Defaults to the next patch increment after the highest existing .nupkg
    found in OutputDir.

.PARAMETER OutputDir
    Folder where .nupkg files are written.
    Defaults to <repo-root>\out  (created if absent; gitignored).

.EXAMPLE
    .\scripts\pack_and_run.ps1 `
        -EntryPoint "Tests\RoutingPipeline\Workflow_RoutingPipeline_Pipeline.xaml"

.EXAMPLE
    .\scripts\pack_and_run.ps1 `
        -EntryPoint "Tests\EligibilityDecision\Workflow_EligibilityDecision_IfElse.xaml" `
        -Version 1.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $EntryPoint = '',

    [switch] $PackOnly,

    [string] $Version   = '',

    [string] $OutputDir = ''
)

if (-not $PackOnly -and -not $EntryPoint) {
    throw "Provide -EntryPoint <path> or use -PackOnly."
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve paths ─────────────────────────────────────────────────────────────
$RepoRoot    = Split-Path $PSScriptRoot -Parent
$ProjectJson = Join-Path $RepoRoot 'project\project.json'

if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot 'out'
}
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

# ── Find uipcli ───────────────────────────────────────────────────────────────
$uipcli = Get-ChildItem "$env:LOCALAPPDATA\cpmf\tools\uipcli-*\uipcli.exe" `
              -ErrorAction SilentlyContinue |
          Sort-Object FullName -Descending |
          Select-Object -First 1 -ExpandProperty FullName

if (-not $uipcli) {
    throw "uipcli.exe not found under $env:LOCALAPPDATA\cpmf\tools\uipcli-*"
}

# ── Find UiRobot.exe (skipped for pack-only) ─────────────────────────────────
function Find-UiRobot {
    # Enterprise: %ProgramFiles%\UiPath\Studio\
    $fixed = @(
        "$env:ProgramFiles\UiPath\Studio\UiRobot.exe",
        "$env:ProgramFiles\UiPath\UiRobot.exe",
        # Enterprise user: %LOCALAPPDATA%\UiPath\Studio\
        "$env:LOCALAPPDATA\UiPath\Studio\UiRobot.exe",
        "$env:LOCALAPPDATA\Programs\UiPath\Studio\UiRobot.exe",
        "$env:LOCALAPPDATA\UiPath\UiRobot.exe"
    )
    foreach ($p in $fixed) {
        if (Test-Path $p) { return $p }
    }
    # Community: %ProgramFiles%\UiPathPlatform\Studio\<version>\UiRobot.exe
    # Take the highest version found.
    $community = Get-ChildItem "$env:ProgramFiles\UiPathPlatform\Studio\*\UiRobot.exe" `
                     -ErrorAction SilentlyContinue |
                 Sort-Object FullName -Descending |
                 Select-Object -First 1 -ExpandProperty FullName
    if ($community) { return $community }

    return $null
}

$uiRobot = $null
if (-not $PackOnly) {
    $uiRobot = Find-UiRobot
    if (-not $uiRobot) {
        throw @"
UiRobot.exe not found. Searched:
  $env:ProgramFiles\UiPath\Studio\
  $env:LOCALAPPDATA\UiPath\Studio\
  $env:LOCALAPPDATA\Programs\UiPath\Studio\
  $env:ProgramFiles\UiPathPlatform\Studio\*\
"@
    }
}

# ── Auto-version: next patch after highest existing nupkg in OutputDir ────────
if (-not $Version) {
    $highest = Get-ChildItem (Join-Path $OutputDir 'Decisions.*.nupkg') `
                   -ErrorAction SilentlyContinue |
               ForEach-Object { $_.BaseName -replace '^Decisions\.', '' } |
               Where-Object    { $_ -match '^\d+\.\d+\.\d+$' } |
               Sort-Object     { [version]$_ } -Descending |
               Select-Object   -First 1
    if ($highest) {
        $v       = [version]$highest
        $Version = "$($v.Major).$($v.Minor).$($v.Build + 1)"
    } else {
        $Version = '0.1.0'
    }
}

$NupkgPath = Join-Path $OutputDir "Decisions.$Version.nupkg"

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host "uipcli    : $uipcli"
if ($uiRobot)   { Write-Host "UiRobot   : $uiRobot" }
Write-Host "Version   : $Version"
Write-Host "OutputDir : $OutputDir"
if ($EntryPoint){ Write-Host "EntryPoint: $EntryPoint" }
Write-Host ""

# ── Remove stale generated file (Studio lock artefact) ────────────────────────
$triggers = Join-Path $RepoRoot 'project\.local\generated\Triggers.Generated.xaml'
if (Test-Path $triggers) { Remove-Item $triggers -Force }

# ── Pack ──────────────────────────────────────────────────────────────────────
Write-Host "==> Packing v$Version ..."
$packOutput = & $uipcli package pack $ProjectJson --output $OutputDir --version $Version 2>&1
$packOutput | Write-Host
if ($LASTEXITCODE -ne 0) {
    # Friendly message for the Studio database lock
    if ($packOutput -match 'already opened in another Studio instance') {
        Write-Host ""
        Write-Host "ERROR: UiPath Studio has the project open and holds a database lock." -ForegroundColor Red
        Write-Host "       Close Studio (or the project), then retry." -ForegroundColor Yellow
        exit 1
    }
    throw "Pack failed (exit code $LASTEXITCODE)"
}
Write-Host ""

# ── Execute ───────────────────────────────────────────────────────────────────
if (-not $PackOnly) {
    Write-Host "==> Executing: $EntryPoint"
    & $uiRobot execute --file $NupkgPath --entry $EntryPoint
    if ($LASTEXITCODE -ne 0) { throw "Execution failed (exit code $LASTEXITCODE)" }
}
