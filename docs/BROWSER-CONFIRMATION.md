# Confirmation navigateur indépendante de la bibliothèque

Une capture navigateur est sauvegardée comme demande en attente, sans créer de DownloadJob ni de ligne dans la liste. SQLite stocke ces demandes séparément dans un réglage `pending_downloads`, entièrement protégé par DPAPI, avec les cookies nécessaires. Le navigateur reçoit un acquittement après cette sauvegarde.

Le dialogue Download File Info est ensuite présenté seul. Annuler supprime la demande en attente sans créer une entrée Cancelled. Start Download transforme atomiquement la demande en tâche et démarre le transfert. Download Later est un choix explicite qui ajoute une tâche Ready à la bibliothèque. Une requête répétée pendant la confirmation ne crée pas plusieurs dialogues.

Si Apex est fermé, le bridge lance `SwiftFetch.exe --browser`. Ce mode initialise le moteur et le pipe sans montrer la fenêtre principale. Le dialogue de confirmation n'a pas de propriétaire caché ou minimisé ; le dialogue de progression peut ensuite s'afficher seul. Lorsque la fenêtre principale reste cachée et qu'il n'y a plus de transfert ou de dialogue visible, le processus se ferme normalement après sauvegarde. L'action explicite Open Apex continue d'afficher le gestionnaire.

Après une interruption, les demandes en attente sont restaurées comme confirmations, pas comme lignes de téléchargement. Les anciennes entrées Cancelled provenant du navigateur, sans StartedAt ni aucun segment, sont écartées de la bibliothèque ; aucune suppression de fichier n'est effectuée.

Validation : 48 tests moteur/stockage, tests WPF de capture (annulation, déduplication, choix Start/Download Later, récupération, fenêtre principale invisible, anciennes entrées non démarrées), et tests WPF d'intégration existants réussis.
