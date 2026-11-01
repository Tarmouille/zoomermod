# ZoomerMod

Mod fan-made pour **Dale & Dawson Stationery Supplies** : nouvelles tâches, rôles et effets, à jouer entre amis.

## Installation

1. Télécharge le zip de la dernière version dans [Releases](../../releases).
2. Steam > clic droit sur Dale & Dawson > Gérer > Parcourir les fichiers locaux.
3. Dézippe **tout** le contenu du zip dans ce dossier (à côté de `DDSS.exe`).
4. Lance le jeu depuis Steam. Le premier lancement est long (1 à 3 minutes).

Si tout marche, `ZoomerMod vX.Y.Z` s'affiche en bas à droite du menu principal.
Tout le monde dans la partie doit avoir la même version. Joue uniquement en lobby privé avec des gens qui ont le mod.

`ZoomerMod OFF.bat` / `ZoomerMod ON.bat` (dans le dossier du jeu) désactivent / réactivent le mod.

## Ce que fait (et ne fait pas) le mod

- Le zip contient [BepInEx 6](https://github.com/BepInEx/BepInEx) (le chargeur de mods) et `DaleMod.dll`, compilé depuis ce dépôt.
- Le mod ne se connecte à rien sur internet : il passe uniquement par le réseau du jeu, entre les joueurs de la partie.
- Il ne lit ni n'écrit rien en dehors du dossier du jeu.
- Il ne touche pas aux cosmétiques payants ni aux lobbies publics.

Certains antivirus signalent les mods BepInEx (`winhttp.dll` injecte le chargeur dans le jeu) : c'est un faux positif
connu de tous les jeux moddés avec BepInEx. Le code est entièrement ici, tu peux le compiler toi-même.

## Désinstaller

Supprime `winhttp.dll` du dossier du jeu : le jeu redevient normal. Pour tout enlever, supprime aussi
`doorstop_config.ini`, `.doorstop_version`, `ZoomerMod ON/OFF.bat` et les dossiers `BepInEx` et `dotnet`.

## Compiler

Prérequis : le jeu installé avec BepInEx 6 IL2CPP (lancé une fois, pour générer `BepInEx/interop`), le SDK .NET 6+.

```
dotnet build DaleMod -c Debug      # compile et copie le mod dans le jeu
powershell -ExecutionPolicy Bypass -File tools\package.ps1   # zip de release dans dist\
```

Le chemin du jeu est dans `DaleMod/DaleMod.csproj` (`GameDir`) et `tools/package.ps1`.

Les médias sous droits d'auteur (musique du karaoké, vidéo, certains sons) ne sont pas dans le dépôt : récupère-les
dans le zip de release (`BepInEx/plugins/DaleMod/Assets/`) et pose-les dans `DaleMod/Assets/`.

## Crédits

Voir [`DaleMod/Assets/CREDITS.txt`](DaleMod/Assets/CREDITS.txt). Mod non officiel, sans lien avec les développeurs du jeu.
