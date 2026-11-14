using System;
using System.Collections.Generic;
using Draygo.API;
using VRage.Utils;

namespace GVK.Navigation
{
    /// <summary>
    /// One HudAPIv2 for the whole mod. HudAPIv2 allows one instance per mod: a second one never
    /// registers, so its callback never runs. The zone HUD and the KOTH client both use this one.
    /// </summary>
    public static class GvkHudApi
    {
        private static HudAPIv2 api;
        private static readonly List<Action> pending = new List<Action>();
        private static int users;

        /// <summary>Returns the shared instance; onRegistered runs once the Text HUD API is active.</summary>
        public static HudAPIv2 Register(Action onRegistered)
        {
            users++;
            if (api != null && api.Heartbeat)
                onRegistered();
            else
                pending.Add(onRegistered);

            if (api == null)
                api = new HudAPIv2(OnRegistered);
            return api;
        }

        /// <summary>Call from UnloadData after deleting this script's HUD messages.</summary>
        public static void Release(Action onRegistered)
        {
            pending.Remove(onRegistered);
            if (--users > 0 || api == null)
                return;

            api.Close();
            api = null;
            pending.Clear();
            users = 0;
        }

        private static void OnRegistered()
        {
            for (int i = 0; i < pending.Count; i++)
            {
                try
                {
                    pending[i]();
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole($"[GvkHudApi] HUD setup failed: {ex}");
                }
            }
            pending.Clear();
        }
    }
}
