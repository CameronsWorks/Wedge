using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace Wedge.Client.Brain
{
    // Takes over a Wedge unit when there's a player to hunt and SAIN isn't already fighting, handing
    // control to the rush logic. Priority sits below SAIN's combat layers, and IsActive yields to
    // SAIN explicitly, so the gunfight always belongs to SAIN.
    internal class WedgeRushLayer : CustomLayer
    {
        public WedgeRushLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }

        public override string GetName() => "WedgeRush";

        public override bool IsActive()
        {
            if (!WedgePlugin.MasterEnable.Value || !WedgePlugin.BrainEnable.Value) return false;
            if (SainInterop.SainOwns(BotOwner)) return false;
            return WedgeRushLogic.NearestHuman(BotOwner, WedgePlugin.RushRadius.Value).HasValue;
        }

        public override Action GetNextAction() => new Action(typeof(WedgeRushLogic), "rush");

        public override bool IsCurrentActionEnding() => !IsActive();
    }
}
