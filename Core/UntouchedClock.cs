using System.Collections.Generic;

namespace CheetosOreSpawnFix
{
    // How long each node has gone untouched. First sight or any touch restarts its clock.
    public sealed class UntouchedClock
    {
        readonly Dictionary<uint, double> _since = new Dictionary<uint, double>();

        public void Seen(uint id, bool touched, double nowSeconds)
        {
            if (touched || !_since.ContainsKey(id)) _since[id] = nowSeconds;
        }

        public bool Due(uint id, double nowSeconds, double hours)
        {
            double since;
            return _since.TryGetValue(id, out since) && nowSeconds - since >= hours * 3600.0;
        }

        public void Forget(uint id) { _since.Remove(id); }

        public IEnumerable<uint> Ids { get { return new List<uint>(_since.Keys); } }
    }
}
