using System.Collections.Generic;
using System.Linq;
using Alta.Caves;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // The game's cave ore table ("Ore Distribution - Caves"): one entry per ore, each weighted by level attributes.
    public static class OreTable
    {
        public const string TableName = "Ore Distribution - Caves";
        static List<Distribution.Item> _items;
        static readonly Dictionary<string, int> _byPrefab = new Dictionary<string, int>();
        static bool _tried, _ok;

        public static bool Load()
        {
            if (_tried) return _ok;
            _tried = true;
            Distribution table = Resources.FindObjectsOfTypeAll<Distribution>().FirstOrDefault(d => d.name == TableName);
            if (table == null) { Main.Log("WARNING: '" + TableName + "' not found; ore mix is left to the game"); return false; }
            _items = Traverse.Create(table).Field("items").GetValue<List<Distribution.Item>>();
            if (_items == null || _items.Count == 0) { Main.Log("WARNING: '" + TableName + "' has no items; ore mix is left to the game"); return false; }
            PipelineFlattenDistribution flatten = new PipelineFlattenDistribution();
            for (int i = 0; i < _items.Count; i++)
                foreach (Object prefab in flatten.PossibleResults(_items[i].Topic))
                    if (prefab != null) _byPrefab[prefab.name] = i;
            _ok = true;
            Main.Log("ore table: " + string.Join(", ", Enumerable.Range(0, Count).Select(i => Name(i)).ToArray()) + " (" + _byPrefab.Count + " boulder prefabs)");
            return true;
        }

        public static int Count { get { return _items != null ? _items.Count : 0; } }

        public static string Name(int i)
        {
            string n = _items[i].Topic != null ? _items[i].Topic.name : "?";
            return n.EndsWith(" Distribution") ? n.Substring(0, n.Length - " Distribution".Length) : n;
        }

        public static Distribution Ore(int i) { return _items[i].Topic as Distribution; }

        public static double[] TargetShares(AttributeSet a)
        {
            double[] w = new double[Count];
            for (int i = 0; i < Count; i++) w[i] = _items[i].GetValue(a);
            return MixPicker.Normalize(w);
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

        public static int[] Counts(MineLevel lvl, NetworkEntity except)
        {
            int[] c = new int[Count];
            if (lvl == null || lvl.Boulders == null) return c;
            foreach (NetworkEntity e in lvl.Boulders.SpawnedEntities.Keys)
            {
                if (e == null || e == except) continue;
                int i = OreOf(e);
                if (i >= 0) c[i]++;
            }
            return c;
        }
    }
}
