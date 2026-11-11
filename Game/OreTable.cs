using System.Collections.Generic;
using System.Linq;
using Alta.Caves;
using Alta.Networking;
using Alta.Populations;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // The game's ore roll for a "Rock Cluster": "Ore Distribution Core" picks a sub-table by depth ("Ore Distribution -
    // Overworld" on the surface: coal and copper; "Ore Distribution - Caves" in the mines: all six), and the sub-table
    // picks the ore. The mod's targets follow the same two steps, so a place only ever gets ores the game would give it.
    public static class OreTable
    {
        public const string CoreName = "Ore Distribution Core";
        static List<Distribution.Item> _core;                      // the sub-tables
        static readonly List<List<Distribution.Item>> _subs = new List<List<Distribution.Item>>();
        static readonly List<int[]> _subOre = new List<int[]>();   // sub-table item -> ore index
        static readonly List<Distribution> _ores = new List<Distribution>();
        static readonly Dictionary<string, int> _byPrefab = new Dictionary<string, int>();
        static bool _tried, _ok;

        public static bool Load()
        {
            if (_tried) return _ok;
            _tried = true;
            Distribution core = Resources.FindObjectsOfTypeAll<Distribution>().FirstOrDefault(d => d.name == CoreName);
            if (core == null) { Main.Log("WARNING: '" + CoreName + "' not found; ore mix is left to the game"); return false; }
            _core = Traverse.Create(core).Field("items").GetValue<List<Distribution.Item>>();
            if (_core == null || _core.Count == 0) { Main.Log("WARNING: '" + CoreName + "' has no items; ore mix is left to the game"); return false; }
            PipelineFlattenDistribution flatten = new PipelineFlattenDistribution();
            foreach (Distribution.Item sub in _core)
            {
                Distribution table = sub.Topic as Distribution;
                List<Distribution.Item> items = table != null ? Traverse.Create(table).Field("items").GetValue<List<Distribution.Item>>() : null;
                if (items == null) items = new List<Distribution.Item>();
                int[] map = new int[items.Count];
                for (int k = 0; k < items.Count; k++)
                {
                    Distribution ore = items[k].Topic as Distribution;
                    int i = ore != null ? _ores.IndexOf(ore) : -1;
                    if (ore != null && i < 0)
                    {
                        i = _ores.Count;
                        _ores.Add(ore);
                        foreach (Object prefab in flatten.PossibleResults(ore)) if (prefab != null) _byPrefab[prefab.name] = i;
                    }
                    map[k] = i;
                }
                _subs.Add(items);
                _subOre.Add(map);
            }
            if (_ores.Count == 0) { Main.Log("WARNING: '" + CoreName + "' has no ores; ore mix is left to the game"); return false; }
            _ok = true;
            Main.Log("ore table: " + string.Join(", ", Enumerable.Range(0, Count).Select(i => Name(i)).ToArray()) + " in " + _subs.Count + " sub-tables (" + _byPrefab.Count + " boulder prefabs)");
            return true;
        }

        public static int Count { get { return _ores.Count; } }

        public static string Name(int i)
        {
            string n = _ores[i] != null ? _ores[i].name : "?";
            return n.EndsWith(" Distribution") ? n.Substring(0, n.Length - " Distribution".Length) : n;
        }

        public static Distribution Ore(int i) { return _ores[i]; }

        // What share of new clusters the game gives each ore, for these attributes (null on the surface).
        public static double[] TargetShares(AttributeSet a)
        {
            double[] outer = new double[_core.Count];
            double[][] inner = new double[_core.Count][];
            for (int j = 0; j < _core.Count; j++)
            {
                outer[j] = _core[j].GetValue(a);
                inner[j] = new double[Count];
                for (int k = 0; k < _subs[j].Count; k++)
                    if (_subOre[j][k] >= 0) inner[j][_subOre[j][k]] += _subs[j][k].GetValue(a);
            }
            return MixPicker.Mixture(outer, inner);
        }

        public static string Clean(string name)
        {
            string n = name.Replace("(Clone)", "").Trim();
            int dash = n.IndexOf(" - ");
            return dash >= 0 && dash < 10 ? n.Substring(dash + 3) : n;
        }

        // Majority ore over the cluster's boulders; -1 when it has none left.
        public static int OreOf(NetworkEntity cluster)
        {
            if (cluster == null || !Load()) return -1;
            int[] votes = new int[Count];
            int any = 0;
            foreach (NetworkEntity child in cluster.GetComponentsInChildren<NetworkEntity>())
            {
                if (child == cluster) continue;
                int i;
                if (_byPrefab.TryGetValue(Clean(child.name), out i)) { votes[i]++; any++; }
            }
            if (any == 0) return -1;
            int best = 0;
            for (int i = 1; i < votes.Length; i++) if (votes[i] > votes[best]) best = i;
            return best;
        }

        public static int[] Counts(PopulationSample group, NetworkEntity except)
        {
            int[] c = new int[Count];
            if (group == null) return c;
            foreach (NetworkEntity e in group.SpawnedEntities.Keys)
            {
                if (e == null || e == except) continue;
                int i = OreOf(e);
                if (i >= 0) c[i]++;
            }
            return c;
        }
    }
}
