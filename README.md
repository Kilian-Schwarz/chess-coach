# Chess Coach

[![CI](https://github.com/Kilian-Schwarz/chess-coach/actions/workflows/ci.yml/badge.svg)](https://github.com/Kilian-Schwarz/chess-coach/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kilian-Schwarz/chess-coach)](https://github.com/Kilian-Schwarz/chess-coach/releases/latest)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)
![Platforms](https://img.shields.io/badge/platform-macOS%20%7C%20Windows-lightgrey)

A chess trainer that sits next to the board you already play on. It reads the board from your screen, lets Stockfish check the position, and explains in plain words what is going on. It does not hand you moves: you get a hint ladder that starts with a theme and ends, only if you ask for it, with the answer.

The app speaks English and German completely and follows your system language. Twelve more languages (French, Spanish, Italian, Portuguese, Dutch, Polish, Turkish, Russian, Ukrainian, Chinese, Japanese, Korean) are machine translations from the same string catalog; fixes are welcome.

## What it does

- Recognizes the board and pieces on screen (lichess, chess.com, desktop programs) and learns your piece set from the starting position.
- Draws arrows and marks on a click-through overlay and shows a movable coach panel next to the board.
- Gives hints in four steps: theme, piece, idea, solution. Every step is optional and the app counts which step you needed.
- Warns about threats: "Threat: knight e5 takes queen d8".
- Rates every move you play and explains mistakes.
- Names the opening ("Italian Game: Two Knights Defense (C55)").
- Keeps a player file per profile: rating estimate, skills, typical mistakes, current learning focus.
- Turns your own mistakes into puzzles and repeats them at growing intervals.
- Evaluates finished games with accuracy, key moments and three lessons, and saves them as JSON and PGN.
- Supports training with two people at one board.
- Checks for updates and installs them itself (official builds; self-built apps point you to the releases page).

Full guide: [docs/usage.md](docs/usage.md).

## Install

Download the latest build from the [Releases](https://github.com/Kilian-Schwarz/chess-coach/releases) page. Stockfish is included.

**macOS 15 or newer:** download `ChessCoach-<version>.dmg`, open it and drag Chess Coach onto the Applications folder next to it, then start it from there. If you start it from somewhere else, it offers to move itself to Applications. A knight icon appears in the menu bar. On first use macOS asks for the Screen Recording permission (System Settings, Privacy & Security, Screen & System Audio Recording). Without it the app cannot see the board. Restart the app after granting it.

**Windows 10 (2004) or newer, 64 bit:** download `ChessCoach-<version>-setup.exe` and run it. It installs for your user account only (no administrator rights), adds a Start menu entry and starts the app. The file is not code-signed, so SmartScreen may warn: choose More info, then Run anyway. Remove the app under Settings, Apps. The ZIP `ChessCoach-<version>-windows-x64.zip` is the same app without an installer: unpack it anywhere and start `ChessCoach.exe`. The app lives in the notification area of the taskbar. .NET does not need to be installed.

## Set up the AI explanations

Explanations are optional. Without a server the app still shows hints, threats and ratings, built from the facts of the position.

Open Settings and enter the address of any OpenAI-compatible server, for example Open WebUI, vLLM, llama.cpp server or Ollama, plus an access token if the server wants one. Choose "Load models" and pick a model. The token is stored in the macOS Keychain or, on Windows, encrypted with DPAPI. Plain `http://` addresses work for servers in your own network; the app warns you when a token would travel unencrypted. A model with about 7 billion parameters or more gives good explanations; replies should start within a second or two, so a local or nearby server works best.

## Privacy

Screenshots never leave your computer. Board recognition runs locally, and the captured image is neither stored nor sent anywhere. The AI server receives only text: the position as FEN, the move, and a few computed facts such as hanging pieces and evaluation. Profiles, puzzles and games are plain files in your user folder (`~/Library/Application Support/ChessCoach/` on macOS, `%APPDATA%\ChessCoach\` on Windows). The only other network traffic is the update check against GitHub.

## Fair play

Chess Coach is for learning: analysis of your own games, practice against friends and bots, training with a partner. Do not use it in rated or tournament games against humans, and do not use it in any game where outside help is forbidden. The overlay does not hide itself from screen sharing on purpose, and nothing in the app moves pieces for you.

## Build from source

You do not need an Apple developer account. The result is signed ad hoc and runs on your own Mac.

macOS (Xcode 16 or newer from the App Store, [Homebrew](https://brew.sh)):

```sh
brew install xcodegen stockfish
Tools/build-local.sh
```

The script generates the Xcode project, builds the Release app, copies Stockfish into it (Homebrew's, or the official release if Homebrew has none), installs `Chess Coach.app` into `/Applications` and starts it. Then switch the app on in its menu and allow Screen Recording in System Settings, Privacy & Security, Screen & System Audio Recording; restart the app afterwards. Because the build is signed ad hoc, macOS asks again after every rebuild: remove the old entry from that list first.

Tests: `SKIP_LLM=1 Tools/run-tests.sh`.

Windows: see [docs/building-windows.md](docs/building-windows.md).

Details on layout and data formats: [docs/architecture.md](docs/architecture.md). Contributions are welcome, see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

GPL-3.0-or-later, see [LICENSE](LICENSE). Third-party components are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
