using System;
using System.Reflection;
using EFT;
using HarmonyLib;

namespace Wedge.Client.Brain
{
    // Reflects into SAIN to tell whether SAIN's own combat layer is driving a bot right now. When it
    // is, the rush layer yields so SAIN owns the gunfight — "Wedge routes, SAIN fights."
    internal static class SainInterop
    {
        static bool _init;
        static PropertyInfo _spawnControllerInstance;
        static MethodInfo _getSain;
        static PropertyInfo _activeLayer;

        static void EnsureInit()
        {
            if (_init) return;
            _init = true;
            try
            {
                var spawn = AccessTools.TypeByName("SAIN.Components.BotController.BotSpawnController");
                if (spawn != null)
                {
                    _spawnControllerInstance = spawn.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    _getSain = AccessTools.Method(spawn, "GetSAIN", new[] { typeof(BotOwner) });
                }
                var component = AccessTools.TypeByName("SAIN.Components.BotComponent");
                _activeLayer = component?.GetProperty("ActiveLayer", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                WedgePlugin.Log.LogWarning($"[Wedge] SAIN interop init failed, rush will not yield to SAIN: {ex.Message}");
            }
        }

        // True when SAIN is actively driving this bot (any ESAINLayer but None == 0). SAIN absent or
        // bot unregistered => false, so we drive.
        public static bool SainOwns(BotOwner bot)
        {
            EnsureInit();
            if (_spawnControllerInstance == null || _getSain == null || _activeLayer == null) return false;
            try
            {
                var controller = _spawnControllerInstance.GetValue(null);
                if (controller == null) return false;
                var comp = _getSain.Invoke(controller, new object[] { bot });
                if (comp == null) return false;
                return (int)_activeLayer.GetValue(comp) != 0;
            }
            catch { return false; }
        }
    }
}
