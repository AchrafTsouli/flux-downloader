# Apex Downloader 0.7.1 — Vues des files et sélection

Correction du 15 octobre 2026.

- La vue initiale **All Downloads** reste l’historique complet.
- **Queues** affiche la file principale, tout comme son enfant **Main download queue**. Chaque enfant affiche sa propre file.
- Les vues des files et la liste des fichiers du planificateur contiennent uniquement les états en attente, actifs et en pause. Les téléchargements terminés (même si le fichier manque), échoués, annulés ou bloqués par une erreur ne s’y affichent plus. Ils restent dans l’historique. Un filtre **Failed** regroupe les erreurs.
- Démarrer une file reprend ses fichiers en attente/en pause ; cela ne relance plus les anciens échecs de l’historique. Les échecs peuvent toujours être repris explicitement depuis leur ligne.
- Double-clic sur **Queues** : développer/réduire l’arbre. Double-clic sur une file : ouvrir son planificateur indépendant, sur la bonne file. Une fenêtre déjà ouverte est réutilisée.
- Les actions de fin de file suivent les fichiers effectivement démarrés dans cette exécution, sans être bloquées par des échecs historiques exclus de la file.
- Glissement de souris dans la liste : sélection de plusieurs lignes sans Ctrl, dans les deux sens et depuis l’espace vide. Un rectangle indique la sélection et les bords font défiler la liste. Ctrl-clic, Maj-clic, Ctrl+A, les en-têtes et les barres de défilement gardent leurs comportements habituels. La sélection fonctionne aussi dans le planificateur.
- Le dialogue de suppression propose **Don't show this dialog again**. Le choix est enregistré uniquement après confirmation ; il est réversible dans **Options → Downloads → Confirm removal of downloads from the list**.
- Sans confirmation, seules les entrées de l’historique sont retirées. L’option de supprimer aussi les fichiers terminés est décochée par défaut et n’est pas mémorisée pour les suppressions sans dialogue. Les fichiers partiels restent sur disque.

## Vérifications

Tests WPF avec données et fichiers temporaires : séparation des vues, exclusion des échecs et des fichiers terminés/manquants, clics de l’arbre, planificateur indépendant sélectionné, glissement dans les deux sens et depuis l’espace vide, défilement automatique sur 80 lignes virtualisées, confirmation, préférence persistante et suppression explicite de fichiers terminés sans toucher aux fragments. Les gestes sont injectés au composant de sélection sans dépendre de l’état du bouton physique de la souris.

Les tests du moteur vérifient aussi tous les états possibles de la politique de file et la persistance SQLite de la confirmation de suppression. Les téléchargements et le tray utilisent les tests de régression de la version 0.7.

Les compteurs correspondent à la vue courante. Les transitions vers un état terminé ou échoué actualisent immédiatement les vues des files. Les actualisations conservent les lignes encore visibles sélectionnées ; une sélection par glissement suspend le rafraîchissement de la vue jusqu’à la fin du geste.

Validation finale : compilation sans avertissement, **66 tests moteur réussis**, suites WPF d’interactions, files/tray, intégration et menus réussies. Exécutable publié et version vérifiée : `dist/ApexDownloader.exe` **0.7.1.0**.
