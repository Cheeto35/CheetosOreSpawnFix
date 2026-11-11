using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Caves;
using Alta.Chunks;
using System.Linq;
using Alta.Networking;
using System.Text;
using Alta.Console;

namespace CheetosOreSpawnFix
{
    // Admin console commands: "orefix census 2" and friends.
    [Module("orefix", "Ore Spawn Fix: mine ore mix and buried nodes")]
    public static class OreFixCommands
    {
        [Command("census", "Ore mix on a loaded mine level against the game's odds")]
        public static string Census(int level)
        {
            MineLevel lvl = Levels.Number(level);
            if (lvl == null)
            {
                CaveLayerManager m = UnityEngine.Object.FindObjectOfType<CaveLayerManager>();
                ICaveLayer layer;
                if (m != null && m.Layers.TryGetValue(level, out layer) && layer.Chunk != null && layer.Chunk.IsLoaded && layer.Chunk.GetComponent<CaveLayerStreamer>() == null)
                    return "level " + level + " is a special level (no cave generator, no ore groups): " + layer.Chunk.name;
                return "level " + level + " is not loaded (a player must be on it)";
            }
            if (!OreTable.Load()) return "ore table not found";
            int[] counts = OreTable.Counts(lvl, null);
            double[] shares = OreTable.TargetShares(lvl.Attributes);
            int total = counts.Sum();
            StringBuilder sb = new StringBuilder();
            sb.Append("level " + level + ": " + total + " ore clusters");
            for (int i = 0; i < OreTable.Count; i++)
            {
                if (counts[i] == 0 && shares[i] < 0.005) continue;
                sb.Append(" | " + OreTable.Name(i) + ": " + counts[i] + " (" + (total > 0 ? 100.0 * counts[i] / total : 0).ToString("0") + "%) target " + (100 * shares[i]).ToString("0") + "%");
            }
            sb.Append(" | " + BuriedSummary(lvl));
            return sb.ToString();
        }

        static string BuriedSummary(MineLevel lvl)
        {
            int buried = 0, total = 0, queued = 0;
            Buried.Sync();
            if (lvl.Boulders != null)
                foreach (NetworkEntity e in lvl.Boulders.SpawnedEntities.Keys)
                {
                    if (e == null) continue;
                    total++;
                    if (Buried.Is(e)) { buried++; if (BadPoints.IsQueued(e)) queued++; }
                }
            return "buried: " + buried + " of " + total + " clusters (" + queued + " queued to move), bad points: " + BadPoints.Count;
        }

        [Command("destroy", "Destroy ore clusters on a level, as if mined: destroy <level> <ore name part> [count, -1 = all]")]
        public static string Destroy(int level, string ore, int count = -1)
        {
            MineLevel lvl = Levels.Number(level);
            if (lvl == null || lvl.Boulders == null) return "level " + level + " is not loaded";
            if (!OreTable.Load()) return "ore table not found";
            string want = (ore ?? "").ToLowerInvariant();
            int done = 0;
            foreach (NetworkEntity e in new List<NetworkEntity>(lvl.Boulders.SpawnedEntities.Keys))
            {
                if (count >= 0 && done >= count) break;
                if (e == null) continue;
                int i = OreTable.OreOf(e);
                if (want.Length > 0 && (i < 0 || OreTable.Name(i).ToLowerInvariant().IndexOf(want) < 0)) continue;
                lvl.Boulders.LoseMember(e);
                e.SceneDestroy();
                done++;
            }
            return "destroyed " + done;
        }

        [Command("refill", "Refill a level's ore spawn group now instead of waiting for its timer")]
        public static string Refill(int level)
        {
            MineLevel lvl = Levels.Number(level);
            if (lvl == null || lvl.Boulders == null) return "level " + level + " is not loaded";
            lvl.Boulders.ForceSpawn(lvl.Boulders.MaximumPopulation);
            return "population " + lvl.Boulders.CurrentPopulation + "/" + lvl.Boulders.MaximumPopulation + ", spawned " + lvl.Boulders.SpawnedEntities.Count;
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

        [Command("save", "Save every loaded chunk now (run before stopping the server)")]
        public static string Save()
        {
            int n = 0;
            foreach (Alta.Chunks.LocationChunk c in UnityEngine.Object.FindObjectsOfType<Alta.Chunks.LocationChunk>())
            {
                if (c == null || !c.IsLoaded) continue;
                c.Save();
                n++;
            }
            Main.Log("saving " + n + " loaded chunks (console)");
            return "saving " + n + " loaded chunks";
        }

        [Command("inspect", "Every ore cluster on a level: both buried checks and a spot to view it from")]
        public static string Inspect(int level)
        {
            MineLevel lvl = Levels.Number(level);
            if (lvl == null || lvl.Boulders == null) return "level " + level + " is not loaded";
            Buried.Sync();
            StringBuilder sb = new StringBuilder();
            int i = 0;
            foreach (NetworkEntity e in lvl.Boulders.SpawnedEntities.Keys)
            {
                if (e == null) continue;
                int rays = Buried.InsideRockRays(e), ok, n;
                Buried.Reach(e, out ok, out n);
                int ore = OreTable.OreOf(e);
                sb.Append("#" + (i++) + " id " + e.Identifier + " " + (ore >= 0 ? OreTable.Name(ore) : "?") + " at " + e.transform.position.ToString("F2")
                    + " | rays " + rays + "/6" + (rays >= Main.Settings.BuriedRays ? " RAYS-BURIED" : "")
                    + " | reach " + ok + "/" + n + (n > 0 && ok < n ? " REACH-BURIED" : "")
                    + " | layers " + string.Join(",", e.GetComponentsInChildren<UnityEngine.Collider>().Where(c => !c.isTrigger).Select(c => UnityEngine.LayerMask.LayerToName(c.gameObject.layer)).Distinct().ToArray())
                    + " | centre " + Buried.Centre(e).ToString("F2") + " | view " + Buried.ViewPoint(e).ToString("F2") + Environment.NewLine);
            }
            return sb.ToString();
        }

        [Command("inspect1", "One cluster in detail: inspect1 <level> <entity id>")]
        public static string Inspect1(int level, uint id)
        {
            MineLevel lvl = Levels.Number(level);
            if (lvl == null || lvl.Boulders == null) return "level " + level + " is not loaded";
            NetworkEntity e = lvl.Boulders.SpawnedEntities.Keys.FirstOrDefault(x => x != null && x.Identifier == id);
            if (e == null) return "no cluster " + id + " on level " + level;
            Buried.Sync();
            return Buried.Detail(e);
        }

        [Command("sweep", "Move buried nodes now: sweep [ignoreAge=true]")]
        public static string SweepNow(bool ignoreAge = true) { return Sweep.RunOnce(ignoreAge); }

        [Command("reload", "Reload UserData/CheetosOreSpawnFix/config.txt")]
        public static string Reload() { return Main.LoadSettings(); }
    }
}
