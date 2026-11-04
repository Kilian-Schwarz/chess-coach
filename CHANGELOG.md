# Changelog

All notable changes are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.1] - 2026-11-04

### Added
- Automatic recap: the game end is detected (checkmate, stalemate, a "game over" dialog over the board, a new starting position; a missing board or another tab only pauses the game) and the recap window opens by itself. It shows engine numbers at once, fills in missing move analyses in the background with a progress bar, and streams the AI summary afterwards. New setting "Open recap after a game", menu item "Last recap …", game list in the player file, and a "Finish game & show recap" button in the panel.
- Hints while dragging: a lifted piece is never counted as a move. Unsafe target squares are marked in red with a short line in the panel. New setting "Hints while dragging".
- The piece set is learned from positions up to four plies after the start.

### Fixed
- Only certain moves are rated; moves from a guessed side or an unknown gap are no longer rated.
- Stage-2 hint texts.
- German chess terms in explanations.
- The panel stays where you put it.
- Windows windows are no longer marked as protected content, which fixes the DRM warning (error S:102) on Shadow and other remote desktops.
- The board is now read directly from its window, so the overlay and panel never appear in the capture.

## [0.1.0] - 2026-11-04

First public release.

### Added
- Board and piece recognition from the screen on macOS and Windows; pieces are learned from the starting position.
- Click-through overlay with arrows and marks, plus a movable, collapsible coach panel.
- Four-step hints: theme, piece, idea, solution. The step used is recorded per move.
- Threat detection ("Threat: knight e5 takes queen d8") that can be switched off.
- Move ratings with explanations, opening names, and an option to ask the coach follow-up questions.
- Player files with several profiles: rating estimate with a three-game placement phase, skills, typical mistakes, learning focus and adaptive explanation style.
- Puzzles built from your own mistakes with spaced repetition and a training window.
- Game evaluation with accuracy, average centipawn loss, key moments, three lessons, and PGN and JSON export.
- Training for two people at one board.
- Explanations from any OpenAI-compatible server; works without one using computed facts.
- Interface in 14 languages, following the system language. English and German are complete; the other 12 (French, Spanish, Italian, Brazilian Portuguese, Dutch, Polish, Turkish, Russian, Ukrainian, Simplified Chinese, Japanese, Korean) are machine-translated.
- Automatic updates through a built-in updater with no third-party update framework. Releases carry an `update.json` and one ZIP per platform; the app checks the SHA-256 hash and an ECDSA P-256 signature before installing. A "Check for updates" menu item is included.

[Unreleased]: https://github.com/Kilian-Schwarz/chess-coach/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Kilian-Schwarz/chess-coach/releases/tag/v0.1.0
