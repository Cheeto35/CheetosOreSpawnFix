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
    // Never moves a node with a player within PlayerRadius, nor onto a spot within ArriveRadius of a player (a move
    // shows for players at once, so it would look like ore spawning in front of them). Works on every loaded ore group: the mine levels and the
    // surface (the plot outside the mine entrance and the quarry).
    // The work shares a small time budget per frame, but the boulder changes themselves are made in batches (see Run),
    // since the game rebuilds its pathfinding once for each frame in which boulders moved.
    public static class Sweep
    {
        public const double FrameBudgetMs = 2.0;
        public const float DrainSeconds = 2f;
        public const int MaxMovesPerBatch = 64;
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

            // Queued moves are made together, once the new-node checks are done, so the game rebuilds its pathfinding
            // areas once for the lot instead of once per move. Nodes waiting on a player are put back for later.
            if (now >= _nextDrain && BadPoints.Queued > 0 && BadPoints.PendingChecks == 0)
            {
                _nextDrain = now + DrainSeconds;
                Stopwatch sw = Perf.Start();
                int n = System.Math.Min(BadPoints.Queued, MaxMovesPerBatch);
                NetworkEntity e; PopulationSample g;
                for (int i = 0; i < n && BadPoints.TryDequeueMove(out e, out g); i++) MoveQueued(e, g);
                Perf.Stop("move queued nodes (batch)", sw);
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
                    if (_manual || !r.StartsWith("moved 0,") || !r.EndsWith("nudged out 0, removed 0")) Main.Log("sweep: " + r);
                    _manual = false;
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

        static readonly Dictionary<NetworkEntity, float> _retryAt = new Dictionary<NetworkEntity, float>();
        public const float RetrySeconds = 30f;

        static void MoveQueued(NetworkEntity e, PopulationSample group)
        {
            if (e == null || group.Chunk == null || !group.SpawnedEntities.ContainsKey(e)) { if (e != null) _retryAt.Remove(e); return; }
            float now = Time.realtimeSinceStartup, at;
            if (_retryAt.TryGetValue(e, out at) && now < at) { BadPoints.QueueMove(e, group); return; } // not time yet
            if (PlayerNear(e.transform.position, PlayerPositions()))
            {
                // Someone is right there: keep it queued and try again shortly, rather than leaving it to the timed sweep.
                _retryAt[e] = now + RetrySeconds;
                BadPoints.QueueMove(e, group);
                return;
            }
            _retryAt.Remove(e);
            string r = Move(group, e, "buried when it spawned");
            if (r == BlockedResult) { _retryAt[e] = now + RetrySeconds; BadPoints.QueueMove(e, group); return; }
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

        public static bool PlayerNear(Vector3 at, List<Vector3> players) { return PlayerNear(at, players, Main.Settings.PlayerRadius); }

        public static bool PlayerNear(Vector3 at, List<Vector3> players, double radius)
        {
            float r2 = (float)(radius * radius);
            foreach (Vector3 p in players) if ((p - at).sqrMagnitude <= r2) return true;
            return false;
        }

        // A moved node never lands closer than this to a player (player_radius if that is bigger).
        public const double MinArriveRadius = 30;
        public static double ArriveRadius { get { return System.Math.Max(MinArriveRadius, Main.Settings.PlayerRadius); } }

        // Nodes whose boulders have been checked for sinking (and fixed if needed), by position key.
        // By entity id: a node newly spawned on the same spot is a different node and gets checked too.
        static readonly HashSet<uint> _clean = new HashSet<uint>();

        // A node's clock key: where it stands and what it is. Entity ids change each time an area loads.
        public static string Key(NetworkEntity e)
        {
            Vector3 p = e.transform.position;
            return Mathf.RoundToInt(p.x * 10f) + "," + Mathf.RoundToInt(p.y * 10f) + "," + Mathf.RoundToInt(p.z * 10f) + ":" + OreTable.OreOf(e);
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

        const string BlockedResult = "kept: every free spot is near a player";

        static string Move(PopulationSample sample, NetworkEntity e, string why)
        {
            Vector3 from = e.transform.position;
            bool moved, blocked;
            PickPointPatch.AvoidPlayers = PlayerPositions();
            PickPointPatch.BlockedByPlayers = false;
            try { moved = sample.RequestMove(e, false); }
            finally { blocked = PickPointPatch.BlockedByPlayers; PickPointPatch.AvoidPlayers = null; PickPointPatch.BlockedByPlayers = false; }
            Buried.Sync();
            // Every free spot is near a player: leave it where it is and try again later, rather than removing it.
            if (!moved && blocked) return BlockedResult;
            BadPoints.Mark(BadPoints.PointOf(sample, e), why);
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
        // A pass worked off a slice at a time by someone else (the reset), so a big level doesn't stall one frame.
        public static object Begin(bool ignoreAge) { return new Run(ignoreAge); }
        public static bool Step(object run, double budgetMs, out string result)
        {
            Run r = (Run)run;
            bool done = r.Step(budgetMs);
            result = done ? r.Result : null;
            return done;
        }

        static bool _manual;

        // `orefix sweep`: start a pass now, worked off a slice at a time like the timed one; the result goes to the log.
        public static string StartNow(bool ignoreAge)
        {
            if (!Main.Settings.Enabled || !Main.Settings.FixBuried) return "fix_buried is off";
            if (_running != null) return "a sweep is already running; its result will be in the server log";
            _running = new Run(ignoreAge);
            _manual = true;
            _nextSweep = Time.realtimeSinceStartup + (float)(Main.Settings.SweepMinutes * 60.0);
            return "sweep started; the result goes to the server log when it's done";
        }

        // One pass over every cluster of every group loaded when it started, done a slice at a time.
        sealed class Run
        {
            readonly bool _ignoreAge;
            readonly List<KeyValuePair<PopulationSample, NetworkEntity>> _todo = new List<KeyValuePair<PopulationSample, NetworkEntity>>();
            readonly HashSet<uint> _seen = new HashSet<uint>();
            int _next, _moved, _near, _young, _replaced, _buried, _lifted, _removed;

            public Run(bool ignoreAge)
            {
                _ignoreAge = ignoreAge;
                foreach (PopulationSample sample in OreGroups.Loaded())
                    foreach (NetworkEntity e in sample.SpawnedEntities.Keys)
                        if (e != null) _todo.Add(new KeyValuePair<PopulationSample, NetworkEntity>(sample, e));
            }

            public string Result
            {
                get { return "moved " + _moved + ", skipped (player near) " + _near + ", skipped (young) " + _young + ", replaced " + _replaced + " (of " + _buried + " buried); sunk boulders nudged out " + _lifted + ", removed " + _removed; }
            }

            // The pass works in four stages, so the boulder changes happen in two frames instead of dozens: every boulder
            // carries a pathfinding navmesh cut, and the game rebuilds its pathfinding areas (60-90 ms on the main thread)
            // once for each frame in which boulders moved.
            //  1. look at every cluster and work out its sunk-boulder fixes (a slice at a time; nothing changes yet);
            //  2. make all those fixes in one frame;
            //  3. check which clusters are buried and due a move (a slice at a time);
            //  4. make all those moves in one frame.
            Unbury.Job _fix;                 // stage 1: the cluster being worked out over several frames
            PopulationSample _fixSample;
            readonly List<Unbury.Job> _toApply = new List<Unbury.Job>();
            readonly List<KeyValuePair<PopulationSample, NetworkEntity>> _toCheck = new List<KeyValuePair<PopulationSample, NetworkEntity>>();
            readonly List<KeyValuePair<PopulationSample, NetworkEntity>> _toMove = new List<KeyValuePair<PopulationSample, NetworkEntity>>();
            int _stage = 1, _nextCheck;

            // Work until the budget is used up; true once the pass is done.
            public bool Step(double budgetMs)
            {
                Stopwatch sw = Stopwatch.StartNew();
                Buried.Sync();
                List<Vector3> players = PlayerPositions();
                double now = Time.realtimeSinceStartup;
                if (_stage == 1)
                {
                    if (_fix != null)
                    {
                        if (!Unbury.Continue(_fix, budgetMs, sw)) return false;
                        FinishFix();
                    }
                    while (_next < _todo.Count)
                    {
                        if (sw.Elapsed.TotalMilliseconds >= budgetMs) return false;
                        PopulationSample sample = _todo[_next].Key;
                        NetworkEntity e = _todo[_next].Value;
                        _next++;
                        if (!Alive(sample, e)) continue; // mined or unloaded since the pass began
                        _seen.Add(e.Identifier);
                        _clock.Seen(Key(e), Touched(e), now);
                        _toCheck.Add(new KeyValuePair<PopulationSample, NetworkEntity>(sample, e));
                        if (Unbury.On && !_clean.Contains(e.Identifier) && !PlayerNear(e.transform.position, players))
                        {
                            _fix = Unbury.Start(e);
                            _fixSample = sample;
                            if (!Unbury.Continue(_fix, budgetMs, sw)) return false;
                            FinishFix();
                        }
                    }
                    _stage = 2;
                }
                if (_stage == 2)
                {
                    Unbury.Apply(_toApply);
                    _toApply.Clear();
                    _stage = 3;
                    return false; // let the moved boulders' colliders settle before the buried checks
                }
                if (_stage == 3)
                {
                    while (_nextCheck < _toCheck.Count)
                    {
                        if (sw.Elapsed.TotalMilliseconds >= budgetMs) return false;
                        KeyValuePair<PopulationSample, NetworkEntity> kv = _toCheck[_nextCheck++];
                        if (Alive(kv.Key, kv.Value)) CheckBuried(kv.Key, kv.Value, players, now);
                    }
                    _stage = 4;
                }
                // Stage 4: every move in this one frame. Players may have walked up since stage 3, so check again.
                foreach (KeyValuePair<PopulationSample, NetworkEntity> kv in _toMove)
                {
                    if (!Alive(kv.Key, kv.Value)) continue;
                    if (PlayerNear(kv.Value.transform.position, players)) { _near++; continue; }
                    string r = Move(kv.Key, kv.Value, "buried and untouched");
                    if (r == BlockedResult) _near++;
                    else if (r.StartsWith("moved")) _moved++; else _replaced++;
                }
                _toMove.Clear();
                // Nodes in areas that have unloaded keep their clocks (an unloaded node can't be touched); only nodes not
                // seen for two days are dropped.
                _clock.Prune(now, 48 * 3600.0);
                foreach (uint id in new List<uint>(_parts.Keys)) if (!_seen.Contains(id)) _parts.Remove(id);
                return true;
            }

            static bool Alive(PopulationSample sample, NetworkEntity e)
            {
                return e != null && sample != null && sample.Chunk != null && sample.SpawnedEntities.ContainsKey(e);
            }

            void FinishFix()
            {
                Unbury.Job j = _fix;
                PopulationSample sample = _fixSample;
                _fix = null; _fixSample = null;
                Unbury.Note(j.R);
                _lifted += j.R.Lifted; _removed += j.R.Removed;
                if (j.HasChanges) _toApply.Add(j);
                NetworkEntity e = j.Cluster;
                if (!Alive(sample, e)) return; // mined since
                if (_clean.Count > 50000) _clean.Clear();
                _clean.Add(e.Identifier);
            }

            void CheckBuried(PopulationSample sample, NetworkEntity e, List<Vector3> players, double now)
            {
                if (!Buried.Is(e)) return;
                _buried++;
                if (!_ignoreAge && !_clock.Due(Key(e), now, Main.Settings.UntouchedHours)) { _young++; return; }
                if (PlayerNear(e.transform.position, players)) { _near++; return; }
                _toMove.Add(new KeyValuePair<PopulationSample, NetworkEntity>(sample, e));
            }
        }
    }
}
