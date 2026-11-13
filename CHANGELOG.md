# Changelog

## 1.1.3 (12 Nov 2026)

- **Mined crystals respawn.** In the normal game, mining crystals never come back once a level has been loaded again (after a restart, or after nobody's been down there): the game only works out where crystals can grow the first time a level is created. The mod now has it work those spots out again each time a level loads, the same spots as at first, so crystals respawn on the same 10-minute timer as ore. Nothing else the game only makes once (like the teleport totems) is made again. New setting `respawn_crystals` (`false` = the normal game).
- Crystals get the same treatment as ore nodes: the buried check, the sunk fix, bad spawn spots, and never moving next to a player. `refill`, `reset`, `destroy` (`crystal` or `all`), `inspect` and `sunk` include them, and `census` shows a crystal line.
- **Fix:** ore could appear right in front of players. When the mod moved a buried node, it only checked for players at the spot the node was leaving, not the spot it was going to, and players see a move straight away. A moved node now never lands within 30 metres of a player (or `player_radius`, if that's bigger).
- If every free spot is near a player, the buried node stays where it is and the mod tries again 30 seconds later, instead of removing it.
- **Much less lag from the boulder fixes.** Every boulder affects the game's pathfinding, and the game rebuilds its pathfinding for the whole world (about 70 ms, which freezes the server) each frame boulders move. The fixes used to be spread over many frames, so one check on a level could cause 26 of those freezes. The mod now works the fixes out a bit at a time but makes all of a level's boulder changes in one go, and moves buried nodes in batches instead of 2 a second. On a test level, a refill plus a full check went from 38 freezes to 5.
- The respawn timer is unchanged: ore respawns at the game's normal rate. `orefix refill` still respawns an area by hand.
- The bad-spawn-point check only ever looks at the groups the mod looks after (ore, sandstone and mining crystals), so wyrms and everything else spawn exactly like the normal game.

## 1.1.2 (12 Nov 2026)

- **Sunk boulders get pulled out of the rock.** A node is 3 to 5 boulders, and on uneven ground some of them end up half inside the floor, a wall or a cave rock. Any boulder more than 20% hidden is now nudged out the smallest distance that frees it. New nodes are fixed as they spawn, and older ones by the regular check. A boulder that can't be freed is removed. On a test level, sunk boulders went from 180 of 322 to none. The limit is the new `max_sunk` setting.
- **Fix:** old buried nodes on busy servers were basically never moved, because the 6-hour untouched timer restarted every time an area unloaded. It now keeps counting.
- **Fix:** a new buried node with a player close by was dropped from the move queue. It now waits and tries again 30 seconds later.
- `orefix reset surface` and `reset all` load the quarry themselves. Nobody has to be there.
- New `orefix sunk` command shows how sunk each node's boulders are.
- The boulder fixing is spread over several frames, so it doesn't lag the server. `orefix sweep` now runs in the background too and writes its result to the server log.

## 1.1.1 (11 Nov 2026)

- **New: `orefix reset`.** Clears and respawns the ore on every mine level that exists, with the right mix, without wiping the mine. Good for servers that went bad before the mod was installed. Use `reset all`, `reset mines`, `reset surface`, a single level number, or `reset stop`. It takes about 15 seconds per level.
- **Fix:** the ore-mix fix could switch itself off until the next restart if something asked for the ore table while the server was still starting. It now waits until the game has loaded the table.
- `orefix destroy` takes `all` as the ore name.
- The README explains every console command, with examples.

## 1.1.0 (11 Nov 2026)

- Works on the surface too: the plot outside the mine entrance and the quarry above it. The surface only gets coal and copper, at the game's own 2:1 odds.
- Much less lag on busy servers. The sweep and new-node checks are spread out over time, and refills are cheaper.
- New commands: `orefix groups` and `orefix perf`. Commands that take an area accept a level number or `surface`.
- `orefix reload` applies a shorter sweep interval straight away.

## 1.0.0 (11 Nov 2026)

- First release. Keeps each mine level's ore mix where the game intended, stops new ore spawning inside the cave walls, and moves old buried nodes somewhere players can reach.
