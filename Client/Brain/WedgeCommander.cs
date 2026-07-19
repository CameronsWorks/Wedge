using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Wedge.Client.Brain
{
    // Reads Wedge's situation and turns it into a standing order for his group, speaking the matching
    // line at the same moment so voice and behaviour can't drift apart. Between AI, EFT throws a
    // phrase away before it reaches behaviour (BotReceiver drops anything from an AI speaker), so the
    // order travels through WedgeOrders instead and the phrase is only the audible half.
    internal static class WedgeCommander
    {
        const float GlobalCooldown = 9f;
        const float PerOrderCooldown = 26f;

        static readonly List<BotOwner> Leaders = new List<BotOwner>();
        static readonly Dictionary<string, float> NextOrder = new Dictionary<string, float>();
        static readonly Dictionary<string, Dictionary<WedgeOrder, float>> NextByOrder =
            new Dictionary<string, Dictionary<WedgeOrder, float>>();
        static readonly Dictionary<string, int> LastMembers = new Dictionary<string, int>();

        public static void Register(BotOwner bot)
        {
            if (bot == null || !bot.IsRole((WildSpawnType)WedgePlugin.WedgeType)) return;
            if (Leaders.Contains(bot)) return;
            Leaders.Add(bot);

            // The count here is a snapshot taken as his layer is built, before the escort has finished
            // spawning — it reads 1 even when the group ends up four strong. Evaluate logs the real
            // composition as it settles.
            var group = bot.BotsGroup;
            WedgePlugin.Log.LogInfo($"[Wedge] commander registered: grp={group?.Id} members={group?.MembersCount} (still spawning)");
        }

        public static void Reset()
        {
            Leaders.Clear();
            NextOrder.Clear();
            NextByOrder.Clear();
            LastMembers.Clear();
            WedgeOrders.Reset();
        }

        public static void Tick()
        {
            if (!WedgePlugin.MasterEnable.Value || !WedgePlugin.CommandsEnable.Value) return;

            for (var i = Leaders.Count - 1; i >= 0; i--)
            {
                var bot = Leaders[i];
                if (bot == null || bot.IsDead || bot.BotsGroup == null)
                {
                    Leaders.RemoveAt(i);
                    continue;
                }
                Evaluate(bot);
            }
        }

        static void Evaluate(BotOwner wedge)
        {
            var id = wedge.ProfileId;
            if (NextOrder.TryGetValue(id, out var next) && Time.time < next) return;

            var group = wedge.BotsGroup;
            if (group == null) return;

            // Whether the guards actually joined his group is the one thing that decides if any of this
            // runs, and a closed gate is otherwise completely silent. Logging on change answers it in
            // about four lines a raid instead of leaving an absence to interpret.
            if (!LastMembers.TryGetValue(id, out var seen) || seen != group.MembersCount)
            {
                LastMembers[id] = group.MembersCount;
                WedgePlugin.Log.LogInfo($"[Wedge] group grp={group.Id} members={group.MembersCount}");
            }

            if (group.MembersCount < 2) return;

            Vector3 focus;
            float duration;
            var order = Choose(wedge, out focus, out duration);
            if (order == WedgeOrder.None || !OffCooldown(id, order)) return;

            WedgeOrders.Publish(group.Id, order, focus, duration, id);
            WedgeVoice.Say(wedge, TriggerFor(order));

            NextOrder[id] = Time.time + GlobalCooldown;
            NextByOrder[id][order] = Time.time + PerOrderCooldown;

            if (WedgePlugin.CommandDebug.Value)
            {
                WedgePlugin.Log.LogInfo($"[Wedge] order {order} grp={group.Id} for {duration:0.0}s");
            }
        }

        // Cheap situational read; the check order is the priority order.
        static WedgeOrder Choose(BotOwner wedge, out Vector3 focus, out float duration)
        {
            focus = wedge.Position;
            duration = 12f;

            var target = WedgeRushLogic.NearestHuman(wedge, WedgePlugin.RushRadius.Value);
            var contact = SainInterop.HasEnemy(wedge);

            // Taking rounds and can't see who's shooting — break the squad off before pushing again.
            if (wedge.Memory != null && wedge.Memory.IsUnderFire && !target.HasValue)
            {
                duration = 20f;
                return WedgeOrder.GetInCover;
            }

            if (contact && target.HasValue)
            {
                focus = target.Value;
                if ((target.Value - wedge.Position).magnitude < 25f)
                {
                    duration = 10f;
                    return WedgeOrder.Gogogo;
                }
                duration = 14f;
                return WedgeOrder.Suppress;
            }

            if (target.HasValue)
            {
                focus = target.Value;
                duration = 16f;
                return WedgeOrder.GoForward;
            }

            // Nothing in sight — hold the line and sweep angles rather than wandering off.
            focus = wedge.Position + wedge.LookDirection * 25f;
            duration = 12f;
            return Random.value < 0.5f ? WedgeOrder.HoldPosition : WedgeOrder.Look;
        }

        static bool OffCooldown(string id, WedgeOrder order)
        {
            if (!NextByOrder.TryGetValue(id, out var map))
            {
                map = new Dictionary<WedgeOrder, float>();
                NextByOrder[id] = map;
            }
            return !map.TryGetValue(order, out var t) || Time.time >= t;
        }

        // Only these seven exist as banks in his voice; anything else would fall through to the
        // default scav voice or play nothing at all.
        public static EPhraseTrigger TriggerFor(WedgeOrder order)
        {
            switch (order)
            {
                case WedgeOrder.HoldPosition: return EPhraseTrigger.HoldPosition;
                case WedgeOrder.Stop: return EPhraseTrigger.Stop;
                case WedgeOrder.GetInCover: return EPhraseTrigger.GetInCover;
                case WedgeOrder.GoForward: return EPhraseTrigger.GoForward;
                case WedgeOrder.Gogogo: return EPhraseTrigger.Gogogo;
                case WedgeOrder.Look: return EPhraseTrigger.Look;
                default: return EPhraseTrigger.Suppress;
            }
        }
    }
}
