using System.Collections.Generic;
using Alta.Networking;
using Alta.Populations;
using HarmonyLib;

namespace CheetosOreSpawnFix
{
    // Spawn points that buried a node, and buried nodes waiting to be moved. In memory: after a restart they are
    // learned again from new spawns and by the sweep.
    public static class BadPoints
    {
        // A cluster's boulders are laid out at random around its point, so a point near rock buries a node only some of
        // the time. A point is refused once it has buried two nodes.
        public const int Strikes = 2;
        static readonly Dictionary<SpawnPoint, int> _strikes = new Dictionary<SpawnPoint, int>();
        static readonly HashSet<SpawnPoint> _bad = new HashSet<SpawnPoint>();
        static readonly Queue<NetworkEntity> _move = new Queue<NetworkEntity>();
        static readonly HashSet<NetworkEntity> _queued = new HashSet<NetworkEntity>();

        public static void Mark(SpawnPoint p, string why)
        {
            if (p == null) return;
            int n; _strikes.TryGetValue(p, out n); _strikes[p] = ++n;
            if (n >= Strikes && _bad.Add(p)) Main.Log("bad spawn point at " + p.Position.ToString("F1") + " (" + n + " buried nodes): " + why);
        }

        public static bool Has(SpawnPoint p) { return p != null && _bad.Contains(p); }
        public static int Count { get { return _bad.Count; } }

        public static void QueueMove(NetworkEntity e) { if (e != null && _queued.Add(e)) _move.Enqueue(e); }
        public static bool IsQueued(NetworkEntity e) { return e != null && _queued.Contains(e); }
        public static int Queued { get { return _move.Count; } }

        public static bool TryDequeueMove(out NetworkEntity e)
        {
            while (_move.Count > 0)
            {
                e = _move.Dequeue();
                _queued.Remove(e);
                if (e != null) return true;
            }
            e = null;
            return false;
        }

        // The spawn point a population member was placed on, if it has one.
        public static SpawnPoint PointOf(PopulationSample sample, NetworkEntity e)
        {
            PopulationItem item;
            if (sample == null || e == null || !sample.SpawnedEntities.TryGetValue(e, out item) || item == null) return null;
            return item.SpawnPoint;
        }
    }

    // The game picks a spawn point by trying 3 random free points and giving up if all 3 are refused. With bad points
    // refused that gives up far too often, so pick uniformly among the free points that are not bad instead.
    [HarmonyPatch(typeof(PointsPopulationValue), "GetRandom")]
    static class PickPointPatch
    {
        static bool Prefix(PointsPopulationValue __instance, PopulationDefinition definition, ref SpawnPoint __result)
        {
            if (!Main.IsServer || !Main.Settings.Enabled || !Main.Settings.FixBuried || BadPoints.Count == 0) return true;
            // Only the mine node groups this mod manages; every other population (wyrms, crystals, ...) stays vanilla.
            if (definition == null || (definition.name != Levels.BoulderGroup && definition.name != Levels.SandstoneGroup)) return true;
            List<SpawnPoint> ok = new List<SpawnPoint>();
            foreach (SpawnPoint p in __instance.Points)
                if (p != null && p.IsFree && !BadPoints.Has(p) && definition.IsPositionValid(p)) ok.Add(p);
            __result = ok.Count > 0 ? ok[UnityEngine.Random.Range(0, ok.Count)] : null;
            return false;
        }
    }

    // Refuse spawn points that are known to bury nodes. The game then tries another free point.
    [HarmonyPatch(typeof(PopulationDefinition), "IsPositionValid")]
    static class BadPointPatch
    {
        static void Postfix(SpawnPoint point, ref bool __result)
        {
            if (__result && Main.IsServer && Main.Settings.Enabled && Main.Settings.FixBuried && BadPoints.Has(point)) __result = false;
        }
    }
}
