using System.Collections;
using System.Reflection;
using Alta.Caves;
using HarmonyLib;

namespace CheetosOreSpawnFix
{
    // Mining crystals never respawn after a level has been saved and loaded again (normal game behaviour).
    // A cave level's "dynamic" parts only run the first time the level is ever generated; the level is then saved as
    // generated and they are skipped on every later load. The spot list for mining crystals is one of those parts, so
    // after a restart the crystal group has no spots at all: its 10-minute timer keeps trying and every spawn fails,
    // and a mined crystal never comes back.
    //
    // On later loads, run just that part again. The cave's generation is seeded from the world, so it rebuilds the same
    // spots the level had at first. Its only other dynamic input is the teleport-totem map (crystals keep away from
    // totems): that map is computed too, but the totems themselves are not spawned again. Every other dynamic part
    // stays skipped, as in the normal game.
    static class CrystalRespawn
    {
        public const string CrystalPlugin = "Mining Crystals";
        public const string TotemPlugin = "Teleport Totem Positions";
        internal static readonly PropertyInfo IsEnabledProp = AccessTools.Property(typeof(PluginInstance), "IsEnabled");

        public static bool On { get { return Main.IsServer && Main.Settings != null && Main.Settings.Enabled && Main.Settings.RespawnCrystals; } }

        // A dynamic part the game skipped on this load that we turn back on.
        internal static bool Wanted(PluginInstance p)
        {
            return p != null && p.Plugin != null && p.Plugin.IsDynamic && p.Generator != null && !p.Generator.IsGeneratingDynamic
                && (p.Name == CrystalPlugin || p.Name == TotemPlugin);
        }
    }

    [HarmonyPatch(typeof(PluginInstance), MethodType.Constructor, new[] { typeof(IGenerator), typeof(Plugin), typeof(bool) })]
    static class CrystalRespawnEnable
    {
        static void Postfix(PluginInstance __instance, bool isEnabled)
        {
            try
            {
                if (!isEnabled || __instance.IsEnabled || !CrystalRespawn.On || !CrystalRespawn.Wanted(__instance)) return;
                CrystalRespawn.IsEnabledProp.SetValue(__instance, true, null);
            }
            catch (System.Exception e) { Main.Log("crystal respawn error: " + e.Message); }
        }
    }

    // Crystals saved while their group had no spots are saved with spot number -1. When the game links saved crystals
    // back to their group it looks that number up, and with spots back a lookup of -1 throws, which stops the linking of
    // every crystal after it: they stay in the world but outside the group, and the group then spawns a full set on top.
    // Link such a crystal to the spot it stands on instead (crystals are placed exactly on a spot).
    [HarmonyPatch(typeof(Alta.Populations.PopulationSample), "AddEntity", new[] { typeof(Alta.Networking.NetworkEntity), typeof(int) })]
    static class CrystalRelink
    {
        public const float MaxDistance = 0.5f;
        static readonly FieldInfo _spawner = AccessTools.Field(typeof(Alta.Populations.PopulationSample), "spawner");

        static void Prefix(Alta.Populations.PopulationSample __instance, Alta.Networking.NetworkEntity entity, ref int pointIndex)
        {
            try
            {
                if (pointIndex >= 0 || entity == null || !OreGroups.IsCrystal(__instance)) return;
                Alta.Populations.ISpawnSystem sys = _spawner != null ? _spawner.GetValue(__instance) as Alta.Populations.ISpawnSystem : null;
                Alta.Populations.PointsPopulationValue points = sys != null ? sys[__instance.Definition.Hash] as Alta.Populations.PointsPopulationValue : null;
                if (points == null) return;
                UnityEngine.Vector3 at = entity.transform.position;
                float best = MaxDistance * MaxDistance;
                foreach (Alta.Populations.SpawnPoint p in points.Points)
                {
                    if (p == null || !p.IsFree) continue;
                    float d = (p.Position - at).sqrMagnitude;
                    if (d <= best) { best = d; pointIndex = p.Index; }
                }
            }
            catch (System.Exception e) { Main.Log("crystal relink error: " + e.Message); }
        }
    }

    // A spot number of -1 means "no spot": answer that instead of throwing (the game only ever asked for -1 when a group
    // had no spots, so it never came up before).
    [HarmonyPatch(typeof(Alta.Populations.PointsPopulationValue), "GetPoint")]
    static class NoSpotForMinusOne
    {
        static bool Prefix(int pointIndex, ref Alta.Populations.SpawnPoint __result)
        {
            if (pointIndex >= 0) return true;
            __result = null;
            return false;
        }
    }

    // The totem part is only needed for its map: don't spawn the totems again.
    [HarmonyPatch(typeof(PluginInstance), "RenderChunk")]
    static class CrystalRespawnNoTotems
    {
        static bool Prefix(PluginInstance __instance, ref IEnumerator __result)
        {
            if (__instance.Name != CrystalRespawn.TotemPlugin || !CrystalRespawn.On || !CrystalRespawn.Wanted(__instance)) return true;
            __result = new object[0].GetEnumerator();
            return false;
        }
    }
}
