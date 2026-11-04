# Release setup

Two ways to publish a release. Both start from the same scripts in `Tools/release/`.

- Local: you build, sign and notarize on your own Mac. Works as soon as your signing identity and a notarytool profile exist.
- GitHub Actions: pushing a tag builds everything. Needs the secrets below.

## Before every release

1. Move the entries under `Unreleased` in `CHANGELOG.md` to a new section `## [X.Y.Z] - YYYY-MM-DD` and fix the compare links at the bottom. The release notes are cut from that section; the build fails if it is missing.
2. Commit and push to `main`, wait for green CI.
3. Tag and push: `git tag vX.Y.Z && git push origin vX.Y.Z`. The workflow sets the version in `project.yml` and the `.csproj` files itself, you do not edit them for a release.
4. The workflow creates the GitHub release with notes, the DMG, the macOS and Windows ZIPs and the signed `update.json`.

## One-time: update signing key

```sh
openssl ecparam -name prime256v1 -genkey -noout -out update-signing-key.pem
openssl pkey -in update-signing-key.pem -pubout -outform DER | base64
```

The second command prints the public key. Put it into `UpdateCheck.publicKey` in `ChessCoach/App/Updater.swift` and into the constant of the Windows updater. Keep the PEM outside the repository, back it up in a password manager and store its text in the secret `UPDATE_SIGNING_KEY`. If the key is lost, installed apps cannot verify new updates and users must reinstall.

## One-time: Apple credentials

1. A paid Apple Developer membership and a "Developer ID Application" certificate. Create it in Xcode (Settings, Accounts, Manage Certificates) or in the developer portal.
2. In Keychain Access, export the certificate with its private key as `.p12` with a password.
3. In App Store Connect, Users and Access, Integrations, create an API key with the Developer role. Note the key ID and the issuer ID, download the `.p8` once.
4. Encode files for secrets: `base64 -i certificate.p12 | pbcopy`, same for the `.p8`.

## GitHub secrets

Repository settings, Secrets and variables, Actions, New repository secret.

| Secret | Content |
| --- | --- |
| `MACOS_CERTIFICATE_P12_BASE64` | base64 of the Developer ID `.p12` |
| `MACOS_CERTIFICATE_PASSWORD` | password of the `.p12` |
| `MACOS_KEYCHAIN_PASSWORD` | any new random password, used for the temporary build keychain |
| `APPSTORE_API_KEY_P8_BASE64` | base64 of the App Store Connect `.p8` key |
| `APPSTORE_API_KEY_ID` | key ID |
| `APPSTORE_API_ISSUER_ID` | issuer ID |
| `UPDATE_SIGNING_KEY` | text of the update signing PEM |

Windows code signing is optional and not wired up. Without it, SmartScreen may warn on first start until the download has built reputation. If you get a certificate later, add a signing step to the `windows` job before the ZIP is created.

Enable "Private vulnerability reporting" in the repository's Security settings, and set Actions to "Read repository contents" as default token permission; the release job requests write access on its own.

## Local release

Prerequisites: the Developer ID identity in your keychain, a notarytool profile created once with `xcrun notarytool store-credentials <name>`, and the update signing key as a file.

```sh
export NOTARY_PROFILE=<your profile name>
Tools/release/release-local.sh --version X.Y.Z --dry-run
Tools/release/release-local.sh --version X.Y.Z
```

`--dry-run` builds, adds Stockfish and signs, then stops before notarization. Without it the script notarizes and staples the app and the DMG, writes the ZIP, DMG and, when `UPDATE_SIGNING_KEY` points at the PEM file, `update.json` to `dist/mac/`. Pass `--windows-zip FILE` to include the Windows ZIP in `update.json`. With `--upload` (and `GITHUB_TOKEN` in the environment) it also creates a draft release with the files; publish the draft on GitHub. Note that `release-local.sh` runs `set-version.sh`, which edits `project.yml` and the `.csproj` files; do not commit that unless you want to.

The update feed is `https://github.com/Kilian-Schwarz/chess-coach/releases/latest/download/update.json`, so the `update.json` of the newest release is what clients see. Mark a release as latest only after you tested the DMG.

## If something fails

- Notarization rejected: `xcrun notarytool log <submission-id> --keychain-profile <name>` lists the offending binary. Usually a nested binary is not signed with Hardened Runtime and a timestamp.
- Missing changelog section: the tag version and the heading in `CHANGELOG.md` must match exactly.
- Old installations do not update: check that the public key in the app matches the private key that signed `update.json`.
