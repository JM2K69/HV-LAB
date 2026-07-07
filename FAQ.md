# 🤔 FAQ - Questions Fréquemment Posées

## Installation & Setup

### Q: Je reçois l'erreur "Hyper-V n'est pas installé"
**R:** Vous devez d'abord activer Hyper-V:
```powershell
# Exécuter en tant qu'administrateur
Enable-WindowsOptionalFeature -FeatureName Hyper-V -Online -All
```
Puis redémarrez Windows.

### Q: L'application ne se lance pas
**R:** Vérifiez:
1. ✅ Vous êtes en administrateur
2. ✅ .NET 8.0 est installé: `dotnet --version`
3. ✅ Hyper-V est activé
4. ✅ Windows 10/11 Pro ou supérieur

### Q: "Build failed" lors de la compilation
**R:** Nettoyez et reconstruisez:
```bash
dotnet clean
dotnet restore
dotnet build
```

---

## Utilisation

### Q: Aucun vSwitch n'apparaît
**R:** Hyper-V n'a pas de vSwitch par défaut. Créez-en un via:
- L'application: bouton "Créer vSwitch NAT"
- Ou manuellement:
```powershell
New-VMSwitch -Name "Default" -SwitchType Internal
```

### Q: Les VMs ne s'affichent pas
**R:** Cliquez sur "Rafraîchir" dans l'onglet "Machines Virtuelles".

### Q: Je veux créer une VM mais le bouton ne marche pas
**R:** Les dialogs ne sont pas encore implémentées. Vous pouvez:
1. Créer une VM en PowerShell:
```powershell
New-VM -Name "MyVM" -MemoryStartupBytes 2GB
```
2. Puis rafraîchir l'application

---

## Gestion du Réseau & NAT

### Q: Comment configurer le NAT après création du vSwitch?
**R:** Le NAT se configure automatiquement lors de la création. Pour modifier:
```powershell
Set-NetNat -Name "nat-VortSwitch01" -InternalRoutingDomainPrefix 192.168.200.0/24
```

### Q: Les VMs ne peuvent pas accéder à Internet
**R:** Vérifiez:
1. ✅ Le vSwitch est créé et en NAT
2. ✅ Les VMs utilisent le bon vSwitch
3. ✅ L'adaptateur réseau hôte a accès Internet
4. ✅ Firewall n'bloque pas

### Q: Quelle adresse IP donner aux VMs?
**R:** Utilisez la plage NAT configurée. Ex: Si le vSwitch est `192.168.100.0/24`:
- Passerelle: `192.168.100.1` (hôte)
- VMs: `192.168.100.2` - `192.168.100.254`
- DHCP activé automatiquement

---

## Disques & Stockage

### Q: Quelle est la taille correcte pour une image de base?
**R:** Dépend du système:
- Windows Server 2022: 20-30 GB
- Windows 11: 20-25 GB
- Windows 10: 15-20 GB

### Q: Les disques différentiels prennent de la place?
**R:** Non, c'est l'intérêt! 
- Image de base: 30 GB
- Chaque disque différentiel: 2-5 GB (au lieu de 30 GB)

### Q: Puis-je convertir un disque existant en image de base?
**R:** Oui:
```powershell
# Copier le disque
Copy-Item "C:\Chemin\VM.vhdx" "C:\Chemin\VM-Base.vhdx"

# Puis utiliser comme base pour les différentiels
New-VHD -Path "C:\VM-Clone.vhdx" -ParentPath "C:\Chemin\VM-Base.vhdx" -Differencing
```

---

## Dépannage

### Q: "Access to path is denied"
**R:** Lancez l'application **en tant qu'administrateur**.

### Q: PowerShell commandes retournent des erreurs
**R:** Vérifiez:
```powershell
# Test basic connectivity
Get-VMSwitch
Get-VM
```

Si ça ne marche pas, Hyper-V n'est pas correctement configuré.

### Q: L'application se fige
**R:** C'est probablement une commande PowerShell qui prend du temps. Attendez quelques secondes. Si ça persiste:
1. Forcez la fermeture
2. Redémarrez
3. Vérifiez les ressources système

### Q: Je reçois "JSON parse error"
**R:** C'est un bug non critique. Les données PowerShell ne sont pas en format JSON valide.
Actuellement, c'est comment à TODO - cela sera fixé dans une future version.

---

## Performance

### Q: Comment améliorer les performances?
**R:**
1. ✅ Utilisez des SSD, pas des HDD
2. ✅ Minimisez le nombre de rafraîchissements
3. ✅ Limitez le nombre de VMs simultanées
4. ✅ Allouez suffisamment de RAM à l'hôte

### Q: Combien de VMs puis-je créer?
**R:** Dépend de votre système:
- RAM disponible: 2 GB par VM minimum
- Processeurs: 1-2 par VM
- Disque: Taille de l'image base + différentiels

Ex: Hôte 32 GB RAM → Max ~8-10 VMs de 2 GB chacune

---

## Fonctionnalités Futures

### Q: Quand aurez-vous les snapshots?
**R:** À la prochaine version majeure (roadmap TBD).

### Q: Pouvez-vous ajouter X fonctionnalité?
**R:** Ouvrez une issue sur GitHub avec votre request!

### Q: L'API REST est-elle prévue?
**R:** Oui, dans les plans futurs. Cela permettra l'automatisation externe.

---

## Contribution & Support

### Q: Comment rapporter un bug?
**R:** 
1. Décrivez précisément le problème
2. Fournissez les étapes pour reproduire
3. Annexez les logs/erreurs
4. Ouvrez une issue GitHub

### Q: Puis-je contribuer?
**R:** Bien sûr! Commencez par:
1. Fork le repo
2. Créez une branch: `git checkout -b feature/mon-feature`
3. Commitez vos changements
4. Push et ouvrez une PR

### Q: Où trouver de l'aide?
**R:**
1. 📖 [Guide Complet](./GUIDE_COMPLET.md)
2. 🏗️ [Architecture](./ARCHITECTURE.md)
3. 📝 [Documentation du Code](./ARCHITECTURE.md#extension-future)
4. 💬 Issues GitHub

---

## Infos Supplémentaires

### Système d'Exploitation Supportés
- ✅ Windows 11 Pro/Enterprise
- ✅ Windows 10 Pro/Enterprise
- ✅ Windows Server 2022
- ✅ Windows Server 2019

### Architecture Supportées
- ✅ x86-64 (64-bit)
- ❌ ARM (Pas supporté par Hyper-V natif)

### .NET Versions Supportées
- ✅ .NET 8.0 (Actuellement)
- 🔜 .NET 9.0 (À venir)

---

**Avez une question qui n'est pas listée? Ouvrez une issue!**
