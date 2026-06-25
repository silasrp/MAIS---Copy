# build.ps1 — Run from MAIS.Modules.IdaLogIngestion/Agent/python/
# Produces Agent/IdaLogIngestionAgent.exe alongside this script's parent folder.

param(
    [string]$PythonExe = "python"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path $ScriptDir ".."

Write-Host "Installing dependencies..."
& $PythonExe -m pip install -r "$ScriptDir\requirements.txt" --quiet
& $PythonExe -m pip install pyinstaller --quiet

Write-Host "Building IdaLogIngestionAgent.exe..."
& $PythonExe -m PyInstaller `
    --onefile `
    --name IdaLogIngestionAgent `
    --distpath $OutputDir `
    --workpath "$ScriptDir\build" `
    --specpath "$ScriptDir\build" `
    --noconfirm `
    "$ScriptDir\agent.py"

Write-Host "Done. Output: $OutputDir\IdaLogIngestionAgent.exe"
