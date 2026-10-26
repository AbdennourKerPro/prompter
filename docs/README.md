# Prompteur web (téléphone)

Web app statique : ton script défile en haut de l'écran, près de la caméra avant, pendant que le téléphone
enregistre la vidéo (caméra + micro). Le texte est une couche HTML par-dessus l'aperçu : **il n'apparaît pas
dans la vidéo**. Aucun flou, aucun serveur, rien n'est envoyé nulle part.

Fichiers : `index.html`, `style.css`, `app.js` (HTML/CSS/JS sans dépendance).

## Mise en ligne sur GitHub Pages (HTTPS obligatoire pour la caméra)
1. Pousse le dépôt sur GitHub (le dossier `docs/` doit être sur la branche `main`).
2. Sur GitHub : **Settings → Pages → Build and deployment → Deploy from a branch → `main` / `/docs` → Save**.
3. Après 1 à 2 minutes, ouvre `https://<ton-login>.github.io/<nom-du-repo>/` sur le téléphone.
4. Option : « Ajouter à l'écran d'accueil » (Safari : bouton Partager ; Chrome : menu ⋮).

Ouvrir `index.html` directement depuis les fichiers du téléphone ne marche pas : la caméra exige HTTPS.

## Utilisation
- **Activer la caméra**, puis accepte caméra et micro.
- **Crayon** : coller / modifier le script (sauvegardé automatiquement dans le navigateur).
- **Bouton rouge** : compte à rebours 3 s, puis enregistrement + défilement automatique. Re-touche pour arrêter.
- **Toucher le texte** : pause / reprise du défilement. **Glisser le texte** : avancer / reculer à la main.
- **− / +** : vitesse. **⏮** : retour au début. **Réglages** : taille du texte, hauteur et opacité du prompteur,
  marges, texte centré, miroir, caméra avant/arrière, 720p/1080p, compte à rebours, défilement auto.
- Après l'arrêt : aperçu de la vidéo, puis **Enregistrer / partager** (iPhone : « Enregistrer la vidéo » → Photos)
  ou **Télécharger**. Pense à sauvegarder avant de fermer la page.
- Clavier / télécommande Bluetooth : Espace ou Entrée = pause, ↑/↓ = vitesse, ←/→ = ligne, R = enregistrer.

Format : MP4 sur iPhone (Safari) et sur Chrome Android récent, sinon WebM.
L'orientation de la vidéo suit celle du téléphone au moment où tu lances l'enregistrement
(tourne le téléphone avant d'appuyer sur le bouton rouge).

## Conseils
- Garde le prompteur assez petit (35 à 45 %) et la taille du texte grande : tes yeux restent près de l'objectif.
- Fais un essai de 10 s et vérifie le son avant la vraie prise.
- Mode avion / « Ne pas déranger » pour éviter qu'un appel coupe l'enregistrement.
