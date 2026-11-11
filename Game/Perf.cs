using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CheetosOreSpawnFix
{
    // How long the mod's work takes on the server's main thread. `orefix perf` shows it. "worst frame" is the most time
    // the mod spent in any single frame, which is what players would feel as a hitch.
    public static class Perf
    {
        sealed class Stat { public int Calls; public double TotalMs, MaxMs; }
        static readonly Dictionary<string, Stat> _stats = new Dictionary<string, Stat>();
        static int _frame = -1;
        static double _frameMs, _worstFrameMs;

        public static Stopwatch Start() { return Stopwatch.StartNew(); }

        public static void Stop(string name, Stopwatch sw)
        {
            double ms = sw.Elapsed.TotalMilliseconds;
            Stat s;
            if (!_stats.TryGetValue(name, out s)) _stats[name] = s = new Stat();
            s.Calls++; s.TotalMs += ms;
            if (ms > s.MaxMs) s.MaxMs = ms;

            int frame = UnityEngine.Time.frameCount;
            if (frame != _frame) { _frame = frame; _frameMs = 0; }
            _frameMs += ms;
            if (_frameMs > _worstFrameMs) _worstFrameMs = _frameMs;
        }

        public static string Report(bool reset)
        {
            if (_stats.Count == 0) return "no timings yet";
            string r = "worst frame " + _worstFrameMs.ToString("0.00") + " ms | " + string.Join(" | ", _stats.OrderBy(kv => kv.Key).Select(kv =>
                kv.Key + ": " + kv.Value.Calls + " calls, avg " + (kv.Value.TotalMs / kv.Value.Calls).ToString("0.000") +
                " ms, max " + kv.Value.MaxMs.ToString("0.00") + " ms").ToArray());
            if (reset) { _stats.Clear(); _worstFrameMs = 0; }
            return r;
        }
    }
}
