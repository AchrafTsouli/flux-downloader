# Correctifs de démarrage HTTP et de métadonnées — 14 octobre 2026

## Taille et démarrage

Le dialogue Download File Info et le moteur utilisent maintenant la même méthode `DownloadEngine.ProbeAsync`, avec les mêmes cookies, user-agent, règles de redirection et contrôles HTTP.

- Une réponse 206 utilise la longueur totale de Content-Range, jamais le Content-Length de la portion d'un octet.
- Une longueur totale inconnue reste Unknown.
- Une page HTML ou une réponse d'erreur ne fournit pas une taille de fichier au dialogue.
- Les hôtes refusant une sonde Range minuscule peuvent être sondés avec un GET normal. Seuls les en-têtes sont examinés ; le corps du fichier n'est pas lu pendant la sonde.
- Le dialogue transmet désormais au téléchargement l'URL et la session réellement résolues. Une destination ou un nom personnalisé n'est pas remplacé par une réponse tardive de métadonnées.

## Sessions et erreurs

L'extension 0.5.2 combine les cookies ordinaires et partitionnés au lieu de perdre tous les cookies ordinaires dès qu'une partition contient une valeur. Le domaine, le chemin, l'expiration et Secure restent contrôlés.

Un refus 403 affiche une erreur d'accès et propose une nouvelle capture du téléchargement. Une page HTML reçue à la place du fichier est une erreur de lien/protocole, pas une preuve qu'un nom d'utilisateur et un mot de passe sont requis. Une vraie réponse 401 reste distinguée. La [sémantique HTTP officielle](https://www.rfc-editor.org/rfc/rfc9110.html#section-15.5.4) précise qu'un refus 403 peut avoir des causes indépendantes des identifiants.

Si un transfert par plages est refusé alors que plusieurs connexions sont configurées, le moteur peut retenter avec une seule connexion, en conservant les fragments compatibles. Les délais et le nombre de tentatives restent bornés.

Le résolveur mp4upload utilise le contexte de session disponible et transmet uniquement les cookies correspondant au serveur du fichier résolu. Il ne transporte pas l'autorisation vers une autre origine.

## Réception navigateur et sauvegarde

La tâche et sa session sont sauvegardées avant l'acquittement au navigateur. Le dialogue de confirmation configure cette tâche déjà enregistrée au lieu de créer une deuxième tâche. Un identifiant de requête répété retrouve la même tâche. Annuler explicitement le dialogue annule la tâche ; une fermeture de l'application avant confirmation laisse une tâche récupérable.

Les dossiers définis dans le formulaire et dans les fixtures de test sont maintenant utilisés par le chemin d'ajout. Les filtres Compressed/General correspondent aux catégories du moteur.

Le démarrage secondaire attend la réponse de l'instance existante sans bloquer le dispatcher et ne tue plus ses processus. Le script de publication ferme uniquement les exécutables de ce dist, normalement, et attend la sauvegarde au lieu de les arrêter de force.

## Validation

- 46 tests moteur réussis, dont taille totale, taille inconnue, sonde refusée, refus 403, repli sur une connexion, résolution de session, et reprise depuis 34 % sans relecture complète.
- 25 tests extension réussis, dont coexistence des cookies ordinaires/partitionnés.
- Quatre scénarios WPF du dialogue réussis : taille complète, taille inconnue, HTML, refus HTTP.
- Intégration WPF réussie : acquittement durable, noms, déduplication, préférences, files, téléchargement différé, fermeture et bibliothèque.

Les tests HTTP utilisent des réponses simulées. Aucun site réel précis n'a été fourni pour reproduire les échecs signalés. Ces corrections ne garantissent pas une prise en charge universelle des liens expirés, des requêtes POST ou des sites exigeant un contexte de navigateur supplémentaire.

Après publication, recharger l'extension dans Chrome/Brave et rafraîchir les pages ouvertes. Les sessions expirées doivent être recapturées depuis le navigateur.
