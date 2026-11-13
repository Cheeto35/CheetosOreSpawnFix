using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Caves;
using Alta.Chunks;
using System.Linq;
using Alta.Networking;
using Alta.Populations;
using System.Text;
using Alta.Console;

namespace CheetosOreSpawnFix
{
    // Admin console commands: "orefix census 2", "orefix census surface" and friends. An area is a mine level number
    // or "surface" (the plot outside the mine entrance and the quarry above it).
    [Module("orefix", "Ore Spawn Fix: ore mix and buried nodes in the mines and the quarry")]
    public static class OreFixCommands
    {
        // The loaded ore groups of an area. Null with a message when there are none.
        static List<PopulationSample> Area(string area, out string name, out string error)
        {
            error = null;
            string a = (area ?? "").Trim().ToLowerInvariant();
            List<PopulationSample> groups;
            if (a == "surface" || a == "quarry")
            {
                name = "surface";
                groups = OreGroups.Loaded().Where(g => OreGroups.IsMined(g) && OreGroups.LevelOf(g) == OreGroups.Surface).ToList();
                if (groups.Count == 0) error = "no surface ore groups are loaded (a player must be near the quarry)";
                return groups.Count > 0 ? groups : null;
            }
            int level;
            if (!int.TryParse(a, out level)) { name = a; error = "area must be a mine level number or 'surface'"; return null; }
            name = "level " + level;
            groups = OreGroups.Loaded().Where(g => OreGroups.IsMined(g) && OreGroups.LevelOf(g) == level).ToList();
            if (groups.Count > 0) return groups;
            CaveLayerManager m = UnityEngine.Object.FindObjectOfType<CaveLayerManager>();
            ICaveLayer layer;
            if (m != null && m.Layers.TryGetValue(level, out layer) && layer.Chunk != null && layer.Chunk.IsLoaded && layer.Chunk.GetComponent<CaveLayerStreamer>() == null)
                error = "level " + level + " is a special level (no cave generator, no ore groups): " + layer.Chunk.name;
            else error = "level " + level + " is not loaded (a player must be on it)";
            return null;
        }

        // What to call a cluster that isn't one of the ores (a mining crystal, sandstone): its prefab name.
        static string NoOreName(NetworkEntity e) { return e == null ? "?" : OreTable.Clean(e.name); }

        static IEnumerable<NetworkEntity> Clusters(List<PopulationSample> groups)
        {
            foreach (PopulationSample g in groups)
                foreach (NetworkEntity e in g.SpawnedEntities.Keys.ToList())
                    if (e != null) yield return e;
        }

        internal static string Mix(int[] counts, double[] shares)
        {
            int total = counts.Sum();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < OreTable.Count; i++)
            {
                if (counts[i] == 0 && shares[i] < 0.005) continue;
                sb.Append(" | " + OreTable.Name(i) + ": " + counts[i] + " (" + (total > 0 ? 100.0 * counts[i] / total : 0).ToString("0") + "%) target " + (100 * shares[i]).ToString("0") + "%");
            }
            return sb.ToString();
        }

        [Command("census", "Ore mix of an area against the game's odds: census <level|surface>")]
        public static string Census(string area)
        {
            string name, error;
            List<PopulationSample> groups = Area(area, out name, out error);
            if (groups == null) return error;
            if (!OreTable.Load()) return "ore table not found";
            List<PopulationSample> crystals = groups.Where(OreGroups.IsCrystal).ToList();
            groups = groups.Where(OreGroups.IsOre).ToList();
            StringBuilder sb = new StringBuilder();
            if (groups.Count > 0)
            {
                double[] shares = OreTable.TargetShares(OreGroups.AttributesOf(groups[0]));
                int[] counts = new int[OreTable.Count];
                foreach (PopulationSample g in groups) { int[] c = OreTable.Counts(g, null); for (int i = 0; i < c.Length; i++) counts[i] += c[i]; }
                sb.Append(name + ": " + (groups.Count > 1 ? groups.Count + " groups, " : "") + counts.Sum() + " ore clusters" + Mix(counts, shares) + " | " + BuriedSummary(groups));
                if (groups.Count > 1)
                    foreach (PopulationSample g in groups)
                        sb.Append(Environment.NewLine + "  " + OreGroups.Label(g) + ": " + OreTable.Counts(g, null).Sum() + " of " + g.MaximumPopulation + Mix(OreTable.Counts(g, null), shares));
            }
            else sb.Append(name + ": no ore groups");
            // Mining crystals: one kind, so no mix; how many are out of the game's target, and how many are buried.
            // The game aims for 20 a level but can only place as many as the level has crystal spots.
            foreach (PopulationSample g in crystals)
                sb.Append(Environment.NewLine + "  crystals: " + g.SpawnedEntities.Count + " spawned, population " + g.CurrentPopulation + "/" + g.MaximumPopulation + " | " + BuriedSummary(new List<PopulationSample> { g }));
            return sb.ToString();
        }

        static string BuriedSummary(List<PopulationSample> groups)
        {
            int buried = 0, total = 0, queued = 0;
            Buried.Sync();
            foreach (NetworkEntity e in Clusters(groups))
            {
                total++;
                if (Buried.Is(e)) { buried++; if (BadPoints.IsQueued(e)) queued++; }
            }
            return "buried: " + buried + " of " + total + " clusters (" + queued + " queued to move), bad points: " + BadPoints.Count;
        }

        [Command("destroy", "Destroy ore clusters in an area, as if mined: destroy <level|surface> <ore name part, crystal, or all> [count, -1 = all]")]
        public static string Destroy(string area, string ore, int count = -1)
        {
            string name, error;
            List<PopulationSample> groups = Area(area, out name, out error);
            if (groups == null) return error;
            if (!OreTable.Load()) return "ore table not found";
            string want = (ore ?? "").ToLowerInvariant();
            if (want == "all" || want == "*") want = "";
            int done = 0;
            foreach (PopulationSample g in groups)
                foreach (NetworkEntity e in g.SpawnedEntities.Keys.ToList())
                {
                    if (count >= 0 && done >= count) break;
                    if (e == null) continue;
                    if (want.Length > 0)
                    {
                        // "crystal" picks the mining crystals; any other name is matched against the ore.
                        if (OreGroups.IsCrystal(g)) { if ("crystal".IndexOf(want) < 0 && want.IndexOf("crystal") < 0) continue; }
                        else { int i = OreTable.OreOf(e); if (i < 0 || OreTable.Name(i).ToLowerInvariant().IndexOf(want) < 0) continue; }
                    }
                    g.LoseMember(e);
                    e.SceneDestroy();
                    done++;
                }
            return "destroyed " + done;
        }

        [Command("refill", "Refill an area's ore groups now instead of waiting for their timer: refill <level|surface>")]
        public static string Refill(string area)
        {
            string name, error;
            List<PopulationSample> groups = Area(area, out name, out error);
            if (groups == null) return error;
            int cur = 0, max = 0, spawned = 0;
            foreach (PopulationSample g in groups)
            {
                g.ForceSpawn(g.MaximumPopulation);
                cur += g.CurrentPopulation; max += g.MaximumPopulation; spawned += g.SpawnedEntities.Count;
            }
            return "population " + cur + "/" + max + ", spawned " + spawned;
        }

        [Command("set", "Change a setting until the next reload: set <keep_mix|fix_buried> <true|false>")]
        public static string Set(string key, bool on)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "keep_mix": Main.Settings.KeepMix = on; break;
                case "fix_buried": Main.Settings.FixBuried = on; break;
                default: return "unknown setting '" + key + "' (keep_mix, fix_buried)";
            }
            Main.Log(key + "=" + on + " (console)");
            return key + "=" + on;
        }

        [Command("enable", "Switch the mod on or off (off = vanilla spawning)")]
        public static string Enable(bool on)
        {
            Main.Settings.Enabled = on;
            Main.Log("enabled=" + on + " (console)");
            return "enabled=" + on;
        }

        // Load a mine level on the server with no player there (for testing deep levels). Levels are created in order,
        // so load them top-down: a level is generated from the one above it. 100 is the game's own limit.
        [Command("load", "Load (and create if needed) a mine level on the server: load <level>")]
        public static async Task Load(int level)
        {
            if (level < 0 || level > 100) { Main.Log("load: level must be 0-100"); return; }
            CaveLayerManager m = UnityEngine.Object.FindObjectOfType<CaveLayerManager>();
            if (m == null) { Main.Log("load: no cave layer manager"); return; }
            Main.Log("load: level " + level + " (exists: " + m.Layers.ContainsKey(level) + ")");
            ICaveLayer layer = m.SpawnWithoutGenerating(level);
            Levels.Pinned.Add(level);
            Levels.PinnedChunks.Add(layer.Chunk);
            await layer.Chunk.ForceLoad();
            Main.Log("load: level " + level + " loaded");
        }

        [Command("unload", "Save a force-loaded mine level and let it unload: unload <level>")]
        public static string Unload(int level)
        {
            CaveLayerManager m = UnityEngine.Object.FindObjectOfType<CaveLayerManager>();
            ICaveLayer layer;
            if (m == null || !m.Layers.TryGetValue(level, out layer)) return "level " + level + " does not exist";
            Levels.Pinned.Remove(level);
            Levels.PinnedChunks.Remove(layer.Chunk);
            layer.Chunk.ForceUnloadAndSave();
            return "unloaded level " + level;
        }

        [Command("groups", "Every ore and sandstone group the mod is looking after right now")]
        public static string Groups()
        {
            StringBuilder sb = new StringBuilder();
            foreach (PopulationSample g in OreGroups.Loaded().OrderBy(x => OreGroups.LevelOf(x)))
                sb.Append(OreGroups.Label(g) + " '" + g.Definition.name + "' " + g.CurrentPopulation + "/" + g.MaximumPopulation + " in chunk '" + g.Chunk.name + "'" + Environment.NewLine);
            return sb.Length > 0 ? sb.ToString() : "no ore groups loaded";
        }

        // For testing the surface without a player there: list the world chunks near a spot and force-load one by name.
        [Command("chunks", "World chunks near a spot: chunks <x> <z> <radius> [name to force-load, or * for every surface chunk there]")]
        public static async Task<string> Chunks(float x, float z, float radius, string forceLoad = "")
        {
            StringBuilder sb = new StringBuilder();
            UnityEngine.Vector2 at = new UnityEngine.Vector2(x, z);
            foreach (LocationChunk c in UnityEngine.Object.FindObjectsOfType<LocationChunk>())
            {
                UnityEngine.Vector3 p = c.transform.position;
                if (UnityEngine.Vector2.Distance(at, new UnityEngine.Vector2(p.x, p.z)) > radius) continue;
                sb.Append("'" + c.name + "' at " + p.ToString("F0") + " loaded " + c.IsLoaded + " cave " + (c.GetComponent<CaveLayerStreamer>() != null) + Environment.NewLine);
                if (forceLoad.Length > 0 && !c.IsLoaded && c.GetComponent<CaveLayerStreamer>() == null && (forceLoad == "*" || c.name == forceLoad)) { await c.ForceLoad(); sb.Append("  -> force-loaded" + Environment.NewLine); }
            }
            return sb.Length > 0 ? sb.ToString() : "no chunks within " + radius + " m";
        }

        [Command("save", "Save every loaded chunk now (run before stopping the server)")]
        public static string Save()
        {
            int n = 0;
            foreach (LocationChunk c in UnityEngine.Object.FindObjectsOfType<LocationChunk>())
            {
                if (c == null || !c.IsLoaded) continue;
                c.Save();
                n++;
            }
            Main.Log("saving " + n + " loaded chunks (console)");
            return "saving " + n + " loaded chunks";
        }

        [Command("inspect", "Every ore cluster in an area: both buried checks and a spot to view it from: inspect <level|surface>")]
        public static string Inspect(string area)
        {
            string name, error;
            List<PopulationSample> groups = Area(area, out name, out error);
            if (groups == null) return error;
            Buried.Sync();
            StringBuilder sb = new StringBuilder();
            int i = 0;
            foreach (PopulationSample g in groups)
                foreach (NetworkEntity e in g.SpawnedEntities.Keys.ToList())
                {
                    if (e == null) continue;
                    int rays = Buried.InsideRockRays(e), ok, n;
                    Buried.Reach(e, out ok, out n);
                    int ore = OreTable.OreOf(e);
                    SpawnPoint p = BadPoints.PointOf(g, e);
                    sb.Append("#" + (i++) + " id " + e.Identifier + " " + (ore >= 0 ? OreTable.Name(ore) : NoOreName(e)) + " at " + e.transform.position.ToString("F2")
                        + " | rays " + rays + "/6" + (rays >= Main.Settings.BuriedRays ? " RAYS-BURIED" : "")
                        + " | reach " + ok + "/" + n + (n > 0 && ok < n ? " REACH-BURIED" : "")
                        + " | point " + (p != null ? UnityEngine.Vector3.Distance(p.Position, e.transform.position).ToString("0.0") + " m away" : "none")
                        + " | layers " + string.Join(",", e.GetComponentsInChildren<UnityEngine.Collider>().Where(c => !c.isTrigger).Select(c => UnityEngine.LayerMask.LayerToName(c.gameObject.layer)).Distinct().ToArray())
                        + " | centre " + Buried.Centre(e).ToString("F2") + " | view " + Buried.ViewPoint(e).ToString("F2") + " | " + OreGroups.Label(g) + Environment.NewLine);
                }
            return sb.ToString();
        }

        [Command("inspect1", "One cluster in detail: inspect1 <level|surface> <entity id>")]
        public static string Inspect1(string area, uint id)
        {
            string name, error;
            List<PopulationSample> groups = Area(area, out name, out error);
            if (groups == null) return error;
            NetworkEntity e = Clusters(groups).FirstOrDefault(x => x.Identifier == id);
            if (e == null) return "no cluster " + id + " in " + name;
            Buried.Sync();
            return Buried.Detail(e);
        }

        [Command("sunk", "How much of each node is hidden in rock or ground, worst first: sunk <level|surface> [how many=10]")]
        public static string Sunk(string area, int show = 10)
        {
            string name, error;
            List<PopulationSample> groups = Area(area, out name, out error);
            if (groups == null) return error;
            Buried.Sync();
            List<KeyValuePair<double, string>> rows = new List<KeyValuePair<double, string>>();
            int[] bands = new int[5];
            int boulders = 0, over = 0; int[] overHist = new int[6];
            foreach (NetworkEntity e in Clusters(groups))
            {
                Buried.SunkInfo si = Buried.Sunk(e);
                double s = si.Share;
                boulders += si.Boulders; over += si.Over; overHist[Math.Min(5, si.Over)]++;
                bands[Math.Min(4, (int)(s * 5))]++;
                int ore = OreTable.OreOf(e);
                rows.Add(new KeyValuePair<double, string>(s, "id " + e.Identifier + " " + (ore >= 0 ? OreTable.Name(ore) : NoOreName(e)) + " sunk " + (100 * s).ToString("0") + "% | reach " + (Buried.IsByReach(e) ? "BURIED" : "ok")
                    + " | worst " + si.WorstId + " at " + si.WorstCentre.ToString("F2") + " | stand " + si.Stand1.ToString("F2") + " | stand2 " + si.Stand2.ToString("F2")
                    + (s * 100 > Main.Settings.MaxSunk ? " | OVER max_sunk" : "")));
            }
            StringBuilder sb = new StringBuilder(name + ": " + rows.Count + " clusters; worst boulder hidden: 0-20% " + bands[0] + ", 20-40% " + bands[1] + ", 40-60% " + bands[2] + ", 60-80% " + bands[3] + ", 80-100% " + bands[4]);
            sb.Append(Environment.NewLine + "  boulders: " + boulders + ", over max_sunk: " + over + "; nodes with 0/1/2/3/4/5+ boulders over: " + string.Join("/", overHist.Select(x => x.ToString()).ToArray()));
            foreach (KeyValuePair<double, string> kv in rows.OrderByDescending(x => x.Key).Take(show)) sb.Append(Environment.NewLine + "  " + kv.Value);
            return sb.ToString();
        }

        [Command("sweep", "Fix sunk boulders and move buried nodes now, in the background: sweep [ignoreAge=true]")]
        public static string SweepNow(bool ignoreAge = true) { return Sweep.StartNow(ignoreAge); }

        [Command("perf", "How long the mod's work takes on the main thread: perf [reset=false]")]
        public static string PerfReport(bool reset = false) { return Perf.Report(reset); }

        [Command("reload", "Reload UserData/CheetosOreSpawnFix/config.txt")]
        public static string Reload() { return Main.LoadSettings(); }

        [Command("reset", "Clear and respawn the ore on every existing mine level, one at a time: reset <mines|surface|all|level number|stop>")]
        public static string ResetOre(string what)
        {
            return (what ?? "").Trim().ToLowerInvariant() == "stop" ? Reset.Stop() : Reset.Begin(what);
        }
    }
}
