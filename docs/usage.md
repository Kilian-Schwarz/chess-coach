# Using Chess Coach

## Switch it on

Click the knight icon (menu bar on macOS, notification area on Windows) and turn on "Active". The status line under it tells you what the app sees. Turning it off stops all capturing, timers and the engine; an inactive app uses no CPU.

Show a board on screen, preferably fully visible and not covered by other windows. The app looks at the whole screen every few seconds until it finds a board, then watches only that area.

If the board is not found, make the window larger, avoid heavy zoom, and check the screen-recording permission (macOS) or the monitor selection (Windows).

## Teach it your pieces

Set up a new game so that the starting position is visible. The app learns the look of the pieces and squares from that image and shows "Pieces learned". It also detects which side is at the bottom. If you switch to a different piece set or board theme, choose "Relearn pieces" and start from a starting position again.

Under "I play" you pick white, black or automatic. The side to move is normally read from the highlighted last move. If the app is wrong, use "Switch side to move" (macOS Option+Command+T, Windows Ctrl+Alt+T).

## Hints

Hints come in four steps. Ask for the next one with the "Next hint" button in the panel or with the shortcut (macOS Option+Command+H, Windows Ctrl+Alt+H).

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

## Evaluation

When a game ends (checkmate, stalemate, a new starting position, the board gone for a minute, or "End game") the "Evaluation" window opens. It shows for each side: accuracy, average centipawn loss, counts of inaccuracies, mistakes and blunders, a performance rating, the opening, three key moments with the facts behind them, and an AI summary with three lessons. Games are saved as JSON and PGN in the data folder, and "Copy game as PGN" is in the menu.

## Training with two people

Turn on "Two-person training" when two players share one board. Both sides get ratings and hints, and each side's results go to its own profile.

## Language

The interface follows the system language. Settings has a Language picker with System plus English, German, French, Spanish, Italian, Portuguese (Brazil), Dutch, Polish, Turkish, Russian, Ukrainian, Chinese (Simplified), Japanese and Korean. English and German are written by hand; all other languages are machine translations and may contain mistakes. Corrections are welcome as pull requests to the string catalog. On macOS a changed language applies after a restart, and the settings offer a button for it.

## AI server

Settings, field for the server address and token, "Load models", pick a model. The server address and model are empty until you enter them. The AI answers in the interface language.

## Updates

The app checks for updates at start and then every 24 hours, and installs them in the background. The new version starts the next time you quit and open the app, or after you confirm. "Check for Updates ..." in the menu checks right now; "Install updates automatically" in Settings turns the background install off. A failed automatic check is silent.

## Where data lives

macOS: `~/Library/Application Support/ChessCoach/`. Windows: `%APPDATA%\ChessCoach\`. The folder holds `profiles`, `puzzles` and `games` as JSON. The formats are the same on both systems, so a profile can be copied between a Mac and a Windows PC.
