using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace Wedge.Server.Services;

// A custom item can register cleanly and still never reach the bot: the generator only equips what the
// default inventory's slot filter accepts, and only fits mods the item actually has slots for. Neither
// failure logs anything, and both survive a server boot — so check the end state directly.
[Injectable(InjectionType = InjectionType.Singleton)]
public class KitVerifier(DatabaseService databaseService, ISptLogger<KitVerifier> logger)
{
    const string DefaultInventory = "55d7217a4bdc2d86028b456d";

    public void Verify(string tpl, string slotName, params string[] expectedModSlots)
    {
        var items = databaseService.GetItems();
        if (!items.TryGetValue(new MongoId(tpl), out var item))
        {
            logger.Error($"[Wedge] {tpl} is NOT in the item database — it never registered");
            return;
        }

        var slots = item.Properties?.Slots?.ToList() ?? [];
        var names = slots.Select(s => s.Name ?? "?").ToList();
        logger.Info($"[Wedge] {tpl} registered: parent={item.Parent} slots=[{string.Join(", ", names)}]");

        foreach (var expected in expectedModSlots)
        {
            if (!names.Contains(expected))
            {
                logger.Error($"[Wedge] {tpl} has no '{expected}' slot — anything the bot type puts there cannot be fitted");
            }
        }

        // The gate the generator actually consults before it will equip this on a bot.
        if (!items.TryGetValue(new MongoId(DefaultInventory), out var inventory))
        {
            return;
        }

        var slot = inventory.Properties?.Slots?.FirstOrDefault(s =>
            string.Equals(s.Name, slotName, StringComparison.OrdinalIgnoreCase));
        if (slot is null)
        {
            logger.Error($"[Wedge] default inventory has no '{slotName}' slot to check");
            return;
        }

        var accepted = slot.Properties?.Filters?.Any(f => f.Filter?.Contains(new MongoId(tpl)) == true) == true;
        if (accepted)
        {
            logger.Info($"[Wedge] {tpl} is accepted by the '{slotName}' slot");
        }
        else
        {
            logger.Error($"[Wedge] {tpl} is NOT in the '{slotName}' slot filter — the generator will refuse to equip it");
        }
    }

    // The helmet clone inherits the EXFIL's mod_nvg filter, which accepts two mounts and not the Wilcox
    // his NVG chain runs through — so without this the generator drops the goggles before they exist.
    // Widening our own clone's filter is fair game; foreign items only get verified (below).
    public void EnsureSlotAccepts(string tpl, string slotName, string modTpl)
    {
        var slot = SlotOf(tpl, slotName);
        var filter = slot?.Properties?.Filters?.FirstOrDefault();
        if (filter?.Filter is null)
        {
            logger.Error($"[Wedge] {tpl} has no '{slotName}' filter to widen");
            return;
        }

        var mod = new MongoId(modTpl);
        if (filter.Filter.Contains(mod))
        {
            logger.Info($"[Wedge] {tpl}.{slotName} already accepts {modTpl}");
            return;
        }

        filter.Filter.Add(mod);
        logger.Info($"[Wedge] {tpl}.{slotName} widened to accept {modTpl}");
    }

    public void VerifySlotAccepts(string tpl, string slotName, string modTpl)
    {
        var slot = SlotOf(tpl, slotName);
        if (slot is null)
        {
            logger.Error($"[Wedge] {tpl} has no '{slotName}' slot — the chain below it is dead");
            return;
        }

        var accepted = slot.Properties?.Filters?.Any(f => f.Filter?.Contains(new MongoId(modTpl)) == true) == true;
        if (accepted)
        {
            logger.Info($"[Wedge] {tpl}.{slotName} accepts {modTpl}");
        }
        else
        {
            logger.Error($"[Wedge] {tpl}.{slotName} does NOT accept {modTpl} — that link of the chain will not generate");
        }
    }

    Slot? SlotOf(string tpl, string slotName)
    {
        if (!databaseService.GetItems().TryGetValue(new MongoId(tpl), out var item)) return null;
        return item.Properties?.Slots?.FirstOrDefault(s =>
            string.Equals(s.Name, slotName, StringComparison.OrdinalIgnoreCase));
    }
}
