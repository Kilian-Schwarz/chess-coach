# Using Chess Coach

## Install

macOS: open the DMG and drag Chess Coach onto the Applications folder. If you launch the app from the DMG or the Downloads folder instead, it asks once whether to move itself to Applications; "Move to Applications" copies it there, replaces an older version, restarts from the new place and ejects the DMG.

Windows: run `ChessCoach-<version>-setup.exe`. It installs to `%LOCALAPPDATA%\Programs\Chess Coach` for your user account, adds a Start menu shortcut (and a desktop shortcut if you tick the box), registers the app under Settings, Apps, and starts it. Running a newer setup over an installed copy updates it in place and closes the running app first. The setup is not code-signed, so Windows SmartScreen may show "Windows protected your PC": choose More info, then Run anyway. To uninstall, use Settings, Apps; the dialog asks whether to delete your settings, profiles and game history too. For scripts: `/S` installs silently, `/desktop` adds the desktop shortcut, `/nostart` skips starting the app; `Uninstall.exe /uninstall /S` removes silently and keeps your data unless you add `/deletedata`.

## Switch it on

Click the knight icon (menu bar on macOS, notification area on Windows) and turn on "Active". The status line under it tells you what the app sees. Turning it off stops all capturing, timers and the engine; an inactive app uses no CPU.

Show a board on screen, preferably fully visible and not covered by other windows. The app looks at the whole screen every few seconds until it finds a board, then watches only that area.

If the board is not found, make the window larger, avoid heavy zoom, and check the screen-recording permission (macOS) or the monitor selection (Windows).

## Teach it your pieces

Set up a new game so that the starting position is visible. The app learns the look of the pieces and squares from that image and shows "Pieces learned". It also detects which side is at the bottom. If you switch to a different piece set or board theme, choose "Relearn pieces" and start from a starting position again.

Under "I play" you pick white, black or automatic. The side to move is normally read from the highlighted last move. If the app is wrong, use "Switch side to move" (macOS Control+Option+Command+T, Windows Ctrl+Shift+F9).

## Hints

Hints come in four steps. Ask for the next one with the "Next hint" button in the panel or with the shortcut (macOS Control+Option+Command+H, Windows Ctrl+Shift+F8).

1. Theme: what kind of move to look for, for example "You can win material here" or "Watch the threat".
2. Piece: the piece worth moving is marked.
3. Idea: one sentence from the AI (or built from facts without a server) that does not name the target square.
4. Solution: arrow and explanation.

The app records which step you needed for each move. The player file uses this, and the puzzle schedule does too.

## Threats

When it is your move, the app also checks what the opponent would do if it were their move. A threat is reported when that costs you at least 1.5 pawns: a red arrow and a line such as "Threat: knight e5 takes queen d8". Switch it off with "Show threats".

## Ratings

After each of your moves a rating appears: Best, Good, Inaccuracy, Mistake, Blunder, with a short explanation and the better alternative. "Rate moves" turns this off. "Explain again" and the question field in the panel send the position facts to the AI for a fresh or a specific answer. The buttons "Too long", "Too short" and "Helpful" under an explanation teach the app your preferred style.

## Player file

Open "Player profile" from the menu. The first profile is named after your system user name; add or rename profiles there. Each profile (for example your own and a friend's) has:

- a rating estimate and level (beginner, intermediate, strong). The first three evaluated games are placement games; the display says "Placement: game 2 of 3".
- skills as bars: piece safety, threats, tactics, opening, middlegame, endgame, king safety
- your most frequent mistake types
- the current learning focus, which the explanations stress
- puzzle statistics and recent games, with the rating history

The profile at the bottom of the board is the active one. In two-person training the top profile is the partner's.

## Training

"Training" opens a window with puzzles made from your own mistakes. Move by clicking or dragging. You get feedback, and the same hint steps are available. A solved puzzle comes back after 1, 3, 7, 16 and 35 days; a failed one starts over at one day. Using a hint repeats the box instead of advancing. The app watches your success rate: above about 85 percent it makes puzzles harder and spacing longer, below 70 percent it repeats more.

## Dragging hints

When you lift a piece and have not put it down yet, the app does not count it as a move. With "Hints while dragging" on (default), the squares where the piece would be in danger are marked in red and one line in the panel says why, for example "Careful: Knight on e5 would be undefended." A square is marked when the piece would be attacked and undefended or attacked by a cheaper piece, or when the move leaves another of your pieces hanging. The best move is never revealed.

## Recap

The app ends a game when it sees checkmate or stalemate, when a "game over" dialog covers the middle of the board for 3 seconds while the board frame stays in place, or when a new starting position appears. The last two need at least 6 half-moves. If the board disappears or another page replaces it (for example when you switch tabs), the game is only paused and continues when the board returns. After 10 minutes without a board the game is finished quietly; its recap is then available under "Last recap …" without opening by itself. The result is only recorded when it is certain (checkmate or stalemate); otherwise it stays unknown. You can also press "Finish game & show recap" in the panel or the menu.

The recap window then opens by itself and comes to the front (switch this off with "Open recap after a game"). It shows accuracy, mistakes and key moments straight away. Moves that were not analysed yet are filled in in the background with a progress bar, and the AI summary follows as soon as it is written. "Last recap …" in the menu and the game list in the player file open past games again. Games are saved as JSON and PGN in the data folder.

## Training with two people

Turn on "Two-person training" when two players share one board. Both sides get ratings and hints, and each side's results go to its own profile.

## Language

The interface follows the system language. Settings has a Language picker with System plus English, German, French, Spanish, Italian, Portuguese (Brazil), Dutch, Polish, Turkish, Russian, Ukrainian, Chinese (Simplified), Japanese and Korean. English and German are written by hand; all other languages are machine translations and may contain mistakes. Corrections are welcome as pull requests to the string catalog. On macOS a changed language applies after a restart, and the settings offer a button for it.

## AI server

Settings, field for the server address and token, "Load models", pick a model. The server address and model are empty until you enter them. The AI answers in the interface language.

## Updates

The app checks for updates at start and then every 24 hours, and installs them in the background. The new version starts the next time you quit and open the app, or after you confirm. "Check for Updates ..." in the menu checks right now; "Install updates automatically" in Settings turns the background install off. Automatic checks never open a dialog or take the focus: a new version shows up as a menu entry at the top ("Restart to update to version X", or "Version X available ..." when the app cannot update itself) and is installed when you quit or click the entry. Only "Check for Updates ..." opens dialogs. A failed automatic check is silent.

## Where data lives

macOS: `~/Library/Application Support/ChessCoach/`. Windows: `%APPDATA%\ChessCoach\`. The folder holds `profiles`, `puzzles` and `games` as JSON. The formats are the same on both systems, so a profile can be copied between a Mac and a Windows PC.
