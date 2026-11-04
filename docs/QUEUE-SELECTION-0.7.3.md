# Apex Downloader 0.7.3 — Sélection des files et historique

> Historique : ce comportement est remplacé par [les vues de files de 0.7.4](QUEUE-VIEWS-0.7.4.md).

Correction du 15 octobre 2026 selon la dernière précision : appliquer aussi le comportement de sélection observé dans la capture d’IDM.

Cette version remplace le filtrage des files dans la liste principale introduit en 0.7.1 et conservé en 0.7.2.

- Un clic sur **Queues** sélectionne la file principale et laisse l’historique complet dans la liste.
- Un clic sur **Main download queue**, **Synchronization queue** ou une file personnalisée sélectionne cette file sans masquer les téléchargements terminés ou échoués de l’historique.
- Les filtres de catégories, d’états et la recherche restent disponibles. Cliquer sur une file remet la vue sur All Downloads ; une recherche saisie continue de s’appliquer.
- Les boutons **Start queue**, **Stop queue** et **Scheduler** ciblent la file sélectionnée. Les sous-menus Start/Stop queue proposent toujours les files nommées explicitement.
- Double-clic sur **Queues** : développer/réduire. Double-clic sur une file : ouvrir son planificateur indépendant sur cette file.
- Dans le planificateur, **Files in the queue** reste limité aux téléchargements en attente, actifs ou en pause appartenant à la file choisie. Les anciens échecs ne sont pas relancés automatiquement.
- La présentation compacte de 0.7.2 et les options de suppression de 0.7.1 sont conservées.

Validation WPF : historique conservé après sélection de la file principale ou de synchronisation, retour depuis un filtre d’état, commandes démarrant/arrêtant uniquement la file sélectionnée, sélection correcte dans le planificateur indépendant, fichiers non terminés isolés dans son onglet, sélection par glissement, suppression et sauvegarde. Régression files/tray/synchronisation et intégration.
