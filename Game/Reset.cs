using System.Collections.Generic;
using System.Linq;
using Alta.Caves;
using Alta.Chunks;
using Alta.Networking;
using Alta.Populations;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // `orefix reset`: clear and refill the ore on every existing mine level (and the surface if it is loaded), one level
    // at a time, for servers that had gone bad before the mod was installed. Runs from OnUpdate so the server keeps
    // ticking while levels load. It only visits levels that already exist, so it never creates new ones. Levels it had
    // to load itself are saved and unloaded again afterwards; levels a player was already on are saved and left loaded.
    public static class Reset
    {
        enum Stage { Start, WaitLoad, Tidy }

        sealed class Job
        {
            public Queue<int> Levels = new Queue<int>();
            public bool Surface;
            public int Level = -2;
            public Stage Stage;
            public bool WeLoaded;
            public float Deadline, SettleUntil;
            public int LastSpawned = -1;
            public List<PopulationSample> Groups;
            public string Cleared1;
            public int Phase;
            public int LevelsDone, Cleared, Skipped;
        }

        public const float LoadTimeout = 180f, SettleSeconds = 5f, EmptyLevelSeconds = 30f, TidyTimeout = 60f;
        static Job _job;

        public static bool Running { get { return _job != null; } }

        // what: "mines", "surface", "all" or a level number. Returns what was started, or why not.
        public static string Begin(string what)
        {
            if (_job != null) return "a reset is already running (orefix reset stop to cancel it)";
            if (!Main.Settings.Enabled || !Main.Settings.KeepMix) return "the mod is off or keep_mix is false: a reset would just respawn the vanilla mix. Turn them on first";
            if (!OreTable.Load()) return "the game has not loaded its ore table yet (the server is still starting); try again in a minute";
            CaveLayerManager m = Object.FindObjectOfType<CaveLayerManager>();
            string w = (what ?? "").Trim().ToLowerInvariant();
            if (w != "surface" && w != "quarry" && m != null && m.Layers.Count == 0)
                return "the mines haven't finished loading since the server started; try again in a minute";
            Job j = new Job();
            int one;
            if (w == "mines" || w == "all")
            {
                if (m == null) return "no cave layer manager (is the world loaded?)";
                foreach (int n in Existing(m)) j.Levels.Enqueue(n);
                if (j.Levels.Count == 0 && w == "mines") return "no mine levels found in the world save";
                j.Surface = w == "all";
            }
            else if (w == "surface" || w == "quarry") j.Surface = true;
            else if (int.TryParse(w, out one))
            {
                if (m == null || !Existing(m).Contains(one)) return "level " + one + " does not exist (reset never creates levels)";
                j.Levels.Enqueue(one);
            }
            else return "reset what? orefix reset <mines|surface|all|level number|stop>";

            string list = j.Levels.Count > 0 ? j.Levels.Count + " mine level(s) (" + j.Levels.First() + " to " + j.Levels.Last() + ")" : "";
            string msg = "reset started: " + list + (j.Surface ? (list.Length > 0 ? " and " : "") + "the surface" : "")
                + ". Progress goes to the server log, one line per level";
            _job = j;
            _spawnedTotal = 0;
            Main.Log(msg);
            return msg;
        }

        public static string Stop()
        {
            if (_job == null) return "no reset is running";
            Finish("reset stopped by console");
            return "reset stopped";
        }

        public static void Tick()
        {
            Job j = _job;
            if (j == null) return;
            if (!Main.Settings.Enabled || !Main.Settings.KeepMix) { Finish("reset stopped: the mod or keep_mix was switched off"); return; }
            float now = Time.realtimeSinceStartup;
            CaveLayerManager m = Object.FindObjectOfType<CaveLayerManager>();
            if (m == null) { Finish("reset stopped: no cave layer manager"); return; }

            switch (j.Stage)
            {
                case Stage.Start:
                    if (j.Levels.Count == 0) { if (!StartSurface(j, now)) Finish(null); return; }
                    j.Level = j.Levels.Dequeue();
                    ICaveLayer layer;
                    m.Layers.TryGetValue(j.Level, out layer);
                    j.WeLoaded = layer == null || layer.Chunk == null || !layer.Chunk.IsLoaded;
                    if (j.WeLoaded)
                    {
                        try { layer = m.SpawnWithoutGenerating(j.Level); }
                        catch (System.Exception e) { Main.Log("reset level " + j.Level + ": the game could not set it up: " + e.Message); layer = null; }
                        if (layer == null || layer.Chunk == null) { j.WeLoaded = false; Line(j, "could not be loaded, skipped"); return; }
                        global::CheetosOreSpawnFix.Levels.Pinned.Add(j.Level);
                        global::CheetosOreSpawnFix.Levels.PinnedChunks.Add(layer.Chunk);
                        layer.Chunk.ForceLoad();
                    }
                    j.Deadline = now + LoadTimeout;
                    j.SettleUntil = 0;
                    j.LastSpawned = -1;
                    j.Stage = Stage.WaitLoad;
                    return;

                case Stage.WaitLoad:
                {
                    ICaveLayer l;
                    if (!m.Layers.TryGetValue(j.Level, out l) || l.Chunk == null) { Line(j, "gone, skipped"); return; }
                    if (!l.Chunk.IsLoaded)
                    {
                        if (now > j.Deadline) { Line(j, "did not load within " + LoadTimeout.ToString("0") + " s, skipped"); return; }
                        return;
                    }
                    if (l.Chunk.GetComponent<CaveLayerStreamer>() == null) { Line(j, "special level (no ore), skipped"); return; }
                    if (j.SettleUntil == 0) { j.SettleUntil = now + SettleSeconds; j.Deadline = now + EmptyLevelSeconds; }
                    List<PopulationSample> groups = Groups(j.Level);
                    if (groups.Count == 0)
                    {
                        if (now > j.Deadline) Line(j, "no ore groups on this level, skipped");
                        return;
                    }
                    // Wait until the saved clusters have finished streaming in, so none are missed by the clear.
                    int spawned = groups.Sum(g => g.SpawnedEntities.Count);
                    if (spawned != j.LastSpawned) { j.LastSpawned = spawned; j.SettleUntil = now + SettleSeconds; return; }
                    if (now < j.SettleUntil) return;
                    j.Cleared1 = Clear(groups, j);
                    j.Groups = groups;
                    j.Phase = 0;
                    j.Deadline = now + TidyTimeout;
                    j.Stage = Stage.Tidy;
                    return;
                }

                case Stage.Tidy:
                    if (!Tidy(j, now)) return;
                    Line(j, j.Cleared1 + Summary(j.Groups));
                    return;
            }
        }

        static readonly Regex SavedLevel = new Regex(@"^Cave Layer (\d+)(\.bytes)?$");

        // Mine levels that exist: every level with a save file, plus any loaded right now (not saved yet). The game's own
        // list is no good for this: after a restart it only holds some levels, and it also holds the next level down
        // before it has been generated, which loading would create.
        public static List<int> Existing(CaveLayerManager m)
        {
            HashSet<int> levels = new HashSet<int>(m.Layers.Where(p => p.Key >= 0 && p.Value != null && p.Value.Chunk != null && p.Value.Chunk.IsLoaded).Select(p => p.Key));
            try
            {
                foreach (Alta.Utilities.IAltaFile f in ChunkFileHelper.GetFolder().AllFiles)
                {
                    Match x = SavedLevel.Match(f.Name ?? "");
                    int n;
                    if (x.Success && int.TryParse(x.Groups[1].Value, out n)) levels.Add(n);
                }
            }
            catch (System.Exception e) { Main.Log("reset: could not read the save folder: " + e.Message); }
            return levels.OrderBy(k => k).ToList();
        }

        static List<PopulationSample> Groups(int level)
        {
            return OreGroups.Loaded().Where(g => OreGroups.IsOre(g) && OreGroups.LevelOf(g) == level).ToList();
        }

        // Clear every cluster that has no player near it, then fill the groups back up. Returns how many were cleared.
        static string Clear(List<PopulationSample> groups, Job j)
        {
            List<Vector3> players = Sweep.PlayerPositions();
            int cleared = 0, near = 0;
            foreach (PopulationSample g in groups)
                foreach (NetworkEntity e in g.SpawnedEntities.Keys.ToList())
                {
                    if (e == null) continue;
                    if (Sweep.PlayerNear(e.transform.position, players)) { near++; continue; }
                    g.LoseMember(e);
                    e.SceneDestroy();
                    cleared++;
                }
            foreach (PopulationSample g in groups) g.ForceSpawn(g.MaximumPopulation);
            j.Cleared += cleared;
            return "cleared " + cleared + (near > 0 ? " (kept " + near + " next to players)" : "");
        }

        // New nodes are checked for being buried over the next frames, and buried ones are queued to move. Wait for that,
        // then sweep (fresh nodes would otherwise wait out untouched_hours) and fill any spots the sweep freed up. True
        // once the level is settled, or after TidyTimeout.
        static bool Tidy(Job j, float now)
        {
            if (!Main.Settings.FixBuried) return true;
            bool busy = BadPoints.PendingChecks > 0 || BadPoints.Queued > 0;
            switch (j.Phase)
            {
                case 0: // wait for the new nodes' buried checks, then sweep
                    if (busy && now < j.Deadline) return false;
                    string r = Sweep.RunOnce(true);
                    if (!r.StartsWith("moved 0,")) Main.Log("reset " + Name(j.Level) + ": sweep " + r);
                    j.Phase = 1;
                    j.Deadline = now + 2f; // nodes the sweep removed are only gone at the end of the frame
                    return false;
                case 1: // fill the spots the sweep freed
                    if (now < j.Deadline) return false;
                    foreach (PopulationSample g in j.Groups) if (g != null) g.ForceSpawn(g.MaximumPopulation);
                    j.Phase = 2;
                    j.Deadline = now + TidyTimeout;
                    return false;
                default: // and let those be checked (and moved if buried) too
                    return !busy || now >= j.Deadline;
            }
        }

        static string Summary(List<PopulationSample> groups)
        {
            int cur = 0, max = 0, buried = 0;
            int[] counts = new int[OreTable.Count];
            Buried.Sync();
            foreach (PopulationSample g in groups)
            {
                if (g == null) continue;
                cur += g.CurrentPopulation; max += g.MaximumPopulation;
                int[] c = OreTable.Counts(g, null);
                for (int i = 0; i < c.Length; i++) counts[i] += c[i];
                foreach (NetworkEntity e in g.SpawnedEntities.Keys.ToList()) if (e != null && Buried.Is(e)) buried++;
            }
            _spawnedTotal += counts.Sum();
            return ", now " + cur + "/" + max + ", " + buried + " buried"
                + OreFixCommands.Mix(counts, OreTable.TargetShares(OreGroups.AttributesOf(groups[0])));
        }
        static int _spawnedTotal;

        // Log the level's result, put it back the way it was found, and move on to the next level.
        static void Line(Job j, string result)
        {
            if (result.EndsWith("skipped")) j.Skipped++; else j.LevelsDone++;
            Main.Log("reset " + Name(j.Level) + ": " + result);
            Release(j);
            j.Stage = Stage.Start;
        }

        static void Release(Job j)
        {
            if (j.Level == OreGroups.Surface && j.Groups != null)
                foreach (LocationChunk c in j.Groups.Where(g => g != null).Select(g => g.Chunk).Distinct()) if (c != null && c.IsLoaded) c.Save();
            CaveLayerManager m = Object.FindObjectOfType<CaveLayerManager>();
            ICaveLayer layer;
            if (m == null || j.Level < 0 || !m.Layers.TryGetValue(j.Level, out layer) || layer.Chunk == null) return;
            if (j.WeLoaded && global::CheetosOreSpawnFix.Levels.Pinned.Contains(j.Level))
            {
                global::CheetosOreSpawnFix.Levels.Pinned.Remove(j.Level);
                global::CheetosOreSpawnFix.Levels.PinnedChunks.Remove(layer.Chunk);
                if (layer.Chunk.IsLoaded) layer.Chunk.ForceUnloadAndSave();
            }
            else if (layer.Chunk.IsLoaded) layer.Chunk.Save();
            j.Level = -2;
        }

        static string Name(int level) { return level == OreGroups.Surface ? "surface" : "level " + level; }

        // The surface last, if asked for and loaded. It is never loaded or unloaded by the reset. False when there is
        // nothing more to do.
        static bool StartSurface(Job j, float now)
        {
            if (!j.Surface) return false;
            j.Surface = false;
            List<PopulationSample> groups = OreGroups.Loaded().Where(g => OreGroups.IsOre(g) && OreGroups.LevelOf(g) == OreGroups.Surface).ToList();
            if (groups.Count == 0) { j.Skipped++; Main.Log("reset surface: not loaded, skipped (someone has to be near the quarry, then run orefix reset surface)"); return false; }
            j.Level = OreGroups.Surface;
            j.WeLoaded = false;
            j.Cleared1 = Clear(groups, j);
            j.Groups = groups;
            j.Phase = 0;
            j.Deadline = now + TidyTimeout;
            j.Stage = Stage.Tidy;
            return true;
        }

        static void Finish(string why)
        {
            Job j = _job;
            _job = null;
            if (j == null) return;
            if (j.Level != -2) Release(j);
            Main.Log(why ?? ("reset done: " + j.LevelsDone + " area(s) reset, " + j.Skipped + " skipped, " + j.Cleared + " clusters cleared, " + _spawnedTotal + " on the levels now"));
        }
    }
}
