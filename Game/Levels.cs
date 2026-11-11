using System.Collections.Generic;
using System.Linq;
using Alta.Chunks;
using Alta.Populations;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // A loaded mine level and the spawn groups this mod works with.
    public sealed class MineLevel
    {
        public int Number;
        public LocationChunk Chunk;
        public AttributeSet Attributes;
        public PopulationSample Boulders;
        public PopulationSample Sandstone;
    }

    // While a level is pinned for testing, ignore the game's attempts to drop its force-load (it does that when the
    // level's cave finishes rendering, which would unload it with no player there). `orefix unload` clears the
    // force-load through ForceUnloadAndSave, which writes the field directly and so is not affected.
    [HarmonyPatch(typeof(Chunk), "IsForceLoaded", MethodType.Setter)]
    static class KeepPinnedLoaded
    {
        static bool Prefix(Chunk __instance, bool value)
        {
            return value || !Levels.PinnedChunks.Contains(__instance);
        }
    }

    public static class Levels
    {
        public const string BoulderGroup = "Boulder Population";
        public const string SandstoneGroup = "Sandstone Population Population";

        // Levels held loaded by `orefix load` (testing). The game drops a level's force-load once its cave has finished
        // rendering on the server, so pinned levels are force-loaded again whenever that happens.
        public static readonly HashSet<int> Pinned = new HashSet<int>();
        public static readonly HashSet<Chunk> PinnedChunks = new HashSet<Chunk>();
        static float _nextPin;

        public static void KeepPinned()
        {
            if (Pinned.Count == 0 || Time.realtimeSinceStartup < _nextPin) return;
            _nextPin = Time.realtimeSinceStartup + 2f;
            Alta.Caves.CaveLayerManager m = Object.FindObjectOfType<Alta.Caves.CaveLayerManager>();
            if (m == null) return;
            foreach (int n in Pinned)
            {
                ICaveLayer layer;
                if (m.Layers.TryGetValue(n, out layer) && layer.Chunk != null && !layer.Chunk.IsForceLoaded)
                {
                    Main.Log("load: level " + n + " lost its force-load; loading it again");
                    layer.Chunk.ForceLoad();
                }
            }
        }

        public static MineLevel At(Vector3 p)
        {
            LocationChunk c = LocationChunkHelper.GetBestContaining(p);
            CaveLayerStreamer st = c != null ? c.GetComponent<CaveLayerStreamer>() : null;
            return st != null ? From(st) : null;
        }

        static MineLevel From(CaveLayerStreamer st)
        {
            LocationChunk chunk = st.GetComponent<LocationChunk>();
            if (chunk == null || !chunk.IsLoaded) return null;
            MineLevel l = new MineLevel { Number = st.LayerIdentifier, Chunk = chunk };
            if (st.Generator != null && st.Generator.SpawnData != null) l.Attributes = st.Generator.SpawnData.Attributes;
            Ecosystem eco = st.Ecosystem;
            List<PopulationFolderBase> folders = eco != null ? Traverse.Create(eco).Field("populations").GetValue<List<PopulationFolderBase>>() : null;
            if (folders != null)
                foreach (PopulationFolderBase f in folders)
                {
                    if (f == null || f.Population == null || f.Population.Definition == null) continue;
                    string n = f.Population.Definition.name;
                    if (n == BoulderGroup) l.Boulders = f.Population;
                    else if (n == SandstoneGroup) l.Sandstone = f.Population;
                }
            return l;
        }
    }
}
