# Client routes and what the server does with them

Source: decompilation of `Assembly-CSharp.dll` of the 2.4.9 client (`Server/*.cs` and `ServerConfig.cs`).
The client references about **131 routes**. All of them are declared by the server; roughly **90 are really implemented**
and the other **42** (`LeftoverApi.cs`) answer with an empty but valid reply (`{}`, `{"data":[]}` or `{"count":0}`)
because no screen of the game reaches them. Any unknown route answers `{}` with `PLAY_STATUS: OK` and logs
`[HTTP] not implemented`.

## Implemented

Player and login, friends, levels (save, publish, search, data, screenshots, ratings, comments, creator rewards,
export/import as zip), high scores and ghosts, path and progression, friend challenges, versus, music
(`preload/checkFile`), ads config, timed events.

## Game features that depended on the server

| Feature | Status |
|---|---|
| Daily Gems (daily diamond challenge) | Serves a public `StarCollect` level, different each day (UTC). Empty if no such level exists. |
| Fresh and Free | `/v1/minigame/oneFresh`: a random public level. |
| Versus / friend challenge | Matchmaking, rounds, winner, ghosts, stats. The window's "Challenge a player" tool creates challenges. |
| Ghosts | `/v2/ghost/next` returns up to 3 distinct ghosts per level (a player's best run plus two older ones). |
| Adventure map | `/v1/path/db/find` serves the rebuilt, repaired graph (see `UnlockBuilder.cs`, `MapContent.cs`). |

## Routes declared with an empty reply, and why

Checked by analysing every caller in the client: none of them is reachable from the 2.4.9 interface.

- **Tournaments** (`tournament/claim|create|unlock|score/next`, `v2/tournament/score/find|send`): only called by methods
  that have no caller themselves.
- **Social networks** (`player/check|solve|remove|follow/social`, `follow|check|join/gameCenter`, `join/facebook`):
  need Facebook or Game Center, impossible offline.
- **Purchases** (`iap/config`, `iap/purchase`, `analytics/trackPurchase`, `player/tester/*`, `player/rent`): the in-app
  purchase flow has no caller; `trackPurchase` only follows a real purchase.
- **Dead code** (no caller): `ghosts` (v1), `path/get`, `customisation/add`, `player/data/find|setNumber|setString`,
  `player/stats/change|set`, `player/join/ninja`, `push/token/*`, `notification/*`, `planet/current/find`,
  `research/*`, `minigame/level/update`, `minigame/like/save`, `v2/path/db/update` (internal editor), the other
  `v1/ghost/*` variants.

## Finding this list again

The analysis scripts were not kept. To redo it, decompile the APK with `ilspycmd` and search for the strings
`"/v<N>/..."` in `ServerConfig.cs` and the classes of `Server/`.
