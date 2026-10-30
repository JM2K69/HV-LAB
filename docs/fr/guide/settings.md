# Paramètres

| Paramètre | Par défaut | Rôle |
|---|---|---|
| Images de base VHDX | `C:\HV-LAB\BaseImages` | Dossier contenant les fichiers `BASE_*.vhdx` |
| Machines virtuelles | `C:\HV-LAB\VMs` | Dossier où sont créés les VM et leurs disques différentiels |
| Thème de l'application | Système | **Système** suit le thème de Windows ; **Clair** ou **Sombre** l'impose. |
| Langue de l'interface | Français | Français ou anglais. Le changement est immédiat. |

Les paramètres sont enregistrés dans `%LocalAppData%\HV-LAB\settings.json`. Les journaux de l'application sont dans `%LocalAppData%\HV-LAB\logs`.

!!! tip "Stockage rapide"
    Placez les deux dossiers sur un SSD : l'image de base est lue par toutes les VM qui en dépendent.
