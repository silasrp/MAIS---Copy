# build.ps1 — Run from MAIS.Modules.CrimsAddinHealth/Agent/python/
# Produces Agent/agent.exe alongside this script's parent folder.

param(
    [string]$PythonExe = "python"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputDir = Join-Path $ScriptDir ".."

Write-Host "Installing dependencies..."
& $PythonExe -m pip install -r "$ScriptDir\requirements.txt" --quiet
& $PythonExe -m pip install pyinstaller --quiet

Write-Host "Building agent.exe..."
& $PythonExe -m PyInstaller `
    --onefile `
    --name agent `
    --distpath $OutputDir `
    --workpath "$ScriptDir\build" `
    --specpath "$ScriptDir\build" `
    --noconfirm `
    "$ScriptDir\agent.py"

Write-Host "Done. Output: $OutputDir\agent.exe"