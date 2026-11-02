# Package ZoomerMod pour les amis : BepInEx + le mod en Release (sans menu F7), prêt à dézipper dans le dossier du jeu.
# Usage : powershell -ExecutionPolicy Bypass -File tools\package.ps1
# Résultat : dist\ZoomerMod-<version>.zip
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$game = "C:\Program Files (x86)\Steam\steamapps\common\Dale&Dawson"
$mod = Join-Path $root "DaleMod"

$version = [regex]::Match((Get-Content (Join-Path $mod "Plugin.cs") -Raw), 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw "Version introuvable dans Plugin.cs" }

# 0. Musiques / sons en .mp3 dans Assets (ex. Assets\karaoke\) -> .wav lisibles par le mod
python (Join-Path $PSScriptRoot "convert_audio.py") (Join-Path $mod "Assets")
if ($LASTEXITCODE -ne 0) { throw "Échec de la conversion audio" }

# 1. Build Release (n'écrase pas le mod de debug installé dans le jeu)
dotnet build $mod -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Échec du build Release" }
$dll = Join-Path $mod "bin\Release\net6.0\DaleMod.dll"

# 2. Dossier de préparation, même arborescence que le dossier du jeu
$stage = Join-Path $root "dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# Chargeur BepInEx (doorstop) et runtime .NET
foreach ($f in "winhttp.dll", "doorstop_config.ini", ".doorstop_version") { Copy-Item (Join-Path $game $f) $stage }
Copy-Item (Join-Path $game "dotnet") (Join-Path $stage "dotnet") -Recurse

# BepInEx : core + bibliothèques Unity (évite un téléchargement au premier lancement).
# interop/ et cache/ ne sont pas inclus : BepInEx les régénère au premier lancement.
$bep = Join-Path $stage "BepInEx"
New-Item -ItemType Directory -Force (Join-Path $bep "config"), (Join-Path $bep "plugins\DaleMod\Assets") | Out-Null
Copy-Item (Join-Path $game "BepInEx\core") (Join-Path $bep "core") -Recurse
Copy-Item (Join-Path $game "BepInEx\unity-libs") (Join-Path $bep "unity-libs") -Recurse

# Config : pas de fenêtre console chez les amis (le log fichier BepInEx\LogOutput.log reste actif pour dépanner)
$cfg = Get-Content (Join-Path $game "BepInEx\config\BepInEx.cfg")
$section = ""
$cfg = foreach ($line in $cfg) {
    if ($line -match '^\[(.+)\]') { $section = $Matches[1] }
    if ($section -eq "Logging.Console" -and $line -match '^Enabled\s*=') { "Enabled = false" } else { $line }
}
Set-Content (Join-Path $bep "config\BepInEx.cfg") $cfg -Encoding utf8

# Le mod
Copy-Item $dll (Join-Path $bep "plugins\DaleMod")
# Assets, sous-dossiers compris (karaoke\, popups\…), sans les .mp3 d'origine
$assets = Join-Path $mod "Assets"
Get-ChildItem $assets -File -Recurse | Where-Object { $_.Extension -ne ".mp3" } | ForEach-Object {
    $dest = Join-Path (Join-Path $bep "plugins\DaleMod\Assets") $_.FullName.Substring($assets.Length + 1)
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item $_.FullName $dest
}

# Interrupteurs : activent / désactivent le chargement de BepInEx (ligne "enabled" de doorstop_config.ini).
# Textes sans accents : la console cmd ne les affiche pas correctement.
foreach ($s in @(@{ File = "ZoomerMod ON.bat"; Value = "true"; Msg = "ZoomerMod ACTIVE. Lance le jeu depuis Steam." },
                 @{ File = "ZoomerMod OFF.bat"; Value = "false"; Msg = "ZoomerMod DESACTIVE. Le jeu se lance sans mod." })) {
@"
@echo off
cd /d "%~dp0"
if not exist doorstop_config.ini (
  echo doorstop_config.ini introuvable : ce fichier doit etre dans le dossier du jeu, a cote de DDSS.exe.
  pause
  exit /b 1
)
powershell -NoProfile -Command "(Get-Content 'doorstop_config.ini') -replace '^enabled\s*=.*','enabled = $($s.Value)' | Set-Content 'doorstop_config.ini'"
echo $($s.Msg)
pause
"@ | Set-Content (Join-Path $stage $s.File) -Encoding ascii
}

# Notice
@"
ZoomerMod v$version pour Dale & Dawson
=====================================

INSTALLATION
1. Steam > clic droit sur Dale & Dawson > Gérer > Parcourir les fichiers locaux.
2. Dézippe TOUT le contenu de ce zip dans ce dossier (à côté de DDSS.exe).
   Si Windows demande de remplacer des fichiers, accepte.
3. Lance le jeu depuis Steam, comme d'habitude.

Le PREMIER lancement est long (1 à 3 minutes, écran noir possible) : le mod prépare ses fichiers.
C'est normal, ne ferme pas le jeu. Les lancements suivants sont rapides.

Si tout marche, "ZoomerMod v$version" s'affiche en bas à droite du menu principal.

ACTIVER / DÉSACTIVER LE MOD
Double-clique sur "ZoomerMod OFF.bat" (dans le dossier du jeu) pour jouer sans le mod, par exemple en partie publique.
Double-clique sur "ZoomerMod ON.bat" pour le réactiver. Le jeu doit être fermé.

IMPORTANT
- Tout le monde dans la partie doit avoir exactement la même version du mod.
- Joue uniquement en lobby privé avec des gens qui ont le mod.

EN CAS DE PROBLÈME
Envoie le fichier BepInEx\LogOutput.log (dans le dossier du jeu).

DÉSINSTALLER
Supprime winhttp.dll du dossier du jeu (le jeu redevient normal). Pour tout enlever : supprime aussi
doorstop_config.ini, .doorstop_version, les deux fichiers "ZoomerMod ON/OFF.bat", et les dossiers BepInEx et dotnet.

Crédits : musique du karaoké "Rise of the Machines", Music by Karl Casey @ White Bat Audio ;
sniff.wav par spookymodem (CC-BY 3.0). Voir BepInEx\plugins\DaleMod\Assets\CREDITS.txt
"@ | Set-Content (Join-Path $stage "LISEZ-MOI.txt") -Encoding utf8

# 3. Zip
$zip = Join-Path $root "dist\ZoomerMod-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
# Zip écrit à la main : Compress-Archive (PowerShell 5.1) met des "\" dans les chemins, mal lus par 7-Zip et compagnie
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, "Create")
try {
    Get-ChildItem $stage -Recurse -File -Force | ForEach-Object {
        $name = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $name, "Optimal")
    }
} finally { $archive.Dispose() }
$z = [IO.Compression.ZipFile]::OpenRead($zip); $entries = $z.Entries.FullName; $z.Dispose()
if (-not ($entries -contains ".doorstop_version")) { Write-Warning ".doorstop_version absent du zip" }
Remove-Item $stage -Recurse -Force
"{0} ({1:N1} Mo, {2} fichiers)" -f $zip, ((Get-Item $zip).Length / 1MB), $entries.Count
