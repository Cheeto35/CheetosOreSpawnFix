using System.Collections.Generic;
using System.Linq;
using Alta.Networking;
using UnityEngine;

namespace CheetosOreSpawnFix
{
    // Is a node buried? Two checks:
    //  - Reach (used): each boulder of a cluster must have at least one spot just outside its surface that has a clear
    //    line, through no rock, to the cluster's anchor (its spawn point, which sits on the open cave floor). A cluster
    //    with any boulder that fails is buried: that boulder can never be mined, so the cluster never frees its point.
    //  - Rays (old, kept for comparison): of 6 rays from the cluster's centre, how many first hit rock from behind.
    //    It misses nodes deep in thick rock (rays into solid rock hit nothing) and tests the cluster, not each boulder.
    public static class Buried
    {
        static readonly Vector3[] Axes = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        static readonly Vector3[] Around = BuildAround();
        static int _world = -1;

        static Vector3[] BuildAround()
        {
            List<Vector3> d = new List<Vector3>(Axes);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) d.Add(new Vector3(x, y, z).normalized);
            return d.ToArray();
        }

        // Cave rock and floors. Boulders, items and players are on other layers, so they never block the line.
        public static int World { get { if (_world < 0) _world = LayerMask.GetMask("Terrain", "Ground", "Wall"); return _world; } }

        public static void Sync() { Physics.SyncTransforms(); }

        public static Vector3 Centre(NetworkEntity e)
        {
            Collider[] own = e.GetComponentsInChildren<Collider>();
            if (own.Length == 0) return e.transform.position;
            Bounds b = own[0].bounds;
            for (int i = 1; i < own.Length; i++) b.Encapsulate(own[i].bounds);
            return b.center;
        }

        // The cluster's anchor: its spawn point lifted half a metre off the floor (open cave air).
        public static Vector3 Anchor(NetworkEntity cluster) { return cluster.transform.position + Vector3.up * 0.5f; }

        // One bounds per boulder (each boulder is a child entity of the cluster).
        public static List<Bounds> Boulders(NetworkEntity cluster)
        {
            List<Bounds> list = new List<Bounds>();
            foreach (NetworkEntity child in cluster.GetComponentsInChildren<NetworkEntity>())
            {
                if (child == cluster) continue;
                Collider[] cs = child.GetComponentsInChildren<Collider>();
                bool any = false; Bounds b = default(Bounds);
                foreach (Collider c in cs)
                {
                    if (c.isTrigger) continue;
                    if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
                }
                if (any) list.Add(b);
            }
            return list;
        }

        // Is there clear air (no rock surface, front or back) on the straight line between two points?
        public static bool Clear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            return !Physics.Raycast(from, d / len, len, World, QueryTriggerInteraction.Ignore);
        }

        public static bool Reachable(Bounds boulder, Vector3 anchor)
        {
            float r = boulder.extents.magnitude + 0.15f;
            foreach (Vector3 d in Around)
                if (Clear(anchor, boulder.center + d * r)) return true;
            return false;
        }

        // How many of a cluster's boulders can be reached, and how many it has. Call Sync() once before a batch.
        public static void Reach(NetworkEntity cluster, out int reachable, out int total)
        {
            bool old = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true; // a line that leaves the cave through the back of a rock surface is blocked too
            reachable = 0; total = 0;
            try
            {
                Vector3 anchor = Anchor(cluster);
                foreach (Bounds b in Boulders(cluster)) { total++; if (Reachable(b, anchor)) reachable++; }
            }
            finally { Physics.queriesHitBackfaces = old; }
        }

        public static int InsideRockRaysAt(Vector3 c, Transform ignore)
        {
            bool old = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            int inside = 0;
            try
            {
                foreach (Vector3 d in Axes)
                {
                    RaycastHit h;
                    if (Physics.Raycast(c, d, out h, 30f, World, QueryTriggerInteraction.Ignore) && Vector3.Dot(h.normal, d) > 0f) inside++;
                }
            }
            finally { Physics.queriesHitBackfaces = old; }
            return inside;
        }

        public static string Detail(NetworkEntity cluster)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Vector3 a = Anchor(cluster);
            bool old = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                sb.Append("anchor " + a.ToString("F2") + " (rays from it into rock-from-behind: " + InsideRockRaysAt(a, null) + "/6), cluster up " + cluster.transform.up.ToString("F2"));
                int i = 0;
                foreach (Bounds b in Boulders(cluster))
                {
                    float r = b.extents.magnitude + 0.15f;
                    int clear = 0;
                    foreach (Vector3 d in Around) if (Clear(a, b.center + d * r)) clear++;
                    RaycastHit h; string blockedBy = "";
                    Vector3 dir = b.center - a;
                    if (Physics.Raycast(a, dir.normalized, out h, dir.magnitude, World, QueryTriggerInteraction.Ignore))
                        blockedBy = " (line to centre hits " + h.collider.name + "/" + LayerMask.LayerToName(h.collider.gameObject.layer) + " at " + h.distance.ToString("0.00") + " m, " + (Vector3.Dot(h.normal, dir) > 0 ? "from behind" : "front") + ")";
                    sb.Append(" || boulder " + (i++) + " centre " + b.center.ToString("F2") + " size " + b.size.ToString("F2") + " clear sample points " + clear + "/14, centre inside-rock rays " + InsideRockRaysAt(b.center, null) + "/6" + blockedBy);
                }
            }
            finally { Physics.queriesHitBackfaces = old; }
            return sb.ToString();
        }

        public static int InsideRockRays(NetworkEntity e)
        {
            Vector3 c = Centre(e);
            bool old = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            int inside = 0;
            try
            {
                foreach (Vector3 d in Axes)
                {
                    RaycastHit[] hits = Physics.RaycastAll(c, d, 6f, ~0, QueryTriggerInteraction.Ignore);
                    float best = float.MaxValue; RaycastHit first = default(RaycastHit); bool any = false;
                    foreach (RaycastHit h in hits)
                    {
                        if (h.collider.transform.IsChildOf(e.transform)) continue;
                        if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;
                        if (h.distance < best) { best = h.distance; first = h; any = true; }
                    }
                    if (any && Vector3.Dot(first.normal, d) > 0f) inside++;
                }
            }
            finally { Physics.queriesHitBackfaces = old; }
            return inside;
        }

        public static bool IsByRays(NetworkEntity e) { return e != null && InsideRockRays(e) >= Main.Settings.BuriedRays; }

        public static bool IsByReach(NetworkEntity e)
        {
            if (e == null) return false;
            int ok, total;
            Reach(e, out ok, out total);
            return total > 0 && ok < total;
        }

        // Buried: a boulder can't be reached at all. (Boulders that are only partly hidden are nudged out by Unbury.)
        public static bool Is(NetworkEntity e)
        {
            if (Main.Settings.BuriedCheck == "rays") return IsByRays(e);
            return IsByReach(e);
        }

        // Directions to the top and sides of a boulder. The underside rests on the floor, so it is left out.
        public static readonly Vector3[] Faces = BuildFaces();
        static Vector3[] BuildFaces()
        {
            List<Vector3> d = new List<Vector3>();
            for (int x = -1; x <= 1; x++) for (int y = 0; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (x != 0 || y != 0 || z != 0) d.Add(new Vector3(x, y, z).normalized);
            return d.ToArray();
        }

        // On the surface the world's terrain heightmap can lie above a carved floor (the quarry): players walk on the
        // mesh floor underneath and never meet the heightmap there. True when that is the case at this spot.
        public static bool FakeTerrain(Vector3 at)
        {
            bool sawTerrain = false;
            foreach (RaycastHit h in Physics.RaycastAll(at + Vector3.up * 40f, Vector3.down, 60f, World, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (h.collider is TerrainCollider) sawTerrain = true;
                else if (sawTerrain) return true;
            }
            return false;
        }

        // Clear line between two points, optionally looking through the terrain heightmap.
        public static bool ClearSight(Vector3 from, Vector3 to, bool ignoreTerrain)
        {
            if (!ignoreTerrain) return Clear(from, to);
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            foreach (RaycastHit h in Physics.RaycastAll(from, d / len, len, World, QueryTriggerInteraction.Ignore))
                if (!(h.collider is TerrainCollider)) return false;
            return true;
        }

        static bool Floor(Vector3 above, float maxDrop, bool ignoreTerrain, out Vector3 floor)
        {
            floor = Vector3.zero;
            float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(above, Vector3.down, maxDrop, World, QueryTriggerInteraction.Ignore))
                if (!(ignoreTerrain && h.collider is TerrainCollider) && h.distance < best) { best = h.distance; floor = h.point; }
            return best < float.MaxValue;
        }

        // Open air: few short rays from the point start inside rock.
        static bool OpenAir(Vector3 p) { return InsideRockRaysAt(p, null) <= 1; }

        // Spots in open air around a cluster where a player's eyes could be: a ring around it, 1 m and 1.7 m above the
        // floor, plus the anchor when it is in the open.
        public static List<Vector3> Observers(NetworkEntity cluster, bool ignoreTerrain)
        {
            Bounds all = new Bounds(Centre(cluster), Vector3.zero);
            foreach (Bounds b in Boulders(cluster)) all.Encapsulate(b);
            return ObserversAround(Anchor(cluster), all, ignoreTerrain);
        }

        public static List<Vector3> ObserversAround(Vector3 a, Bounds all, bool ignoreTerrain)
        {
            float ring = new Vector2(all.extents.x, all.extents.z).magnitude + 0.8f;
            List<Vector3> list = new List<Vector3>();
            bool old = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                if (OpenAir(a)) list.Add(a);
                foreach (float r in new[] { ring + 0.6f })
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 at = new Vector3(all.center.x, all.max.y + 1.5f, all.center.z) + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * r;
                        Vector3 floor;
                        if (!Floor(at, 6f, ignoreTerrain, out floor)) continue;
                        foreach (float h in new[] { 1.0f, 1.7f })
                        {
                            Vector3 p = floor + Vector3.up * h;
                            if (p.y < at.y + 0.5f && OpenAir(p)) list.Add(p);
                        }
                    }
            }
            finally { Physics.queriesHitBackfaces = old; }
            return list;
        }

        // A spot on the boulder's own surface, found by casting at its colliders from outside along -d.
        public static bool SurfacePoint(Collider[] own, Bounds b, Vector3 d, out Vector3 p)
        {
            float far = b.extents.magnitude + 1f;
            Ray ray = new Ray(b.center + d * far, -d);
            float best = float.MaxValue; p = Vector3.zero;
            foreach (Collider c in own)
            {
                RaycastHit h;
                if (c.Raycast(ray, out h, far * 2f) && h.distance < best) { best = h.distance; p = h.point; }
            }
            return best < float.MaxValue;
        }

        // How much of a cluster's worst boulder is hidden in rock or ground: the share of points on its top and sides
        // that no open-air spot around the cluster can see. `view` is the spot that sees the most of the cluster.
        public sealed class SunkInfo
        {
            public double Share;          // worst boulder's hidden share
            public uint WorstId;          // that boulder's entity id
            public Vector3 WorstCentre;
            public Vector3 Stand1, Stand2; // floor spots to stand on to look at it, from two sides
            public int Boulders, Over;     // boulders measured, and how many are over max_sunk
        }

        public static double SunkShare(NetworkEntity cluster, out Vector3 view)
        {
            SunkInfo i = Sunk(cluster);
            view = i.Stand1;
            return i.Share;
        }

        public static SunkInfo Sunk(NetworkEntity cluster)
        {
            SunkInfo info = new SunkInfo { WorstCentre = Centre(cluster), Stand1 = Anchor(cluster), Stand2 = Anchor(cluster) };
            bool fake = FakeTerrain(Centre(cluster));
            List<Vector3> obs = Observers(cluster, fake);
            if (obs.Count == 0) { info.Share = 1.0; return info; }
            int[] worstSeenBy = null;
            double worst = -1;
            bool old = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                foreach (NetworkEntity child in cluster.GetComponentsInChildren<NetworkEntity>())
                {
                    if (child == cluster) continue;
                    List<Vector3> skin = Unbury.SkinPoints(child);
                    if (skin.Count == 0) continue;
                    Bounds b = new Bounds(skin[0], Vector3.zero); foreach (Vector3 q in skin) b.Encapsulate(q);
                    int total = 0, hidden = 0;
                    int[] seenBy = new int[obs.Count];
                    foreach (Vector3 p in skin)
                    {
                        total++;
                        bool seen = false;
                        for (int i = 0; i < obs.Count; i++) if (ClearSight(obs[i], p, fake)) { seenBy[i]++; seen = true; }
                        if (!seen) hidden++;
                    }
                    double share = total > 0 ? (double)hidden / total : 0;
                    if (total > 0) { info.Boulders++; if (share * 100 > Main.Settings.MaxSunk) info.Over++; }
                    if (total > 0 && share > worst) { worst = share; worstSeenBy = seenBy; info.WorstId = child.Identifier; info.WorstCentre = b.center; }
                }
            }
            finally { Physics.queriesHitBackfaces = old; }
            info.Share = System.Math.Max(0, worst);
            if (worstSeenBy == null) return info;
            // Best spot for the worst boulder (most of it seen; nearest if none sees it), then the best one from a
            // different side.
            System.Func<int, double> score = i => worstSeenBy[i] * 100.0 - System.Math.Abs(Vector3.Distance(obs[i], info.WorstCentre) - 2.5f) * 10.0;
            int b1 = Enumerable.Range(0, obs.Count).OrderByDescending(score).First();
            Vector3 side1 = Flat(obs[b1] - info.WorstCentre);
            int b2 = Enumerable.Range(0, obs.Count).Where(i => Vector3.Angle(side1, Flat(obs[i] - info.WorstCentre)) >= 90f).OrderByDescending(score).DefaultIfEmpty(b1).First();
            info.Stand1 = StandUnder(obs[b1], fake);
            info.Stand2 = StandUnder(obs[b2], fake);
            return info;
        }

        static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0f, v.z); }

        // The floor under an eye spot, a few centimetres up, for feet.
        static Vector3 StandUnder(Vector3 eye, bool ignoreTerrain)
        {
            Vector3 f;
            return Floor(eye + Vector3.up * 0.2f, 3f, ignoreTerrain, out f) ? f + Vector3.up * 0.05f : eye;
        }

        // A spot for a player to stand and look at the cluster: open floor about 1.6 m from the anchor.
        public static Vector3 ViewPoint(NetworkEntity cluster)
        {
            Vector3 a = Anchor(cluster);
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                RaycastHit floor;
                if (Clear(a, a + d * 2f) && Physics.Raycast(a + d * 1.6f + Vector3.up * 0.5f, Vector3.down, out floor, 3f, World, QueryTriggerInteraction.Ignore))
                    return floor.point + Vector3.up * 0.1f;
            }
            return a;
        }
    }
}
