# Changelog

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
