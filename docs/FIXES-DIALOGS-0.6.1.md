# Dialogues indépendants — Apex Downloader 0.6.1

Correctifs du 14 octobre 2026.

- Les confirmations de téléchargement et l’ajout d’URL utilisent désormais des fenêtres non modales. Plusieurs peuvent rester ouvertes, avec le gestionnaire principal utilisable. Les captures navigateur restent enregistrées séparément jusqu’à la confirmation ; Annuler ou fermer la fenêtre ne crée pas de ligne.
- Démarrage et fin : barre de titre sombre cohérente, hauteur calculée selon le contenu, contrôles et boutons lisibles, chemins longs consultables et sélection de file masquée lorsqu’il n’existe qu’une file.
- Progression : quatre commandes de titre, dont la flèche verte pour masquer dans la zone de notification ; minimisation et maximisation restent distinctes. Un clic sur l’icône ou l’action « Download progress dialog » restaure la même fenêtre. Le transfert continue pendant le masquage et une progression en pause masquée reste accessible.
- Les trois onglets ont une hauteur adaptée au contenu ; options de fin et limiteur ne sont plus coupés. Les détails peuvent être masqués sans écraser l’onglet actif.
- La limite par fichier est restaurée à l’ouverture, au lieu d’être effacée par les événements d’initialisation des contrôles.
- Les noms personnalisés, descriptions, catégories et préférences de dossier sont conservés lors d’une confirmation non modale. Les destinations existantes produisent un nom disponible plutôt qu’un échec pour écrasement.
- Les entrées de lots trop grandes sont rejetées avec un message au lieu de provoquer une exception non traitée.
- Le choix de montrer la fenêtre de fin est mémorisé par téléchargement lorsqu’il est modifié. Les actions de fin (fermeture, veille, hibernation, arrêt) sont raccordées ; les autres transferts sont mis en pause et sauvegardés avant une action système. La fermeture de l’application passe par sa sauvegarde normale.
- Un dialogue déjà terminé ne laisse plus son temporisateur tourner après fermeture.
- Le pont accepte une fermeture propre d’instance ; le script de publication peut demander de fermer le gestionnaire même lorsqu’il est masqué, sans tuer le processus.

Validation : 61 contrôles du moteur, tests de bibliothèque/confirmation/métadonnées, test WPF de plusieurs fenêtres simultanées et vérification native de leur activation, masquage/restauration via le message de l’icône, durée de vie d’une progression masquée, sauvegarde du nom/description et dispatch des actions de fin. Les actions d’alimentation sont interceptées dans le test : le PC n’est ni arrêté, ni mis en veille pendant la validation. Les requêtes des tests utilisent des réponses HTTP simulées, sans télécharger les liens MediaFire des captures.

Ces correctifs couvrent les problèmes identifiés dans ces parcours. Ils ne constituent pas une garantie d’absence de bugs pour tous les sites, configurations réseau et services externes.
