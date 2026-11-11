using System.Collections.Generic;
using System.Linq;
using Alta.Networking;
using Alta.Populations;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // Moves buried nodes to other free spawn points with the game's own RequestMove:
    //  - nodes that were buried the moment they spawned (queued by the spawn check), soon after spawning;
    //  - older buried nodes, once they have sat untouched for UntouchedHours (checked every SweepMinutes).
    // When every spawn point is taken there is nowhere to move to, so the node is removed as if mined instead and
    // the group's refill (every 10 min) respawns it on a point that is not refused.
    // Never moves a node with a player within PlayerRadius.
    public static class Sweep
    {
        static readonly UntouchedClock _clock = new UntouchedClock();
        static readonly Dictionary<uint, int> _parts = new Dictionary<uint, int>();
        static float _nextSweep = 120f, _nextDrain;

        public static void Tick()
        {
            if (!Main.Settings.Enabled || !Main.Settings.FixBuried) return;
            float now = Time.realtimeSinceStartup;
            if (now >= _nextDrain)
            {
                _nextDrain = now + 0.5f; // at most 2 moves a second
                NetworkEntity e;
                if (BadPoints.TryDequeueMove(out e)) MoveQueued(e);
            }
            if (now >= _nextSweep)
            {
                _nextSweep = now + (float)(Main.Settings.SweepMinutes * 60.0);
                string r = RunOnce(false);
                if (!r.StartsWith("moved 0,")) Main.Log("sweep: " + r);
            }
        }

        static void MoveQueued(NetworkEntity e)
        {
            if (e == null) return;
            MineLevel level = Levels.At(e.transform.position);
            if (level == null) return;
            PopulationSample sample = SampleOf(level, e);
            if (sample == null || PlayerNear(e.transform.position)) return; // the sweep tries again later
            string r = Move(sample, e, "buried when it spawned");
            Main.Log("level " + level.Number + ": queued buried node " + r);
        }

        static PopulationSample SampleOf(MineLevel level, NetworkEntity e)
        {
            if (level.Boulders != null && level.Boulders.SpawnedEntities.ContainsKey(e)) return level.Boulders;
            if (level.Sandstone != null && level.Sandstone.SpawnedEntities.ContainsKey(e)) return level.Sandstone;
            return null;
        }

        public static bool PlayerNear(Vector3 p)
        {
            float r = (float)Main.Settings.PlayerRadius;
            foreach (PlayerController pc in Object.FindObjectsOfType<PlayerController>())
                if (pc != null && Vector3.Distance(pc.transform.position, p) <= r) return true;
            return false;
        }

        static bool Touched(NetworkEntity e)
        {
            Rock[] rocks = e.GetComponentsInChildren<Rock>();
            int parts, before;
            parts = rocks.Length;
            bool lost = _parts.TryGetValue(e.Identifier, out before) && parts < before;
            _parts[e.Identifier] = parts;
            if (lost) return true;
            foreach (Rock r in rocks)
                if (r.Definition != null && r.Health < r.Definition.Health - 0.01f) return true;
            return false;
        }

        static string Move(PopulationSample sample, NetworkEntity e, string why)
        {
            Vector3 from = e.transform.position;
            BadPoints.Mark(BadPoints.PointOf(sample, e), why);
            bool moved = sample.RequestMove(e, false);
            Buried.Sync();
            if (!moved)
            {
                // Every point is taken: remove it as if mined; the group's next refill puts a new node on a good point.
                sample.LoseMember(e);
                e.SceneDestroy();
                return "replaced (no free point) at " + from.ToString("F1");
            }
            return "moved " + from.ToString("F1") + " -> " + e.transform.position.ToString("F1") + (Buried.Is(e) ? " (still buried)" : "");
        }

        // One pass over every loaded level. ignoreAge moves buried nodes regardless of how long they sat untouched.
        public static string RunOnce(bool ignoreAge)
        {
            int moved = 0, near = 0, young = 0, replaced = 0, buried = 0;
            Buried.Sync();
            double now = Time.realtimeSinceStartup;
            HashSet<uint> seen = new HashSet<uint>();
            foreach (MineLevel level in Levels.Loaded())
                foreach (PopulationSample sample in new[] { level.Boulders, level.Sandstone })
                {
                    if (sample == null) continue;
                    foreach (NetworkEntity e in sample.SpawnedEntities.Keys.ToList())
                    {
                        if (e == null) continue;
                        seen.Add(e.Identifier);
                        _clock.Seen(e.Identifier, Touched(e), now);
                        if (!Buried.Is(e)) continue;
                        buried++;
                        if (!ignoreAge && !_clock.Due(e.Identifier, now, Main.Settings.UntouchedHours)) { young++; continue; }
                        if (PlayerNear(e.transform.position)) { near++; continue; }
                        string r = Move(sample, e, "buried and untouched");
                        if (r.StartsWith("moved")) moved++; else replaced++;
                    }
                }
            foreach (uint id in _clock.Ids) if (!seen.Contains(id)) { _clock.Forget(id); _parts.Remove(id); }
            return "moved " + moved + ", skipped (player near) " + near + ", skipped (young) " + young + ", replaced " + replaced + " (of " + buried + " buried)";
        }
    }
}
