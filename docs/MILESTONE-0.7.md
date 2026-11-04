# Apex Downloader 0.7 — Zone de notification et files

Mise à jour du 14 octobre 2026. Chrome et Brave restent les navigateurs pris en charge.

## Fenêtres et zone de notification

- Un lancement par le navigateur (`--browser`) crée le gestionnaire masqué et son icône commune. La fenêtre de confirmation reste indépendante.
- Fermer la fenêtre principale la masque dans la zone de notification, sans mettre les téléchargements en pause.
- Le menu de l’icône regroupe les téléchargements actifs/en attente et les fenêtres de progression masquées. Il permet de rouvrir une progression, toutes les progressions, ou le gestionnaire, et de tout mettre en pause/reprendre.
- **Tasks → Exit** ou **Exit Apex Downloader** dans le menu de l’icône arrête réellement l’application : pause des transferts, sauvegarde des positions et de l’historique, puis retrait de l’icône.
- Les fenêtres de progression utilisent l’icône commune, sans créer une icône par téléchargement. L’icône est réinscrite si Explorer redémarre.
- Si Windows refuse la création de l’icône, le gestionnaire reste accessible au lieu de disparaître ou de planter.

## Liste et files

- Sélection multiple avec Ctrl, Maj et Ctrl+A. Reprise, pause, suppression de l’historique et affectation à une file traitent la sélection. Un clic droit sur une ligne déjà sélectionnée conserve le groupe.
- **Main download queue** et **Synchronization queue** sont créées automatiquement. La clé historique `Default` reste celle de la file principale ; les noms affichés peuvent être personnalisés sans déplacer les anciens téléchargements.
- Un nouvel ajout avec **Start Download** rejoint la file principale et démarre ou attend une place, même si l’ancienne option de mise en attente des nouveaux ajouts était activée. **Download Later** conserve la file choisie.
- Le menu Downloads propose les sous-menus Start queue et Stop queue, et le limiteur global On/Off/Settings. Désactiver le limiteur garde sa dernière valeur pour une réactivation ultérieure.

## Planificateur indépendant

**Downloads → Scheduler** ouvre une fenêtre indépendante du gestionnaire :

- Liste des files, création, nom affiché, suppression des files personnalisées à l’arrêt.
- Démarrage au lancement d’Apex ; horaires de départ/arrêt ; date unique ou jours de semaine ; nombre de tentatives par fichier.
- Onglet Files in the queue : nombre de fichiers simultanés, progression/état/taille, ordre avec Move up/down et retrait de la file sans supprimer les fichiers.
- Onglet Download limits : limite simultanée globale et débit maximal global.
- Actions après réussite de toute la file : ouverture d’un fichier, déconnexion du VPN connecté par Apex, sortie d’Apex, veille/hibernation/arrêt de Windows. Aucune action système n’est activée par défaut.
- Réglages et ordre sauvegardés dans SQLite. Les limites de chaque file s’appliquent sous la limite globale.

La file de synchronisation compare périodiquement les métadonnées HTTP des fichiers terminés. Un changement de taille, de date serveur ou d’ETag déclenche le téléchargement d’une nouvelle copie et conserve l’ancien fichier. Start now active les vérifications périodiques ; Stop les suspend. Les liens expirés et les serveurs sans métadonnées de changement fiables restent des limites de cette fonction.

Les horaires fonctionnent tant qu’Apex tourne, y compris masqué. Ils ne réveillent pas un ordinateur éteint et ne créent pas de tâche Windows. La déconnexion vise le profil VPN utilisé par Apex, pas un modem téléphonique. Les actions d’alimentation et l’ouverture de fichiers externes n’ont pas été exécutées pendant les tests.

## Validation

Tests du moteur, de l’extension et du pont natif, plus tests WPF de confirmation, des menus, des préférences, des dialogues indépendants et du fonctionnement des files/tray. Le scénario files/tray utilise des réponses HTTP simulées : aucun fichier privé ni jeton utilisateur n’est utilisé.

Résultats de cette version : **64 tests moteur**, **28 tests extension**, **9 tests pont natif**, ainsi que les suites WPF (files/tray, confirmation, métadonnées, menus, options et fenêtres indépendantes) réussis. Publication vérifiée : `dist/ApexDownloader.exe`, version `0.7.0.0`.
