using System.Collections.Generic;
using System.Linq;
using Alta.Networking;
using HarmonyLib;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // The game lays a node's boulders out at fixed offsets around its spawn point, so on uneven floors some of them end
    // up partly or wholly inside the floor, a wall, or a cave rock. Each boulder more than max_sunk hidden is nudged
    // the smallest distance (up, and out to the sides) that brings it under the limit; one that no nudge frees is
    // removed. Players are told a node's boulder positions when they load its area, so a fix made as the node spawns
    // is seen by everyone; a fix to an older node shows for each player the next time they load that area.
    public static class Unbury
    {
        public const float Step = 0.15f, MaxNudge = 1.5f;
        static readonly Vector3[] Ways = BuildWays();

        static Vector3[] BuildWays()
        {
            List<Vector3> w = new List<Vector3> { Vector3.up };
            for (int k = 0; k < 8; k++)
            {
                Vector3 side = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                w.Add((side + Vector3.up).normalized);
                w.Add(side);
            }
            return w.ToArray();
        }

        public sealed class Result { public int Checked, Over, Lifted, Removed; }

        static double Limit { get { return Main.Settings.MaxSunk / 100.0; } }
        public static bool On { get { return Main.IsServer && Main.Settings.Enabled && Main.Settings.FixBuried && Main.Settings.MaxSunk < 100; } }

        // Is no more than `limit` of the skin, shifted by `offset`, hidden from every observer? Stops early either way.
        static bool UnderLimit(List<Vector3> skin, Vector3 offset, List<Vector3> obs, bool fake, double limit)
        {
            int allowed = (int)System.Math.Floor(limit * skin.Count), hidden = 0;
            foreach (Vector3 p0 in skin)
            {
                Vector3 p = p0 + offset;
                bool seen = false;
                for (int i = 0; i < obs.Count && !seen; i++) seen = Buried.ClearSight(obs[i], p, fake);
                if (!seen && ++hidden > allowed) return false;
            }
            return true;
        }

        public enum Outcome { Fine, Lifted, Stuck }

        // Skin points in a boulder's own space, per boulder model: every boulder of a model has the same shape, so they
        // are found once (from a placed boulder) and reused at any position and rotation without touching physics.
        sealed class LocalSkin { public Vector3[] Points, Normals; }
        static readonly Dictionary<string, LocalSkin> _skins = new Dictionary<string, LocalSkin>();
        static readonly Vector3[] AllDirs = BuildAllDirs();
        static Vector3[] BuildAllDirs()
        {
            List<Vector3> d = new List<Vector3>();
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (x != 0 || y != 0 || z != 0) d.Add(new Vector3(x, y, z).normalized);
            return d.ToArray();
        }

        static LocalSkin SkinOf(NetworkEntity b, Collider[] own)
        {
            string key = OreTable.Clean(b.name) + "|" + b.transform.lossyScale.ToString("F2");
            LocalSkin ls;
            if (_skins.TryGetValue(key, out ls)) return ls;
            Physics.SyncTransforms(); // once per model
            Bounds bb = own[0].bounds; foreach (Collider c in own) bb.Encapsulate(c.bounds);
            List<Vector3> pts = new List<Vector3>(), nrm = new List<Vector3>();
            foreach (Vector3 d in AllDirs)
            {
                Vector3 p;
                if (!Buried.SurfacePoint(own, bb, d, out p)) continue;
                pts.Add(b.transform.InverseTransformPoint(p));
                nrm.Add(b.transform.InverseTransformDirection(d));
            }
            ls = new LocalSkin { Points = pts.ToArray(), Normals = nrm.ToArray() };
            if (pts.Count > 0) _skins[key] = ls;
            return ls;
        }

        // The skin where the boulder stands now: points on its top and sides (the underside rests on the floor).
        static List<Vector3> SkinNow(NetworkEntity b, LocalSkin ls)
        {
            List<Vector3> pts = new List<Vector3>();
            for (int i = 0; i < ls.Points.Length; i++)
            {
                Vector3 n = b.transform.TransformDirection(ls.Normals[i]).normalized;
                if (n.y < -0.35f) continue;
                pts.Add(b.transform.TransformPoint(ls.Points[i]) + n * 0.05f);
            }
            pts.Sort((x, y) => x.y.CompareTo(y.y));
            return pts;
        }

        // The points the fixer tests for a boulder where it stands now (for `orefix sunk`, so it reports what the mod acts on).
        public static List<Vector3> SkinPoints(NetworkEntity b)
        {
            Collider[] own = b.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
            if (own.Length == 0) return new List<Vector3>();
            LocalSkin ls = SkinOf(b, own);
            return ls.Points.Length == 0 ? new List<Vector3>() : SkinNow(b, ls);
        }

        // Nudge one boulder out if it is over max_sunk. Backface hits must be on. Doesn't need physics in sync.
        // Gives up (Stuck) after maxMs, leaving the boulder for the sweep, which works a slice at a time.
        public static Outcome FixBoulder(NetworkEntity b, List<Vector3> obs, bool fake, double limit, double maxMs)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            Collider[] own = b.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
            if (own.Length == 0) return Outcome.Fine;
            LocalSkin ls = SkinOf(b, own);
            if (ls.Points.Length == 0) return Outcome.Fine;
            List<Vector3> skin = SkinNow(b, ls);
            if (skin.Count == 0 || UnderLimit(skin, Vector3.zero, obs, fake, limit)) return Outcome.Fine;
            for (float dist = Step; dist <= MaxNudge + 0.001f; dist += Step)
                foreach (Vector3 w in Ways)
                {
                    if (UnderLimit(skin, w * dist, obs, fake, limit)) { b.transform.position += w * dist; return Outcome.Lifted; }
                    if (sw.Elapsed.TotalMilliseconds > maxMs) return Outcome.Stuck;
                }
            return Outcome.Stuck;
        }

        // Fix every boulder of an existing cluster that is over max_sunk; a boulder no nudge frees is removed (as if
        // mined, without drops), as long as the cluster keeps at least one. All in one go; call with physics in sync.
        public static Result Fix(NetworkEntity cluster)
        {
            Job j = Start(cluster);
            Continue(j, double.MaxValue, System.Diagnostics.Stopwatch.StartNew());
            Apply(new List<Job> { j });
            return j.R;
        }

        // The same work, done a slice at a time so a badly sunk node doesn't stall a frame. A job only reads the world:
        // the nudges and removals it decides on are kept until Apply, so a whole level's changes can be made in one
        // frame. Every boulder carries a pathfinding navmesh cut, and the game rebuilds its pathfinding areas (60-90 ms
        // on the main thread) once for each frame in which boulders moved, so changes spread over frames cost far more.
        public sealed class Job
        {
            public NetworkEntity Cluster;
            public Result R = new Result();
            internal IEnumerator<bool> Steps;
            public bool Done;
            internal readonly List<KeyValuePair<NetworkEntity, Vector3>> Nudges = new List<KeyValuePair<NetworkEntity, Vector3>>();
            internal readonly List<NetworkEntity> Removals = new List<NetworkEntity>();
            public bool HasChanges { get { return Nudges.Count > 0 || Removals.Count > 0; } }
        }

        // Make the finished jobs' nudges and removals now, all in this frame. Boulders mined since are skipped.
        public static void Apply(List<Job> jobs)
        {
            bool any = false;
            foreach (Job j in jobs)
            {
                foreach (KeyValuePair<NetworkEntity, Vector3> n in j.Nudges)
                    if (n.Key != null) { n.Key.transform.position += n.Value; any = true; }
                if (j.Removals.Count > 0 && j.Cluster != null)
                {
                    // Keep at least one boulder (a destroy only takes effect at the end of the frame, so count here).
                    int left = j.Cluster.GetComponentsInChildren<NetworkEntity>().Count(x => x != j.Cluster);
                    foreach (NetworkEntity b in j.Removals)
                        if (b != null && left > 1) { b.SceneDestroy(); left--; any = true; }
                }
                j.Nudges.Clear(); j.Removals.Clear();
            }
            if (any) Physics.SyncTransforms();
        }

        public static Job Start(NetworkEntity cluster)
        {
            Job j = new Job { Cluster = cluster };
            j.Steps = FixSteps(j).GetEnumerator();
            return j;
        }

        // Work on the job until it is done (true) or the stopwatch passes budgetMs (false: call again next frame).
        public static bool Continue(Job j, double budgetMs, System.Diagnostics.Stopwatch sw)
        {
            if (j.Done) return true;
            bool old = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                while (sw.Elapsed.TotalMilliseconds < budgetMs)
                    if (!j.Steps.MoveNext()) { j.Done = true; return true; }
                return false;
            }
            finally { Physics.queriesHitBackfaces = old; }
        }

        const int TriesPerStep = 6;

        static IEnumerable<bool> FixSteps(Job j)
        {
            NetworkEntity cluster = j.Cluster;
            Result r = j.R;
            if (cluster == null) yield break;
            bool fake = Buried.FakeTerrain(Buried.Centre(cluster));
            List<Vector3> obs = Buried.Observers(cluster, fake);
            if (obs.Count == 0) yield break; // shut in on all sides: the reach check deals with it
            yield return true;
            List<NetworkEntity> boulders = cluster.GetComponentsInChildren<NetworkEntity>().Where(x => x != cluster).ToList();
            double limit = Limit;
            foreach (NetworkEntity b in boulders)
            {
                if (b == null || cluster == null) continue; // mined since the job began
                Collider[] own = b.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
                if (own.Length == 0) continue;
                LocalSkin ls = SkinOf(b, own);
                if (ls.Points.Length == 0) continue;
                List<Vector3> skin = SkinNow(b, ls);
                r.Checked++;
                bool fine = UnderLimit(skin, Vector3.zero, obs, fake, limit);
                yield return true;
                if (fine) continue;
                Vector3 best = Vector3.zero;
                bool found = false;
                int tries = 0;
                for (float dist = Step; dist <= MaxNudge + 0.001f && !found; dist += Step)
                    foreach (Vector3 w in Ways)
                    {
                        if (UnderLimit(skin, w * dist, obs, fake, limit)) { best = w * dist; found = true; break; }
                        if (++tries % TriesPerStep == 0) yield return true;
                    }
                if (b == null) continue;
                if (found) { j.Nudges.Add(new KeyValuePair<NetworkEntity, Vector3>(b, best)); r.Lifted++; continue; }
                r.Over++;
                if (boulders.Count(x => x != null) - r.Removed > 1) { j.Removals.Add(b); r.Removed++; }
            }
        }

        // Totals for the log and `orefix census`.
        public static int Lifted, Removed, StuckAtSpawn;

        public static void Note(Result r)
        {
            Lifted += r.Lifted; Removed += r.Removed;
        }
    }

    // The game places each boulder of a new node with ClusterGenerator.PositionChild, before the boulder is set up
    // and sent to players. Nudge it out right there, so nobody ever sees it sunk.
    [HarmonyPatch(typeof(Alta.Caves.Displays.ClusterGenerator), "PositionChild")]
    static class UnburyOnSpawn
    {
        sealed class Spot { public int Frame; public List<Vector3> Obs; public bool Fake; }
        // A big refill places every missing node in one frame. Spend at most this long per frame here; nodes past it
        // are left for the sweep (players then see the fix the next time they load the area).
        public const double FrameBudgetMs = 6.0, PerBoulderMs = 2.0;
        static int _budgetFrame = -1;
        static double _spentMs;
        static readonly Dictionary<int, Spot> _spots = new Dictionary<int, Spot>();

        static void Postfix(Alta.Caves.Displays.ClusterGenerator __instance, NetworkEntity child)
        {
            if (!Unbury.On || __instance == null || child == null) return;
            if (_budgetFrame != Time.frameCount) { _budgetFrame = Time.frameCount; _spentMs = 0; }
            if (_spentMs >= FrameBudgetMs) return;
            System.Diagnostics.Stopwatch sw = Perf.Start();
            System.Diagnostics.Stopwatch mine = System.Diagnostics.Stopwatch.StartNew();
            bool old = Physics.queriesHitBackfaces;
            try
            {
                NetworkEntity e = __instance.Entity;
                if (e == null) return;
                string prefab = OreTable.Clean(e.name);
                if (MixPatch.KindOfPrefab(prefab) == OreGroups.Kind.None) return;
                // Viewpoints around the node's spot, worked out once per node (its boulders are placed in one frame).
                int id = __instance.GetInstanceID();
                Spot s;
                if (!_spots.TryGetValue(id, out s) || s.Frame != Time.frameCount)
                {
                    if (_spots.Count > 500) _spots.Clear();
                    Vector3 at = __instance.transform.position;
                    bool fake = Buried.FakeTerrain(at);
                    s = new Spot { Frame = Time.frameCount, Fake = fake, Obs = Buried.ObserversAround(at + Vector3.up * 0.5f, new Bounds(at + Vector3.up * 0.4f, new Vector3(2.4f, 1.2f, 2.4f)), fake) };
                    _spots[id] = s;
                }
                if (s.Obs.Count == 0) return;
                Physics.queriesHitBackfaces = true;
                Unbury.Outcome o = Unbury.FixBoulder(child, s.Obs, s.Fake, Main.Settings.MaxSunk / 100.0, System.Math.Min(PerBoulderMs, FrameBudgetMs - _spentMs));
                if (o == Unbury.Outcome.Lifted) { Unbury.Lifted++; Main.Log("spawn: nudged sunk boulder " + child.Identifier + " out to " + child.transform.position.ToString("F2")); }
                else if (o == Unbury.Outcome.Stuck) Unbury.StuckAtSpawn++;
            }
            catch (System.Exception ex) { Main.Log("unbury error: " + ex.Message); }
            finally { Physics.queriesHitBackfaces = old; _spentMs += mine.Elapsed.TotalMilliseconds; Perf.Stop("spawn: nudge sunk boulders", sw); }
        }
    }
}
