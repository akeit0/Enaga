param(
    [switch]$Run,
    [switch]$Watch
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectPath = Join-Path $projectRoot '..\Enaga.LayoutVisualizer.csproj'
$reactEntryPath = Join-Path $projectRoot 'dist\react-entry.mjs'

Push-Location $projectRoot
try {
    & pnpm run build:react
    & dnrelay build $projectPath

    if ($Watch -and $Run) {
        $watchProcess = Start-Process -FilePath 'pnpm.cmd' -ArgumentList 'run', 'watch:react' -WorkingDirectory $projectRoot -PassThru
        try {
            & dnrelay run $projectPath -- --react-entry $reactEntryPath --title "Enaga layout visualizer"
        }
        finally {
            if ($null -ne $watchProcess -and -not $watchProcess.HasExited) {
                Stop-Process -Id $watchProcess.Id
            }
        }
    }
    elseif ($Watch) {
        & pnpm run watch:react
    }
    elseif ($Run) {
        & dnrelay run $projectPath -- --react-entry $reactEntryPath --title "Enaga layout visualizer"
    }
}
finally {
    Pop-Location
}
