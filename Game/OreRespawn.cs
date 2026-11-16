using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Alta.Networking;
using Alta.Populations;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    static class OreRespawn
    {
        const double MinSeconds = 10 * 60, MaxSeconds = 60 * 60;
        static readonly System.Random _random = new System.Random();

        sealed class Schedule
        {
            public readonly List<double> Due = new List<double>();
            public double RetryAt;
        }

        // A chunk can recreate its population sample on load. Placed surface groups also need their folder's position.
        static readonly Dictionary<Tuple<string, Vector3>, Schedule> _pending = new Dictionary<Tuple<string, Vector3>, Schedule>();
        static PopulationSample _releasing;
        static double _nextTick;

        static Tuple<string, Vector3> Key(PopulationSample group)
        {
            if (!Main.IsServer || !Main.Settings.Enabled || !OreGroups.IsOre(group) || group.Chunk == null) return null;
            Vector3 position = group.Parent != null && group.Parent is PopulationSpawnArea ? group.Parent.transform.position : Vector3.zero;
            return Tuple.Create(group.Chunk.ChunkIdentifier, position);
        }

        static int Capacity(PopulationSample group)
        {
            return Mathf.CeilToInt(group.MaximumPopulation * group.Definition.PercentageOfPopulationSpawned);
        }

        internal static bool TrackLoss(PopulationSample group, NetworkEntity entity)
        {
            return !ReferenceEquals(entity, null) && Key(group) != null && group.Chunk.IsFullyLoaded && group.Definition.TickFrequency > 0f
                && group.SpawnedEntities.ContainsKey(entity);
        }

        internal static void ScheduleLoss(PopulationSample group)
        {
            var key = Key(group);
            if (key == null) return;
            Schedule schedule;
            _pending.TryGetValue(key, out schedule);
            int vacancies = Capacity(group) - group.SpawnedEntities.Count;
            if (vacancies <= (schedule == null ? 0 : schedule.Due.Count)) return;
            if (schedule == null) _pending.Add(key, schedule = new Schedule());
            double due = Time.realtimeSinceStartupAsDouble + MinSeconds + _random.NextDouble() * (MaxSeconds - MinSeconds);
            int index = schedule.Due.BinarySearch(due);
            schedule.Due.Insert(index < 0 ? ~index : index, due);
        }

        // Keep the game's population growth and placement. Reserve the missing slots until their timers expire.
        internal static int LimitSpawnTarget(int target, PopulationSample group)
        {
            if (_pending.Count == 0) return target;
            var key = Key(group);
            Schedule schedule;
            if (key == null || !_pending.TryGetValue(key, out schedule)) return target;
            int spawned = group.SpawnedEntities.Count;
            int limit = group == _releasing ? spawned + 1 : Math.Max(spawned, Capacity(group) - schedule.Due.Count);
            return Math.Min(target, limit);
        }

        internal static bool Forget(PopulationSample group)
        {
            if (_pending.Count == 0 || group == _releasing) return false;
            var key = Key(group);
            return key != null && _pending.Remove(key);
        }

        internal static void Tick()
        {
            if (_pending.Count == 0) return;
            if (!Main.Settings.Enabled) { _pending.Clear(); return; }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < _nextTick) return;
            _nextTick = now + 1;

            foreach (PopulationSample group in OreGroups.Loaded())
            {
                var key = Key(group);
                Schedule schedule;
                if (key == null || !_pending.TryGetValue(key, out schedule) || !group.Chunk.IsFullyLoaded) continue;
                int before = group.SpawnedEntities.Count;
                int vacancies = Math.Max(0, Capacity(group) - before);
                if (schedule.Due.Count > vacancies) schedule.Due.RemoveRange(0, schedule.Due.Count - vacancies);
                if (schedule.Due.Count == 0) { _pending.Remove(key); continue; }
                if (now < schedule.Due[0] || now < schedule.RetryAt || group.Definition.TickFrequency <= 0f) continue;

                _releasing = group;
                try
                {
                    if (group.CurrentPopulation < group.MaximumPopulation) group.ForceSpawn(group.MaximumPopulation);
                    else group.CheckSpawned();
                }
                catch (Exception e) { Main.Log("ore respawn error: " + e.Message); }
                finally { _releasing = null; }

                // A failed placement keeps its timer ready so it can retry without taking another slot.
                if (group.SpawnedEntities.Count <= before) { schedule.RetryAt = now + 10; continue; }
                schedule.RetryAt = 0;
                schedule.Due.RemoveAt(0);
                if (schedule.Due.Count == 0) _pending.Remove(key);
            }
        }
    }

    [HarmonyPatch(typeof(PopulationSample), "LoseMember")]
    static class ScheduleOreRespawn
    {
        static void Prefix(PopulationSample __instance, NetworkEntity entity, out bool __state)
        {
            __state = OreRespawn.TrackLoss(__instance, entity);
        }

        static void Postfix(PopulationSample __instance, NetworkEntity entity, bool __state)
        {
            if (__state && !__instance.SpawnedEntities.ContainsKey(entity)) OreRespawn.ScheduleLoss(__instance);
        }
    }

    [HarmonyPatch(typeof(PopulationSample), "CheckSpawned")]
    static class ReserveOreRespawns
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo ceil = AccessTools.Method(typeof(Mathf), "CeilToInt", new[] { typeof(float) });
            int index = code.FindIndex(i => i.Calls(ceil));
            if (index < 0 || index != code.FindLastIndex(i => i.Calls(ceil)))
                throw new InvalidOperationException("Could not locate the spawn target in PopulationSample.CheckSpawned");
            code.Insert(index + 1, new CodeInstruction(OpCodes.Ldarg_0));
            code.Insert(index + 2, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OreRespawn), "LimitSpawnTarget")));
            return code;
        }
    }

    [HarmonyPatch(typeof(PopulationSample), "ForceSpawn")]
    static class ForceOreRefill
    {
        static bool Prefix(PopulationSample __instance, int targetPopulation)
        {
            if (targetPopulation < __instance.MaximumPopulation || !OreRespawn.Forget(__instance)) return true;
            if (__instance.CurrentPopulation != __instance.MaximumPopulation) return true;
            // The game skips ForceSpawn at max population even when some of those slots were reserved.
            __instance.CheckSpawned();
            return false;
        }
    }

    [HarmonyPatch(typeof(PopulationSample), "ForceKillAll")]
    static class ClearOreRespawns
    {
        static void Postfix(PopulationSample __instance) { OreRespawn.Forget(__instance); }
    }
}
