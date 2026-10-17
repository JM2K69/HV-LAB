using HVLab.Services;

namespace HVLab.Helpers;

/// <summary>x:Bind helpers for accessible names of icon-only buttons.</summary>
public static class A11y
{
    /// <summary>"Start — SRV-01": localized action label followed by the item it applies to.</summary>
    public static string Action(string actionKey, string itemName)
        => LocalizationService.T("VM_ActionFor", LocalizationService.Instance[actionKey], itemName);
}
