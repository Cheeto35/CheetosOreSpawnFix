using System;
using System.Reflection;
using Alta.Networking;
using Alta.Populations;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // How often ore and crystals respawn. In the normal game, each level's ore group and crystal group checks every
    // 10 minutes and may add a few nodes each time. With respawn_minutes left at 10 the mod doesn't touch the timer at
    // all. Any other value replaces those two timers (sandstone, wyrms, mushrooms and the rest keep the game's own).
    // The server's spawn rate multiplier still applies on top, as it does to the game's timer.
    static class RespawnTimer
    {
        public const double GameMinutes = 10;
        static readonly FieldInfo _changed = AccessTools.Field(typeof(PopulationDefinitionManager), "ValuedUpdated");

        public static bool Custom
        {
            get { return Main.IsServer && Main.Settings != null && Main.Settings.Enabled && Math.Abs(Main.Settings.RespawnMinutes - GameMinutes) > 1e-6; }
        }

        public static bool Applies(PopulationDefinition d)
        {
            if (d == null) return false;
            OreGroups.Kind k = OreGroups.KindOf(d.name);
            return k == OreGroups.Kind.Ore || k == OreGroups.Kind.Crystal;
        }

        public static string Describe()
        {
            return Custom ? "every " + Main.Settings.RespawnMinutes + " min (respawn_minutes)" : "every 10 min (game default)";
        }

        // The game only reads the timer when a group starts. After a reload or enable, tell running groups to pick up
        // the new value: the game's own hook for this keeps the time already waited, capped at the new length.
        public static void Refresh()
        {
            try
            {
                Action<PopulationDefinition> changed = _changed != null ? _changed.GetValue(null) as Action<PopulationDefinition> : null;
                if (changed == null) return;
                foreach (PopulationDefinition d in Resources.FindObjectsOfTypeAll<PopulationDefinition>())
                    if (Applies(d)) changed(d);
            }
            catch (Exception e) { Main.Log("respawn timer error: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PopulationDefinition), "TickFrequency", MethodType.Getter)]
    static class RespawnTimerPatch
    {
        static void Postfix(PopulationDefinition __instance, ref float __result)
        {
            if (!RespawnTimer.Custom || !RespawnTimer.Applies(__instance)) return;
            float rate = NetworkSceneManager.Current != null ? NetworkSceneManager.Current.SpawnRateMultiplier : 1f;
            if (rate <= 0f) rate = 1f;
            __result = (float)(Main.Settings.RespawnMinutes * 60.0) / rate;
        }
    }
}
