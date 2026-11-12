using System.Collections.Generic;

namespace CheetosOreSpawnFix
{
    // How long each node has gone untouched, by a key that stays the same while its area unloads and loads again
    // (the node's position; entity ids change on every load). First sight or any touch restarts its clock.
    public sealed class UntouchedClock
    {
        readonly Dictionary<string, double> _since = new Dictionary<string, double>();
        readonly Dictionary<string, double> _lastSeen = new Dictionary<string, double>();

        public void Seen(string key, bool touched, double nowSeconds)
        {
            if (touched || !_since.ContainsKey(key)) _since[key] = nowSeconds;
            _lastSeen[key] = nowSeconds;
        }

        public bool Due(string key, double nowSeconds, double hours)
        {
            double since;
            return _since.TryGetValue(key, out since) && nowSeconds - since >= hours * 3600.0;
        }

        public void Forget(string key) { _since.Remove(key); _lastSeen.Remove(key); }

        // Drop nodes not seen for this long (mined, or in an area nobody has visited since).
        public void Prune(double nowSeconds, double maxAgeSeconds)
        {
            foreach (string k in new List<string>(_lastSeen.Keys))
                if (nowSeconds - _lastSeen[k] > maxAgeSeconds) Forget(k);
        }

        public int Count { get { return _since.Count; } }
    }
}
