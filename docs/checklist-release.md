# Checklist avant une release

À faire avec la **version Release** (installée par l'installeur ou le zip, comme chez les potes), pas la version de
dev, dans un **lobby privé où tout le monde a la même version**. En cas de doute, le fichier `BepInEx\LogOutput.log`
de chacun contient les lignes du mod, marquées entre crochets (`[UPDATE]`, `[JESTER]`…).

Quand quelque chose ne passe pas : on note le problème, on corrige, et on refait au moins la ligne concernée.

## 1. Ce qui change dans cette release

À remplir au moment de préparer la release, à partir des commits depuis la dernière version
(`git log --oneline v<dernière>..HEAD`). Une ligne par changement, avec comment le vérifier en jeu.

Pour la prochaine (après 0.9.4), en cours :

- [ ] **Mise à jour automatique** : installer la version précédente, publier la nouvelle, relancer le jeu.
      Sur le menu, sous la version : « en téléchargement… » puis « installée : relance le jeu ». Au lancement
      suivant, le badge affiche la nouvelle version. Log : `[UPDATE]`.
- [ ] **Mise à jour déjà faite** : relancer encore une fois, le badge ne montre plus de ligne de mise à jour
      (log `[UPDATE] à jour`).
- [ ] **Tournée de café** : poser une tasse pleine sur le porte-tasse du poste d'un collègue, compté même si le
      collègue la reprend tout de suite. Une tasse ne compte qu'une fois.
- [ ] **Mail en chaîne** : envoyer à un collègue dont le pseudo a une apostrophe ou un accent, et à soi-même : compté.
- [ ] **Karaoké** : lancer le karaoké, laisser le manager arriver (musique coupée), puis remettre un CD dans la
      chaîne : elle doit l'accepter (avant, elle restait bloquée « en lecture »). Si le CD ne rentre pas : log `[KARAOKE]`.
- [ ] **Ponpons** : plus de petit trou dans les cheveux devant (raie moins creuse).
- [ ] **Commande de fournitures** : jouer deux cartes de suite dans la même session, le carton arrive dans la bonne
      pièce sur la deuxième carte.
- [ ] **Tenue de Noël du janitor** : combinaison normale et modèle squelette, couleurs correctes.
- [ ] **Téléphones** : si un appel vers un joueur pas viré échoue, récupérer le log de l'hôte (`[PHONE]`).

## 2. À chaque release

### Installation

- [ ] Installeur `.exe` sur un PC sans le mod : le jeu démarre avec le badge « ZoomerMod vX.Y » sur le menu.
- [ ] `ZoomerMod OFF.bat` : le jeu se lance sans mod. `ZoomerMod ON.bat` : le mod revient.

### Une partie complète à plusieurs (3 joueurs minimum)

- [ ] Créer le lobby, tout le monde rejoint, la partie démarre.
- [ ] Les tâches du mod apparaissent avec les bonnes couleurs (bleu spécialiste, rouge glandeur, noir saboteur) et
      peuvent être terminées.
- [ ] Au moins un rôle bonus distribué : la carte de rôle s'affiche au début, l'écran de fin donne le bon gagnant.
- [ ] Une réunion : appel, début, fin, rien ne reste bloqué.
- [ ] Un licenciement : le joueur viré passe janitor, son poste est désactivé chez tout le monde.
- [ ] Pseudos visibles au-dessus des têtes, y compris à New York.
- [ ] Sons du mod audibles de près, étouffés depuis une autre pièce.
- [ ] Performances : pas de grosse chute de FPS par rapport au jeu sans mod.

### Cosmétiques

- [ ] Menu de personnalisation : les catégories du mod sont là, rien ne déborde.
- [ ] Un cosmétique ajouté depuis la dernière release, vu par un autre joueur **et** en vue à la première personne.

### Après la partie

- [ ] Quitter vers le menu : aucun texte du mod ne reste à l'écran (sauf le badge).
- [ ] Relancer une deuxième partie dans la même session : tout fonctionne encore.

## 3. Publier

- [ ] Version mise à jour dans `DaleMod/Plugin.cs`, tout est commité.
- [ ] `powershell -ExecutionPolicy Bypass -File tools\package.ps1` : zip + installeur dans `dist\`.
- [ ] Release GitHub `vX.Y.Z` avec `ZoomerMod-X.Y.Z.zip` (nom exact : la mise à jour automatique le cherche) et
      l'installeur. Notes en français, puis une section `## English` : postée dans un 2e message.
- [ ] L'annonce arrive sur Discord (sinon : onglet Actions → Annonce Discord → regarder l'erreur).
