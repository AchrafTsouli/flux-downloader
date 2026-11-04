# SwiftFetch 0.5 — moteur dynamique, SQLite et sessions

## Segmentation dynamique

Une connexion qui termine sa plage récupère une plage encore en attente ou la moitié de la plus grande plage active restante. Le moteur raccourcit la plage de l'ancienne connexion et ferme son flux au nouveau point de fin. La nouvelle plage est enregistrée avant sa requête réseau.

Les indices des fichiers `.part` restent stables. L'assemblage trie par position dans le fichier, et vérifie l'absence de trou ou de chevauchement. Les positions durables continuent à servir à la reprise. Une plage n'est divisée que si elle contient au moins 1 Mio restant ; le plan est limité à 128 segments et le pool à la limite de connexions choisie. Les fichiers sans prise en charge des plages gardent une connexion.

### Correctif de reprise sans validateur

La prise en charge des plages est indépendante de la présence d'ETag/Last-Modified. Par défaut, le moteur reprend directement aux positions durables sauvegardées et demande uniquement les octets restants. La préparation utilise une sonde HTTP d'un octet et les en-têtes disponibles ; elle ne relit pas les portions déjà reçues. Les anciens téléchargements à une seule plage ouverte sont également repris. Un changement de taille ou de validateur impose un redémarrage expliqué ; sans support des plages, une reprise partielle est impossible. Sans validateur, un changement de contenu à taille identique peut passer inaperçu.

La comparaison complète des octets sauvegardés reste disponible dans Settings, désactivée par défaut. L'activer consomme du trafic proportionnel aux données sauvegardées. Un arrêt ou un échec de cette vérification conserve les fragments. Le [document de reprise](RESUME.md) précise les sources IDM, le comportement et les limites.

Les fichiers de test incluent un serveur simulé avec une plage volontairement lente et une interruption juste après un découpage. La segmentation augmente les possibilités de répartition ; elle ne garantit pas un gain de débit sur tous les serveurs.

## SQLite et migration

Le stockage principal est désormais `%LOCALAPPDATA%/SwiftFetch/history.db`, via `Microsoft.Data.Sqlite`.

| Table | Rôle |
|---|---|
| downloads | Identifiant, nom, état, catégorie, taille ; détails et session protégés |
| segments | Indice stable, début, fin, octets durables |
| settings | Préférences et marqueur d'initialisation |

Chaque sauvegarde utilise une transaction. WAL, `synchronous=FULL`, un timeout de verrouillage et les clés étrangères sont activés. Une empreinte évite de réécrire les tâches inchangées. Une version de base inconnue bloque l'ouverture sans écraser les données.

Au premier démarrage, l'historique JSON 0.3/0.4 est importé. Le JSON d'origine est conservé, ainsi qu'une copie `.pre-sqlite`. Quand la base est initialisée, elle devient la source de vérité : modifier ensuite le JSON ne modifie pas SQLite.

`history.backup.db` est créé avec l'API de backup SQLite à l'import, périodiquement pendant les sauvegardes, et à la fermeture. Les sauvegardes contiennent les mêmes blobs protégés. Si la base ne peut être ouverte, l'application bloque les nouveaux téléchargements et conserve les fichiers ; elle ne remplace pas automatiquement une base illisible. Fermer l'application avant toute restauration manuelle et conserver l'ensemble des fichiers SQLite (`.db`, `-wal`, `-shm`) pour diagnostic.

Les URL, métadonnées détaillées, cookies et en-têtes d'autorisation sont protégés par **DPAPI CurrentUser**. Les informations de liste (nom, taille, état, catégorie) restent consultables dans les colonnes SQLite. La base protégée dépend du compte Windows et de ses clés DPAPI ; elle ne constitue pas un export portable vers un autre compte. Les anciens JSON peuvent encore contenir des URL signées, mais pas les nouvelles sessions.

## Téléchargements authentifiés

L'extension 0.5 demande `cookies` et l'accès aux hôtes HTTP/HTTPS. Elle récupère uniquement les cookies correspondant au téléchargement, en choisissant le magasin du profil/onglet quand disponible et la partition de l'onglet quand l'API le permet. Elle transmet aussi le user-agent et le référent.

Le moteur gère les cookies par tâche, par domaine, chemin, expiration et attribut Secure. Il n'utilise pas de jar global partagé entre téléchargements. Les redirections sont suivies explicitement (10 maximum) ; les boucles et les changements HTTPS → HTTP sont refusés. Les en-têtes Basic/Bearer ne traversent pas un changement d'origine. Un référent envoyé à une autre origine est réduit à son origine.

Sélectionner une tâche en pause ou en erreur, puis **Login**, permet d'appliquer une session existante, HTTP Basic, un token Bearer ou de supprimer la session. Le bouton **Apply and retry** sauvegarde la session protégée et remet la tâche dans la file. Les valeurs des cookies et les mots de passe ne sont pas affichés dans les logs.

### Après la mise à jour de l'extension

1. Ouvrir `chrome://extensions` ou `brave://extensions`.
2. Recharger SwiftFetch et autoriser l'accès aux cookies/hôtes si demandé.
3. Rafraîchir les pages ouvertes, puis déclencher un téléchargement depuis la page connectée.

### Limites actuelles

- Les sessions expirées nécessitent une nouvelle capture depuis le navigateur, ou un identifiant/token valide dans Login.
- Les en-têtes Bearer de requêtes JavaScript ne sont pas automatiquement extraits ; un token peut être fourni manuellement.
- Les cookies requis seulement sur un autre domaine de redirection ne sont pas automatiquement collectés depuis cet autre domaine.
- Les cookies partitionnés exigent l'API correspondante du navigateur ; le fallback de downloads n'expose pas toujours un onglet exploitable.
- NTLM/Negotiate, proxy authentifié, POST, protections liées à l'empreinte du navigateur, pages vidéo et DRM ne sont pas implémentés.
- Les tests utilisent des sessions fictives. Ils valident le moteur et les règles de transfert ; une session réelle sur chaque site doit encore être qualifiée.

Références : [transactions Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [API Chrome cookies](https://developer.chrome.com/docs/extensions/reference/api/cookies).
