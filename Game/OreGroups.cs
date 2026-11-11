using System;
using System.Collections.Generic;
using Alta.Chunks;
using Alta.Populations;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // Every ore and sandstone spawn group the mod works with: one per mine level, plus the surface groups (the plot
    // outside the mine entrance and the quarry above it). Groups are noted as the game sets them up and ticks them,
    // so the mod never has to search the scene for them (that costs 40-50 ms with a dozen levels loaded).
    public static class OreGroups
    {
        public const int Surface = -1;
        static readonly HashSet<PopulationSample> _all = new HashSet<PopulationSample>();
        static readonly Stack<PopulationSample> _spawning = new Stack<PopulationSample>();

        // The group whose SpawnRandom is running right now: a cluster generated inside it belongs to it.
        public static PopulationSample Spawning { get { return _spawning.Count > 0 ? _spawning.Peek() : null; } }
        internal static void BeginSpawn(PopulationSample s) { _spawning.Push(s); Note(s); }
        internal static void EndSpawn() { if (_spawning.Count > 0) _spawning.Pop(); }

        public static bool IsManaged(PopulationSample s)
        {
            if (s == null || s.Definition == null) return false;
            string n = s.Definition.name;
            return n == Levels.BoulderGroup || n == Levels.SandstoneGroup;
        }

        public static bool IsOre(PopulationSample s) { return s != null && s.Definition != null && s.Definition.name == Levels.BoulderGroup; }

        public static void Note(PopulationSample s) { if (IsManaged(s)) _all.Add(s); }
        public static void Forget(PopulationSample s) { if (s != null) _all.Remove(s); }

        // Groups whose area is loaded right now. Groups of destroyed areas are dropped.
        public static List<PopulationSample> Loaded()
        {
            List<PopulationSample> list = new List<PopulationSample>();
            List<PopulationSample> gone = null;
            foreach (PopulationSample s in _all)
            {
                LocationChunk c = s.Chunk;
                if (c == null) { (gone ?? (gone = new List<PopulationSample>())).Add(s); continue; }
                if (c.IsLoaded) list.Add(s);
            }
            if (gone != null) foreach (PopulationSample s in gone) _all.Remove(s);
            return list;
        }

        // The mine level a group belongs to, or Surface.
        public static int LevelOf(PopulationSample s)
        {
            LocationChunk c = s != null ? s.Chunk : null;
            CaveLayerStreamer st = c != null ? c.GetComponent<CaveLayerStreamer>() : null;
            return st != null ? st.LayerIdentifier : Surface;
        }

        // The level attributes the game rolls ore with. The game passes none on the surface.
        public static AttributeSet AttributesOf(PopulationSample s)
        {
            LocationChunk c = s != null ? s.Chunk : null;
            CaveLayerStreamer st = c != null ? c.GetComponent<CaveLayerStreamer>() : null;
            return st != null && st.Generator != null && st.Generator.SpawnData != null ? st.Generator.SpawnData.Attributes : null;
        }

        public static string Label(PopulationSample s)
        {
            int level = LevelOf(s);
            if (level != Surface) return "level " + level;
            Vector3 p = s.Parent != null ? s.Parent.transform.position : Vector3.zero;
            return "surface group at (" + p.x.ToString("0") + ", " + p.z.ToString("0") + ")";
        }
    }

    [HarmonyPatch(typeof(PopulationSample), "Initialize")]
    static class NoteGroupOnInitialize { static void Postfix(PopulationSample __instance) { OreGroups.Note(__instance); } }

    [HarmonyPatch(typeof(PopulationSample), "CheckSpawned")]
    static class NoteGroupOnCheck { static void Postfix(PopulationSample __instance) { OreGroups.Note(__instance); } }

    [HarmonyPatch(typeof(PopulationSample), "Destroy")]
    static class ForgetGroupOnDestroy { static void Postfix(PopulationSample __instance) { OreGroups.Forget(__instance); } }

    [HarmonyPatch(typeof(PopulationSample), "SpawnRandom")]
    static class TrackSpawningGroup
    {
        static void Prefix(PopulationSample __instance) { OreGroups.BeginSpawn(__instance); }
        static Exception Finalizer(Exception __exception) { OreGroups.EndSpawn(); return __exception; }
    }
}
