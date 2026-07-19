using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace Wedge.Client.Brain
{
    // Takes a guard while Wedge has a live order for their group. Sits above SAIN's combat layers so a
    // cover or hold call can actually cut into a firefight; move and look orders check SainOwns and
    // yield, leaving ordinary gunfights to SAIN.
    internal class WedgeGuardLayer : CustomLayer
    {
        const float ObeyRadius = 70f;

        public WedgeGuardLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }

        public override string GetName() => "WedgeCommand";

        public override bool IsActive()
        {
            if (!WedgePlugin.MasterEnable.Value || !WedgePlugin.CommandsEnable.Value) return false;
            if (!BotOwner.IsRole((WildSpawnType)WedgePlugin.WedgeGuardType)) return false;
            if (!WedgeOrders.TryGet(BotOwner, out var order)) return false;
            if (!order.Preempts && SainInterop.SainOwns(BotOwner)) return false;

            var leader = FindLeader();
            if (leader == null) return false;
            return (leader.Position - BotOwner.Position).sqrMagnitude < ObeyRadius * ObeyRadius;
        }

        public override Action GetNextAction() => new Action(typeof(WedgeGuardLogic), "obey");

        public override bool IsCurrentActionEnding() => !IsActive();

        // Find the issuer by role, never through SAIN's squad leader — the guards are registered
        // isBoss to keep their brain active, so one of them can win that election instead of Wedge.
        BotOwner FindLeader()
        {
            var members = BotOwner.BotsGroup?.Members;
            if (members == null) return null;

            for (var i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m != null && !m.IsDead && m.IsRole((WildSpawnType)WedgePlugin.WedgeType)) return m;
            }
            return null;
        }
    }
}
