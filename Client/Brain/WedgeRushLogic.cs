using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Wedge.Client.Brain
{
    // Drives a Wedge unit toward the nearest player. SAIN handles the shooting; this just keeps the
    // pressure on by advancing (and sprinting when the gap is wide). Re-paths only after the bot has
    // moved a few metres so it isn't recomputing a route every frame.
    internal class WedgeRushLogic : CustomLogic
    {
        const float ReachDist = 8f;
        const float SprintBeyond = 25f;
        const float ReissueDist = 4f;

        // Don't lob at point-blank (self-frag) or from across the map; between these he throws.
        const float MinThrowSq = 10f * 10f;
        const float MaxThrowSq = 45f * 45f;
        const float ThrowAttemptInterval = 1.5f;

        Vector3 _lastOrderPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        float _nextThrowAttempt;

        public WedgeRushLogic(BotOwner botOwner) : base(botOwner) { }

        public override void Start() { }

        public override void Update(CustomLayer.ActionData data)
        {
            if (SainInterop.SainOwns(BotOwner)) return;

            var target = NearestHuman(BotOwner, WedgePlugin.RushRadius.Value);
            if (!target.HasValue) return;

            // Grenade barrage on approach — attempt a throw at the player before moving so it fires
            // even while holding. The engine's own gates (ReadyToThrow + the tuned DELTA_NEXT_ATTEMPT
            // cooldown) throttle the actual throws; this just keeps offering the target.
            if (WedgePlugin.GrenadeEnable.Value)
            {
                TryThrowGrenade(target.Value);
            }

            if ((_lastOrderPos - BotOwner.Position).sqrMagnitude < ReissueDist * ReissueDist) return;
            if (!NavMesh.SamplePosition(target.Value, out var hit, 5f, NavMesh.AllAreas)) return;
            if (BotOwner.Mover.GoToPoint(hit.position, false, ReachDist) == NavMeshPathStatus.PathInvalid) return;

            BotOwner.Sprint(BotOwner.Mover.DistDestination > SprintBeyond);
            BotOwner.Steering.LookToMovingDirection();
            _lastOrderPos = BotOwner.Position;
        }

        // Drives the engine's own throw sequence (HaveGrenade -> ReadyToThrow -> CanThrowGrenade ->
        // DoThrow), the exact vanilla pattern, so the group-coordination + arc checks still apply and
        // it can't self-frag or crash. Our interval just limits how often we run the arc raycast.
        void TryThrowGrenade(Vector3 target)
        {
            if (Time.time < _nextThrowAttempt) return;
            _nextThrowAttempt = Time.time + ThrowAttemptInterval;

            var grenades = BotOwner.WeaponManager?.Grenades;
            if (grenades == null || !grenades.HaveGrenade || grenades.ThrowindNow || !grenades.ReadyToThrow) return;

            var distSq = (BotOwner.Position - target).sqrMagnitude;
            if (distSq < MinThrowSq || distSq > MaxThrowSq) return;

            if (grenades.CanThrowGrenade(target))
            {
                grenades.DoThrow();
            }
        }

        // Nearest live human within maxDist, or null. Initialising the best distance to maxDist²
        // doubles as the range gate.
        public static Vector3? NearestHuman(BotOwner bot, float maxDist)
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null) return null;

            Vector3? best = null;
            var bestSq = maxDist * maxDist;
            foreach (var player in world.AllAlivePlayersList)
            {
                if (player == null || player.IsAI) continue;
                var d = (player.Position - bot.Position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = player.Position;
                }
            }
            return best;
        }
    }
}
