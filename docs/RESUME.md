# Reprise rapide de SwiftFetch

## Ce que documente IDM

La [documentation officielle de segmentation](https://support.internetdownloadmanager.com/support/segmentation.html) explique qu'IDM sauvegarde les positions des segments régulièrement et reprend depuis les positions enregistrées. Le [dialogue principal](https://www.internetdownloadmanager.com/support/main.html) précise que la reprise dépend de la prise en charge par le serveur. Les [options](https://www.internetdownloadmanager.com/support/options.html) décrivent aussi un réglage pour ignorer les dates de modification erronées de certains serveurs. Les [liens temporaires](https://source.internetdownloadmanager.com/register/new_faq/sites2_6.html) peuvent empêcher une reprise.

Ces pages ne publient pas l'algorithme complet d'identification du fichier ni tous les échanges HTTP internes. SwiftFetch applique le comportement documenté de reprise depuis les positions enregistrées ; il ne prétend pas reproduire un code propriétaire ni garantir zéro trafic de contrôle.

## Comportement livré

- Par défaut, aucune relecture distante des données déjà téléchargées.
- Une sonde `Range: bytes=0-0` vérifie les plages et la taille, avec un corps d'un octet si le serveur accepte les plages. Les en-têtes et l'établissement de la connexion utilisent aussi du trafic. Si le serveur ignore Range, le corps complet n'est pas lu par l'application.
- Les segments sont ramenés à leur checkpoint durable si des octets non enregistrés subsistent après un crash. Une pause normale enregistre les octets après flush ; elle conserve la position atteinte.
- Chaque transfert reprend à `segment.Start + savedBytes`. Les segments déjà complets ne sont pas téléchargés de nouveau.
- Les ETag forts ou Last-Modified connus sont comparés et envoyés dans If-Range. Le moteur contrôle le statut 206, le début, la fin et la taille de Content-Range avant d'ajouter des octets aux fragments.
- Les anciens fragments uniques sans validateur sont repris sans les supprimer si les plages et la taille restent compatibles.
- Un changement connu de taille ou de validateur entraîne un redémarrage expliqué. Un refus de plage pendant le transfert produit une erreur et ne fait pas ajouter un corps complet à un fragment.

Sans identifiant serveur fiable, une modification du fichier à taille identique peut passer inaperçue. L'option **Full resume check when the server has no file validator**, dans Settings, permet de comparer chaque octet sauvegardé au contenu distant. Elle est désactivée par défaut et consomme autant de trafic que les données vérifiées. Un SHA-256 fourni par l'utilisateur permet également de contrôler le fichier assemblé avant publication.

## Validation

Le test à 34 % vérifie que la progression repart à la position sauvegardée, que le transfert demande uniquement les 66 % restants et qu'aucune plage de relecture n'est demandée. Les tests couvrent aussi les segments multiples après sérialisation, les anciens fragments, la persistance du réglage, les changements d'ETag et la vérification complète optionnelle.
