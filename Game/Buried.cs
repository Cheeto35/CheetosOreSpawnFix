using System.Collections.Generic;
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
        static bool Clear(Vector3 from, Vector3 to)
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

        public static bool Is(NetworkEntity e) { return Main.Settings.BuriedCheck == "rays" ? IsByRays(e) : IsByReach(e); }

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
