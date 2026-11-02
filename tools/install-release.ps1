# Installe dans le jeu le mod d'une version publiée (zip de dist\, sinon téléchargé depuis la release GitHub).
# Sert à rejouer avec les amis en version publiée entre deux tests de la version de dev (dev.cmd la réinstalle).
# Usage : powershell -ExecutionPolicy Bypass -File tools\install-release.ps1 0.6.2
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$game = "C:\Program Files (x86)\Steam\steamapps\common\Dale&Dawson"
$plugin = Join-Path $game "BepInEx\plugins\DaleMod"

if (Get-Process DDSS -ErrorAction SilentlyContinue) { throw "Ferme le jeu d'abord (DDSS.exe tourne encore)." }

$zip = Join-Path $root "dist\ZoomerMod-$Version.zip"
if (-not (Test-Path $zip)) {
    Write-Host "Zip absent, téléchargement de la release v$Version..."
    $gh = "C:\Program Files\GitHub CLI\gh.exe"
    & $gh release download "v$Version" --repo Tarmouille/zoomermod --pattern "ZoomerMod-$Version.zip" --dir (Join-Path $root "dist") --clobber
    if ($LASTEXITCODE -ne 0) { throw "Téléchargement impossible" }
}

# On remplace tout le dossier du mod (DLL + Assets) par celui du zip
if (Test-Path $plugin) { Remove-Item $plugin -Recurse -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
$n = 0
try {
    foreach ($e in $archive.Entries) {
        if (-not $e.FullName.StartsWith("BepInEx/plugins/DaleMod/") -or $e.FullName.EndsWith("/")) { continue }
        $dest = Join-Path $game ($e.FullName.Replace('/', '\'))
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
        $n++
    }
} finally { $archive.Dispose() }
"ZoomerMod $Version installé ($n fichiers). Le menu du jeu doit afficher 'ZoomerMod $Version'."
