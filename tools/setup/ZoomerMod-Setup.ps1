# Installe / désinstalle ZoomerMod dans Dale & Dawson, depuis n'importe quel dossier où le zip a été extrait.
# Lancé par "Installer ZoomerMod.bat" / "Desinstaller ZoomerMod.bat" (même logique que ZoomerMod-Installer.exe).
# Usage : powershell -ExecutionPolicy Bypass -File ZoomerMod-Setup.ps1 install|uninstall [dossier du jeu]
param([ValidateSet("install", "uninstall")][string]$Action = "install", [string]$Game = "")
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$appId = "2920570"

# Fichiers posés à la racine du jeu (le reste est dans BepInEx\ et dotnet\)
$rootFiles = "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "ZoomerMod ON.bat", "ZoomerMod OFF.bat",
             "LISEZ-MOI.txt", "ZoomerMod-Setup.ps1", "Installer ZoomerMod.bat", "Desinstaller ZoomerMod.bat"

# Dossier du jeu : bibliothèques Steam (libraryfolders.vdf) -> appmanifest_2920570.acf -> installdir
function Find-Game {
    $steam = $null
    foreach ($k in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam") {
        try { $p = Get-ItemProperty $k -ErrorAction Stop; $steam = if ($p.SteamPath) { $p.SteamPath } else { $p.InstallPath }; if ($steam) { break } } catch {}
    }
    $libs = @()
    if ($steam) {
        $libs += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += $m.Groups[1].Value.Replace('\\', '\') } }
    }
    $libs += "C:\Program Files (x86)\Steam"
    foreach ($lib in $libs | Select-Object -Unique) {
        $acf = Join-Path $lib "steamapps\appmanifest_$appId.acf"
        if (-not (Test-Path $acf)) { continue }
        $m = [regex]::Match((Get-Content $acf -Raw), '"installdir"\s+"([^"]+)"')
        if (-not $m.Success) { continue }
        $dir = Join-Path $lib ("steamapps\common\" + $m.Groups[1].Value)
        if (Test-Path (Join-Path $dir "DDSS.exe")) { return (Resolve-Path $dir).Path }
    }
    return $null
}

function Fail($msg) { Write-Host ""; Write-Host "ERREUR : $msg" -ForegroundColor Red; exit 1 }

if (-not $Game) {
    # Lancé depuis le dossier du jeu lui-même (zip extrait directement dedans, comme avant)
    if (Test-Path (Join-Path $here "DDSS.exe")) { $Game = $here } else { $Game = Find-Game }
}
if (-not $Game -or -not (Test-Path (Join-Path $Game "DDSS.exe"))) {
    Fail "dossier du jeu introuvable. Relance avec le chemin : ZoomerMod-Setup.ps1 $Action `"D:\...\Dale&Dawson`""
}
Write-Host "Dossier du jeu : $Game"
if (Get-Process DDSS -ErrorAction SilentlyContinue) { Fail "ferme le jeu d'abord." }

function Set-Enabled($value) {
    $ini = Join-Path $Game "doorstop_config.ini"
    if (Test-Path $ini) { (Get-Content $ini) -replace '^enabled\s*=.*', "enabled = $value" | Set-Content $ini }
}

if ($Action -eq "install") {
    $plugin = Join-Path $Game "BepInEx\plugins\DaleMod"
    if ((Resolve-Path $here).Path -ne $Game) {
        if (-not (Test-Path (Join-Path $here "BepInEx\plugins\DaleMod\DaleMod.dll"))) { Fail "fichiers du mod introuvables à côté de ce script : extrais tout le zip." }
        # Ancienne version du mod retirée (assets supprimés depuis) ; BepInEx\interop et cache sont gardés
        if (Test-Path $plugin) { Remove-Item $plugin -Recurse -Force }
        foreach ($f in $rootFiles) { $src = Join-Path $here $f; if (Test-Path $src) { Copy-Item $src $Game -Force } }
        foreach ($d in "BepInEx", "dotnet") { Copy-Item (Join-Path $here $d) $Game -Recurse -Force }
    }
    Set-Enabled "true"
    $v = Join-Path $plugin "version.txt"
    Write-Host ""
    Write-Host ("ZoomerMod {0} installé et activé." -f $(if (Test-Path $v) { (Get-Content $v -Raw).Trim() } else { "" })) -ForegroundColor Green
    Write-Host "Lance le jeu depuis Steam. Le premier lancement est long (1 à 3 minutes)."
}
else {
    $plugins = Join-Path $Game "BepInEx\plugins"
    $others = @(Get-ChildItem $plugins -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne "DaleMod" })
    Remove-Item (Join-Path $plugins "DaleMod") -Recurse -Force -ErrorAction SilentlyContinue
    if ($others.Count -gt 0) {
        # D'autres mods BepInEx sont installés : on ne retire que ZoomerMod et ses interrupteurs, le chargeur reste
        foreach ($f in "ZoomerMod ON.bat", "ZoomerMod OFF.bat", "LISEZ-MOI.txt") { Remove-Item (Join-Path $Game $f) -Force -ErrorAction SilentlyContinue }
        Write-Host ""
        Write-Host "ZoomerMod désinstallé. BepInEx est gardé : d'autres mods l'utilisent ($($others.Name -join ', '))." -ForegroundColor Green
    }
    else {
        foreach ($d in "BepInEx", "dotnet") { Remove-Item (Join-Path $Game $d) -Recurse -Force -ErrorAction SilentlyContinue }
        # Lancé depuis le dossier du jeu : ce script et ses .bat sont effacés une fois la fenêtre fermée (un .bat
        # effacé pendant qu'il tourne fait afficher "Le fichier de commandes est introuvable")
        $later = @()
        if ((Resolve-Path $here).Path -eq $Game) { $later = "ZoomerMod-Setup.ps1", "Installer ZoomerMod.bat", "Desinstaller ZoomerMod.bat" }
        foreach ($f in $rootFiles) { if ($later -notcontains $f) { Remove-Item (Join-Path $Game $f) -Force -ErrorAction SilentlyContinue } }
        if ($later.Count -gt 0) {
            $parent = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
            $paths = ($later | ForEach-Object { "'" + (Join-Path $Game $_).Replace("'", "''") + "'" }) -join ","
            Start-Process powershell -WindowStyle Hidden -ArgumentList "-NoProfile", "-Command",
                "Wait-Process -Id $parent -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300; Remove-Item $paths -Force -ErrorAction SilentlyContinue"
        }
        Write-Host ""
        Write-Host "ZoomerMod désinstallé : le jeu est revenu à l'origine." -ForegroundColor Green
    }
}
