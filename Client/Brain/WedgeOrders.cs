using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Wedge.Client.Brain
{
    internal enum WedgeOrder
    {
        None = 0,
        HoldPosition,
        Stop,
        GetInCover,
        GoForward,
        Gogogo,
        Look,
        Suppress,
    }

    internal struct WedgeOrderData
    {
        public WedgeOrder Order;
        public Vector3 Focus;
        public float Expires;
        public string IssuerId;

        public bool Live => Order != WedgeOrder.None && Time.time < Expires;

        // Cover, hold and stop are the leader overruling a firefight, which is the whole point of an
        // order. Move and look calls only land when SAIN isn't already driving the guard.
        public bool Preempts =>
            Order == WedgeOrder.HoldPosition || Order == WedgeOrder.Stop || Order == WedgeOrder.GetInCover;
    }

    // One standing order per bot group: Wedge publishes, his guards read. Keyed on BotsGroup.Id and
    // not on SAIN's squad leader, because a guard can win that election — SAIN breaks on the first
    // member flagged IsBoss and the guards are registered isBoss so their brain stays active.
    internal static class WedgeOrders
    {
        static readonly Dictionary<int, WedgeOrderData> Standing = new Dictionary<int, WedgeOrderData>();

        public static void Publish(int groupId, WedgeOrder order, Vector3 focus, float duration, string issuerId)
        {
            Standing[groupId] = new WedgeOrderData
            {
                Order = order,
                Focus = focus,
                Expires = Time.time + duration,
                IssuerId = issuerId,
            };
        }

        public static bool TryGet(BotOwner bot, out WedgeOrderData data)
        {
            data = default(WedgeOrderData);
            var group = bot?.BotsGroup;
            if (group == null) return false;
            return Standing.TryGetValue(group.Id, out data) && data.Live;
        }

        public static void Reset() => Standing.Clear();
    }
}
