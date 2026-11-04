# Apex Downloader 0.6 — Menus et préférences

Mise à jour du 14 octobre 2026. L’intégration reste limitée à Chrome et Brave.

## Interface et commandes

- Formulaire d’ajout intégré supprimé. Ajouter un lien passe par la fenêtre séparée ; une capture navigateur peut l’ouvrir sans afficher le gestionnaire.
- Menu clic droit entièrement redessiné : fond uniforme, contraste des actions désactivées, sélection de la ligne visée et aucune action sur une zone vide.
- Tâches : ajout individuel, lots et presse-papiers, Site Grabber, cible de dépôt, import/export de listes de liens et sortie.
- Fichier : arrêt, retrait de l’historique, reprise, nouveau téléchargement, ouverture, dossier et renommage.
- Téléchargements : pause/arrêt/reprise globale, retrait des terminés, recherche Ctrl+F/F3, planificateur, démarrage/arrêt des files et limitation de vitesse.
- Affichage : catégories, tri, barre d’outils, icône de notification, colonnes, thèmes sombre/clair, police et menus anglais/français.
- Aide : guide local, files, collecteur, astuce, diagnostic, informations du projet, flux de mises à jour configurable et description à partager.

## Préférences fonctionnelles

Onze onglets : Général, Types de fichiers, Dossiers, Téléchargements, Connexion, Proxy/SOCKS, Connexions aux sites, VPN, Sons, Planificateur et Affichage.

- Démarrage Windows : activation explicite via le registre utilisateur ; démarrage masqué avec icône de notification.
- Surveillance du presse-papiers désactivée par défaut ; lorsqu’elle est activée, un lien de fichier ouvre une confirmation.
- Capture Chrome/Brave, menu navigateur, touches de contournement/forçage, extensions et exclusions. Le pont natif transmet uniquement ces réglages non secrets, sans démarrer le gestionnaire. La politique est rafraîchie par l’extension.
- Panneau facultatif sur les sources HTTP/HTTPS directes des éléments vidéo/audio.
- Catégories personnalisées avec extensions et dossiers ; répertoire temporaire appliqué aux nouvelles tâches. Les tâches déjà en pause conservent leurs emplacements.
- Fenêtres de confirmation, progression et fin configurables ; détails des connexions et onglet de limitation masquables ; choix des files ; doublons ; User-Agent manuel.
- Reprise rapide conservée par défaut. La vérification complète des octets reste facultative. Ignorer Last-Modified est un réglage explicite ; ETag reste vérifié.
- Limites globale et par fichier distinctes. Le temporisateur ne tronque plus les longues réservations de bande passante.
- Proxy HTTP/HTTPS et SOCKS4/4a/5 ; identifiants HTTP Basic limités à l’origine exacte. Les préférences contenant les secrets sont chiffrées avec DPAPI, avec lecture des anciennes préférences JSON.
- Connexion d’un profil VPN Windows existant via rasdial ; déconnexion facultative à l’inactivité.
- Scanner antivirus externe facultatif avec arguments sans shell ; `{file}` désigne le fichier assemblé. Le code de sortie zéro permet sa publication.
- Sons Windows de fin et d’erreur ; dates de création et de modification fournies par le serveur.
- Files nommées avec leurs propres capacités, démarrage/arrêt et horaires HH:mm, quotidiens ou ponctuels, pendant que l’application reste ouverte.

## Limites explicites

- L’option IDM qui commence le transfert pendant l’affichage des informations n’est pas exposée : elle contredirait la demande de confirmer avant d’ajouter/démarrer une capture. Annuler ne crée toujours aucune ligne.
- Les options de Safari, Edge, Firefox et anciens navigateurs ne sont pas incluses dans cette étape, conformément au choix Chrome/Brave.
- Le Site Grabber est un collecteur HTML du même site : 20 pages, 500 fichiers, 2 Mo par page, puis revue des liens avant ajout. Il n’exécute pas JavaScript et n’extrait pas les flux protégés, HLS ou les URL blob.
- L’aperçu ZIP exige des réponses Range valides, limite le répertoire à 4 Mo et refuse ZIP64/multivolume. Il ne télécharge plus le corps complet si le serveur ignore Range.
- Les identifiants Basic ne remplacent pas les cookies de connexion des sites ; les cookies du navigateur restent transférés par le pont existant.
- La langue française couvre les menus principaux et contextuels. Les dialogues et les messages restent en anglais dans cette version.
- Aucun serveur de mises à jour, site public ou support tiers n’est inventé. Le flux HTTPS et la page du projet peuvent être configurés dans Affichage. Le flux doit fournir `version` et `downloadUrl`; une mise à jour ouvre une page après confirmation, sans installation automatique.
- Les listes exportées contiennent les liens de téléchargement, pas les sessions ni les mots de passe.
- VPN, proxy, démarrage Windows, scanner externe et flux de mises à jour sont configurables mais n’ont pas été testés avec les services personnels de l’utilisateur. Les tests utilisent des données locales, des réponses simulées et le vrai pont Windows.

## Validation

- 59 contrôles moteur : reprise à 34 %, intégrité des octets, sessions, SQLite/DPAPI, capacités par file, dossiers temporaires, catégories, lots bornés, limitation de vitesse et aperçu ZIP borné.
- 28 tests extension : transfert, navigation privée, reprise du navigateur si le pont échoue, exclusions et capture forcée.
- Tests WPF : bibliothèque, confirmation masquée, métadonnées, onze onglets, sauvegarde, préférences d’affichage, thèmes et menus français, clic droit.
- 9 contrôles du pont natif et construction de la distribution dans `dist`.

Après l’installation, recharger l’extension sur `chrome://extensions` ou `brave://extensions` pour activer les fichiers de la version 0.6.0.

