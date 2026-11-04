# Updates

Chess Coach updates itself without any third-party framework. The only service involved is GitHub.

## Release assets

Every release carries:

- `ChessCoach-<version>-macos.zip`: the notarized, stapled app
- `ChessCoach-<version>-windows-x64.zip`: the self-contained publish folder with Stockfish
- `ChessCoach-<version>.dmg`: manual install on macOS
- `update.json`

```json
{
  "version": "0.2.0",
  "notes": "Release notes",
  "macos": { "file": "ChessCoach-0.2.0-macos.zip", "sha256": "<hex>", "signature": "<base64>" },
  "windows": { "file": "ChessCoach-0.2.0-windows-x64.zip", "sha256": "<hex>", "signature": "<base64>" }
}
```

The app fetches `https://github.com/Kilian-Schwarz/chess-coach/releases/latest/download/update.json` (one HTTPS request) and downloads the file from `https://github.com/Kilian-Schwarz/chess-coach/releases/download/v<version>/<file>`.

## Signature

ECDSA P-256 over the ZIP bytes with SHA-256, DER encoded, base64. The public key (SPKI DER, base64) is a constant in both apps (`UpdateCheck.publicKey` in `Updater.swift`, the equivalent constant in `Updater.cs`). The private key stays outside the repository; `Tools/release/make-update-json.sh` signs with `openssl dgst -sha256 -sign "$UPDATE_SIGNING_KEY"`.

## Behavior

- A check runs 20 seconds after start and then every 24 hours. A failed check is silent (log line only) unless you started it.
- Settings: "Install updates automatically" (on by default). Menu: "Check for Updates ...", which always reports the result.
- macOS: download, verify SHA-256 and signature, unpack with `ditto` next to the installed app, check that the unpacked app has the bundle identifier, the version named in the feed (`x.y.z` only) and a version newer than the running one, and that it is signed by team `29GTPRH5R3` (strict `SecStaticCode` requirement). A helper process then renames the old app aside, renames the new one into place and restores the old one if that fails. A manual check asks whether to restart right away.
- Self-built apps (ad-hoc signed or unsigned) never install updates. They show "Update available" with a link to the releases page, once per version. A missing feed (no release yet) is logged once and otherwise ignored.
- Windows: download, verify, unpack to `%APPDATA%\ChessCoach\update\<version>`, and a small helper script replaces the program folder after the app exits and starts it again.

## Tests

`Tools/release/test-updater.sh` creates a throwaway key pair with `openssl` and checks version comparison, JSON parsing and signature verification of the macOS updater.
