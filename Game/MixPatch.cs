using System;
using System.Collections.Generic;
using Alta.Caves.Displays;
using Alta.Networking;
using Alta.Populations;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // When the game generates a new ore cluster, choose its ore so its spawn group's mix tracks the game's own odds
    // for that place (a mine level's depth, or the surface). The cluster's prefab pipeline caches its ore after two
    // steps (Core -> sub-table -> ore); seeding that cache leaves only the boulder variant to the game's roll.
    [HarmonyPatch(typeof(ClusterGenerator), "Generate")]
    static class MixPatch
    {
        public const string OrePrefab = "Rock Cluster";
        public const string SandstonePrefab = "Sandstone Cluster";
        static readonly Dictionary<PopulationSample, float> _lastLog = new Dictionary<PopulationSample, float>();

        // A refill spawns all of a group's missing clusters in one frame. Count the group's ores once per frame and
        // add each pick to that count, instead of recounting every cluster for every new one. The count never outlives
        // its frame, so nodes mined or removed since are always seen.
        sealed class GroupCount { public int Frame; public int[] Counts; }
        static readonly Dictionary<PopulationSample, GroupCount> _counts = new Dictionary<PopulationSample, GroupCount>();

        // The group a newly generated cluster belongs to: the one spawning it, or (if the game generates it some other
        // way) the mine level it is on.
        static PopulationSample GroupOf(NetworkEntity entity, bool ore)
        {
            PopulationSample g = OreGroups.Spawning;
            if (g != null && OreGroups.IsManaged(g) && OreGroups.IsOre(g) == ore) return g;
            MineLevel level = Levels.At(entity.transform.position);
            return level == null ? null : ore ? level.Boulders : level.Sandstone;
        }

        static void Prefix(ClusterGenerator __instance, SpawnData spawnData)
        {
            System.Diagnostics.Stopwatch sw = Perf.Start();
            try
            {
                if (!Main.IsServer || !Main.Settings.Enabled || !Main.Settings.KeepMix) return;
                NetworkEntity entity = __instance.Entity;
                if (entity == null || OreTable.Clean(entity.name) != OrePrefab) return;
                PopulationSample group = GroupOf(entity, true);
                if (group == null || !OreTable.Load()) return;
                double[] shares = OreTable.TargetShares(spawnData != null && spawnData.Attributes != null ? spawnData.Attributes : OreGroups.AttributesOf(group));
                int frame = Time.frameCount;
                GroupCount gc;
                if (!_counts.TryGetValue(group, out gc) || gc.Frame != frame || gc.Counts.Length != OreTable.Count)
                {
                    if (_counts.Count > 200) _counts.Clear(); // groups of unloaded areas
                    _counts[group] = gc = new GroupCount { Frame = frame, Counts = OreTable.Counts(group, entity) };
                }
                int[] counts = gc.Counts;
                int i = MixPicker.PickMostUnder(shares, counts, UnityEngine.Random.value);
                if (i < 0) return;
                object pipeline = Traverse.Create(__instance).Field("prefab").GetValue();
                Traverse t = Traverse.Create(pipeline);
                t.Field("isRun").SetValue(true);
                t.Field("cached").SetValue(OreTable.Ore(i));
                counts[i]++;
                float now = Time.realtimeSinceStartup, last;
                if (!_lastLog.TryGetValue(group, out last) || now - last > 10f)
                {
                    if (_lastLog.Count > 200) _lastLog.Clear();
                    _lastLog[group] = now;
                    int total = 0; foreach (int c in counts) total += c;
                    Main.Log(OreGroups.Label(group) + ": chose " + OreTable.Name(i) + " (had " + (counts[i] - 1) + " of " + (total - 1) + ", target " + (100 * shares[i]).ToString("0") + "%)");
                }
            }
            catch (Exception e) { Main.Log("mix patch error: " + e.Message); }
            finally { Perf.Stop("spawn: pick ore", sw); }
        }

        // After a new ore or sandstone cluster is placed, queue it for the buried check. The check runs over the next
        // frames (Sweep.Tick), a few at a time, so a big refill doesn't do them all in one frame.
        static void Postfix(ClusterGenerator __instance)
        {
            try
            {
                if (!Main.IsServer || !Main.Settings.Enabled || !Main.Settings.FixBuried) return;
                NetworkEntity entity = __instance.Entity;
                if (entity == null) return;
                string prefab = OreTable.Clean(entity.name);
                if (prefab != OrePrefab && prefab != SandstonePrefab) return;
                PopulationSample group = GroupOf(entity, prefab == OrePrefab);
                if (group != null) BadPoints.QueueCheck(entity, group);
            }
            catch (Exception e) { Main.Log("buried check error: " + e.Message); }
        }
    }
}
