using System;
using System.Reflection;
using Alta.Networking;
using Alta.Populations;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // How often ore and crystals respawn, and how many come back each time.
    //
    // In the normal game, each level's ore group and crystal group checks every 10 minutes and adds up to 7 nodes:
    // nearly always 7 while the level is mostly empty, fewer (often 1 or 2) as it fills up. Sandstone, wyrms, mushrooms
    // and the rest have their own timers and are never changed here.
    //
    // respawn_minutes: a fixed time ("5") or a range ("10-60"); with a range, the wait before each respawn is picked at
    // random. Left at 10, the mod doesn't touch the timer. The server's spawn rate multiplier still applies on top.
    // respawn_amount: 0 is the game's own amount. A number ("3") or range ("2-5") adds exactly that many (or a random
    // number in the range) each respawn, until the level is full.
    static class RespawnTimer
    {
        public const double GameMinutes = 10;
        static readonly FieldInfo _changed = AccessTools.Field(typeof(PopulationDefinitionManager), "ValuedUpdated");
        static readonly FieldInfo _entry = AccessTools.Field(typeof(PopulationSample), "entry");
        static readonly PropertyInfo _current = AccessTools.Property(typeof(PopulationSample), "CurrentPopulation");
        static readonly System.Random _rng = new System.Random();

        // Set while the game catches up on respawns a level missed while it was unloaded. The number of catch-up
        // respawns is worked out from the timer, so a range uses its middle there instead of a random pick.
        internal static bool CatchingUp;
        // Set while the game runs a respawn whose amount the mod has already added.
        internal static bool AmountDone;

        static Settings S { get { return Main.Settings; } }
        static bool On { get { return Main.IsServer && S != null && S.Enabled; } }

        public static bool CustomTime
        {
            get { return On && (Math.Abs(S.RespawnMinutes - GameMinutes) > 1e-6 || Math.Abs(S.RespawnMinutesMax - GameMinutes) > 1e-6); }
        }
        static bool RandomTime { get { return CustomTime && S.RespawnMinutesMax > S.RespawnMinutes; } }
        public static bool CustomAmount { get { return On && S.RespawnAmount > 0; } }

        public static bool Applies(PopulationDefinition d)
        {
            if (d == null) return false;
            OreGroups.Kind k = OreGroups.KindOf(d.name);
            return k == OreGroups.Kind.Ore || k == OreGroups.Kind.Crystal;
        }

        static string Span(double a, double b) { return a == b ? a.ToString("0.##") : a.ToString("0.##") + "-" + b.ToString("0.##"); }

        public static string Describe()
        {
            string time = CustomTime ? "every " + Span(S.RespawnMinutes, S.RespawnMinutesMax) + " min" : "every 10 min (game default)";
            string amount = CustomAmount ? Span(S.RespawnAmount, S.RespawnAmountMax) + " nodes each time" : "game's amount";
            return time + ", " + amount;
        }

        // Seconds until the next respawn: random in the range, or its middle while catching up.
        public static float Seconds()
        {
            double lo = S.RespawnMinutes, hi = S.RespawnMinutesMax;
            double minutes = hi <= lo ? lo : CatchingUp ? (lo + hi) / 2 : lo + _rng.NextDouble() * (hi - lo);
            float rate = NetworkSceneManager.Current != null ? NetworkSceneManager.Current.SpawnRateMultiplier : 1f;
            if (rate <= 0f) rate = 1f;
            return (float)(minutes * 60.0) / rate;
        }

        public static int Amount()
        {
            int lo = S.RespawnAmount, hi = Math.Max(S.RespawnAmount, S.RespawnAmountMax);
            return lo + _rng.Next(hi - lo + 1);
        }

        // With a range, pick a new wait after each respawn. The game's timer adds its repeat interval after the respawn
        // runs, so changing that interval here sets the next wait.
        internal static void PickNextWait(PopulationSample s)
        {
            if (!RandomTime || CatchingUp || _entry == null) return;
            object entry = _entry.GetValue(s);
            if (entry == null) return;
            PropertyInfo interval = entry.GetType().GetProperty("RepeatInterval", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo set = interval != null ? interval.GetSetMethod(true) : null;
            if (set != null) set.Invoke(entry, new object[] { TimeSpan.FromSeconds(Seconds()) });
        }

        // With a custom amount, add it before the game's own growth step runs, and have that step add nothing.
        // Returns true if the level was empty, so the caller can tell the game it has started again.
        internal static bool AddAmount(PopulationSample s)
        {
            int cur = s.CurrentPopulation, max = s.MaximumPopulation;
            if (max <= 0 || cur >= max || _current == null) return false;
            _current.SetValue(s, Math.Min(max, cur + Amount()), null);
            AmountDone = true;
            return cur <= 0;
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
            if (!RespawnTimer.CustomTime || !RespawnTimer.Applies(__instance)) return;
            __result = RespawnTimer.Seconds();
        }
    }

    // While the mod has added the respawn amount itself, the game's growth step adds nothing more.
    [HarmonyPatch(typeof(PopulationDefinition), "MaxPopulationGrowth", MethodType.Getter)]
    static class RespawnAmountPatch
    {
        static void Postfix(ref int __result) { if (RespawnTimer.AmountDone) __result = 0; }
    }

    [HarmonyPatch(typeof(PopulationSample), "LoadTime")]
    static class RespawnCatchUp
    {
        static void Prefix() { RespawnTimer.CatchingUp = true; }
        static Exception Finalizer(Exception __exception) { RespawnTimer.CatchingUp = false; return __exception; }
    }

    [HarmonyPatch(typeof(PopulationSample), "CheckPopulation")]
    static class RespawnTick
    {
        static void Prefix(PopulationSample __instance, out bool __state)
        {
            __state = false;
            try
            {
                if (!RespawnTimer.CustomAmount || !RespawnTimer.Applies(__instance.Definition)) return;
                // The game first trims a group that is over its limit; leave that step alone.
                if (__instance.CurrentPopulation > __instance.MaximumPopulation) return;
                __state = RespawnTimer.AddAmount(__instance);
            }
            catch (Exception e) { Main.Log("respawn amount error: " + e.Message); }
        }

        static void Postfix(PopulationSample __instance, bool __state)
        {
            try
            {
                // The game only says a group has started again if its own growth step took it from empty.
                if (__state && __instance.CurrentPopulation > 0 && __instance.OnPopulationStarted != null) __instance.OnPopulationStarted(__instance);
                if (RespawnTimer.Applies(__instance.Definition)) RespawnTimer.PickNextWait(__instance);
            }
            catch (Exception e) { Main.Log("respawn timer error: " + e.Message); }
        }

        static Exception Finalizer(Exception __exception) { RespawnTimer.AmountDone = false; return __exception; }
    }
}
