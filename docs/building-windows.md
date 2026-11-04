# Building Chess Coach for Windows

You get a self-contained `ChessCoach.exe` with Stockfish next to it. No .NET install is needed to run it. To build it you need the .NET 10 SDK, and that is the only hard requirement. The app targets 64-bit Windows 10 (version 2004) or newer.

## On Windows

Open PowerShell in the repository root.

```powershell
windows\tools\build.ps1
```

The script publishes the app as one self-contained file, downloads the official Stockfish release (AVX2 build, universal as fallback) into a `stockfish` folder next to it, and zips the result. You get `dist\windows\ChessCoach-<version>-windows-x64.zip` and the unpacked folder `dist\windows\publish`. Start `ChessCoach.exe` from either. Pass `-Version 1.2.3` to set the version; without it the script uses the one in the project file.

The script also builds the installer `dist\windows\ChessCoach-<version>-setup.exe`: the small installer program from `windows\ChessCoach.Setup` (self-contained, trimmed, about 11 MB) with the ZIP appended as payload. Run the script with `OFFICIAL_BUILD=1` (bash) or `$env:OFFICIAL_BUILD = 1` (PowerShell) for a release build.

Plain commands do the same without the zip and the installer:

```powershell
dotnet publish windows\ChessCoach.App\ChessCoach.App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o dist\windows\publish
windows\tools\fetch-stockfish.ps1 -Target dist\windows\publish
```

## From macOS or Linux

The app is a WPF project, but the SDK can cross-compile it. The project sets `EnableWindowsTargeting`, so no extra workload is needed.

```bash
windows/tools/build.sh
```

Same result in `dist/windows`. The script needs `curl`, `unzip`, `zip` and `python3`. You cannot run the exe on the build machine; copy the ZIP to a Windows PC.

## Installer

`ChessCoach.Setup` is a single-file .NET 10 program without WPF or WinForms. Its dialogs are the native Windows task dialog (`TaskDialogIndirect`), shortcuts come from the shell link COM object, and the uninstall entry is a per-user key under `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChessCoach`. The build appends the release ZIP to the setup program followed by a 24 byte footer (payload length as 16 hex digits and the marker `CCSETUP1`). At run time the setup reads the ZIP from its own file; the first part of the file, without the payload, becomes `Uninstall.exe` in the install folder.

Options: `/S` silent, `/desktop`, `/nostart`, `/uninstall`, `/deletedata`. Texts live in `windows/ChessCoach.Setup/Strings*.resx` in the same 14 languages as the app; `check-resources.py` covers both folders.

The installer is not Authenticode-signed, so SmartScreen warns until the file has built reputation. If you get a certificate later, sign `ChessCoach-Setup.exe` before the ZIP is appended (the appended payload would otherwise sit behind the signature) and sign `ChessCoach.exe` inside the publish folder before it is zipped.

## Download size

The ZIP is about 150 MB. About 80 MB of that is Stockfish: the official release only offers the universal build with two neural networks embedded, and those do not compress. The rest is the self-contained app (.NET runtime and WPF, 71 MB). The single file is already compressed; WPF and WinForms cannot be trimmed, and `InvariantGlobalization` is not an option because the language list needs cultures. Turning off the compression inside the single file would save only about 6 MB in the ZIP and triple the size on disk.

## Updates

Chess Coach updates itself with a small built-in updater. It reads `update.json` from the latest GitHub release, downloads the Windows ZIP named there, and checks the SHA-256 hash and an ECDSA P-256 signature against the public key compiled into the app. Only then does it unpack the ZIP to `%APPDATA%\ChessCoach\update\<version>`. A short helper script waits for the app to exit, copies the new files over the program folder and starts the app again. The program folder is the one the app runs from, so this works the same for an unpacked ZIP and for a copy installed by the setup (`%LOCALAPPDATA%\Programs\Chess Coach`). After an update the app refreshes the version shown in Settings, Apps.

Your own builds do not install updates. The updater only runs in official release builds, which the release workflow marks with `-p:OfficialBuild=true` (set the environment variable `OFFICIAL_BUILD=1` for `build.sh` or `build.ps1` to do the same). A build without the mark tells you that a newer version exists and offers to open the releases page.

## Tests

The core library and its tests have no Windows dependency, so they run anywhere:

```bash
dotnet test windows/ChessCoach.sln
```

The updater's version comparison, `update.json` parsing and signature check have their own tests in `windows/ChessCoach.App.Tests`.

## Languages

UI texts live in `windows/ChessCoach.App/Strings*.resx`, texts from the core in `windows/ChessCoach.Core/CoreStrings*.resx`. English is the source, German is maintained by hand, and the other twelve languages are machine-translated. After editing the English file, run `python3 windows/tools/check-resources.py` to see which translations lack a key or a placeholder.

## Where things end up

- Settings, profiles, puzzles and games: `%APPDATA%\ChessCoach\`
- Log file: `%LOCALAPPDATA%\ChessCoach\Logs\chesscoach.log`
- Stockfish: the `stockfish` folder next to the exe, else `PATH`, else the path set in Settings.
