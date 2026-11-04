# Security policy

## Supported versions

Only the latest release receives fixes.

## Report a vulnerability

Use GitHub's private vulnerability reporting: open the repository's Security tab, choose "Report a vulnerability". Please do not open a public issue for security problems and do not post exploit details publicly before a fix is released.

Include the app version, platform, steps to reproduce and the impact you see. You get a first answer within seven days. Confirmed problems are fixed in a new release and credited in the changelog unless you prefer otherwise.

## Scope

In scope: the app, the update mechanism, handling of the AI server token, the release workflows. Out of scope: vulnerabilities in Stockfish itself (report that upstream), and servers you operate.

## Design notes

- Screen captures stay in memory and are never sent anywhere.
- The AI token is stored in the macOS Keychain or encrypted with DPAPI on Windows, never in plain files.
- Updates come from the latest GitHub release. `update.json` lists, per platform, the ZIP name, its SHA-256 hash and an ECDSA P-256 signature over the ZIP. The app downloads over HTTPS, checks the hash, then verifies the signature against the public key compiled into the app, and installs nothing if either check fails. The private signing key is never in the repository.
- On macOS the unpacked app must additionally be signed by the expected Apple Developer team ID before it replaces the installed one. Release builds are signed with a Developer ID and notarized.
- Builds you compile yourself do not install updates; they only point to the releases page.