# Checklist avant une release

À faire avec la **version Release** (installée par l'installeur ou le zip, comme chez les potes), pas la version de
dev, dans un **lobby privé où tout le monde a la même version**. En cas de doute, le fichier `BepInEx\LogOutput.log`
de chacun contient les lignes du mod, marquées entre crochets (`[UPDATE]`, `[JESTER]`…).

Quand quelque chose ne passe pas : on note le problème, on corrige, et on refait au moins la ligne concernée.

## 1. Ce qui change dans cette release

À remplir au moment de préparer la release, à partir des commits depuis la dernière version
(`git log --oneline v<dernière>..HEAD`). Une ligne par changement, avec comment le vérifier en jeu.

Pour la 0.10.0 (après 0.9.5) :

**Janitor (nouveau)**
- [ ] **Une tâche à la fois**, tirée au hasard dans son pool : Nettoyage, Papier toilette, Toilettes bouchées,
      Inspection des extincteurs, Distributeur bloqué. Log `[POOL] … (Janitor, pool : …)` : le papier toilette doit
      y être.
- [ ] Les tâches janitor sont **bleues** et aucun spécialiste / manager ne les reçoit.
- [ ] **Toilettes bouchées** : ventouse au placard de la gelée (reprenable après l'avoir posée), tenue cloche rouge
      en haut ; 5 s sur les toilettes indiquées, un des 3 sons (coupé si on lâche avant) ; la ventouse disparaît.
- [ ] **Inspection des extincteurs** : 3 extincteurs différents, pas de mail. Formation sécurité (spécialiste) garde
      son mail.
- [ ] **Distributeur bloqué** : 5 s sur le distributeur, 2 objets tombent dans le bac avec le bruit du distributeur ;
      les manger valide les 2 étapes ; si un collègue en mange un, « Débloquer » revient. Log `[DISTRIB]`.

**Tâches**
- [ ] **Autographe-gate / STONKS** : faire signer une **copie** imprimée du dessin : validé (log `mon dessin=copie`).
- [ ] **Inventaire du frigo** : la flèche vise le frigo, puis les poubelles une fois l'aliment en main.
- [ ] **Audit cybersécurité** : popups plus fréquents (1 chance sur 5).
- [ ] **GlucksTube** : plus de frame de la vidéo sur la télé du bureau ni dans les spams. Fenêtre noire : log
      `[PC] texture vidéo`.

**Usurpateur (à 3 minimum)**
- [ ] Déguisé en un vrai joueur, perdre le costume à la main **et** au début d'une réunion : les **autres** voient
      toujours son corps et sa tête. Sinon, log des autres : `[USURP] diag` / `[USURP] réparé`.
- [ ] Déguisement au hasard (mannequin F7) : appliqué, pas d'erreur.

**Cosmétiques**
- [ ] **T-shirt Dr Jack Abbot**.
- [ ] **Gros nœud noir** : derrière les cheveux sans les cacher, sur plusieurs coupes, en première personne, vu par
      un autre, avec les Ponpons.
- [ ] **Rose dragée** : bouton présent pour chemises, t-shirts, pantalons, jupes, chaussures ; couleurs des persos
      enregistrés pas décalées.

**Restes de la 0.9.5 jamais joués**
- [ ] Tournée de café, mail en chaîne (pseudo avec accent / apostrophe), karaoké (remettre un CD après la coupure),
      commande de fournitures sur 2 cartes de suite, tenue de Noël du janitor, Ponpons.
- [ ] **Mise à jour auto 0.9.5 -> 0.10.0** (premier vrai passage) : log `[UPDATE]`.

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
