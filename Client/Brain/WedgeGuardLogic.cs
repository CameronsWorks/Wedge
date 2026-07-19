using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace Wedge.Client.Brain
{
    // Carries out the standing order through the guard's own actuators. The engine's request pipeline
    // is a dead end here — SAIN forces the vanilla request layer to never run when the requester is a
    // bot — so the movement is driven directly instead.
    internal class WedgeGuardLogic : CustomLogic
    {
        const float ReachDist = 3f;
        const float ReissueDist = 5f;
        const float AdvanceDist = 22f;
        const float CoverSearchRadius = 12f;

        WedgeOrder _current = WedgeOrder.None;
        float _reactAt;
        bool _acked;
        Vector3 _lastOrderPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

        public WedgeGuardLogic(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            // Staggered so three guards don't snap to an order on the same frame.
            _reactAt = Time.time + Random.Range(0.35f, 0.9f);
            _acked = false;
        }

        public override void Stop()
        {
            BotOwner.Sprint(false);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (Time.time < _reactAt) return;
            if (!WedgeOrders.TryGet(BotOwner, out var order)) return;

            if (order.Order != _current)
            {
                _current = order.Order;
                _lastOrderPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                _acked = false;
            }

            if (!_acked)
            {
                _acked = true;
                if (Random.value < WedgePlugin.AckChance.Value)
                {
                    WedgeVoice.Say(BotOwner, EPhraseTrigger.Roger);
                }
            }

            switch (order.Order)
            {
                case WedgeOrder.HoldPosition: Hold(order.Focus, 0.75f); break;
                case WedgeOrder.Stop: Hold(order.Focus, 0.4f); break;
                case WedgeOrder.GetInCover: TakeCover(order.Focus); break;
                case WedgeOrder.Look: Hold(order.Focus, 1f); break;
                case WedgeOrder.Suppress: Hold(order.Focus, 0.5f); break;
                case WedgeOrder.GoForward: Advance(order.Focus, false); break;
                case WedgeOrder.Gogogo: Advance(order.Focus, true); break;
            }
        }

        void Hold(Vector3 focus, float pose)
        {
            BotOwner.Mover.Stop();
            BotOwner.Mover.SetPose(pose);
            BotOwner.Steering.LookToPoint(focus);
        }

        // Reuses the finder the vanilla cover layers run on, and BotMover takes the resulting point
        // directly, so we never have to unpack it ourselves.
        void TakeCover(Vector3 threat)
        {
            var point = BotOwner.Covers?.GetFreeClosePoint(BotOwner.Position, CoverSearchRadius, false);
            if (point == null)
            {
                Hold(threat, 0.4f);
                return;
            }

            BotOwner.Mover.GoToPoint(point, true, true);
            BotOwner.Steering.LookToPoint(threat);
        }

        void Advance(Vector3 focus, bool rush)
        {
            if ((_lastOrderPos - BotOwner.Position).sqrMagnitude < ReissueDist * ReissueDist) return;

            var dest = Vector3.MoveTowards(BotOwner.Position, focus,
                Mathf.Min(AdvanceDist, (focus - BotOwner.Position).magnitude));

            if (!NavMesh.SamplePosition(dest, out var hit, 5f, NavMesh.AllAreas)) return;
            if (BotOwner.Mover.GoToPoint(hit.position, false, ReachDist) == NavMeshPathStatus.PathInvalid) return;

            BotOwner.Sprint(rush);
            BotOwner.Steering.LookToMovingDirection();
            _lastOrderPos = BotOwner.Position;
        }
    }
}
