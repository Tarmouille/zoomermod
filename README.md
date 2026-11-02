<p align="center"><img src="DaleMod/Assets/zoomer_nobg.png" alt="ZoomerMod" width="200"></p>

<h1 align="center">ZoomerMod</h1>

Mod fan-made pour **Dale & Dawson Stationery Supplies** : nouvelles tâches, rôles et effets, à jouer entre amis.

**Ce mod est fait pour être joué en serveur privé uniquement, entre joueurs qui ont tous le mod. Ne l'utilise pas en partie publique.**

Ce mod est **vibe codé** : écrit en grande partie avec une IA (Claude), testé en jeu entre amis. Attends-toi à quelques bugs.

## Installation

1. Télécharge le zip de la dernière version dans [Releases](../../releases).
2. Steam > clic droit sur Dale & Dawson > Gérer > Parcourir les fichiers locaux.
3. Dézippe **tout** le contenu du zip dans ce dossier (à côté de `DDSS.exe`).
4. Lance le jeu depuis Steam. Le premier lancement est long (1 à 3 minutes).

Si tout marche, `ZoomerMod vX.Y.Z` s'affiche en bas à droite du menu principal.
Tout le monde dans la partie doit avoir la même version. **Joue uniquement en lobby privé, avec des gens qui ont le mod.**

`ZoomerMod OFF.bat` / `ZoomerMod ON.bat` (dans le dossier du jeu) désactivent / réactivent le mod.

## Nouvelles tâches

Elles tombent comme celles du jeu : **bleues** (Specialist), **rouges** (Slacker) et **noires** (sabotages).

### Bleues

| Tâche | Ce qu'il faut faire |
|---|---|
| Discours inspirants | Regarder 10 s de vidéo motivante dans l'app GlucksTube de son PC |
| Tupperware 2.0 | Prendre la bolo maison au frigo, la manger, puis laver le tupperware à l'évier |
| Transfert de mails | Transférer 3 mails de travail reçus, chacun à un collègue différent |
| Inventaire du frigo | Sortir 3 aliments périmés du frigo (ça sent) et les jeter |
| Formation sécurité | Vérifier 3 extincteurs (1 chance sur 4 qu'il parte tout seul), puis envoyer le compte rendu au manager |
| Café pour l'équipe | Déposer 2 cafés dans le porte-tasse d'autres postes |
| Audit Cybersécurité | Désactiver puis réactiver l'antivirus de 3 PC différents (gare aux pubs) |
| STONKS | Imprimer une courbe et la faire signer par le manager |
| Sauvegarde des serveurs | Lancer la sauvegarde en salle serveur (échoue si les serveurs sont en panne) |
| Macron Explosion | Imprimer 3 fois le tract FO de son PC, puis le déposer dans 3 bacs à papier d'autres postes |
| 1 to 1 | 15 s en tête-à-tête avec le chef dans son bureau, sans personne d'autre |
| 1 to 1 Manager | La version du chef (ou de l'Assistant) : 15 s seul avec n'importe qui dans son bureau |
| Commande de fournitures | Commander par mail au fournisseur, récupérer le carton à l'accueil, le ranger à l'entrepôt |
| Présentation client | Préparer au tableau blanc, puis présenter 15 s dans la salle de réunion |

### Rouges

| Tâche | Ce qu'il faut faire |
|---|---|
| Rail de bureau | Prendre le sachet à son bureau et aller le sniffer aux toilettes |
| Recyclage | Manger du caca aux toilettes (effet Nauséeux garanti) |
| Lettre au boss | Insulter le manager par mail, depuis le PC (et l'adresse) d'un collègue |
| Tupperware 2.0 : ni vu ni connu | Manger la bolo et remettre le tupperware sale au frigo |
| Rafraîchissement d'équipe | Asperger un collègue à l'extincteur |
| Moulin à vent | Dessiner dans Paint, imprimer, punaiser au panneau d'affichage |
| L'Autographe-gate | Dessiner, imprimer, et faire signer le dessin par un autre joueur |
| Douche froide | Verser un gobelet d'eau sur la tête d'un collègue |
| Dégât des eaux | Renverser un gobelet d'eau sur un PC : il court-circuite jusqu'à ce qu'on le sèche |
| Power Nap | Dormir 15 s à son poste sans que le manager passe à côté (on entend le ronflement) |
| Spam RH | Envoyer une chaîne de mails à 5 collègues |
| Photocopie de fesses | S'asseoir sur la photocopieuse, récupérer la copie, la punaiser |
| Karaoké : Rise of the Machines | Mettre le CD en salle de pause et chanter 15 s sans que le manager débarque |

### Noires (sabotages)

| Tâche | Ce qu'il faut faire |
|---|---|
| Panne générale | Lancer un virus sur tous les PC depuis la salle serveur ; seul le manager peut tout réparer |
| Inkish Coffee | Verser de l'encre dans la machine à café : ses cafés rendent nauséeux jusqu'à réparation |
| Attention Mr. Zoomer | Saboter le broyeur : le prochain qui broie un document y laisse sa main (jusqu'à 60 s) |
| Agrafeuse piégée | Piéger l'agrafeuse d'un poste : le prochain qui s'y assoit ou touche la gelée se fait agrafer le doigt |

## Effets et événements

- **Nauséeux** : écran vert et tangage. Vient du caca, des cafés à l'encre, ou des aliments périmés gardés trop longtemps en main.
- **Panne générale** : écran virus partout, LED des serveurs en rouge et alarme, jusqu'à la réparation par le manager.
- **Main dans le broyeur** : la victime est bloquée, crie, saigne ; un collègue peut la dégager.
- **Pluie de pubs** : 10 fenêtres de pub sur un PC, à fermer une par une (parfois pendant l'Audit Cybersécurité).
- **Sauvegarde** : LED des serveurs en bleu pendant la sauvegarde.
- **PC en court-circuit** : étincelles, fumée et écran d'erreur jusqu'à ce que quelqu'un le sèche.
- **Douche** : voile d'eau puis gouttes sur l'écran de la victime, et pas mouillés pendant 15 s.
- **Extincteur** : nuage blanc sur l'écran des aspergés.
- **Caca** : éclaboussures marron en continu autour des toilettes et en bas de l'écran du mangeur.
- **Tract FO** : il apparaît de temps en temps au hasard sur le bureau d'un PC.

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

Les médias sous droits d'auteur ne sont pas dans le dépôt : la vidéo et certains sons sont dans le zip de release
(`BepInEx/plugins/DaleMod/Assets/`), à poser dans `DaleMod/Assets/`.

La musique du karaoké (`Assets/karaoke/rise.wav`) est aussi dans le zip de release, pas dans le dépôt.

## Crédits

Musique du karaoké : *Rise of the Machines*, Music by Karl Casey @ White Bat Audio.

Voir [`DaleMod/Assets/CREDITS.txt`](DaleMod/Assets/CREDITS.txt). Mod non officiel, sans lien avec les développeurs du jeu.
