using System.Collections.Generic;
using System.Diagnostics;
using Alta.Networking;
using Alta.Populations;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // Moves buried nodes to other free spawn points of their group with the game's own RequestMove:
    //  - new nodes are checked a few frames after they spawn; buried ones are queued and moved soon after;
    //  - older buried nodes, once they have sat untouched for UntouchedHours (checked every SweepMinutes).
    // When every spawn point is taken there is nowhere to move to, so the node is removed as if mined instead and
    // the group's refill (every 10 min) respawns it on a point that is not refused.
    // Never moves a node with a player within PlayerRadius. Works on every loaded ore group: the mine levels and the
    // surface (the plot outside the mine entrance and the quarry).
    // All of this shares a small time budget per frame, so a full refill or a sweep over many loaded groups is
    // spread over several frames instead of stalling one.
    public static class Sweep
    {
        public const double FrameBudgetMs = 2.0;
        static readonly UntouchedClock _clock = new UntouchedClock();
        static readonly Dictionary<uint, int> _parts = new Dictionary<uint, int>();
        static float _nextSweep = 120f, _nextDrain;
        static Run _running;

        public static void Tick()
        {
            if (!Main.Settings.Enabled || !Main.Settings.FixBuried) { _running = null; return; }
            float now = Time.realtimeSinceStartup;
            Stopwatch frame = Stopwatch.StartNew();

            if (BadPoints.PendingChecks > 0)
            {
                Stopwatch sw = Perf.Start();
                Buried.Sync();
                NetworkEntity e; PopulationSample g;
                while (frame.Elapsed.TotalMilliseconds < FrameBudgetMs && BadPoints.TryDequeueCheck(out e, out g)) CheckNew(e, g);
                Perf.Stop("new node checks (per frame)", sw);
            }

            if (now >= _nextDrain && BadPoints.Queued > 0)
            {
                _nextDrain = now + 0.5f; // at most 2 moves a second
                NetworkEntity e; PopulationSample g;
                if (BadPoints.TryDequeueMove(out e, out g)) { Stopwatch sw = Perf.Start(); MoveQueued(e, g); Perf.Stop("move queued node", sw); }
            }

            if (_running == null && now >= _nextSweep)
            {
                _nextSweep = now + (float)(Main.Settings.SweepMinutes * 60.0);
                Stopwatch sw = Perf.Start();
                _running = new Run(false);
                Perf.Stop("sweep (per frame)", sw);
                return;
            }
            if (_running != null)
            {
                double left = FrameBudgetMs - frame.Elapsed.TotalMilliseconds;
                if (left <= 0) return;
                Stopwatch sw = Perf.Start();
                bool done = _running.Step(left);
                Perf.Stop("sweep (per frame)", sw);
                if (done)
                {
                    string r = _running.Result;
                    _running = null;
                    if (!r.StartsWith("moved 0,")) Main.Log("sweep: " + r);
                }
            }
        }

        // After the settings change, don't wait out the old interval if the new one is shorter.
        public static void Reschedule()
        {
            float next = Time.realtimeSinceStartup + (float)(Main.Settings.SweepMinutes * 60.0);
            if (next < _nextSweep) _nextSweep = next;
        }

        // A new cluster just spawned: if it is buried, queue it to be moved. Moving it gives its spawn point a strike
        // (Move), so the point is refused once it has buried two nodes, not one.
        static void CheckNew(NetworkEntity e, PopulationSample group)
        {
            if (!group.SpawnedEntities.ContainsKey(e) || !Buried.Is(e)) return;
            BadPoints.QueueMove(e, group);
            Main.Log(OreGroups.Label(group) + ": new cluster " + e.Identifier + " at " + e.transform.position.ToString("F1") + " is buried; queued to move");
        }

        static void MoveQueued(NetworkEntity e, PopulationSample group)
        {
            if (e == null || group.Chunk == null || !group.SpawnedEntities.ContainsKey(e)) return;
            if (PlayerNear(e.transform.position, PlayerPositions())) return; // the sweep tries again later
            string r = Move(group, e, "buried when it spawned");
            Main.Log(OreGroups.Label(group) + ": queued buried node " + r);
        }

        // Where every player's body is, from the game's own player list (searching the scene for players costs 20-50 ms).
        public static List<Vector3> PlayerPositions()
        {
            List<Vector3> list = new List<Vector3>();
            foreach (Alta.Networking.Scripts.Player.IPlayer ip in Player.AllPlayers)
            {
                Player p = ip as Player;
                if (p != null && p.PlayerController != null) list.Add(p.PlayerController.transform.position);
            }
            return list;
        }

        public static bool PlayerNear(Vector3 at, List<Vector3> players)
        {
            float r2 = (float)(Main.Settings.PlayerRadius * Main.Settings.PlayerRadius);
            foreach (Vector3 p in players) if ((p - at).sqrMagnitude <= r2) return true;
            return false;
        }

        static bool Touched(NetworkEntity e)
        {
            Rock[] rocks = e.GetComponentsInChildren<Rock>();
            int parts = rocks.Length, before;
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

        // One full pass over every loaded group in one go (the `orefix sweep` command). ignoreAge moves buried nodes
        // regardless of how long they sat untouched.
        public static string RunOnce(bool ignoreAge)
        {
            Run r = new Run(ignoreAge);
            r.Step(double.MaxValue);
            return r.Result;
        }

        // One pass over every cluster of every group loaded when it started, done a slice at a time.
        sealed class Run
        {
            readonly bool _ignoreAge;
            readonly List<KeyValuePair<PopulationSample, NetworkEntity>> _todo = new List<KeyValuePair<PopulationSample, NetworkEntity>>();
            readonly HashSet<uint> _seen = new HashSet<uint>();
            int _next, _moved, _near, _young, _replaced, _buried;

            public Run(bool ignoreAge)
            {
                _ignoreAge = ignoreAge;
                foreach (PopulationSample sample in OreGroups.Loaded())
                    foreach (NetworkEntity e in sample.SpawnedEntities.Keys)
                        if (e != null) _todo.Add(new KeyValuePair<PopulationSample, NetworkEntity>(sample, e));
            }

            public string Result
            {
                get { return "moved " + _moved + ", skipped (player near) " + _near + ", skipped (young) " + _young + ", replaced " + _replaced + " (of " + _buried + " buried)"; }
            }

            // Work until the budget is used up; true once every cluster has been looked at.
            public bool Step(double budgetMs)
            {
                Stopwatch sw = Stopwatch.StartNew();
                Buried.Sync();
                List<Vector3> players = PlayerPositions();
                double now = Time.realtimeSinceStartup;
                while (_next < _todo.Count)
                {
                    if (sw.Elapsed.TotalMilliseconds >= budgetMs) return false;
                    PopulationSample sample = _todo[_next].Key;
                    NetworkEntity e = _todo[_next].Value;
                    _next++;
                    if (e == null || sample.Chunk == null || !sample.SpawnedEntities.ContainsKey(e)) continue; // mined or unloaded since the pass began
                    _seen.Add(e.Identifier);
                    _clock.Seen(e.Identifier, Touched(e), now);
                    if (!Buried.Is(e)) continue;
                    _buried++;
                    if (!_ignoreAge && !_clock.Due(e.Identifier, now, Main.Settings.UntouchedHours)) { _young++; continue; }
                    if (PlayerNear(e.transform.position, players)) { _near++; continue; }
                    string r = Move(sample, e, "buried and untouched");
                    if (r.StartsWith("moved")) _moved++; else _replaced++;
                }
                foreach (uint id in _clock.Ids) if (!_seen.Contains(id)) { _clock.Forget(id); _parts.Remove(id); }
                return true;
            }
        }
    }
}
