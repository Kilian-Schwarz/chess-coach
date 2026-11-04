# Testing Chess Coach on Windows

This is a checklist for someone with a Windows PC who wants to try a build or a release. You need Windows 10 (2004 or newer) or Windows 11, 64 bit, and a browser with a chess game open, for example on lichess.org. An AI server is optional; the app works without one but then shows no written explanations.

Please report what fails with the Windows version, the screen setup (monitors, scaling) and the last lines of the log: `%LOCALAPPDATA%\ChessCoach\Logs\chesscoach.log`.

## 1. Install and start

- Unpack the ZIP to a folder you can write to, for example `%LOCALAPPDATA%\Programs\ChessCoach`, and start `ChessCoach.exe`. SmartScreen may warn about an unsigned file: choose More info, then Run anyway.
- No window opens. A knight icon appears in the notification area, possibly under the hidden icons.
- Check the icon in light and in dark taskbar mode. Switch the Windows theme while the app runs; the icon should follow.

## 2. Find the board

- Click the icon and turn on Active. The status line in the menu says what the app sees.
- Show a game with the start position. The status should change to "Pieces learned".
- A green frame should sit exactly on the board, and clicks must pass through it to the browser.
- Move a piece. The side to move is read from the highlighted last move. If it is wrong, use "Switch side to move" (Ctrl+Alt+T).

## 3. Hints and ratings

- The panel appears next to the board. It can be dragged, collapsed and reset from the menu.
- Press "Next hint" or Ctrl+Alt+H four times. You should see a topic, then a marked piece, then an idea, then an arrow with the move. Clicking the button must not steal keyboard focus from the browser.
- After you move, a rating card shows. With "Show threats" on, a red arrow marks what the opponent threatens.
- Play to the end of a game, or choose "End game" in the menu. Open Evaluation and look for the accuracy, key moments and the PGN copy button.

## 4. Learning windows

- Player record: rating, skills, common mistakes, puzzle counts.
- Training: solve a puzzle by click or drag, use the hints, ask for the solution.
- The first profile carries your Windows display name, or "Player" if Windows has none. Create a second profile and tick "Training for two" to check that both are tracked.

## 5. Screen sharing

Share your screen in Teams, Zoom or OBS. The frame and the panel must not appear in the shared picture. Chess Coach excludes only its own capture; it does not hide from anything else on purpose, so tell us if you see the opposite.

## 6. Language

Settings has a language list. Pick another language and confirm the restart. Menus, windows and (with an AI server) the explanations should switch. If a text is cut off, in the wrong language or clearly mistranslated, note the language and the screen. Everything except English and German is machine-translated.

## 7. AI server

Enter the address of an OpenAI-compatible server, add a token if it needs one, choose Load models and pick a model. Explanations should start within a second or two. With a wrong address or token the app shows an error and keeps working.

## 8. Several monitors and scaling

If you can, test two monitors with different scaling (100 % and 150 %). The frame must match the board on both, and the panel position is remembered per monitor.

## 9. Pause and resume

Lock the PC with Win+L, start the screensaver, or let it sleep. The status shows "Paused", and capturing stops. After you return, it resumes by itself.

## 10. Load

Open Task Manager, Details tab. With an unchanged board in view, `ChessCoach.exe` should stay under 3 % CPU and 150 MB of memory. With Active off it should sit at 0 % and no `stockfish.exe` should run. Write down your numbers and your CPU.

## 11. Updates

Settings has "Install updates automatically" (on by default). The tray menu entry "Check for updates …" always answers: a newer version, already current, or an error. With an official build and the setting on, a newer version downloads in the background, you get a notification, and it is installed when you quit. With the setting off, the app asks first and restarts itself after installing. A build you compiled yourself only offers to open the releases page. After an update, check that the version number changed and that your profile and settings are still there.

## 12. Hotkeys and autostart

- If another program owns Ctrl+Alt+H or Ctrl+Alt+T, the log says so.
- "Start with Windows" in Settings should add and remove the app from the startup list.

## 13. Uninstall

Delete the program folder. Your data in `%APPDATA%\ChessCoach\` stays; delete it by hand if you want a clean slate.
