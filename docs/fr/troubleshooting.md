# Résolution de problèmes

## Application

Bandeau rouge « Hyper-V indisponible »
:   Activez Hyper-V, puis redémarrez l'ordinateur. Utilisez l'une de ces commandes :

    ```powershell
    Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V -All
    ```

    ```powershell
    .\Install.ps1 -EnableHyperV
    ```

    Si seul le service `vmms` est arrêté, utilisez le bouton **Démarrer le service** du bandeau.

« Accès refusé »
:   L'application doit tourner en administrateur. Elle demande l'élévation au démarrage : acceptez l'invite UAC.

L'application ne démarre pas
:   Consultez les journaux de `%LocalAppData%\HV-LAB\logs` et le journal Windows *Application*, puis réinstallez avec `Install.ps1`.

Bandeau d'erreur sur une page
:   Le message est l'erreur renvoyée par PowerShell ou Hyper-V, par exemple un nom de commutateur déjà utilisé, ou un fichier verrouillé par une VM démarrée. Le bandeau d'une action reste affiché jusqu'à ce que vous le fermiez ou lanciez une autre action. **Copier les détails** copie la sortie de PowerShell, utile pour signaler un problème.

Quelles opérations ont tourné, et combien de temps
:   `%LocalAppData%\HV-LAB\logs\operations-aaaaMMjj.log` contient une ligne par opération Hyper-V : nom, durée, résultat. Les scripts n'y sont jamais écrits (ils peuvent contenir des mots de passe). Les journaux de plus de 30 jours sont supprimés au démarrage.

Une création a échoué ou a été annulée
:   HV-LAB supprime ce que cette création a produit : les disques sont démontés, puis la VM et son dossier sont supprimés. Une VM ou un dossier qui existait avant n'est jamais touché. Un nom déjà utilisé par une autre VM est refusé.

## Images de base

L'analyse ne trouve aucune image
:   Choisissez le fichier `install.wim` ou `install.esd` du dossier `sources` de l'ISO monté, pas l'ISO lui-même.

« Aucun index trouvé » pour l'édition
:   Le WIM ne contient pas cette édition. Choisissez une autre édition ou une autre famille d'OS, ou indiquez l'index à la main.

La construction échoue
:   Le bandeau donne le chemin du journal de construction. Vérifiez que :

    - l'ISO est toujours monté ;
    - le disque de destination a assez d'espace libre ;
    - aucune VM n'utilise le fichier à remplacer.

## VM

Le programme d'installation de Windows pose des questions au premier démarrage
:   La VM a été créée sans fichier de réponse (Quick VM, ou **Utiliser un fichier de réponse** décoché). Répondez dans la console, ou recréez la VM avec [Créer une VM](guide/create-vm.md).

La VM ne démarre plus après un changement de son image de base
:   Un disque différentiel ne fonctionne qu'avec le parent exact sur lequel il a été créé. Recréez la VM sur l'image de base actuelle.

Pas d'Internet dans la VM
:   Vérifiez que :

    - la VM est sur le commutateur interne qui porte le [réseau NAT](guide/networking.md#reseaux-nat) ;
    - la VM a une IP fixe dans le sous-réseau du NAT, avec la passerelle du NAT comme passerelle par défaut et un serveur DNS.

## Signaler un problème

Ouvrez une issue sur [GitHub](https://github.com/JM2K69/HV-LAB/issues). Indiquez :

- la version de l'application, affichée dans les **Paramètres** ;
- la version de Windows de l'hôte ;
- le journal concerné.
