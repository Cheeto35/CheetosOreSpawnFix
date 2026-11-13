# CheetosOreSpawnFix

If you've run an A Township Tale server for a while, you've probably noticed the mines going bad. After a week or so of mining, a level fills up with ore nobody wants, and the good stuff is gone or stuck inside the rock where no one can reach it. The quarry above the mine goes the same way. The usual fix is to wipe the mine.

This mod fixes it so you don't have to wipe.

## Why mines go bad

The game decides a node's ore type once, when the node spawns. After that it never changes. Players mine the copper and gold and leave the coal, so the coal never leaves. Only the mined spots respawn. Over time the level fills up with the ore people skip.

On top of that, the game has an already existing "is this spot blocked?" check and its switched off for ore. Some nodes spawn partly or fully inside the cave walls. Nobody can mine those, so they sit there forever and take up a spawn spot.

It's worse than it looks, because a node is really a little pile of 3 to 5 boulders that the game lays out the same way every time, whatever the ground is like. On a bumpy cave floor or a slope in the quarry, some of those boulders end up half inside the floor, a wall, or one of the big cave rocks, even when the rest of the node looks fine. On a typical mine level more than half the boulders were at least partly sunk.

## What the mod does

- **Keeps each level's ore mix where the game intended.** When a node respawns, the mod picks whichever ore that level is shortest on, based on the game's own odds for that depth. Coal can't take over anymore.
- **Does the same on the surface.** The little plot outside the mine entrance and the big quarry above it (the one you cross to get to the climbing tower) keep the game's surface mix: about two coal for every copper.
- **Pulls sunk boulders out of the rock.** Any boulder that's more than 20% hidden in the floor, a wall or a rock gets nudged the smallest distance (up or to the side) that frees it. New nodes are fixed as they spawn, before any player sees them. Older nodes get fixed by the regular check, and players see the change the next time they load that area. A boulder that can't be freed at all is removed.
- **Moves nodes that can't be reached at all.** If a whole node lands inside the rock, it gets moved. If a spot keeps doing that, it's not used again.
- **Cleans up old buried nodes.** Every 10 minutes the mod checks the loaded areas. Unreachable nodes that have sat untouched for 6 hours get moved somewhere minable. The 6 hours keeps counting while an area is unloaded, so this works on busy servers too.

It never moves a node with a player within 10 metres, and never moves one to a spot within 30 metres of a player, so ore doesn't pop up in front of anyone (it tries again a little later), it only works on areas that are loaded, and it never spawns an ore the game wouldn't put there. The surface only ever gets coal and copper, like in the normal game.

## Install

You need an A Township Tale server on the final game build (`main-1.7.2.1.42203`) with MelonLoader installed. The Modding Tavern server launcher sets that up for you. Players don't need anything.

1. Stop the server.
2. Drop `CheetosOreSpawnFix.dll` (it's in this folder, ready to use) into the server's `Mods` folder.
3. Start the server. You should see `[CheetosOreSpawnFix] 1.1.3 ready` in the log.

Levels that had already gone bad before you installed it will fix themselves as players mine them. If you'd rather start clean right away without wiping the mine, run `orefix reset all` in the server console (see [Resetting the ore on an existing server](#resetting-the-ore-on-an-existing-server)).

To uninstall, stop the server and delete the DLL. The game goes back to normal. Boulders the mod nudged out of the rock stay where they are, which is harmless.

## Settings

The first time it runs, the mod creates `UserData\CheetosOreSpawnFix\config.txt` next to the game. Edit it, then run `orefix reload` in the server console, or restart.

| setting | default | what it does |
|---|---|---|
| `enabled` | `true` | turn the whole mod off without uninstalling it |
| `keep_mix` | `true` | keep the ore mix on target in the mines and on the surface |
| `fix_buried` | `true` | fix sunk boulders, move buried nodes, and avoid spots that bury them |
| `max_sunk` | `20` | how much of a boulder (in %) may be hidden in rock or ground before it's nudged out; `100` turns the nudging off |
| `sweep_minutes` | `10` | how often to look for buried nodes |
| `untouched_hours` | `6` | how long a node nobody can reach has to sit untouched before it's moved |
| `player_radius` | `10` | never move a node with a player this close (metres). A moved node also never lands within 30 metres of a player, or this distance if it is bigger |
| `buried_check` | `reach` | how "buried" is decided; leave this alone unless you know you need `rays` (the older, rougher check) |

## Console commands

Type these into the server's admin console. Every command starts with `orefix`, then the command name, then any extra words it needs, all separated by spaces:

```
orefix census 3
```

Words in `<angle brackets>` below are required. Words in `[square brackets]` are optional and can be left off.

### Areas

Many commands work on one **area** at a time. An area is either:

- **a mine level number**: `0` is the first level under the entrance, `1` is the next one down, and so on, or
- **`surface`**: the little plot outside the mine entrance plus the big quarry above it. `quarry` means the same thing.

The game only keeps a mine level loaded while a player is on it, and the mod can only see an area that's loaded. If a command says a level "is not loaded", either go down there, or load it with `orefix load` (see [Loading levels with nobody there](#loading-levels-with-nobody-there)). The surface is the same: someone has to be near the quarry.

### Everyday commands

#### `orefix census <area>`

Counts the ore in an area and compares it with what the game is meant to spawn at that depth. Use it to check that a level looks healthy.

```
orefix census 12
```
```
level 12: 49 ore clusters | Coal Boulder: 10 (20%) target 21% | Copper Boulder: 20 (41%) target 42% | Gold Boulder: 17 (35%) target 33% | Iron Boulder: 2 (4%) target 3% | buried: 0 of 50 clusters (0 queued to move), bad points: 2
```

- **ore clusters**: how many ore nodes are on the level right now.
- **each ore**: how many of that ore there are, their share, and the share the game aims for. With the mod running, these stay close.
- **buried**: nodes that are stuck in the rock where no one can mine them. "Queued to move" ones are about to be moved.
- **bad points**: spawn spots the mod has stopped using because they kept burying nodes.

#### `orefix groups`

Lists every ore and sandstone spawn group the mod is looking after right now: one per loaded mine level, plus the surface ones. Each line shows how full the group is (for example `48/50`). It's a quick way to see which areas are loaded.

```
orefix groups
```

#### `orefix sweep [ignoreAge]`

Starts the regular check right now, instead of waiting for it. It nudges sunk boulders out of the rock and moves nodes nobody can reach. It works out the fixes in the background a few milliseconds at a time and then makes all the changes at once, so the game only has to rebuild its pathfinding once or twice instead of freezing over and over, and writes its result to the server log when it's done (a few seconds per loaded level). By default it also moves unreachable nodes that haven't sat untouched for 6 hours yet. Add `false` to keep the 6-hour rule.

```
orefix sweep
orefix sweep false
```

#### `orefix save`

Saves every loaded area to disk straight away. Run it before you stop the server so nothing since the last autosave is lost.

```
orefix save
```

### Resetting the ore on an existing server

#### `orefix reset <mines|surface|all|level number|stop>`

If you're adding the mod to a server whose mines have already gone bad, this replaces the old ore with a fresh, correct mix without wiping anything else.

```
orefix reset all        every mine level that exists, then the surface
orefix reset mines      every mine level that exists, but not the surface
orefix reset surface    just the plot and the quarry
orefix reset 5          just level 5
orefix reset stop       cancel a reset that's running
```

What it does, one level at a time from the top down:

1. Loads the level if nobody is on it. It only visits levels that already exist, so it never creates new ones.
2. Removes every ore node on the level, except any within 10 metres of a player, so nobody loses the rock they're hitting.
3. Spawns the level full again with the right mix.
4. Checks the new nodes. Sunk boulders are nudged out of the rock. Nodes that can't be reached at all are moved, or removed if there's nowhere better, and that spot isn't used again. Then it fills any gaps once more.
5. Saves the level. If the reset loaded it, it lets it unload again.

**How long it takes:** about 15 to 25 seconds per level. Big levels with lots of sunk boulders take the longest. Rough times for `orefix reset all`:

| mine depth | time |
|---|---|
| 10 levels | about 3 to 4 minutes |
| 25 levels | about 8 minutes |
| 50 levels | about 15 minutes |
| 100 levels | about 30 to 40 minutes |

A single level (`orefix reset 5`) or the surface takes under half a minute. Players can keep playing while it runs.

It runs in the background, so the server keeps going while it works. Progress goes to the **server log**, one line per level:

```
reset started: 1 mine level(s) (12 to 12). Progress goes to the server log, one line per level
reset level 12: sweep moved 1, skipped (player near) 0, skipped (young) 0, replaced 2 (of 3 buried); sunk boulders nudged out 102, removed 5
reset level 12: cleared 50, now 47/50, 0 buried | Coal Boulder: 9 (19%) target 21% | Copper Boulder: 21 (45%) target 42% | Gold Boulder: 15 (32%) target 33% | Iron Boulder: 2 (4%) target 3%
reset done: 1 area(s) reset, 0 skipped, 50 clusters cleared, 47 on the levels now
```

Good to know:

- **Back up your world first.** The old ore is gone for good once it's cleared.
- It only touches ore. Sandstone and everything players have built or dropped are left alone.
- Levels with no ore (like the trial levels and level 100) are skipped.
- A level can end up a few nodes short of full, for example `27/33`. That means some of its spawn spots are inside the rock, so the mod stopped using them. That's expected, and the normal respawn fills whatever gaps are left over time.
- Right after the server starts, wait a minute before running it. If the game hasn't finished loading the mines yet, `reset` tells you to try again.
- Nobody needs to be near the quarry. If it isn't loaded, the reset loads it, resets it, and lets it unload again:
  ```
  reset surface: loading 20 area(s) around the quarry
  reset surface: cleared 37, now 37/37, 0 buried | Coal Boulder: 25 (68%) target 67% | Copper Boulder: 12 (32%) target 33%
  ```
- The mod must be on, with `keep_mix` on, or the reset would just respawn the old vanilla mix. It refuses to start otherwise.

### Settings without a restart

These changes last until you run `orefix reload` or restart the server. To change a setting for good, edit `config.txt` (see [Settings](#settings)).

#### `orefix enable <true|false>`

Turns the whole mod on or off. Off means the game spawns ore exactly like vanilla.

```
orefix enable false
orefix enable true
```

#### `orefix set <keep_mix|fix_buried> <true|false>`

Turns one part of the mod on or off.

```
orefix set keep_mix false     respawned ore uses the game's normal random pick again
orefix set fix_buried false   stop checking for and moving buried nodes
```

#### `orefix reload`

Reads `config.txt` again. Use it after editing the file, or to undo any `enable` or `set` changes you made from the console.

```
orefix reload
```

### Admin and testing tools

You won't need these for normal running, but they're handy for checking things or setting up a test.

#### `orefix refill <area>`

Spawns an area full right now, instead of waiting for the game's 10-minute respawn timer. It only fills empty spots and doesn't remove anything.

```
orefix refill 3
```

#### `orefix destroy <area> <ore> [count]`

Removes ore nodes as if they'd been mined. **This really does remove ore**, so be careful on a live server.

- `<ore>` is part of an ore name: `coal`, `copper`, `iron` and so on. `iron` matches every kind of iron. Use `all` for every ore.
- `[count]` is how many to remove. Leave it off to remove all that match.

```
orefix destroy 3 coal 10     remove 10 coal nodes from level 3
orefix destroy 3 all         remove every ore node from level 3
orefix destroy surface coal  remove all the coal on the surface
```

To clear and refill a single area by hand, run `destroy <area> all` and then `refill <area>`. `orefix reset` does the same thing for you, more safely.

#### `orefix perf [reset]`

Shows how much server time the mod's work is taking, so you can check it isn't slowing anything down. Add `true` to reset the numbers to zero.

```
orefix perf
orefix perf true
```

#### `orefix sunk <area> [how many]`

Shows how much of each node's worst boulder is hidden in rock or ground, worst first, and how many boulders are over the `max_sunk` limit. Use it to check the boulder fix is doing its job. Leave off the number to list 10 nodes, or give `0` for just the summary.

```
orefix sunk 5
orefix sunk surface 0
```
```
level 5: 80 clusters; worst boulder hidden: 0-20% 79, 20-40% 0, 40-60% 0, 60-80% 0, 80-100% 1
  boulders: 270, over max_sunk: 0; nodes with 0/1/2/3/4/5+ boulders over: 80/0/0/0/0/0
```

#### `orefix inspect <area>` and `orefix inspect1 <area> <id>`

Lists every ore node in an area, with its ID, ore type, position, whether it counts as buried (`RAYS-BURIED` or `REACH-BURIED`), and a spot you can stand at to look at it. `inspect1` gives the full details for one node, using an ID from `inspect`.

```
orefix inspect 3
orefix inspect1 3 48211
```

### Loading levels with nobody there

#### `orefix load <level>`

Loads a mine level on the server without a player on it, and keeps it loaded until you unload it. **If the level doesn't exist yet, this creates it**, and levels are built from the one above, so go down in order (`load 0`, `load 1`, `load 2`, ...). The game stops at level 100. The result goes to the server log; wait for `load: level 5 loaded` before using the level.

```
orefix load 5
```

#### `orefix unload <level>`

Saves a level you loaded with `orefix load` and lets it unload again.

```
orefix unload 5
```

#### `orefix chunks <x> <z> <radius> [name or *]`

Lists the world areas ("chunks") within `radius` metres of a map position, and whether each one is loaded. Add a chunk's name to load that one, or `*` to load every surface chunk in range. It's mainly for testing the quarry with nobody near it.

```
orefix chunks -900 -250 150      list the areas around the quarry
orefix chunks -900 -250 150 *    load the quarry and the plot outside the mine
```

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
