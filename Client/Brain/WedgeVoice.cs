using EFT;

namespace Wedge.Client.Brain
{
    // Speak through the bot's own speaker and nothing else. SAIN's SAINBotTalkClass.Say looks like the
    // polite route, but its bool means "I called the speaker", not "a line played" — tellSpeakerToSay is
    // void and throws away the one result that would tell us. Trusting it made the fallback below
    // unreachable and hid every dropped line. PhraseSpeakerClass.Play is unpatched by SAIN and hands
    // back the bank it actually played, so it can answer the question honestly.
    internal static class WedgeVoice
    {
        public static void Say(BotOwner bot, EPhraseTrigger trigger)
        {
            if (bot == null || bot.IsDead) return;

            var speaker = bot.GetPlayer?.Speaker;
            if (speaker == null)
            {
                WedgePlugin.Log.LogWarning($"[Wedge] say-fail no-speaker {trigger}");
                return;
            }

            if (speaker.Play(trigger, ETagStatus.Unaware, true, null) != null) return;

            // Null bank means nothing played. Mid-line is ordinary and self-corrects; anything else
            // means his voice has no bank for the trigger, which is the failure worth chasing.
            WedgePlugin.Log.LogWarning(speaker.Speaking || speaker.Busy
                ? $"[Wedge] say-dropped {trigger} (already speaking)"
                : $"[Wedge] say-dropped {trigger} (no bank)");
        }
    }
}
