using System;
using System.Collections.Generic;
using Alta.Caves.Displays;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // When the game generates a new ore cluster on a mine level, choose its ore so the level's mix tracks the
    // game's own odds for that depth. The cluster's prefab pipeline caches its ore after two steps
    // (Core -> Caves -> ore); seeding that cache leaves only the boulder variant to the game's roll.
    [HarmonyPatch(typeof(ClusterGenerator), "Generate")]
    static class MixPatch
    {
        public const string OrePrefab = "Rock Cluster";
        public const string SandstonePrefab = "Sandstone Cluster";
        static readonly Dictionary<int, float> _lastLog = new Dictionary<int, float>();

        static void Prefix(ClusterGenerator __instance, SpawnData spawnData)
        {
            try
            {
                if (!Main.IsServer || !Main.Settings.Enabled || !Main.Settings.KeepMix) return;
                NetworkEntity entity = __instance.Entity;
                if (entity == null || OreTable.Clean(entity.name) != OrePrefab) return;
                MineLevel level = Levels.At(entity.transform.position);
                if (level == null || !OreTable.Load()) return;
                double[] shares = OreTable.TargetShares(spawnData != null && spawnData.Attributes != null ? spawnData.Attributes : level.Attributes);
                int[] counts = OreTable.Counts(level, entity);
                int i = MixPicker.PickMostUnder(shares, counts, UnityEngine.Random.value);
                if (i < 0) return;
                object pipeline = Traverse.Create(__instance).Field("prefab").GetValue();
                Traverse t = Traverse.Create(pipeline);
                t.Field("isRun").SetValue(true);
                t.Field("cached").SetValue(OreTable.Ore(i));
                float last;
                if (!_lastLog.TryGetValue(level.Number, out last) || Time.realtimeSinceStartup - last > 10f)
                {
                    _lastLog[level.Number] = Time.realtimeSinceStartup;
                    int total = 0; foreach (int c in counts) total += c;
                    Main.Log("level " + level.Number + ": chose " + OreTable.Name(i) + " (had " + counts[i] + " of " + total + ", target " + (100 * shares[i]).ToString("0") + "%)");
                }
            }
            catch (Exception e) { Main.Log("mix patch error: " + e.Message); }
        }

        // After a new ore or sandstone cluster is placed: if it landed inside rock, refuse its spawn point from now on
        // and queue the cluster to be moved to another point.
        static void Postfix(ClusterGenerator __instance)
        {
            try
            {
                if (!Main.IsServer || !Main.Settings.Enabled || !Main.Settings.FixBuried) return;
                NetworkEntity entity = __instance.Entity;
                if (entity == null) return;
                string prefab = OreTable.Clean(entity.name);
                if (prefab != OrePrefab && prefab != SandstonePrefab) return;
                MineLevel level = Levels.At(entity.transform.position);
                if (level == null) return;
                Buried.Sync();
                if (!Buried.Is(entity)) return;
                Alta.Populations.PopulationSample sample = prefab == OrePrefab ? level.Boulders : level.Sandstone;
                BadPoints.Mark(BadPoints.PointOf(sample, entity), "new " + prefab + " was buried");
                BadPoints.QueueMove(entity);
                Main.Log("level " + level.Number + ": new " + prefab + " at " + entity.transform.position.ToString("F1") + " is buried; point refused, queued to move");
            }
            catch (Exception e) { Main.Log("buried check error: " + e.Message); }
        }
    }
}
