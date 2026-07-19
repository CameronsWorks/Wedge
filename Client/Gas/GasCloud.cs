using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT.HealthSystem;
using EFT;
using UnityEngine;

namespace Wedge.Client.Gas
{
    // Tracks the clouds his gas grenades leave behind and hurts whoever is standing in one without a
    // filter. There is no gas mechanic in 0.16.9 to borrow, and the custom-effect route is a dead end
    // (the effect base's SerializeState is non-virtual, and the deserialiser looks effects up by type
    // name, so a custom one throws the moment anything serialises health). Driving damage and contusion
    // from here sidesteps all of it and leaves vanilla intoxication — antidotes, food poisoning —
    // completely untouched.
    internal static class GasCloud
    {
        // Every machine ticks its own clouds. ObservedSmokeGrenade neither overrides OnExplosion nor
        // waits to be told, so each client already has the cloud locally — host-gating this the way the
        // squad-command code is gated would leave peers walking through harmless gas.
        static readonly List<SmokeGrenade> Clouds = new List<SmokeGrenade>();

        // Gathered across every cloud before anyone is hurt. Two grenades landing together is the
        // barrage working as intended, not an edge case, and without this the overlap would deal
        // double damage, double contusion and two coughs on the same tick.
        static readonly Dictionary<Player, GasProtection> Caught = new Dictionary<Player, GasProtection>();

        static float _nextTick;
        static float _nextCough;

        // How much gas the local player has taken. Builds fast in the cloud, drains slowly outside it,
        // and the sting rides this rather than raw presence — walking out doesn't clear your eyes.
        static float _exposure;
        static bool _lungsBurning;
        static bool _inGasNow;
        static GasProtection _protectionNow;

        public static void Reset()
        {
            Clouds.Clear();
            Caught.Clear();
            _nextTick = 0f;
            _nextCough = 0f;
            _exposure = 0f;
            _lungsBurning = false;
            _coughVoice = null;
            _coughTrigger = null;
        }

        public static void Register(SmokeGrenade grenade)
        {
            if (grenade == null || Clouds.Contains(grenade)) return;
            Clouds.Add(grenade);

            // Retire on the grenade's own end-of-emission event. Watching the collider instead would
            // drop the cloud during the frame between detonating and the area switching on.
            grenade.EmissionEnd += Retire;

            if (WedgePlugin.GasDebug.Value)
            {
                WedgePlugin.Log.LogInfo($"[Wedge] gas cloud armed at {grenade.transform.position}");
            }
        }

        static void Retire(Grenade grenade)
        {
            var cloud = grenade as SmokeGrenade;
            if (cloud == null) return;

            cloud.EmissionEnd -= Retire;
            Clouds.Remove(cloud);
        }

        public static void Tick()
        {
            if (Clouds.Count == 0 && _exposure <= 0f) return;
            if (Time.time < _nextTick) return;

            // Checked here as well as at detonation so switching the gas off mid-raid also puts out
            // the clouds already burning.
            if (!WedgePlugin.MasterEnable.Value || !WedgePlugin.GasEnable.Value)
            {
                Clouds.Clear();
                _exposure = 0f;
                return;
            }

            var interval = WedgePlugin.GasTickInterval.Value;
            _nextTick = Time.time + interval;

            var world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                Clouds.Clear();
                _exposure = 0f;
                return;
            }

            _inGasNow = false;
            _protectionNow = GasProtection.None;

            if (Clouds.Count > 0)
            {
                Caught.Clear();
                for (var i = Clouds.Count - 1; i >= 0; i--)
                {
                    var cloud = Clouds[i];
                    if (cloud == null)
                    {
                        Clouds.RemoveAt(i);
                        continue;
                    }
                    Collect(world, cloud);
                }

                foreach (var pair in Caught)
                {
                    Gas(pair.Key, pair.Value, interval);
                }
            }

            Linger(world, interval);
        }

        static void Collect(GameWorld world, SmokeGrenade cloud)
        {
            var area = cloud.Area;

            // Not burning yet, or between states — skip the tick without retiring the cloud.
            if (area == null || !area.gameObject.activeInHierarchy) return;

            // The collider is reparented to world space while the cloud burns and back onto the grenade
            // when it doesn't, so read the transform every tick rather than caching anything off it.
            // Radius on the grenade is a unitless curve value, not metres — the scaled collider is.
            var centre = area.transform.TransformPoint(area.center);
            var radius = area.radius * area.transform.lossyScale.x;
            if (radius <= 0f) return;

            var players = world.AllAlivePlayersList;
            if (players == null) return;

            var squared = radius * radius;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null) continue;
                if ((player.Position - centre).sqrMagnitude > squared) continue;

                var protection = MaskCheck.Level(player);
                if (protection == GasProtection.Full) continue;

                Caught[player] = protection;
            }
        }

        static void Gas(Player player, GasProtection protection, float interval)
        {
            // Observed peers have no ActiveHealthController — their own client is running this same tick
            // and will hurt them there. Bots only get hurt on the machine that owns them.
            var health = player.ActiveHealthController;
            if (health == null) return;
            if (player.IsAI && !FikaBridge.IsHost()) return;

            // A respirator keeps the gas out of your lungs — no damage, no coughing — but it doesn't
            // cover your eyes, so the sting below still lands.
            if (protection == GasProtection.None)
            {
                var damage = WedgePlugin.GasDamage.Value * interval;
                if (damage > 0f)
                {
                    var info = new DamageInfoStruct
                    {
                        DamageType = EDamageType.Poison,
                        Damage = damage,
                        SourceId = WedgePlugin.PluginId,
                    };

                    // EBodyPart.Common is not a key in the health dictionary and throws if you pass it,
                    // so the gas goes into the chest like any other whole-body source would.
                    health.ApplyDamage(EBodyPart.Chest, damage, info);
                }
            }

            if (!player.IsYourPlayer) return;

            // The sting itself is applied from Linger off the exposure meter, so leaving the cloud
            // doesn't switch it off — this only records that we're breathing it right now.
            _inGasNow = true;
            _protectionNow = protection;
        }

        // 1.0.5's tear gas punishes exposure, not presence: a few seconds inside winds the effect up
        // to nearly unplayable, and it takes most of half a minute to wear off after you stumble clear.
        static void Linger(GameWorld world, float interval)
        {
            if (_inGasNow)
            {
                _exposure = Mathf.Min(1f, _exposure + interval / Mathf.Max(0.5f, WedgePlugin.GasBuildTime.Value));
                if (_protectionNow == GasProtection.None) _lungsBurning = true;
            }
            else
            {
                _exposure = Mathf.Max(0f, _exposure - interval / Mathf.Max(1f, WedgePlugin.GasLingerTime.Value));
            }

            if (_exposure <= 0f)
            {
                _lungsBurning = false;
                return;
            }

            var player = world.MainPlayer;
            if (player == null || player.HealthController?.IsAlive != true)
            {
                _exposure = 0f;
                _lungsBurning = false;
                return;
            }

            // Screen effects are bound to the local camera, so they only mean anything here. Bots get
            // the damage and nothing else, which is the honest limit of what the engine allows.
            var health = player.ActiveHealthController;
            var blur = WedgePlugin.GasBlur.Value * _exposure;
            if (blur > 0f)
            {
                health?.DoContusion(interval * 2f, blur);
            }

            // Contusion alone reads as a wobble; the wall of it in 1.0.5 is the edges of the screen
            // closing in. TunnelVision is a stock effect (so it serialises fine), just protected —
            // hence the reflection.
            var tunnel = WedgePlugin.GasTunnel.Value * _exposure;
            if (tunnel > 0f && health != null)
            {
                Tunnel(health, interval * 2f, tunnel);
            }

            // The coughing follows the burn in your lungs out of the cloud and fades with it. A
            // respirator never lets it start.
            if (_lungsBurning && _exposure > 0.35f)
            {
                Cough(player);
            }
        }

        static MethodInfo _addTunnel;
        static bool _tunnelBroken;

        static void Tunnel(ActiveHealthController health, float time, float strength)
        {
            if (_tunnelBroken) return;

            if (_addTunnel == null)
            {
                try
                {
                    var effect = typeof(ActiveHealthController).GetNestedType("TunnelVision", BindingFlags.NonPublic);
                    _addTunnel = typeof(ActiveHealthController).GetMethod("AddEffect")?.MakeGenericMethod(effect);
                }
                catch { }

                if (_addTunnel == null)
                {
                    _tunnelBroken = true;
                    WedgePlugin.Log.LogWarning("[Wedge] tunnel vision effect moved - gas keeps the blur only");
                    return;
                }
            }

            try
            {
                _addTunnel.Invoke(health, new object[] { EBodyPart.Head, null, time, null, strength, null });
            }
            catch (Exception ex)
            {
                _tunnelBroken = true;
                WedgePlugin.Log.LogWarning($"[Wedge] tunnel vision failed, disabling: {ex.Message}");
            }
        }

        // Best cough this voice can produce, most convincing first. 117/116 are OnTearGasCough/OnCough
        // from the ported 1.0.5 bundles — ints this build's EPhraseTrigger never claimed, which is fine
        // because the speaker resolves banks by the raw int with no bounds check. Toxic is the scav gas
        // line; the pain sounds are the floor for voices that have nothing better (a voice mod, say).
        static readonly EPhraseTrigger[] CoughOrder =
        {
            (EPhraseTrigger)117,
            (EPhraseTrigger)116,
            EPhraseTrigger.Toxic,
            EPhraseTrigger.OnAgony,
            EPhraseTrigger.OnBreath,
        };

        static string _coughVoice;
        static EPhraseTrigger? _coughTrigger;

        static void Cough(Player player)
        {
            var speaker = player.Speaker;
            if (speaker == null) return;

            // Ask the speaker's own bank table what this voice can say, instead of keeping a list of
            // voices we know about — Init fills it from the loaded Voice asset and ReplaceVoice rebuilds
            // it, so voice mods we've never heard of answer for themselves. Play's null return can't be
            // used for this: it means any of six different things, only one of them "no bank".
            if (_coughVoice != speaker.PlayerVoice)
            {
                _coughVoice = speaker.PlayerVoice;
                _coughTrigger = null;

                var banks = speaker.PhrasesBanks;
                if (banks != null)
                {
                    foreach (var trigger in CoughOrder)
                    {
                        if (!banks.ContainsKey(trigger)) continue;
                        _coughTrigger = trigger;
                        break;
                    }
                }
            }

            if (_coughTrigger == null) return;
            if (Time.time < _nextCough) return;

            // A real cough can come thick; a pain grunt standing in for one gets tiring fast.
            _nextCough = _coughTrigger == (EPhraseTrigger)117 || _coughTrigger == (EPhraseTrigger)116
                ? Time.time + UnityEngine.Random.Range(3.5f, 6f)
                : Time.time + UnityEngine.Random.Range(6f, 9f);

            speaker.Play(_coughTrigger.Value, ETagStatus.Unaware, true, null);
        }
    }
}
