# Changelog

All notable changes are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.2] - 2026-11-05

### Changed
- The board is tracked by checking it where it was, so overlay and panel stay visible while you move pieces; they only disappear when the board has really been gone for a moment.
- On macOS the board's own browser window is captured, so other windows in front of it no longer end or disturb the game; switching tabs pauses and resumes the same game.
- The board orientation stays fixed for the whole game.
- A resync keeps the game: the recap, accuracy and PGN always cover all moves.
- Faster reaction: the board is checked at least every 1.5 seconds and immediately after switching apps or Spaces.
- Explanations use the engine's line, pins, discovered attacks and the concrete consequence of a threat; hints name the real theme and hint 3 is concrete.
- A threat is shown only when it wins something, with the reason; "check" and "mate" are verified by the move generator.
- Puzzles explain where they come from, ask a guiding question, offer three concrete hints and show why the solution works plus a habit to remember; progress per theme.

### Fixed
- A piece that is held or dragged never changes the game, and chess.com move dots and rings are ignored.
- Marks while dragging only appear where the piece would really be lost, naming the attacker.
- A move after a longer drag is no longer missed; a stuck position recovers after three seconds.
- No false new game after a misread frame, no relearning of pieces on a wrong board area.
- Windows: no more "Screen capture failed" on short hiccups and no periodic full-screen search that hid the window.
- Correct wording for forced mates, lower-case possessives mid-sentence, German number format and plurals.
- Release builds no longer contain local file paths.

## [0.1.1] - 2026-11-05

### Added
- Installers: a drag-to-Applications disk image for macOS with an offer to move the app into Applications, and a per-user setup program for Windows with Start menu entry, optional desktop shortcut and uninstaller.
- Automatic recap: the game end is detected (checkmate, stalemate, a dialog over the board, a new starting position) and the recap opens without taking focus. If the game continues after a false end, the end is withdrawn. A missing board or another tab only pauses the game.
- Recap takeaways: up to three concrete lessons from your own mistakes, with "Train these puzzles" and "Set as focus".
- Interactive training: click or drag pieces, legal targets, promotion choice, "Not quite, try again", the opponent's refutation after a wrong move, Skip and Show solution.
- Welcome window on first start and a fair-play notice.
- Hints while dragging: a lifted piece never counts as a move; unsafe target squares are marked.
- Short explanations first with a "More" button.

### Changed
- Moves are rated by the drop in winning chance instead of raw centipawns.
- The threat warning appears after a hint or on request, so you look for threats yourself first; new players still see it automatically.
- The first hint is always the gentlest; stronger hints follow a short thinking pause.
- Puzzles with hints count as assisted; reviews fall due at the start of the day; the daily queue is capped and mixed by theme; equally good alternatives are accepted.
- Placement and skills need enough moves and data; the learning focus changes with hysteresis.
- Update notices no longer interrupt a game.
- New global shortcuts: Control-Option-Command-H/T on macOS, Ctrl+Shift+F8/F9 on Windows, active only while the app is on.
- Lower energy use while no board is visible.

### Fixed
- A new piece set is learned automatically, also in the middle of a game.
- A move is reviewed only once it is confirmed and the mouse button is up.
- No false "checkmate" review when the engine gives no result.
- The game history survives a misread frame or a resync.
- Light dialogs over the board are recognised.
- The panel no longer disappears when the board is lost for a moment.
- Training window layout.
- Stage-2 hint texts in German and Dutch, German chess terms in explanations.
- Windows: no more protected-content (S:102) warning on remote desktops, no flicker of the fallback capture, takebacks are reconciled.

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
