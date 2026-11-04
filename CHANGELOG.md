# Changelog

All notable changes are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
