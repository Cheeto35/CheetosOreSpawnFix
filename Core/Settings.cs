using System;
using System.Collections.Generic;
using System.Globalization;

namespace CheetosOreSpawnFix
{
    // UserData/CheetosOreSpawnFix/config.txt: "key = value" lines, "#" starts a comment. Bad values keep the default.
    public sealed class Settings
    {
        public bool Enabled = true, KeepMix = true, FixBuried = true, RespawnCrystals = true;
        public double SweepMinutes = 10, UntouchedHours = 6, PlayerRadius = 10;
        public int BuriedRays = 4;
        public double MaxSunk = 20; // percent of a boulder's top and sides that may be hidden in rock or ground
        public string BuriedCheck = "reach"; // "reach" (each boulder reachable from the open cave) or "rays" (old)

        public static Settings Parse(string text, List<string> warnings)
        {
            Settings s = new Settings();
            if (text == null) return s;
            string[] lines = text.Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) { warnings.Add("line " + (n + 1) + ": expected key = value"); continue; }
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "enabled": s.Enabled = Bool(val, s.Enabled, key, warnings); break;
                    case "keep_mix": s.KeepMix = Bool(val, s.KeepMix, key, warnings); break;
                    case "fix_buried": s.FixBuried = Bool(val, s.FixBuried, key, warnings); break;
                    case "respawn_crystals": s.RespawnCrystals = Bool(val, s.RespawnCrystals, key, warnings); break;
                    case "sweep_minutes": s.SweepMinutes = Num(val, s.SweepMinutes, 0.1, 1440, key, warnings); break;
                    case "untouched_hours": s.UntouchedHours = Num(val, s.UntouchedHours, 0, 1000, key, warnings); break;
                    case "player_radius": s.PlayerRadius = Num(val, s.PlayerRadius, 0, 200, key, warnings); break;
                    case "max_sunk": s.MaxSunk = Num(val, s.MaxSunk, 0, 100, key, warnings); break;
                    case "buried_rays": s.BuriedRays = (int)Num(val, s.BuriedRays, 1, 6, key, warnings); break;
                    case "buried_check":
                        if (val == "reach" || val == "rays") s.BuriedCheck = val;
                        else warnings.Add("buried_check: '" + val + "' must be reach or rays; keeping " + s.BuriedCheck);
                        break;
                    default: warnings.Add("unknown setting '" + key + "'"); break;
                }
            }
            return s;
        }

        static bool Bool(string v, bool fallback, string key, List<string> warnings)
        {
            switch (v.ToLowerInvariant())
            {
                case "true": case "1": case "on": case "yes": return true;
                case "false": case "0": case "off": case "no": return false;
            }
            warnings.Add(key + ": '" + v + "' is not true/false; keeping " + fallback);
            return fallback;
        }

        static double Num(string v, double fallback, double min, double max, string key, List<string> warnings)
        {
            double d;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && d >= min && d <= max) return d;
            warnings.Add(key + ": '" + v + "' must be a number from " + min + " to " + max + "; keeping " + fallback);
            return fallback;
        }
    }
}
