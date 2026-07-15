using System;
using System.Reflection;

namespace Wedge.Client
{
    internal static class FikaBridge
    {
        static PropertyInfo _isServer;
        static bool _fikaAssemblyFound;

        public static void Init()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Fika.Core") continue;
                _fikaAssemblyFound = true;
                var utils = assembly.GetType("Fika.Core.Main.Utils.FikaBackendUtils");
                _isServer = utils?.GetProperty("IsServer", BindingFlags.Public | BindingFlags.Static);
                if (_isServer == null)
                {
                    WedgePlugin.Log.LogWarning("Fika found but host detection failed - Wedge brain disabled on this client");
                }
                return;
            }
        }

        // True when this machine owns the bots: Fika host, or Fika absent entirely.
        // Fika present but unreadable = false (inert beats a duplicate brain on every peer).
        public static bool IsHost()
        {
            if (!_fikaAssemblyFound) return true;
            if (_isServer == null) return false;
            return (bool)_isServer.GetValue(null);
        }
    }
}
