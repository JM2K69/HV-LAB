# Paramètres

| Paramètre | Par défaut | Rôle |
|---|---|---|
| Images de base VHDX | `C:\HV-LAB\BaseImages` | Dossier contenant les fichiers `BASE_*.vhdx` |
| Machines virtuelles | `C:\HV-LAB\VMs` | Dossier où sont créés les VM et leurs disques différentiels |
| Thème de l'application | Système | **Système** suit le thème de Windows ; **Clair** ou **Sombre** l'impose. |
| Langue de l'interface | Français | Français ou anglais. Le changement est immédiat. |
| Mode session avancée (hôte Hyper-V) | Réglage de l'hôte | Autorise `vmconnect` en session avancée : presse-papiers, son, lecteurs, résolution libre. Lu à l'ouverture de la page, appliqué à l'hôte avec `Set-VMHost -EnableEnhancedSessionMode`. Il n'est pas enregistré dans `settings.json`. |
| Protéger le mot de passe dans les notes (DPAPI) | Désactivé | Garde chiffré le mot de passe des nouvelles VM dans leurs notes. Voir [Sécurité](#securite). |

Les paramètres sont enregistrés dans `%ProgramData%\HV-LAB\settings`, un fichier par compte Windows. Les journaux de l'application sont dans `%ProgramData%\HV-LAB\logs`. Seuls les administrateurs peuvent écrire dans ce dossier : un programme lancé sans élévation ne peut pas changer les dossiers où travaille HV-LAB, qui tourne en administrateur.

Les versions 2.0 et antérieures les gardaient dans `%LocalAppData%\HV-LAB` : au premier démarrage, HV-LAB reprend ces paramètres.

!!! tip "Stockage rapide"
    Placez les deux dossiers sur un SSD : l'image de base est lue par toutes les VM qui en dépendent.

## Sécurité

**Protéger le mot de passe dans les notes (DPAPI)**, dans la section **Sécurité**, est désactivé par défaut. Une fois activé, chaque nouvelle VM garde son mot de passe chiffré avec DPAPI, pour votre compte Windows sur cet hôte. Ses [notes](create-vm.md#notes-de-la-vm) contiennent alors cette ligne à la place du mot de passe :

```text
Mot de passe (DPAPI) : AQAAANCMnd8BFdERjHoAwE…
```

- HV-LAB peut toujours copier le mot de passe et le taper dans la VM.
- Le Gestionnaire Hyper-V et `Get-VM` ne l'affichent plus en clair.
- Un autre compte Windows, ou un autre hôte, ne peut pas le déchiffrer.

Les VM créées avant gardent leurs notes telles quelles.
