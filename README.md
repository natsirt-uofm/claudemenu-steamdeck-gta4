# ClaudeMenu: a GTA IV mod menu you can use entirely with a controller

ClaudeMenu is a trainer-style mod menu for GTA IV. Everything works from a gamepad: opening and closing it, moving through it, changing settings, and even rebinding the open/close buttons. It was built for the **Steam Deck**, and it works with any XInput controller on Windows too.

It reads the controller directly through XInput instead of the game's key bindings. Under Proton, Steam Input shows the Deck's controls to the game as an Xbox 360 pad, so button combos work the same way whatever the in-game control settings are.

## Controls

| Action | Controller | Keyboard (optional) |
|---|---|---|
| **Open menu** | **LB + RB + X** (hold LB+RB, tap X) | F4 |
| **Close menu** | **LB + RB + B**, or B on the main page | F4 |
| Move up / down | D-pad or left stick (hold to scroll) | Arrows |
| Page up / down | LT / RT | |
| Select / toggle | A | Enter |
| Change value | D-pad left / right | Left / Right |
| Back | B | Backspace |

You can rebind both combos in-game under **Settings → Rebind Open/Close Combo**: hold the new buttons, then let go. You can also change them in `ClaudeMenu.ini`. By default your character is frozen while the menu is open, so menu buttons don't also make Niko jump, sprint or open the phone. You can turn this off in Settings.

**Steam Deck tip:** in Steam Input you can set a back grip (L4/R4/L5/R5) to press LB+RB+X all at once. Then one paddle opens the menu.

## Features

- **Player:** god mode, heal and armor, never wanted, set wanted level, police ignore you, everyone ignores you, unlimited sprint, move speed, never fall off bike, can't be dragged out, seatbelt, add money
- **Vehicle:** spawner with 9 categories (about 80 models, and models missing from your episode are skipped safely), replace last spawn, warp in, repair, wash, flip, vehicle god mode, auto repair, speed boost, random colour, delete
- **Weapons:** give all, give single weapon, infinite ammo, refill, remove all
- **Teleport:** to map waypoint (finds the ground), forward and up nudges, 6 save/load slots kept in the INI, and your own named locations from the INI
- **World:** set the hour, freeze time, weather, game speed (slow motion), traffic and ped density, clear area, show coordinates
- **Settings:** rebind combos, freeze-while-open, stick navigation, position (left/center/right), 6 colour themes, menu size (bigger is easier to read on the Deck's 7" screen), save

## Requirements

ClaudeMenu is a **ScriptHookDotNet** script. You need:

1. GTA IV on **patch 1.0.8.0 or 1.0.7.0** (or EFLC 1.1.3.0 / 1.1.2.0). The current Steam "Complete Edition" (1.2.x) has to be downgraded first. The usual tool is the **GTA IV Downgrader**. It can also install the ASI loader, ScriptHook, ScriptHookDotNet and FusionFix for you.
2. **ScriptHook** (the GTA IV C++ script hook) and **ScriptHookDotNet v1.7.x**
3. **.NET Framework 4.8** inside the game's Proton prefix (only needed on the Deck/Linux; see [Install on Steam Deck](#install-on-steam-deck)). Whether a Visual C++ runtime is also needed in the prefix hasn't been verified yet.

## Install on Steam Deck

This is the procedure that worked on a real Steam Deck (SteamOS, Desktop Mode, GTA IV Complete Edition on the SD card, Protontricks from Discover). **Steps 1–5 are tested. Step 6 (the Downgrader) has not been tested on a Deck yet.**

Before you start:

- Do everything in **Desktop Mode** and use **Konsole** for the commands.
- **Paste one command at a time** and press Enter after each one. Konsole sometimes joins two pasted lines into one command, which fails in confusing ways.
- GTA IV's Steam app ID is **12210**. Every command below uses it.
- In the commands, `<uuid>` is your SD card's folder name. Find it with `ls /run/media/deck/`. If the game is on internal storage instead, its folder is `~/.local/share/Steam`.

### Quick way: the setup script

`tools/steamdeck-setup.sh` does steps 1 and 3–5 for you and then prints the commands for steps 6 and 7. You still do step 2 by hand first. Download this repository (Code → Download ZIP), extract it, open Konsole in the extracted folder, and run:

```
bash tools/steamdeck-setup.sh --dry-run
```

That shows what it would do without changing anything. If it looks right, run it for real:

```
bash tools/steamdeck-setup.sh
```

It finds the library that holds GTA IV from Steam's `libraryfolders.vdf`, prints free space, skips any step that's already done (so you can run it again safely), and stops with an explanation if something is wrong. It does **not** run the Downgrader. `--help` lists the options, including `--reset-prefix` (backs up your saves, then empties a broken prefix).

### Step by step

1. **Let Protontricks see the SD card and your home folder.** The Protontricks flatpak can't access `/run/media` by default, so it can't find a game on the SD card.
   ```
   flatpak override --user --filesystem=/run/media com.github.Matoking.protontricks
   ```
   ```
   flatpak override --user --filesystem=home com.github.Matoking.protontricks
   ```

2. **Create the prefix on Proton 8.0.** In Steam: GTA IV → Properties → Compatibility → tick *Force the use of a specific Steam Play compatibility tool* → **Proton 8.0**. Launch the game once, then quit. On Proton 8 the Rockstar Games Launcher may crash with "CEF browser crash". That's fine: this launch only creates the prefix, and the downgraded 1.0.8.0 game shouldn't need the launcher. Make sure the game has fully closed before you go on.

3. **If the game is on the SD card, move its prefix there too.** Steam can create the prefix (`compatdata/12210`) on internal storage even when the game is on the SD card, and the next steps need more space than a nearly full internal drive has. First save your game library's folder in a variable, which the next steps use:
   ```
   SD=/run/media/deck/<uuid>
   ```
   (If the game is on internal storage, run `SD=~/.local/share/Steam` instead and skip the rest of this step.) Then check where the prefix is:
   ```
   ls -ld ~/.local/share/Steam/steamapps/compatdata/12210
   ```
   If the line starts with `l` and shows `-> /run/media/...`, it has already been moved; go to step 4. If it starts with `d`, it's a real folder on internal storage. Move it (first check that `$SD/steamapps/compatdata/12210` doesn't already exist, or `mv` puts the folder inside it) and leave a symlink behind:
   ```
   mkdir -p "$SD/steamapps/compatdata"
   ```
   ```
   mv ~/.local/share/Steam/steamapps/compatdata/12210 "$SD/steamapps/compatdata/"
   ```
   ```
   ln -s "$SD/steamapps/compatdata/12210" ~/.local/share/Steam/steamapps/compatdata/12210
   ```
   `SD` only lasts while this Konsole window is open. If you open a new one, set it again.

4. **Replace the symlinks in the prefix's `windows` folder with real files.** In a Proton prefix, most of `drive_c/windows` is symlinks into Proton's own read-only files, and the .NET installer can't write through them. This is what made `dotnet48` install.
   ```
   cd "$SD/steamapps/compatdata/12210/pfx/drive_c"
   ```
   ```
   cp -RL windows windows.deref
   ```
   Only continue if the copy finished without errors such as `No space left on device`. (A few `No such file or directory` lines about broken links are harmless.)
   ```
   mv windows windows.symlinks
   ```
   ```
   mv windows.deref windows
   ```
   ```
   rm -rf windows.symlinks
   ```
   **After this, keep GTA IV on the same Proton version.** Switching Proton may rebuild those symlinks and break .NET.

5. **Install .NET Framework 4.8.** This takes a long time and can look stuck. Lots of Wine error lines are normal.
   ```
   flatpak run com.github.Matoking.protontricks 12210 -q -f dotnet48
   ```
   Check that it worked. This should print the file's path, not "No such file or directory":
   ```
   ls "$SD/steamapps/compatdata/12210/pfx/drive_c/windows/Microsoft.NET/Framework/v4.0.30319/clr.dll"
   ```

6. **Downgrade and mod the game (untested on a Deck).** Download the [GTA IV Downgrader](https://github.com/ClonkAndre/GTAIVDowngrader) and run it inside the game's prefix. Paste this as one line, using the real path and file name of the `.exe` you downloaded:
   ```
   flatpak run --command=protontricks-launch com.github.Matoking.protontricks --appid 12210 ~/Downloads/GTAIVDowngrader.exe
   ```
   In the Downgrader:
   - Game folder: `Z:\run\media\deck\<uuid>\steamapps\common\Grand Theft Auto IV\GTAIV` (Wine's `Z:` drive is the Linux root, `/`)
   - Version: **1.0.8.0**
   - Tick the **ASI loader**, **ScriptHook** and **ScriptHookDotNet**

7. **Install ClaudeMenu.**
   - Copy `scripts/ClaudeMenu.cs` (or a built `ClaudeMenu.net.dll`, see below) and `scripts/ClaudeMenu.ini` into the game's `scripts` folder, next to `GTAIV.exe`: `/run/media/deck/<uuid>/steamapps/common/Grand Theft Auto IV/GTAIV/scripts/`. Create `scripts` if it doesn't exist.
   - **Set the Steam launch options** (GTA IV → Properties → General → Launch Options) so Proton loads the mod DLLs:
     ```
     WINEDLLOVERRIDES="dinput8,xlive,dsound=n,b" %command%
     ```
     Only the DLLs your setup actually uses matter. The Downgrader's readme says which ones they are.
   - **Controller layout:** keep Steam Input on a *Gamepad* template (the default), not a keyboard/mouse one.
   - Start the game, load a save, and press **LB + RB + X**.

**Visual C++ runtimes:** older instructions here said to install `vcrun2019`. On our Deck, `protontricks 12210 -q vcrun2019` failed (`vc_redist.x86.exe /q` returned status 102) and we skipped it, because the Downgrader only needs .NET. Whether ScriptHook or ScriptHookDotNet need a Visual C++ runtime in the prefix hasn't been checked yet; if one is needed, it may be `vcrun2010` or `vcrun2008` rather than `vcrun2019`.

## Install on Windows

Install .NET Framework 4.8 if it isn't already there, run the GTA IV Downgrader directly (step 6, choosing 1.0.8.0 and ticking the ASI loader, ScriptHook and ScriptHookDotNet), copy the two ClaudeMenu files into `scripts\` next to `GTAIV.exe` (step 7), then start the game. Leave out Proton, Protontricks, the prefix steps and the launch options.

## Loose `.cs` or built `.dll`?

ScriptHookDotNet compiles loose `.cs` files in `scripts/` itself when the game starts, so `ClaudeMenu.cs` can be dropped in as it is. If that doesn't work in your setup (for example, no compiler in the Proton prefix), build a DLL against your real `ScriptHookDotNet.dll` and use that instead. **Don't use both at once.**

- Windows: `build.bat "C:\Games\GTA IV"`
- Linux/Deck (`mcs` from mono): `./build.sh "/path/to/GTAIV"`

`./build.sh --check` compiles the script against a small API stub (`tools/shdn-stub`), so you can check it builds without the game. CI runs this check on every push.

## Troubleshooting the Steam Deck setup

These are the problems we hit on a real Deck, in the order we hit them.

- **Protontricks doesn't list GTA IV, or can't find the game on the SD card.** The flatpak can't see `/run/media` by default. Do step 1 (`flatpak override --user --filesystem=/run/media ...`).
- **Protontricks warns about a library folder that doesn't exist** (for example an old `/run/media/deck/<some-other-uuid>`). Steam still remembers an SD card you no longer use. The warning is harmless.
- **The internal drive fills up, or the prefix is on internal storage although the game is on the SD card.** Steam created `compatdata/12210` under `~/.local/share/Steam/steamapps/compatdata/`. Move it to the SD card and symlink it back (step 3, or let the setup script do it).
- **`mv` put the prefix at `compatdata/12210/12210`.** The destination already existed. Move the inner folder up one level, remove the empty outer one, and create the symlink again.
- **`dotnet48` fails with `err:msi:extract_cabinet FDICopy failed`**, `Failed to extract cabinet: L"netfx_core.mzz"` (or `netfx_full.mzz`), `returned status 67`, and the installer log shows `0x80070643` / `1603`. This is the main blocker. The .NET MSI can't write through the symlinks in `drive_c/windows`. Do step 4 (replace them with real copies with `cp -RL`), then run `dotnet48` again with `-f`. These did **not** fix it for us:
  - freeing disk space or moving the prefix to the SD card
  - clearing the winetricks download cache
  - switching from Proton 11 to Proton 8
  - deleting Proton's .NET registry keys (winetricks issue #2367)
  - running the `ndp48` installer directly (same cabinet error)
- **.NET stopped working after changing the Proton version (or after a Proton update).** Switching Proton may rebuild the symlinks in `drive_c/windows`. Set the game back to Proton 8.0 and run `tools/steamdeck-setup.sh` again (or repeat steps 4 and 5). If the prefix is still broken, `bash tools/steamdeck-setup.sh --reset-prefix` backs up your saves (from `pfx/drive_c/users/steamuser/Documents/Rockstar Games`) to `~/GTAIV-save-backups`, then empties the prefix; launch the game once on Proton 8.0 and run the script again.
- **The Rockstar Games Launcher crashes with "CEF browser crash" on Proton 8.** Expected. The launch in step 2 is only needed to create the prefix.
- **`vcrun2019` fails** (`vc_redist.x86.exe /q` returned status 102). We skipped it; it isn't known to be needed. See the note at the end of [Install on Steam Deck](#install-on-steam-deck).
- **A command fails with a strange error, or two commands seem to run as one.** Konsole sometimes merges pasted lines. Paste one command at a time and check it before pressing Enter.

## Troubleshooting in game

- **The combo does nothing.** Look in `scripts/ClaudeMenu.log`. If there's no log, ScriptHookDotNet didn't load the script (check ScriptHookDotNet's own log, .NET in the prefix, and the launch options). If the log exists, open the menu with F4 and check **Settings → Controller Status**.
- **Some option does nothing.** If one game function fails, ClaudeMenu logs it, turns off only that function, and keeps running. Send the log lines.
- **The text sits a bit off in its rows.** Change **Settings → Menu Size**.
- **The menu stays frozen after closing, or you can't move.** Open and close the menu again, or turn off "Freeze Player While Open".

## Layout

```
scripts/ClaudeMenu.cs      the whole menu (one file; written in C# 4 because ScriptHookDotNet needs that)
scripts/ClaudeMenu.ini     settings, combos, saved teleports
tools/shdn-stub/           compile-only stand-in for the ScriptHookDotNet API (CI only, never ship it)
tools/steamdeck-setup.sh   Steam Deck helper: prepares the Proton prefix and installs .NET
build.bat / build.sh       build ClaudeMenu.net.dll
```
