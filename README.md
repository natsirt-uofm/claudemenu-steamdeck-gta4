# ClaudeMenu: an all-in-one GTA IV trainer you can use entirely with a controller

ClaudeMenu is a trainer for GTA IV that you drive completely from a gamepad: opening it, moving through it, changing values and even rebinding its buttons. It was built for the **Steam Deck** and works with any XInput controller on Windows too. Its headline features are a **customs garage** (paint with live preview, extras, livery, doors, tyres, proofs) and **performance multipliers** (engine power, brakes, top speed limit, nitro).

It reads the controller directly through XInput instead of the game's key bindings. Under Proton, Steam Input shows the Deck's controls to the game as an Xbox 360 pad, so button combos work the same whatever the in-game control settings are.

**Status (1.2.0):** 1.1.6 was tested on a real Steam Deck (SteamOS, Proton 8.0-103, GTA IV 1.0.8.0, ScriptHook 0.5.1, ScriptHookDotNet 1.7.2.0 BETA): the menu loads, draws and responds to the controller, and walking is normal. 1.2.0 brings back **Super Run** and **Super Jump** and reworks the **car multipliers**, both using the techniques working GTA IV trainers use (see [Credits](#credits-and-references)). It also makes the menu translucent and keeps the camera usable while the menu is open. **None of the 1.2.0 changes have been tested on the Deck yet.** They're all off by default, and **Live Debug** (Misc or Performance) shows what they see each frame. If something misbehaves, `scripts/ClaudeMenu.log` says which game function failed.

## Controls

| Action | Controller | Keyboard (optional) |
|---|---|---|
| **Open menu** | **LB + RB + X** (hold LB+RB, tap X) | F4 |
| **Close menu** | **LB + RB + B**, or B on the main page | F4 |
| Move up / down | D-pad or left stick (hold to scroll; it speeds up) | Arrows |
| Page up / down | LT / RT | |
| Select / toggle | A | Enter |
| Change value | D-pad left / right | Left / Right |
| Back | B | Backspace |
| **Nitro** (while driving; off by default) | **L3** (click the left stick) | |
| **Super Run** (when on) | hold **A** (sprint) while moving | |
| **Super Jump** (when on) | hold **X** (jump) | |
| Look around while the menu is open | right stick | |

You can rebind the open, close and nitro buttons in-game under **Settings**: choose one, hold the new button(s), then let go. If you moved sprint or jump in Steam Input or the game, tell ClaudeMenu under **Settings → Set Sprint Button / Set Jump Button**. You can also change all of them in `ClaudeMenu.ini`. By default your character is frozen while the menu is open, so menu buttons don't also make Niko jump, sprint or open the phone. The right stick still turns the camera (**Settings → Camera Moves While Open**). You can turn the freeze off in Settings.

**Steam Deck tips:** in Steam Input, set one back grip (L4/R4/L5/R5) to press LB+RB+X at once so a single paddle opens the menu, and another to L3 for nitro.

## Features

- **Player:** god mode, refill health and armour, auto heal, keep armour full, kill player; never wanted, set wanted level, police ignore you, everyone ignores you; **Super Run** and **Super Jump** (experimental, adjustable boost), unlimited sprint, no ragdoll, ragdoll now, never fall off bike, can't be dragged out, seatbelt; change player model (story characters, police, gangs, civilians); cash (see it, add, remove, set to $0)
- **Vehicles:**
  - **Spawner:** 11 categories (about 150 models, including EFLC ones), plus a controller-friendly **search** (pick letters with the D-pad). Models missing from your episode are marked and skipped safely. Spawn driving it or parked in front of you, replace your last spawn, delete the last or all spawned vehicles.
  - **Customs garage:** primary, secondary, specular and trim colour from GTA IV's full 137-colour palette with names and swatches, previewed live as you scroll (A keeps it, B puts the old colour back); random paint; extras 1–9 on/off; livery; dirt level; open, close or break off each door, the hood and trunk; smash or remove windows; repair, wash, burst/fix tyres, bulletproof tyres; engine off; bulletproof, fireproof, explosion-, collision- and melee-proof.
  - **Performance (experimental):** engine power, brake and handbrake multipliers (1.0x–10.0x in 0.5 steps), a top speed limit, and nitro (off by default) with a tank, three power levels and an infinite option. There are three switchable **boost methods** and an experimental grip assist. One tuning applies to every vehicle by default (per-vehicle is optional). A speedometer shows km/h or mph, the nitro tank and the active tuning.
  - **Presets:** save the car you're in (model, paint, extras, livery, dirt, tuning) to `ClaudeMenu.ini`, then spawn it again, apply it to another car, overwrite or delete it.
  - **Quick actions:** repair, wash, flip upright, vehicle god mode, auto repair, delete.
- **Weapons:** give all (for the episode you're playing), give one weapon, infinite ammo, no reload, fast reload, refill, explosive hits, remove all
- **World:** hour and minute, freeze time, weather, traffic / pedestrian / parked car density, clear area (everything, vehicles, peds or police), game speed (slow motion), gravity on/off, unlock all islands
- **Teleport:** map waypoint (finds the ground), forward and up nudges, 29 Liberty City landmarks across Algonquin, Broker & Dukes, Bohan and Alderney, 6 save/load slots and your own named locations from the INI
- **Misc:** hide HUD and radar, hide radar only, coordinates, FPS counter, **Live Debug**, free camera (fly with the sticks, A teleports you there), speedometer and units
- **Settings:** rebind open/close/nitro, set the sprint and jump buttons, freeze-while-open, camera moves while open, stick navigation, keyboard fallback, menu position (left/center/right), 6 colour themes, menu size, rows shown, **menu opacity**, save when the menu closes, controller status

The menu is translucent: its panels let the game show through, with text shadows so it stays readable (**Settings → Menu Opacity**, 85% by default). It has section headers, ON/OFF switches, values shown as `< 2.5x >`, a tooltip line for the selected item, a scrollbar, and a highlight that glides between rows.

### How the performance multipliers work (experimental, untested on the Deck)

GTA IV has no native for engine power, torque or brake force (nothing like GTA V's power multiplier), and editing handling data in memory isn't safe from a loose script. So ClaudeMenu does what working GTA IV trainers do:

- The **XMC mod menu**'s "NFS mode" calls `SET_CAR_FORWARD_SPEED(car, speed × 1.02)` every frame while RT is held. It reads the speed from the game just before, so it never sets the car slower than it's going.
- Forward speed comes from `GET_CAR_SPEED_VECTOR(car, …, true)`, the car's own frame (Y forward, X sideways). Rockstar's own scripts use flag 0 for world space, and LotsofEds' downforce mod reads Y of the flag-1 vector.

Each frame ClaudeMenu works out the forward speed it wants:

- **Engine power:** `+ (0.0022 × speed + 0.03) × (multiplier − 1) × throttle` m/s per 30 fps frame. It eases in as you press RT and stops the moment you let go. Only while moving forward (1 m/s or faster), on the ground, with the handbrake released.
- **Nitro:** off by default (Performance → Nitro). `+0.25 / 0.4 / 0.7` m/s per 30 fps frame while you hold L3. The tank drains in about 3.5 s and refills after a short pause.
- **Brakes / handbrake:** `−0.15 / −0.12 × (multiplier − 1)` while braking (LT) or holding the handbrake (RB). Never below 0.3 m/s, so they can't push you into reverse.
- **Top speed limit:** the result is clamped to the limit.

Then it applies the change with the **Boost Method** (Performance → How It's Applied). If one does nothing on your setup, try another:

| Method | What it does | Based on |
|---|---|---|
| **Smooth** (default) | `SET_CAR_FORWARD_SPEED` every frame | the XMC trainer's NFS mode |
| **Pulses** | the change summed over 200 ms, then one `SET_CAR_FORWARD_SPEED` | ClaudeMenu 1.1.6 |
| **Force** | `APPLY_FORCE_TO_CAR(type 3, local, scaled by mass)` every frame | ScriptHookDotNet's `ApplyForceRelative`, LotsofEds' downforce mod |

Nothing is applied while the menu is open, in the air, while reversing or nearly stopped, or when you aren't the driver. With every setting at stock (the default) the performance code never touches the car. **Grip Assist** pushes against the sideways slide with `APPLY_FORCE_TO_CAR` and stays experimental, off by default.

**Why the earlier versions did nothing or pinned the car:** they scaled the boost by `GET_FRAME_TIME`. If that reads about 0 under Proton, a per-frame force does nothing and a per-frame speed write holds the car at its current speed, which matches both Deck results. 1.2.0 times frames with a high-resolution wall clock instead. **Live Debug** shows both values ("frame 17 ms (game says …)"), so the next Deck test will settle it.

Limits: it changes the car's speed along its nose, so gear ratios, engine sound and wheelspin don't change the way a real handling edit would. It works on cars and bikes (not helicopters or boats). It reads the throttle from the controller triggers, or W/S/Space on a keyboard without a controller. `ForceScale` under `[Performance]` in `ClaudeMenu.ini` (1.0 by default) scales every change. The first eight boosts after you open the Performance menu are logged as `Boost smooth: 20.00 -> 20.07 (power) m/s`.

### How Super Run and Super Jump work (experimental, untested on the Deck)

Deck tests showed `SET_CHAR_MOVE_ANIM_SPEED_MULTIPLIER` and on-ground `SET_CHAR_VELOCITY` freeze walking, and a single in-air velocity write is ignored. So neither is used. Instead, like the **XMC mod menu**, ClaudeMenu adds a small push with `APPLY_FORCE_TO_PED(player, type 1, …, component 1, local, local, scaled by mass)` every frame **while you hold the button**. It adds to the game's own movement instead of overwriting it.

- **Super Run:** a forward push while you hold sprint (A) and move the left stick, on the ground. **Run Boost** 1–10 (5 is XMC's value).
- **Super Jump:** an upward push for up to 0.6 s after you press jump (X), while you keep holding it. **Jump Boost** 1–10 (5 is XMC's value). You're invincible from take-off until a moment after you land, so the fall doesn't hurt.

Both are off by default and do nothing unless you hold the button. Neither does anything while the menu is open or while you're holding LB+RB (the menu combo). Each sprint and jump is logged, like `Super run: top speed 11.2 m/s (normal sprint is about 7)` and `Super jump: rose 4.5 m (a normal jump is about 1)`. That shows whether the push works on your setup.

### What GTA IV can't do (so ClaudeMenu doesn't pretend to)

- **Windows can't be rolled down or up**: there's no native for it. You can smash or remove them, and Repair restores them.
- **IV can't tell which extras a model has.** Extras 1–9 are always listed; the ones a model doesn't have do nothing.
- **Gravity is on or off**: there's no low-gravity setting.
- **Explosive hits** only work on people and vehicles you damage (IV has no bullet-impact native, so walls and the ground don't explode).
- **No animation-speed fast run.** `SET_CHAR_MOVE_ANIM_SPEED_MULTIPLIER` freezes the walk cycle on the Deck (even at 1.0), and writing the player's velocity on the ground freezes it too. Super Run and Super Jump push instead (see above).
- **Liveries** only exist on some models (taxis, police and a few others).
- **Player models and vehicles** that aren't in the episode you're playing are reported as unavailable instead of loading.

## Requirements

ClaudeMenu is a **ScriptHookDotNet** script. You need:

1. GTA IV on **patch 1.0.8.0 or 1.0.7.0** (or EFLC 1.1.3.0 / 1.1.2.0). The current Steam "Complete Edition" (1.2.x) has to be downgraded first. The usual tool is the **GTA IV Downgrader**. It can also install the ASI loader, ScriptHook, ScriptHookDotNet and FusionFix for you.
2. **ScriptHook** (the GTA IV C++ script hook) and **ScriptHookDotNet v1.7.x**
3. **.NET Framework 4.8** inside the game's Proton prefix (only needed on the Deck/Linux; see [Install on Steam Deck](#install-on-steam-deck)). Whether a Visual C++ runtime is also needed in the prefix hasn't been verified yet.

## Install on Steam Deck

This is the procedure that got ClaudeMenu 1.0.1 running on a real Steam Deck (SteamOS, Desktop Mode, GTA IV Complete Edition on the SD card, Protontricks from Discover).

Before you start:

- Do everything in **Desktop Mode** and use **Konsole** for the commands.
- **Paste one command at a time** and press Enter after each one. Konsole sometimes joins two pasted lines into one command, which fails in confusing ways.
- GTA IV's Steam app ID is **12210**. Every command below uses it.
- In the commands, `<uuid>` is your SD card's folder name. Find it with `ls /run/media/deck/`. If the game is on internal storage instead, its folder is `~/.local/share/Steam`.

### Quick way: the setup script

`tools/steamdeck-setup.sh` does steps 1 and 3–5 for you (including the registry and Windows-version fixes) and then prints the commands for steps 6 and 7. You still do step 2 by hand first. Download this repository (Code → Download ZIP), extract it, open Konsole in the extracted folder, and run:

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

5. **Install .NET Framework 4.8.** First remove the .NET registry keys Proton ships. Errors saying a key doesn't exist are fine. Paste this as one line:
   ```
   flatpak run com.github.Matoking.protontricks -c 'wine reg delete "HKLM\Software\Wow6432Node\Microsoft\NET Framework Setup\NDP" /f; wine reg delete "HKLM\Software\Wow6432Node\Microsoft\.NETFramework" /f' 12210
   ```
   Then install .NET. This takes a long time and can look stuck. Lots of Wine error lines are normal.
   ```
   flatpak run com.github.Matoking.protontricks 12210 -q -f dotnet48
   ```
   Check that it worked. This should print the file's path, not "No such file or directory":
   ```
   ls "$SD/steamapps/compatdata/12210/pfx/drive_c/windows/Microsoft.NET/Framework/v4.0.30319/clr.dll"
   ```
   Winetricks leaves the prefix set to Windows 7 after `dotnet48`, and the Downgrader complains about that. Set it back to Windows 10:
   ```
   flatpak run com.github.Matoking.protontricks 12210 -q win10
   ```

6. **Downgrade and mod the game.** The GTA IV Downgrader is a WPF app, and under DXVK WPF windows can come up black. Switching WPF to software rendering fixes that and doesn't affect the game. Paste this as one line:
   ```
   flatpak run com.github.Matoking.protontricks -c 'wine reg add "HKCU\Software\Microsoft\Avalon.Graphics" /v DisableHWAcceleration /t REG_DWORD /d 1 /f' 12210
   ```
   Download the [GTA IV Downgrader](https://github.com/ClonkAndre/GTAIVDowngrader) and run it inside the game's prefix. Paste this as one line, using the real path and file name of the `.exe` you downloaded:
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
  - deleting Proton's .NET registry keys on their own (winetricks issue #2367). Step 5 still does this before `dotnet48`, as a precaution.
  - running the `ndp48` installer directly (same cabinet error)
- **.NET stopped working after changing the Proton version (or after a Proton update).** Switching Proton may rebuild the symlinks in `drive_c/windows`. Set the game back to Proton 8.0 and run `tools/steamdeck-setup.sh` again (or repeat steps 4 and 5). If the prefix is still broken, `bash tools/steamdeck-setup.sh --reset-prefix` backs up your saves (from `pfx/drive_c/users/steamuser/Documents/Rockstar Games`) to `~/GTAIV-save-backups`, then empties the prefix; launch the game once on Proton 8.0 and run the script again.
- **The Downgrader complains about the Windows version.** Winetricks leaves the prefix on Windows 7 after `dotnet48`. Run `flatpak run com.github.Matoking.protontricks 12210 -q win10` (step 5; the setup script does it too).
- **The Downgrader (or another WPF tool) opens as a black window.** WPF's hardware rendering doesn't get along with DXVK. Turn it off with the `Avalon.Graphics` registry command in step 6 (the setup script does it too). It only affects WPF apps, not the game.
- **The Rockstar Games Launcher crashes with "CEF browser crash" on Proton 8.** Expected. The launch in step 2 is only needed to create the prefix.
- **`vcrun2019` fails** (`vc_redist.x86.exe /q` returned status 102). We skipped it; it isn't known to be needed. See the note at the end of [Install on Steam Deck](#install-on-steam-deck).
- **A command fails with a strange error, or two commands seem to run as one.** Konsole sometimes merges pasted lines. Paste one command at a time and check it before pressing Enter.

## Troubleshooting in game

- **The combo does nothing.** Look in `scripts/ClaudeMenu.log`. If there's no log, ScriptHookDotNet didn't load the script (check ScriptHookDotNet's own log, .NET in the prefix, and the launch options). If the log exists, open the menu with F4 and check **Settings → Controller Status**.
- **Some option does nothing.** If one game function fails, ClaudeMenu logs it, turns off only that function, and keeps running. Send the log lines.
- **The performance multipliers do nothing.** Turn on **Live Debug** (Performance or Misc) and drive. The CAR line shows what ClaudeMenu sees: whether you're the driver, RT/LT, forward speed, frame time, and what it applied. If it says `driver N` or `tunable N`, it won't touch that vehicle. If RT stays at 0.00 while you press it, it isn't seeing the trigger. If it shows boosts (`smooth: 20.00 -> 20.07 (power)`) but the car doesn't feel faster, switch **Boost Method** to Pulses or Force, or raise `ForceScale` in `ClaudeMenu.ini`. The log also records the first boosts as `Boost smooth: …` lines.
- **The car stops or won't pull away with a multiplier on.** Set **Boost Method** to Pulses or Force and tell us what Live Debug showed. Reset to Stock always gives you the normal car back.
- **Nitro does nothing.** It's off by default: turn on Performance → Nitro. It also needs you driving a car or bike that's moving forward, with the nitro button (L3 by default) held. Rebind it under **Settings → Rebind Nitro Button**.
- **Super Run / Super Jump do nothing.** Turn on **Live Debug** and check the FOOT line. `run btn` and `jump btn` should say Y while you hold sprint (A) and jump (X). If they don't, set your buttons under **Settings → Set Sprint Button / Set Jump Button**. If they do and it shows `run push` / `jump push` but nothing happens, raise **Run Boost / Jump Boost**. The log records each attempt (`Super run: top speed …`, `Super jump: rose … m`).
- **Walking feels wrong.** Turn Super Run off. With it off ClaudeMenu doesn't touch your movement at all, and if walking is still odd, send the log.
- **Free camera says it isn't working.** The camera couldn't be created in your setup; the log has the details. Everything else keeps working.
- **The text sits a bit off in its rows.** Change **Settings → Menu Size**.
- **The menu stays frozen after closing, or you can't move.** Open and close the menu again, or turn off "Freeze Player While Open".

## ClaudeMenu.ini

The menu writes this file itself (Settings → Save Settings Now, or automatically with "Save When Menu Closes"). You can also edit it with the game closed:

- `[Controls]`: `OpenCombo`, `CloseCombo`, `NitroButton`, `JumpButton`, `RunButton` (buttons joined with `+`), freeze-while-open, `CameraWhileOpen`, stick navigation, keyboard fallback
- `[Display]`: menu position, theme, size, rows shown, `Opacity` (40–100), `SaveOnClose`, `Speedometer`, `SpeedUnits` (`kmh` or `mph`)
- `[Performance]`: `BoostMethod` (`smooth`, `pulses` or `force`), `ForceScale`
- `[Teleport]`: saved slots and your own `Location=Name|X|Y|Z` lines
- `[Vehicles]`: one `Preset=` line per saved vehicle; the first field is its name, so you can rename presets here

## Layout

```
scripts/ClaudeMenu.cs      the whole trainer (one file, C# 4; regions per section)
scripts/ClaudeMenu.ini     settings, combos, saved teleports, vehicle presets
tools/shdn-stub/           compile-only stand-in for the ScriptHookDotNet API (CI only, never ship it)
tools/steamdeck-setup.sh   Steam Deck helper: prepares the Proton prefix and installs .NET
build.bat / build.sh       build ClaudeMenu.net.dll
```

## Notes for developers

These rules come from getting the script running on a real Deck. Please keep them:

- **One loose `scripts/ClaudeMenu.cs`, written in C# 4.** ScriptHookDotNet compiles it in-game and references only System, System.Drawing, System.Windows.Forms, System.Xml and ScriptHookDotNet. There's no System.Core, so no `HashSet` and no LINQ. `./build.sh --check` compiles with exactly that reference set (`-noconfig`).
- **Draw only in `PerFrameDrawing`, with `GTA.Graphics`.** Drawing natives called from `Tick` run but never show up. `Tick` runs the game logic and builds a `FrameView` snapshot; the drawing code only renders that snapshot.
- **Call natives only through `N.Call` / `N.Get`** (and the `N.OutInt` / `N.OutFloat` helpers). A failing native is logged once and switched off instead of crashing the script. Use GTA IV natives only, checked against the IV native list and the ScriptHookDotNet source.
- **Keep `tools/shdn-stub` honest.** When you use a new ScriptHookDotNet member, add it to the stub with the same name and signature as in ScriptHookDotNet 1.7.x, so the compile check stays meaningful.

## Credits and references

ClaudeMenu borrows *techniques*, not code, from GTA IV mods whose source is public. Thanks to their authors:

- **XMC mod menu** (EmmanuelU/xmc-mod-menu): super run and super jump with `APPLY_FORCE_TO_PED` while a button is held, the per-frame `SET_CAR_FORWARD_SPEED(speed × 1.02)` "NFS mode", and `SET_CAMERA_CONTROLS_DISABLED_WITH_PLAYER_CONTROLS` to keep the camera while the player is frozen.
- **ChaosIV** (bountifulellie/ChaosIV) and **TORISOUP's GTA4 Inferno scripts**: how much force it takes to move peds and cars in IV, re-applying a ped's velocity over several ticks, and using collision proofing for falls.
- **LotsofEds' IV mods** (MoveImproveIV-SDK, IVMouseWheelThrottle): per-frame forces on the player's car, and reading forward speed from `GET_CAR_SPEED_VECTOR(…, true)`.
- **Rockstar's decompiled mission scripts** (0x52617665/GTAIVMissionScripts): how the game itself calls `APPLY_FORCE_TO_CAR` and `GET_CAR_SPEED_VECTOR`.
- **HappinessMP's native docs** (HappinessMP/natives): the real argument names of `APPLY_FORCE_TO_CAR` / `APPLY_FORCE_TO_PED` (force, offset, component, local force, local offset, scale by mass).
- **ScriptHookDotNet** (Tomasak/gta4_scripthookdotnet) and **FusionFix**'s native table: the API and native signatures every call here is checked against.

