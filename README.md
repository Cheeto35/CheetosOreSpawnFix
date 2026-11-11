# CheetosOreSpawnFix

If you've run an A Township Tale server for a while, you've probably noticed the mines going bad. After a week or so of mining, a level fills up with ore nobody wants, and the good stuff is gone or stuck inside the rock where no one can reach it. The usual fix is to wipe the mine.

This mod fixes it so you don't have to wipe.

## Why mines go bad

The game decides a node's ore type once, when the node spawns. After that it never changes. Players mine the copper and gold and leave the coal, so the coal never leaves. Only the mined spots respawn. Over time the level fills up with the ore people skip.

On top of that, the game has an already existing "is this spot blocked?" check and its switched off for ore. Some nodes spawn partly or fully inside the cave walls. Nobody can mine those, so they sit there forever and take up a spawn spot.

## What the mod does

- **Keeps each level's ore mix where the game intended.** When a node respawns, the mod picks whichever ore that level is shortest on, based on the game's own odds for that depth. Coal can't take over anymore.
- **Stops nodes spawning in the walls.** If a new node lands inside rock, it gets moved. If a spot keeps doing that, it's not used again.
- **Cleans up old buried nodes.** Every 10 minutes the mod looks for nodes nobody can reach that have sat untouched for 6 hours, and moves them somewhere minable.

It never touches a node with a player within 10 metres, it only works on mine levels that are loaded, and it doesn't change which ores can appear at a given depth.

## Install

You need an A Township Tale server on the final game build (`main-1.7.2.1.42203`) with MelonLoader installed. The Modding Tavern server launcher sets that up for you. Players don't need anything.

1. Stop the server.
2. Drop `CheetosOreSpawnFix.dll` (it's in this folder, ready to use) into the server's `Mods` folder.
3. Start the server. You should see `[CheetosOreSpawnFix] 1.0.0 ready` in the log.

Levels that had already gone bad before you installed it will fix themselves as players mine them. If you want a clean start, wipe the mine once.

To uninstall, stop the server and delete the DLL. The mod doesn't save anything into your world, so the game simply goes back to normal.

## Settings

The first time it runs, the mod creates `UserData\CheetosOreSpawnFix\config.txt` next to the game. Edit it, then run `orefix reload` in the server console, or restart.

| setting | default | what it does |
|---|---|---|
| `enabled` | `true` | turn the whole mod off without uninstalling it |
| `keep_mix` | `true` | keep each level's ore mix on target |
| `fix_buried` | `true` | move buried nodes and avoid spots that bury them |
| `sweep_minutes` | `10` | how often to look for buried nodes |
| `untouched_hours` | `6` | how long a buried node has to sit untouched before it's moved |
| `player_radius` | `10` | never move a node with a player this close (metres) |
| `buried_check` | `reach` | how "buried" is decided; leave this alone unless you know you need `rays` (the older, rougher check) |

## Console commands

Run these from the server's admin console.

| command | what it does |
|---|---|
| `orefix census <level>` | the ore mix on a mine level next to its target, and how many nodes are buried |
| `orefix sweep` | look for buried nodes right now instead of waiting |
| `orefix enable <true\|false>` | turn the mod on or off without a restart |
| `orefix reload` | reread the config file |
| `orefix save` | save all loaded areas (handy before stopping the server) |

There are a few more commands for testing (`refill`, `destroy`, `load`, `unload`, `inspect`, `inspect1`, `set`). `destroy` really does remove ore, so be careful with it on a live server.

## Building it yourself

You only need this if you want to change the code. The DLL in this folder is already built.

You need Windows and the game installed (the build uses the game's own DLLs). The C# compiler comes with Windows.

```powershell
.\build.ps1                                       # game in C:\Games\Alta\A Township Tale
.\build.ps1 -GamePath "C:\Games\A Township Tale"  # or wherever yours is
```

That replaces `CheetosOreSpawnFix.dll` in this folder with your build.

## Licence

MIT. See [LICENSE](LICENSE).
