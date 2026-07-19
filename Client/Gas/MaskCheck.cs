using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;

namespace Wedge.Client.Gas
{
    internal enum GasProtection
    {
        None,
        // A respirator filters what you breathe but leaves your eyes open to the sting.
        Respirator,
        Full,
    }

    // Nothing on a face cover says "this filters gas" — FaceCoverTemplateClass adds exactly one member
    // (IsHalfMask, unset on every mask that matters) — so protection is two template-id lists. That's
    // fine here: we own the only code that applies the effect, so we gate at the source rather than
    // patching the engine's own intoxication.
    internal static class MaskCheck
    {
        static string _rawFull, _rawPartial;
        static HashSet<string> _full = new HashSet<string>();
        static HashSet<string> _partial = new HashSet<string>();

        static void Refresh()
        {
            var full = WedgePlugin.GasMaskTpls.Value ?? string.Empty;
            var partial = WedgePlugin.GasRespiratorTpls.Value ?? string.Empty;
            if (full == _rawFull && partial == _rawPartial) return;
            _rawFull = full;
            _rawPartial = partial;
            _full = Parse(full);
            _partial = Parse(partial);
        }

        static HashSet<string> Parse(string raw) => new HashSet<string>(
            raw.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        public static GasProtection Level(Player player)
        {
            if (player?.Equipment == null) return GasProtection.None;
            Refresh();

            var worn = WornTpl(player, EquipmentSlot.FaceCover);
            if (worn != null)
            {
                if (_full.Contains(worn)) return GasProtection.Full;
                if (_partial.Contains(worn)) return GasProtection.Respirator;
            }

            // A sealed helmet like the Ronin covers the eyes too, so it counts as full.
            if (WedgePlugin.GasMaskCheckHeadwear.Value)
            {
                worn = WornTpl(player, EquipmentSlot.Headwear);
                if (worn != null && _full.Contains(worn)) return GasProtection.Full;
            }

            return GasProtection.None;
        }

        static string WornTpl(Player player, EquipmentSlot slot)
        {
            try
            {
                return player.Equipment.GetSlot(slot)?.ContainedItem?.StringTemplateId;
            }
            catch { return null; }
        }
    }
}
