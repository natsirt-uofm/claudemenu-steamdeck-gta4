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
3. **.NET Framework 4.x** and the **Visual C++ runtimes** inside the game's Proton prefix (only needed on the Deck/Linux)

## Install on Steam Deck

Do this in Desktop Mode.

1. **Downgrade and mod the game.** Run the GTA IV Downgrader on the game folder. On the Deck you can run a Windows `.exe` inside GTA IV's prefix with `protontricks-launch --appid 12210 Downgrader.exe` (get Protontricks from Discover). Select the ASI loader, ScriptHook and ScriptHookDotNet when it offers them.
2. **Install the runtimes into the prefix:**
   ```
   protontricks 12210 dotnet48 vcrun2019
   ```
   ScriptHookDotNet does not load without .NET Framework.
3. **Copy ClaudeMenu.** Put `scripts/ClaudeMenu.cs` (or a built `ClaudeMenu.net.dll`, see below) and `scripts/ClaudeMenu.ini` into
   `~/.local/share/Steam/steamapps/common/Grand Theft Auto IV/GTAIV/scripts/`
   (that is the folder next to `GTAIV.exe`; create `scripts` if it doesn't exist).
4. **Set the Steam launch options** (Game → Properties → General) so Proton loads the mod DLLs:
   ```
   WINEDLLOVERRIDES="dinput8,xlive,dsound=n,b" %command%
   ```
   Only the DLLs your setup actually uses matter. The downgrader's readme says which ones they are.
5. **Controller layout:** keep Steam Input on a *Gamepad* template (the default), not a keyboard/mouse one.
6. Start the game, load a save, and press **LB + RB + X**.

## Install on Windows

Do steps 1 and 3 above, then start the game. Leave out Proton, protontricks and the launch options.

## Loose `.cs` or built `.dll`?

ScriptHookDotNet compiles loose `.cs` files in `scripts/` itself when the game starts, so `ClaudeMenu.cs` can be dropped in as it is. If that doesn't work in your setup (for example, no compiler in the Proton prefix), build a DLL against your real `ScriptHookDotNet.dll` and use that instead. **Don't use both at once.**

- Windows: `build.bat "C:\Games\GTA IV"`
- Linux/Deck (`mcs` from mono): `./build.sh "/path/to/GTAIV"`

`./build.sh --check` compiles the script against a small API stub (`tools/shdn-stub`), so you can check it builds without the game. CI runs this check on every push.

## Troubleshooting

- **The combo does nothing.** Look in `scripts/ClaudeMenu.log`. If there's no log, ScriptHookDotNet didn't load the script (check ScriptHookDotNet's own log, .NET in the prefix, and the launch options). If the log exists, open the menu with F4 and check **Settings → Controller Status**.
- **Some option does nothing.** If one game function fails, ClaudeMenu logs it, turns off only that function, and keeps running. Send the log lines.
- **The text sits a bit off in its rows.** Change **Settings → Menu Size**.
- **The menu stays frozen after closing, or you can't move.** Open and close the menu again, or turn off "Freeze Player While Open".

## Layout

```
scripts/ClaudeMenu.cs      the whole menu (one file; written in C# 4 because ScriptHookDotNet needs that)
scripts/ClaudeMenu.ini     settings, combos, saved teleports
tools/shdn-stub/           compile-only stand-in for the ScriptHookDotNet API (CI only, never ship it)
build.bat / build.sh       build ClaudeMenu.net.dll
```
