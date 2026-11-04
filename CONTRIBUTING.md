# Contributing

Bug reports, board-recognition samples and pull requests are welcome.

## Report a problem

Open an issue with the bug template. For recognition problems, a screenshot of the board (crop out anything private) helps more than any description. Report security problems privately, see [SECURITY.md](SECURITY.md).

## Set up

- macOS: Xcode 16 or newer, `brew install xcodegen stockfish`. Run `SKIP_LLM=1 Tools/run-tests.sh`, then `xcodegen generate` and build.
- Windows core: .NET 10 SDK. `dotnet test windows/ChessCoach.sln` runs on macOS, Linux and Windows. The WPF app itself builds only on Windows or with `EnableWindowsTargeting`.
- Tests that call an AI server read `CHESSCOACH_TEST_LLM_URL` and `CHESSCOACH_TEST_LLM_MODEL` and are skipped when unset.

## Pull requests

1. Open an issue first for anything larger than a fix, so the direction is agreed.
2. Keep the macOS and Windows implementations in step. The core logic exists twice (`ChessCoach/Core` in Swift, `windows/ChessCoach.Core` in C#); shared test vectors live in `Tools/testdata/vectors.json`. Change both or explain why not.
3. Add or update tests. Run both test suites.
4. Add a line under `Unreleased` in `CHANGELOG.md` for user-visible changes.
5. Fill in the pull request template.

## Code style

- English identifiers, file names, documentation and commit messages.
- No code comments. Names and small functions carry the meaning; longer explanations belong in `docs/`.
- Short, clear code. Remove duplication rather than adding options.
- User-visible text goes through the string catalogs, never into code. English is the source language, German is complete.
- Commit messages: imperative, present tense, one logical change per commit ("Fix castling rights after rook capture").

## Privacy rules for contributions

Never commit tokens, certificates, private server addresses or personal game data. Screen content must stay local; only text facts may be sent to an AI server.

## License

By contributing you agree that your contribution is licensed under GPL-3.0-or-later, like the rest of the project.
