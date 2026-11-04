# Architecture

Two native apps share one design and one set of test vectors.

```
ChessCoach/Core        Swift core, no UI, compiled by the test runner and the app
ChessCoach/App         macOS menu bar app (SwiftUI, ScreenCaptureKit)
ChessCoach/Resources   asset catalog, string catalog (Localizable.xcstrings), openings.tsv
windows/ChessCoach.Core        C# core (net10.0, platform neutral)
windows/ChessCoach.Core.Tests  xUnit tests, run on macOS, Linux, Windows
windows/ChessCoach.App         WPF shell (net10.0-windows, GDI capture)
Tools/                  test runner, test data, opening converter, release scripts
```

## Core

The core is a port-by-structure pair: each Swift file has a C# counterpart with the same algorithms and thresholds.

| Area | Responsibility |
| --- | --- |
| RGBImage, BoardDetector | grid line detection, board rectangle |
| PieceRecognizer, BoardScanner | square features, templates learned from the starting position, FEN from an image |
| Position, MoveGen, SAN, GameRecord | placement and FEN, legal move generation (perft tested), SAN in English for PGN and German for display, game history with exact castling and en passant state |
| SideToMove | side to move from the highlighted last move |
| Tactics, Threats | attacks, hanging pieces, move inference, null-move threat check |
| Engine | UCI client for Stockfish, MultiPV 3, timeouts, crash protection, idle shutdown after two minutes |
| Accuracy, Hints, Curriculum | win-probability and accuracy formulas, hint steps, learning path |
| Profile, Puzzles, GameReport, Storage | player file, spaced repetition, evaluation, JSON files |
| Style, Coach | explanation style card and prompts |
| Localization | texts of the core (move descriptions, hint themes, ratings, skills) go through string keys; English is the source text |
| Openings | lookup by EPD in `openings.tsv` |

The two apps only differ in platform code: capture, overlay, hotkeys, token storage (Keychain or DPAPI), updates.

## Languages and updates (macOS)

`Localizable.xcstrings` holds the English source texts, a complete German translation and machine translations for French, Spanish, Italian, Portuguese (Brazil), Dutch, Polish, Turkish, Russian, Ukrainian, Chinese (Simplified), Japanese and Korean. The Language setting stores the choice in the `AppleLanguages` default of the app; the language applies after a restart. The English name of the active language goes into the last line of the AI style card ("Answer in German.").

Updates use a small own updater (`Updater.swift`, CryptoKit and Security only); see [updates.md](updates.md).

## Main loop

Capture the board area (a full-screen search only every five seconds or when the board is lost), compare a cheap checksum of the 64 square centers, and run full recognition only when it changed. A new position goes to the game record, the engine is asked for analysis, and the explanation is requested once per move and cached by FEN. With the app switched off no timers run, and the engine process ends after two minutes without analysis.

## AI prompt

The system prompt is fixed word for word. The variable part is short: position facts in English, a style card of at most about 60 tokens (level, learning focus, typical mistake, length, depth, answer language). The answer language is the interface language. No game history and no earlier answers are included.

## Data formats

Same JSON on both platforms, `schemaVersion: 1`, ISO-8601 UTC dates.

- `profiles/<id>.json`: id, name, games played, placement games, rating, level, skills, mistake counts, focus, learning speed, hint statistics, rating history, explanation style
- `puzzles/<profileId>.json`: puzzles with FEN, accepted moves (UCI), best line, theme, mistake type, box, due date, attempts, mastered flag
- `games/<date>-<id>.json` and `.pgn`: moves (UCI and SAN), rating per move, accuracy, key moments, opening, summary, profile per color
- `Resources/openings.tsv`: `EPD<TAB>ECO<TAB>Name`, generated from lichess chess-openings by `Tools/openings/convert.py`

## Tests

- Swift: `SKIP_LLM=1 Tools/run-tests.sh` compiles `ChessCoach/Core` with each `Tools/tests/<area>/main.swift`. Areas: movegen, game, recognition, threats, openings, learning.
- C#: `dotnet test windows/ChessCoach.sln`.
- `Tools/testdata/vectors.json` holds the shared vectors (perft, SAN, games, threats, hints, accuracy formulas, move inference, openings). Both languages must pass them.
- Tests that need an AI server read `CHESSCOACH_TEST_LLM_URL` and `CHESSCOACH_TEST_LLM_MODEL`.
- CI runs the Swift tests and an unsigned build on macOS, the core tests on Linux and the WPF build on Windows.

## Release

`Tools/release/` holds the scripts used by `.github/workflows/release.yml` and by `release-local.sh`. See [release-setup.md](release-setup.md) for setup and [updates.md](updates.md) for the update code.
