# État d’Apex Downloader — 14 octobre 2026

Mise à jour après audit : le [correctif HTTP](FIXES-HTTP-2026-10-14.md) corrige l'acquittement navigateur prématuré et les arrêts forcés, rétablit les tests d'intégration UI et ajoute des tests du dialogue de taille. Les résultats ci-dessous décrivent l'audit initial ; le document de correctif donne les résultats après modification. La mise à jour [0.6](MILESTONE-0.6.md) traite aussi les limiteurs, les files et horaires, l’aperçu ZIP borné, la limite globale des lots, l’import/export, les préférences proxy et l’icône de notification. Ce document conserve les constatations historiques de l’audit initial.

Audit du code présent dans le workspace, sans modification du moteur ni publication d’une nouvelle version. Le nom visible est désormais Apex Downloader ; les projets, le stockage et le bridge gardent les identifiants SwiftFetch. README annonce encore le jalon 0.5 et ne décrit pas tous les ajouts.

## Validation exécutée

| Vérification | Résultat |
|---|---|
| Build SwiftFetch.csproj | Réussi, zéro avertissement et zéro erreur |
| SwiftFetch.Tests | 40 scénarios réussis |
| BrowserExtension/transfer.test.js | 24 tests réussis |
| Build SwiftFetch.UiSmoke.csproj | Réussi, zéro avertissement et zéro erreur |
| UiSmoke --integration | Échec : « Browser job was not accepted », Program.cs:80 |

Le test UI a été lancé depuis `.test-tmp/status-audit-2026-10-14` pour isoler les assets que son démarrage régénère. Son premier scénario attend toujours qu’AcceptBrowserRequest ait créé une tâche synchroniquement. Le nouveau traitement reporte cette création sur le dispatcher. Le test doit être adapté au parcours avec dialogue, et le protocole d’acquittement doit être corrigé indépendamment.

Les tests du bridge natif n’ont pas été relancés contre l’application utilisateur. Les nouveaux modules de médias, débit, ZIP, lots et règles ne sont pas couverts par les 40 scénarios existants. Aucun test de téléchargement réel sur un site externe n’a été effectué pendant cet audit.

## Fonctions présentes et raccordées

- Moteur HTTP/HTTPS avec segmentation dynamique, reprise aux offsets sauvegardés, contrôles Range/validateurs, tentatives automatiques, intégrité SHA-256 facultative et assemblage.
- SQLite, sauvegardes, sessions protégées par DPAPI et migration des anciens historiques.
- Extension Chromium pour Chrome/Brave, capture de clics et de téléchargements, contexte privé séparé, cookies et native messaging. Le manifeste de l’extension reste en 0.5.1.
- Interface principale proche d’IDM : menus, barre d’actions, arbre de catégories, tableau compact, recherche et actions de bibliothèque.
- Dialogues Add URL, Download File Info, progression avec visualisation des segments et téléchargement terminé.
- Choix et mémorisation des dossiers par catégorie.
- Détection de liens connus dans le presse-papiers, qui remplit le formulaire sans déclencher automatiquement le transfert.
- Expansion de lots avec plages numériques comme `[01-10]`.
- Calcul SHA-256/MD5 d’un fichier terminé.
- Aperçu d’un ZIP distant, avec limites à corriger ci-dessous.
- Résolution dédiée de pages mp4upload vers une URL MP4. Les essais GoFile existent sous forme de scripts ; le moteur limite ses connexions à deux pour les hôtes qui terminent par `gofile.io`. Ce n’est pas un résolveur général GoFile.

## Fonctions partielles

| Domaine | État |
|---|---|
| Limiteur de débit | Le moteur applique un limiteur global. Le dialogue par fichier modifie ce même limiteur ; la propriété Job.SpeedLimitBytes n’est pas consommée par TransferAsync. |
| Règles automatiques | Modèle, persistance et application lors d’Enqueue présents. Pas d’éditeur de règles dans Settings ; les limites de vitesse par règle ne sont pas raccordées au transfert. |
| Files nommées | Une propriété Queue existe, mais le scheduler reste une unique file FIFO. |
| Planification | Scheduler_Click redirige vers Settings_Click. Pas de dates, horaires ou répétitions. |
| Actions de fin | SystemPower implémente des actions, mais aucun appel à SystemPower.Trigger n’est présent dans le parcours de téléchargement. Les options du dialogue ne déclenchent pas ces actions. |
| Médias | MP4 direct et résolveur mp4upload. Pas de pipeline HLS/DASH/FFmpeg ni de sélection générale de qualité. |

## Problèmes à traiter en priorité

1. **Acquittement navigateur prématuré.** `MainWindow.xaml.cs:219` programme le dialogue et l’ajout avec Dispatcher.BeginInvoke, puis renvoie un succès avant l’ajout. L’extension annule le téléchargement navigateur après ce succès (`transfer.js:71`). Annuler le dialogue ou rencontrer une erreur de persistance peut ainsi laisser aucun téléchargement actif. La déduplication ne protège pas non plus la période pendant laquelle le dialogue attend une validation. Il faut définir un acquittement cohérent avec l’acceptation durable ou le refus par l’utilisateur.

2. **Arrêt forcé d’instances.** `App.xaml.cs:46` considère une instance non réactive après un délai très court comme fantôme et tue les processus SwiftFetch/ApexDownloader (`:56`). Le script Build-Release.ps1 tue aussi les processus au début (`:5`). Cela peut interrompre une instance saine occupée et contourne la fermeture qui flush les fragments et sauvegarde les tâches. Préférer l’identification exacte de l’instance et un arrêt propre, sans déduire une panne d’un simple timeout.

3. **Limiteur par fichier incorrect.** `DownloadProgressDialog.xaml.cs:238` change le limiteur partagé ; désactiver l’option sur un autre fichier annule donc la limite globale. Implémenter des limiteurs distincts et restaurer explicitement les préférences.

4. **Aperçu ZIP potentiellement coûteux.** `RemoteZipInspector.cs:36` accepte une réponse 200 à une demande Range puis lit tout le corps en mémoire. Un serveur ignorant Range peut donc faire télécharger l’archive entière. Les plages de central directory n’ont pas non plus de contrôle strict de statut/Content-Range ni de plafond de taille. ZIP64 n’est pas pris en charge.

5. **Lots sans limite globale.** BatchUrlExpander limite chaque plage à 2 000 éléments mais développe récursivement les plages suivantes. Deux plages peuvent produire des millions d’URL. Ajouter une limite au résultat total et à la profondeur.

## Suites fonctionnelles

Avant d’ajouter de nouvelles fonctions : sécuriser l’acquittement du bridge, les arrêts et le limiteur ; adapter les tests UI aux dialogues et couvrir les nouveaux modules.

Ensuite : files multiples et priorités, planification horaire, véritable contrôle du débit par fichier/hôte, éditeur de règles, gestion des liens expirés, import/export, tray et notifications, configuration proxy, médias HLS/DASH, installateur et mises à jour signés.

Le projet correspond à une alpha avancée avec une interface bien développée. La compilation et les tests du socle sont bons, mais ils ne constituent pas une validation des nouvelles fonctions ni une qualification de production.

