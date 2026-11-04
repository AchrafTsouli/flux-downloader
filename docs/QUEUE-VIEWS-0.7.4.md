# Apex Downloader 0.7.4 — Vues des files rétablies

Correction du 15 octobre 2026. Cette version remplace la navigation de 0.7.3.

| Action | Résultat |
|---|---|
| All Downloads | Historique complet, y compris les fichiers terminés ou échoués. |
| Clic sur Queues | Vue des téléchargements non terminés de la file principale. |
| Clic sur Main download queue | Même vue de la file principale. |
| Clic sur Synchronization queue ou une file personnalisée | Téléchargements non terminés appartenant uniquement à cette file. |
| Double-clic sur Queues | Développer ou réduire l’arbre. |
| Double-clic sur une file | Ouvrir ses réglages dans le planificateur indépendant. |

Les états visibles dans une file sont : en attente, actifs et en pause. Les fichiers terminés, manquants après téléchargement, échoués, annulés ou bloqués par une erreur restent exclus des files et conservés dans l’historique. Une file sans élément éligible reste vide. La recherche s’applique à la vue choisie.

Les boutons Start queue, Stop queue et Scheduler continuent de cibler la file sélectionnée. La présentation compacte, la sélection par glissement et les préférences de suppression sont conservées.

Validation : compilation sans avertissement ; tests WPF couvrant le premier clic sur Queues, les deux files, une file vide avec historique présent, le retour à All Downloads, les réglages indépendants, les commandes de file, le glissement, la suppression, le tray, la synchronisation et l’intégration.
