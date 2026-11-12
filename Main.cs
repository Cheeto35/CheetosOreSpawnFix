using System;
using System.Collections.Generic;
using System.IO;
using Alta.Console;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(CheetosOreSpawnFix.Main), "CheetosOreSpawnFix", "1.1.3", "Cheeto35")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace CheetosOreSpawnFix
{
    public class Main : MelonMod
    {
        public static Settings Settings = new Settings();
        public static bool IsServer;
        static MelonLogger.Instance _log;

        public static string Dir { get { return Path.Combine(Path.Combine(Path.GetDirectoryName(Application.dataPath), "UserData"), "CheetosOreSpawnFix"); } }
        static string ConfigPath { get { return Path.Combine(Dir, "config.txt"); } }

        public static void Log(string m) { if (_log != null) _log.Msg(m); }

        public override void OnInitializeMelon()
        {
            _log = LoggerInstance;
            IsServer = ApplicationManager.IsHeadless || CommandLineArguments.Contains(CommandLineArguments.StartServerArgument);
            if (!IsServer) { Log("client: idle"); return; }
            LoadSettings();
            CommandService.CommandCollection.Collect(typeof(Main).Assembly);
            Log("1.1.3 ready: enabled=" + Settings.Enabled + " keep_mix=" + Settings.KeepMix + " fix_buried=" + Settings.FixBuried);
        }

        public override void OnUpdate()
        {
            if (!IsServer) return;
            try { Sweep.Tick(); } catch (Exception e) { Log("sweep error: " + e); }
            try { Levels.KeepPinned(); } catch (Exception e) { Log("pin error: " + e); }
            try { Reset.Tick(); } catch (Exception e) { Log("reset error: " + e); Reset.Stop(); }
        }

        public static string LoadSettings()
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(ConfigPath))
                File.WriteAllText(ConfigPath, string.Join(Environment.NewLine, new[] {
                    "# Ore Spawn Fix settings. Delete this file to get the defaults back.",
                    "enabled = true          # false = the mod changes nothing",
                    "keep_mix = true         # respawned ore follows the game's odds for the level's depth",
                    "fix_buried = true       # refuse spawn points that bury nodes; move buried nodes",
                    "sweep_minutes = 10      # how often the buried-node sweep runs",
                    "untouched_hours = 6     # a buried node must sit untouched this long before it is moved",
                    "player_radius = 10      # never move a node with a player this close (metres)",
                    "max_sunk = 20           # a node is buried if more than this % of a boulder is hidden in rock or ground (100 = off)",
                    "buried_rays = 4         # of 6 rays from a node's centre, how many must start inside rock",
                    "" }));
            List<string> warnings = new List<string>();
            Settings = Settings.Parse(File.ReadAllText(ConfigPath), warnings);
            foreach (string w in warnings) Log("config: " + w);
            Sweep.Reschedule();
            return "loaded " + ConfigPath + (warnings.Count > 0 ? " with " + warnings.Count + " warning(s)" : "");
        }
    }
}
