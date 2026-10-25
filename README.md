# BBR 2.4.9 Offline Server

A self-hosted replacement for the backend of **Big Bang Racing 2.4.9** (Traplight, 2015–2016), now that the
game's official servers are gone. Run it on your PC, point the game at it, and play with your friends on your
own network: levels, scores, ghosts, the level builder, friends, challenges and the adventure map all work again.

> Fan-made, unofficial and not affiliated with Traplight. This repository contains **no game files**: you need your
> own copy of the 2.4.9 game. It only works with version **2.4.9** (not 3.x).

* ASP.NET Core (Kestrel) HTTP/HTTPS server + a tiny DNS server + a UDP "where is the server?" responder
* Everything is stored in one SQLite file (players, levels, scores, ghosts, comments, challenges)
* Windows desktop window (status, your IP, log, tools) or headless `--console` mode
* .NET 8, no external services

## Contents

- [Features](#features)
- [Quick start](#quick-start)
- [Connecting a client](#connecting-a-client)
- [Command-line options](#command-line-options)
- [How the adventure map works](#how-the-adventure-map-works)
- [Data and backups](#data-and-backups)
- [Building from source](#building-from-source)
- [Project layout](#project-layout)
- [Troubleshooting](#troubleshooting)
- [Credits](#credits)

## Features

- **The whole online side of the game**: login, players, friends, level save/publish/search, level data and screenshots,
  ratings, comments, creator rewards, high scores, **ghosts**, timed events (Daily Gems, Fresh and Free), friend
  challenges. About 130 routes are declared, around 90 are fully implemented and the rest answer with valid empty replies
  (see [ENDPOINTS.md](ENDPOINTS.md)).
- **Recreated adventure map.** The official map data is lost, so `Resources/LocalInitialData.txt` is a rebuilt one
  (305 nodes). The server also repairs and extends it on the fly:
  - every editor item is unlocked, and every player is topped up to 2,000,000 coins and diamonds at login
    (`--keep-locks` / `--normal-economy` turn this off);
  - the TV ("signal") nodes are made playable on the unmodified client (cinematic-only sequences, intro level);
  - "Construction" checkpoints instead of some chests, a **builder mission** (build and publish a level to move on),
    a **puzzle level** (fill the gaps with ground material), and a side path of **INSANE** levels. The game never adds
    server-made nodes to the path it builds itself, so the server injects them into each player's saved path;
  - a brand-new database is seeded with 4 starter levels (the map serves public levels in place of the lost official ones).
- **Ghosts**: each player's 3 best runs per level are kept, so you can race several ghosts, even alone.
- **Friend races**: new players automatically become friends with everyone on the server. The window has a
  "Challenge a player" tool (the original client has no button for it).
- **Level import / export** as `.zip` (`/v1/minigame/export` and `/import`).
- **CORS** for browser clients (for example a WebGL build of the game): the server answers the browser's `OPTIONS` preflight, allows any origin, and exposes the response headers the game reads (`PLAY_STATUS`, `PLAY_HASH`, `FILE_SIZES`, `GHOST_*`). Requests without an `Origin` header (the native game) are unchanged.
- **LAN discovery**: patched Android clients find the server by themselves, no DNS or certificate needed.
- **Own certificate authority** generated on first run, for clients that need HTTPS (iOS, unpatched Android).

## Quick start

Requirements: Windows (the build is self-contained, no .NET install needed), the game 2.4.9 on a phone, and both on the
same Wi-Fi.

1. Get `BBR249Server.exe` (build it yourself, see below) and put it next to `scripts/Start BBR249Server.bat`
   (or just run the exe as Administrator).
2. Run **`Start BBR249Server.bat`**. It asks for Administrator rights: the server uses ports **53, 80 and 443**
   (and UDP 49249).
3. A window shows the server status and your PC's local IP (like `192.168.1.xxx`). Keep it open while you play.
   On first run it creates a `Data` folder (database, log, certificates).
4. Connect the game, see below, and launch it.

The program targets Windows (`net8.0-windows`, WinForms window).

## Connecting a client

The game talks to `https://woeprod.traplightgames.com` and uses Facebook's `graph.facebook.com`. There are two
ways to make it talk to your PC instead:

**A. A patched client that discovers the server (easiest, Android).**
It broadcasts `BBR249?` on UDP port **49249**; the server answers `BBR249` and the client uses the sender's IP over
plain HTTP. The patched APK also targets API 24 and ships a network-security config so Android accepts the
local server. No DNS change, no certificate. (The patched APK is not part of this repository.)

**B. DNS + certificate (any client, including iOS and the original unpatched game).**
1. Install `Data/BigBangRacing_RootCA.crt` on the device (Android: *Settings → Security → Install a certificate →
   CA certificate*; iOS: open the file, install it, then *Settings → General → About → Certificate Trust Settings*
   and enable full trust).
2. Set the device's DNS to the PC's IP (Wi-Fi settings → static IP → DNS 1), or on a jailbroken iOS device add
   `YOUR_PC_IP woeprod.traplightgames.com` and `YOUR_PC_IP graph.facebook.com` to `/etc/hosts`.
3. Switch the DNS back to automatic when you are done playing.

**Re-using an account after reinstalling the game.** If you put a file `adopt.txt` containing a player id next to
the server, the next login that arrives without an id adopts that player instead of creating a new one (the file is
deleted after use).

## Command-line options

Add them at the end of the last line of the `.bat`, or pass them to the exe.

| Option | Effect |
|---|---|
| `--console` | Run headless, log to the terminal (no window) |
| `--no-dns` | Do not start the DNS server (port 53 busy, or patched clients only) |
| `--verbose` | Log every request |
| `--keep-locks` | Keep the original item locks instead of unlocking everything |
| `--normal-economy` | Normal coin/diamond amounts instead of 2,000,000 |
| `--no-builder-mission` | Remove the builder mission from the map |
| `--no-puzzle` | Remove the puzzle level from the map |
| `--no-insane` | Remove the INSANE side path from the map |
| `--puzzle-section=NAME` | Force the map section that gets the puzzle (default: automatic) |
| `--http-port N`, `--https-port N` | Use other ports (testing) |

## How the adventure map works

The client builds the map itself from a "graph" file it downloads (`/v1/path/db/find`): sections of levels, chests,
TV nodes and unlocks. The server serves its own graph and fixes it before sending (see `UnlockBuilder.cs` and
`MapContent.cs`). Progress is stored per player as a "saved path", which the server also corrects at login. Things
worth knowing if you change it:

- A section's name is also the name the game uses to mark it as passed, so renaming a node needs the section renamed too.
- TV sequences are chosen from a node's name; unknown names give a short grey screen.
- The game only creates the next strip of map after a checkpoint, so anything extra (puzzle, side paths) is added to
  the saved path, and node ids are renumbered when that is done.
- A chest or TV node that is the current node must stay "not unlocked" in the saved path, otherwise the game skips its
  opening animation and rewards.

## Data and backups

All state is in `Data/game249.db` (SQLite, WAL mode: copy it with SQLite's backup API or with the server stopped,
and keep the `-wal` file next to it). `Data/server249.log` is the log. `Data/` is git-ignored.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
# run for testing (add --no-dns if port 53 is busy)
dotnet run --project BBR249Server.csproj -- --console

# single-file Windows build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`IncludeNativeLibrariesForSelfExtract` is required, otherwise the SQLite library is not found.

## Project layout

| Folder | Content |
|---|---|
| `BBR249Server/` | Start-up, command line, the Windows window (`ServerWindow.cs`) |
| `BBR249Server.Api/` | The game's requests: players, levels, scores, ghosts, challenges, timed events, and the map fixes (`UnlockBuilder.cs`, `MapContent.cs`) |
| `BBR249Server.Net/` | HTTP routing, DNS server, LAN discovery, certificate manager |
| `BBR249Server.Data/` | SQLite schema and helpers |
| `BBR249Server.Core/` | Settings and shared helpers |
| `Resources/` | Embedded data: recreated map, builder template, puzzle level and thumbnail, starter levels |
| `scripts/` | `Start BBR249Server.bat` |

An embedded copy of this server also exists inside a Unity 2018 port of the game (so the game needs no server PC);
the logic is the same.

## Troubleshooting

- **Stuck on "connecting to server"**: the PC firewall is probably blocking it. Allow the program through.
- **Ports 53/80/443 busy**: IIS uses 80 (disable it in *Windows Features*); another DNS service may use 53 (`--no-dns`).
- **Patched client cannot find the server**: same Wi-Fi, server running, firewall open for UDP 49249; guest
  networks and "client isolation" block it.
- **Unpatched client still fails after setting the DNS**: the certificate must be installed *and trusted*. Some routers
  also hand out an IPv6 DNS that the phone asks first: turn IPv6 off on the router or use the patched client.
- **"Connection error" after restarting the server**: the device may have fallen back to the router's DNS; toggle
  Wi-Fi off and on, and keep the server running while playing.
- **Leftover proxy**: a Wi-Fi proxy sends all of the game's traffic to the wrong place.
- **WebGL build blocked by the browser**: a page served over `https://` cannot call this plain `http://` server (mixed content); serve the page over `http://` or use a certificate your browser trusts for the HTTPS port. Also set the game's base URL to the PC's address.
- **Music**: the downloadable music tracks no longer exist, so races play the built-in ones.

## Credits

Big Bang Racing is a game by Traplight. This project is a fan-made server by [BTE-92](https://github.com/BTE-92),
developed together with Claude (Anthropic). It builds on community decompilation work on the game, including
[Dodonickey's decompile](https://github.com/Dodonickey/Big-Bang-Decompiled). No license has been chosen yet:
until the author adds one, all rights are reserved.
