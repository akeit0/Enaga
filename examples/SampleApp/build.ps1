param(
    [switch]$Run,
    [switch]$SkipTypeCheck
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectPath = Join-Path $projectRoot 'Enaga.SampleApp\\Enaga.SampleApp.csproj'
$reactEntryPath = Join-Path $projectRoot 'dist\react-entry.mjs'

Push-Location $projectRoot
try {
    if (-not $SkipTypeCheck) {
        & pnpm exec tsc --noEmit -p .
    }

    & pnpm run build:react
    & dnrelay build $projectPath 

    if ($Run) {
        & dnrelay run --project $projectPath -- --react-entry $reactEntryPath
    }
}
finally {
    Pop-Location
}
