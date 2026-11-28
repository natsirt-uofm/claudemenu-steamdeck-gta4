#!/usr/bin/env bash
# steamdeck-setup.sh - prepare GTA IV's Proton prefix on a Steam Deck so the
# GTA IV Downgrader, ScriptHook and ScriptHookDotNet can run.
#
# Run it in Desktop Mode, from Konsole, with the game closed:
#     bash tools/steamdeck-setup.sh
#
# What it does. Each step checks first and is skipped if it's already done,
# so it is safe to run again:
#   1. Gives the Protontricks flatpak access to /run/media (SD cards) and home.
#   2. Finds the Steam library that holds GTA IV (from libraryfolders.vdf).
#   3. Prints free space on internal storage and on that library.
#   4. If the game is on another drive (SD card) but its Proton prefix was
#      created on internal storage, moves the prefix next to the game and
#      leaves a symlink in the old place.
#   5. Replaces the symlinks in pfx/drive_c/windows with real copies.
#      Without this, dotnet48 fails with "FDICopy failed".
#   6. Deletes Proton's own .NET registry keys (harmless if they're missing),
#      installs .NET Framework 4.8 with Protontricks and checks for clr.dll.
#   7. Sets the prefix to Windows 10 (winetricks leaves it on Windows 7 after
#      dotnet48, and the Downgrader complains) and turns off WPF hardware
#      acceleration, so WPF tools like the Downgrader don't draw a black window
#      under DXVK.
#   8. Prints the command for the GTA IV Downgrader. It does NOT run it.
#
# Options:
#   --dry-run        show what would be done, change nothing
#   --no-move        keep the prefix where it is (skip step 4)
#   --reset-prefix   back up saves, then empty the prefix so Steam recreates it
#                    on the next launch (asks first). Use it when a prefix is
#                    broken; afterwards launch the game once and run this again.
#   --yes            don't ask for confirmation (for --reset-prefix)
#   --appid N        Steam app ID (default 12210, GTA IV Complete Edition)
#   -h, --help       show this help
#
# Environment:
#   STEAM_ROOT       Steam folder, if it isn't ~/.local/share/Steam
#   BACKUP_DIR       where save backups go (default ~/GTAIV-save-backups)
#
# After step 4 you should keep the game on the same Proton version: switching
# Proton can rebuild the symlinks and break .NET. If that happens, run this
# script again.

set -euo pipefail

REPO_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
APPID=12210
PT_APP=com.github.Matoking.protontricks
DRY_RUN=0
NO_MOVE=0
RESET=0
ASSUME_YES=0

# Rough guide only: dotnet48 grows the prefix by a few GB once installed.
DOTNET_NEED_KB=$((3 * 1024 * 1024))
# The winetricks download cache (inside the flatpak, in your home) needs a
# few hundred MB.
CACHE_NEED_KB=$((500 * 1024))
# .NET 4.8 sets "Release" to 528040 or higher.
NET48_RELEASE=528040
# Run before dotnet48: removes the .NET keys Proton ships, which can confuse the
# installer. "reg delete" just complains if a key is already gone.
NET_KEYS_CMD='wine reg delete "HKLM\Software\Wow6432Node\Microsoft\NET Framework Setup\NDP" /f; wine reg delete "HKLM\Software\Wow6432Node\Microsoft\.NETFramework" /f'
# WPF apps (the GTA IV Downgrader is one) can render a black window under DXVK;
# software rendering for WPF fixes that and doesn't affect the game.
WPF_CMD='wine reg add "HKCU\Software\Microsoft\Avalon.Graphics" /v DisableHWAcceleration /t REG_DWORD /d 1 /f'

# ---------------------------------------------------------------- helpers ---
step() { printf '\n== %s\n' "$*"; }
ok()   { printf '   ok: %s\n' "$*"; }
did()  { (( DRY_RUN )) || ok "$@"; }   # report an action that run() performed
info() { printf '   %s\n' "$*"; }
warn() { printf '   WARNING: %s\n' "$*" >&2; }
die() {
    printf '\nERROR: %s\n' "$1" >&2
    shift
    local line
    for line in "$@"; do printf '       %s\n' "$line" >&2; done
    exit 1
}
# Run a command, or only print it with --dry-run.
run() {
    if (( DRY_RUN )); then
        printf '   [dry-run]'
        printf ' %q' "$@"
        printf '\n'
    else
        "$@"
    fi
}
usage() { sed -n '2,/^$/{s/^# \{0,1\}//;p}' "${BASH_SOURCE[0]}"; }
free_kb() { df -Pk -- "$1" | awk 'NR == 2 { print $4 }'; }
human_kb() { numfmt --to=iec --from-unit=1024 "$1" 2>/dev/null || printf '%s KB' "$1"; }
# Size in KB of a folder, following symlinks if $2 = L. Broken links are ignored.
size_kb() {
    local flags=-sk
    [[ ${2:-} == L ]] && flags=-skL
    { du "$flags" -- "$1" 2>/dev/null || true; } | awk 'END { print ($1 == "" ? 0 : $1) }'
}
space_line() { # label, path
    df -Ph -- "$2" | awk -v l="$1" 'NR == 2 { printf "   %-24s %s free of %s (%s used)  [%s]\n", l, $4, $2, $5, $6 }'
}
confirm() {
    (( ASSUME_YES )) && return 0
    local answer
    read -r -p "   $1 Type yes to continue: " answer
    [[ $answer == yes ]]
}
# /run/media/deck/x/y -> Z:\run\media\deck\x\y
win_path() { local p=$1; printf 'Z:%s' "${p//\//\\}"; }

# ------------------------------------------------------------------- args ---
while (( $# )); do
    case $1 in
        --dry-run) DRY_RUN=1 ;;
        --no-move) NO_MOVE=1 ;;
        --reset-prefix) RESET=1 ;;
        --yes|-y) ASSUME_YES=1 ;;
        --appid)
            [[ ${2:-} =~ ^[0-9]+$ ]] || die "--appid needs a number, e.g. --appid 12210"
            APPID=$2
            shift ;;
        -h|--help) usage; exit 0 ;;
        *) die "Unknown option: $1" "Run with --help to see the options." ;;
    esac
    shift
done

(( DRY_RUN )) && printf 'Dry run: nothing will be changed.\n'

# -------------------------------------------------------------- preflight ---
step "Checking requirements"
(( EUID != 0 )) || die "Don't run this with sudo or as root." \
    "Run it as the normal 'deck' user: bash $0"
command -v flatpak >/dev/null || die "flatpak not found. This script is meant for SteamOS / a Linux desktop with flatpak."
flatpak info "$PT_APP" >/dev/null 2>&1 || die "The Protontricks flatpak ($PT_APP) is not installed." \
    "Install 'Protontricks' from Discover, or run:" \
    "  flatpak install flathub $PT_APP"
ok "Protontricks flatpak is installed"

running=$(pgrep -u "$(id -u)" -a -f 'GTAIV\.exe|wineserver' || true)
if [[ -n $running ]]; then
    msg="GTA IV or another Wine/Proton program is still running:"
    if (( DRY_RUN )); then
        warn "$msg"; printf '%s\n' "$running" | sed 's/^/     /' >&2
    else
        die "$msg" "$running" "Quit the game (and any other game or Protontricks window) and try again."
    fi
else
    ok "GTA IV and Wine are not running"
fi

# ------------------------------------------------ 1. flatpak permissions ---
step "1. Protontricks access to SD cards and home"
overrides=$(flatpak override --user --show "$PT_APP" 2>/dev/null || true)
for fs in /run/media home; do
    if grep -Eq "^filesystems=(.*;)?${fs}(;|$)" <<<"$overrides"; then
        ok "already has --filesystem=$fs"
    else
        run flatpak override --user --filesystem="$fs" "$PT_APP"
        did "granted --filesystem=$fs"
    fi
done

# ---------------------------------------------------- 2. find the library ---
step "2. Finding the Steam library with app $APPID"
STEAM=""
for c in "${STEAM_ROOT:-}" "$HOME/.local/share/Steam" "$HOME/.steam/steam" \
         "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
    if [[ -n $c && -f $c/steamapps/libraryfolders.vdf ]]; then
        STEAM=$(readlink -f -- "$c")
        break
    fi
done
[[ -n $STEAM ]] || die "Couldn't find Steam's libraryfolders.vdf." \
    "Looked in ~/.local/share/Steam and ~/.steam/steam." \
    "If Steam is somewhere else, run: STEAM_ROOT=/path/to/Steam bash $0"
VDF=$STEAM/steamapps/libraryfolders.vdf
info "Steam: $STEAM"

# Print "<path><TAB><1 if the library's apps list has APPID, else 0>" per library.
list_libraries() {
    awk -v app="$APPID" '
        {
            line = $0; n = 0
            while (match(line, /"[^"]*"/)) {
                tok[++n] = substr(line, RSTART + 1, RLENGTH - 2)
                line = substr(line, RSTART + RLENGTH)
            }
            if (n == 2 && tok[1] == "path") {
                if (path != "") print path "\t" has
                path = tok[2]; has = 0
            } else if (n == 1 && tok[1] == "apps") {
                want_apps = 1
            } else if (n == 2 && in_apps && tok[1] == app) {
                has = 1
            }
            if ($0 ~ /\{/ && want_apps) { in_apps = 1; want_apps = 0 }
            else if ($0 ~ /\}/ && in_apps) { in_apps = 0 }
        }
        END { if (path != "") print path "\t" has }
    ' "$VDF"
}

LIB=""
fallback=""
while IFS=$'\t' read -r path has; do
    [[ -n $path ]] || continue
    if [[ ! -d $path ]]; then
        info "skipping a library Steam remembers but that isn't there: $path"
        continue
    fi
    if [[ -f $path/steamapps/appmanifest_$APPID.acf ]]; then
        if [[ $has == 1 && -z $LIB ]]; then
            LIB=$path
        elif [[ -z $fallback ]]; then
            fallback=$path
        fi
    fi
done < <(list_libraries)
[[ -n $LIB ]] || LIB=$fallback
[[ -n $LIB ]] || die "No Steam library has app $APPID installed." \
    "Checked every library in $VDF." \
    "Install GTA IV in Steam (and insert the SD card if it's on one), then run this again."
LIB=$(readlink -f -- "$LIB")

installdir=$(awk -F'"' '$2 == "installdir" { print $4; exit }' "$LIB/steamapps/appmanifest_$APPID.acf")
[[ -n $installdir ]] || die "Couldn't read installdir from $LIB/steamapps/appmanifest_$APPID.acf"
GAME_DIR=$LIB/steamapps/common/$installdir
[[ -d $GAME_DIR ]] || die "Steam says the game is in $GAME_DIR, but that folder doesn't exist." \
    "Verify the game files in Steam (Properties > Installed Files) and run this again."
if [[ -d $GAME_DIR/GTAIV ]]; then GTA_DIR=$GAME_DIR/GTAIV; else GTA_DIR=$GAME_DIR; fi
ok "library: $LIB"
ok "game:    $GTA_DIR"
[[ -f $GTA_DIR/GTAIV.exe ]] || warn "GTAIV.exe not found in $GTA_DIR"

# ------------------------------------------------------- 3. free space ---
step "3. Free space"
space_line "Internal (home):" "$HOME"
[[ $(df -P -- "$LIB" | awk 'NR==2{print $6}') == $(df -P -- "$HOME" | awk 'NR==2{print $6}') ]] \
    || space_line "Game library:" "$LIB"
home_free=$(free_kb "$HOME")
if (( home_free < CACHE_NEED_KB )); then
    warn "Only $(human_kb "$home_free") free on internal storage. Protontricks keeps its"
    warn "downloads in ~/.var/app/$PT_APP/cache, which needs a few hundred MB."
fi

# ------------------------------------------------ 4. locate / move prefix ---
step "4. Proton prefix location"
INTERNAL_CD=$STEAM/steamapps/compatdata/$APPID
LIB_CD=$LIB/steamapps/compatdata/$APPID
SAME_LIB=0
[[ $LIB == "$STEAM" ]] && SAME_LIB=1

backup_saves() { # $1 = compatdata/<appid> folder
    local src="$1/pfx/drive_c/users/steamuser/Documents/Rockstar Games"
    if [[ ! -d $src ]]; then
        info "no saves to back up (no Documents/Rockstar Games in the prefix)"
        return 0
    fi
    local root=${BACKUP_DIR:-$HOME/GTAIV-save-backups}
    local dest
    dest=$root/$(date +%Y%m%d-%H%M%S)
    local n=1 base=$dest
    while [[ -e $dest ]]; do dest=$base-$((n++)); done
    run mkdir -p -- "$root" || die "Couldn't create the backup folder $root"
    local need have
    need=$(size_kb "$src" L)
    if [[ -d $root ]]; then have=$(free_kb "$root"); else have=$(free_kb "$HOME"); fi
    (( have > need + 10240 )) || die "Not enough space to back up the saves ($(human_kb "$need") needed in $root)." \
        "Free some space or set BACKUP_DIR to a folder on the SD card."
    run mkdir -p -- "$dest" || die "Couldn't create $dest"
    run cp -RLp -- "$src" "$dest/" || die "Backing up the saves failed, so nothing was changed." \
        "Source: $src"
    did "saves backed up to $dest"
}

# Leave a symlink at INTERNAL_CD pointing at LIB_CD.
link_back() {
    run mkdir -p -- "$(dirname -- "$INTERNAL_CD")"
    run ln -sfn -- "$LIB_CD" "$INTERNAL_CD"
    did "symlinked $INTERNAL_CD -> $LIB_CD"
}

if (( SAME_LIB )); then
    PREFIX=$INTERNAL_CD
    [[ -d $PREFIX ]] && ok "the game is on internal storage, prefix is $PREFIX"
elif [[ -L $INTERNAL_CD ]]; then
    target=$(readlink -f -- "$INTERNAL_CD" || true)
    if [[ -z $target || ! -d $target ]]; then
        die "$INTERNAL_CD is a symlink to $(readlink -- "$INTERNAL_CD"), which doesn't exist." \
            "Is the SD card inserted? If it is, remove the broken link with:" \
            "  rm \"$INTERNAL_CD\"" \
            "then launch the game once and run this again."
    fi
    PREFIX=$target
    if [[ $target == "$(readlink -f -- "$LIB_CD" 2>/dev/null || true)" ]]; then
        ok "prefix is already next to the game: $LIB_CD (with a symlink on internal storage)"
    else
        warn "$INTERNAL_CD points to $target, not $LIB_CD. Leaving it as it is."
    fi
elif [[ -d $INTERNAL_CD && -d $LIB_CD ]]; then
    die "There are two prefixes for app $APPID, and it isn't clear which one Steam uses:" \
        "  $INTERNAL_CD" "  $LIB_CD" \
        "Keep the one with your saves and settings (look in pfx/drive_c/users/steamuser/Documents)," \
        "move the other one out of the way (e.g. rename it to $APPID.old), then run this again."
elif [[ -d $INTERNAL_CD ]]; then
    if (( NO_MOVE )); then
        PREFIX=$INTERNAL_CD
        info "--no-move: keeping the prefix on internal storage: $PREFIX"
    elif (( RESET )); then
        PREFIX=$INTERNAL_CD   # the reset below rebuilds it on the game's drive
    else
        need=$(size_kb "$INTERNAL_CD")
        have=$(free_kb "$LIB")
        info "the game is in $LIB but its prefix is on internal storage ($(human_kb "$need"))"
        (( have > need + 512 * 1024 )) || die "Not enough space in $LIB to move the prefix ($(human_kb "$need") needed, $(human_kb "$have") free)."
        backup_saves "$INTERNAL_CD"
        # Copy to a temporary name first, so an interrupted copy never looks finished.
        run rm -rf -- "$LIB_CD.partial"
        run mkdir -p -- "$(dirname -- "$LIB_CD")"
        info "copying the prefix to $LIB_CD (this can take a few minutes)..."
        run cp -a -- "$INTERNAL_CD" "$LIB_CD.partial" || {
            rm -rf -- "$LIB_CD.partial"
            die "Copying the prefix to the SD card failed. The original is untouched in $INTERNAL_CD."
        }
        run mv -- "$LIB_CD.partial" "$LIB_CD"
        run rm -rf -- "$INTERNAL_CD"
        link_back
        PREFIX=$LIB_CD
        if (( DRY_RUN )); then
            PREFIX=$INTERNAL_CD   # the move didn't happen; inspect the real one
            SPACE_AT=$LIB         # but check space where it would end up
        fi
    fi
elif [[ -d $LIB_CD ]]; then
    PREFIX=$LIB_CD
    ok "prefix is next to the game: $LIB_CD"
    (( NO_MOVE )) || link_back
else
    PREFIX=""
fi

if [[ -z ${PREFIX:-} || ! -d $PREFIX ]]; then
    die "GTA IV has no Proton prefix yet." \
        "In Steam: GTA IV > Properties > Compatibility > force Proton 8.0," \
        "launch the game once (a Rockstar Launcher crash is fine), quit, then run this again."
fi

proton_version=""
[[ -f $PREFIX/version ]] && proton_version=$(head -n1 -- "$PREFIX/version")
if [[ -z $proton_version ]]; then
    info "Proton version of the prefix: unknown"
elif [[ $proton_version == 8.* ]]; then
    ok "prefix was set up by Proton $proton_version"
else
    warn "the prefix was set up by Proton $proton_version. This procedure was tested with Proton 8.0."
    warn "Set Proton 8.0 in Steam, launch once, quit, and run this again (or use --reset-prefix)."
fi

# --------------------------------------------------------- reset prefix ---
if (( RESET )); then
    step "Resetting the prefix"
    info "This deletes everything in $PREFIX"
    info "(the Wine prefix with .NET etc.). Saves are backed up first."
    if (( ! DRY_RUN )); then
        confirm "Delete the prefix?" || die "Cancelled. Nothing was deleted."
    fi
    backup_saves "$PREFIX"
    if (( ! SAME_LIB && ! NO_MOVE )) && [[ $PREFIX == "$INTERNAL_CD" ]]; then
        # Prefix was on internal storage: recreate it empty next to the game instead.
        run rm -rf -- "$INTERNAL_CD"
        run mkdir -p -- "$LIB_CD"
        link_back
    else
        run find "$PREFIX" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
    fi
    did "prefix emptied"
    printf '\nNext: launch GTA IV once on Proton 8.0, quit, then run this script again.\n'
    printf 'If anything about the new prefix looks wrong, the next run will say so.\n'
    exit 0
fi

PFX=$PREFIX/pfx
WIN=$PFX/drive_c/windows
[[ -d $WIN ]] || die "$WIN doesn't exist; the prefix looks incomplete." \
    "Launch the game once on Proton 8.0 and quit, then run this again." \
    "If that doesn't help: bash $0 --reset-prefix"

# --------------------------------------------- 5. real files, not links ---
step "5. Replacing symlinks in drive_c/windows with real files"
nlinks=$(find "$WIN" -type l | wc -l)
if (( nlinks == 0 )); then
    ok "no symlinks left in drive_c/windows"
    for leftover in "$PFX/drive_c/windows.deref" "$PFX/drive_c/windows.symlinks"; do
        [[ -e $leftover ]] && run rm -rf -- "$leftover"
    done
else
    info "$nlinks symlinks into Proton's read-only files found"
    need=$(size_kb "$WIN" L)
    have=$(free_kb "${SPACE_AT:-$PFX/drive_c}")
    info "copying needs about $(human_kb "$need"); $(human_kb "$have") free"
    (( have > need + DOTNET_NEED_KB )) || die "Not enough space next to the prefix for the copy plus .NET." \
        "Need roughly $(human_kb $(( need + DOTNET_NEED_KB ))), have $(human_kb "$have")." \
        "If the prefix is on internal storage, put the game on the SD card and run without --no-move."
    if (( DRY_RUN )); then
        info "[dry-run] would copy windows -> windows.deref with cp -RL, then swap the folders"
    else
        cd -- "$PFX/drive_c"
        rm -rf -- windows.deref windows.symlinks
        dangling=$(find windows -xtype l | wc -l)
        errlog=$(mktemp)
        info "copying (this can take a few minutes)..."
        if ! cp -RL windows windows.deref 2>"$errlog"; then
            nerr=$(grep -c . "$errlog" || true)
            nother=$(grep -vc 'No such file or directory' "$errlog" || true)
            if (( nother > 0 || nerr > dangling )); then
                rm -rf -- windows.deref
                die "Copying drive_c/windows failed; the prefix is unchanged. First errors:" \
                    "$(head -n 5 -- "$errlog")"
            fi
            warn "skipped $nerr broken symlinks (they point at files that don't exist)"
        fi
        rm -f -- "$errlog"
        mv -- windows windows.symlinks
        mv -- windows.deref windows
        rm -rf -- windows.symlinks
        left=$(find windows -type l | wc -l)
        (( left == 0 )) || die "$left symlinks are still in drive_c/windows after the copy."
        ok "drive_c/windows now holds real files"
    fi
fi

# ------------------------------------------------------- 6. .NET 4.8 ---
step "6. .NET Framework 4.8"
CLR=$WIN/Microsoft.NET/Framework/v4.0.30319/clr.dll

# Highest .NET 4 "Release" value in the prefix's registry, as a decimal number (0 if none).
net_release() {
    local reg=$PFX/system.reg hex best=0 v
    [[ -f $reg ]] || { echo 0; return; }
    while read -r hex; do
        v=$((16#$hex))
        (( v > best )) && best=$v
    done < <(awk '
        /^\[/ { in_full = (index($0, "NET Framework Setup\\\\NDP\\\\v4\\\\Full]") > 0) }
        in_full && /^"Release"=dword:/ { sub(/^"Release"=dword:/, ""); print }
    ' "$reg")
    echo "$best"
}

dotnet_ok() { [[ -f $CLR ]] && (( $(net_release) >= NET48_RELEASE )); }

if dotnet_ok; then
    ok "already installed (clr.dll present, registry Release=$(net_release))"
else
    if [[ -f $CLR ]]; then
        info "clr.dll exists but the registry doesn't show .NET 4.8 (Release=$(net_release)); reinstalling"
    fi
    if (( DRY_RUN )); then
        run flatpak run "$PT_APP" -c "$NET_KEYS_CMD" "$APPID"
        run flatpak run "$PT_APP" "$APPID" -q -f dotnet48
    else
        info "removing Proton's .NET registry keys first (errors about missing keys are fine)"
        flatpak run "$PT_APP" -c "$NET_KEYS_CMD" "$APPID" || true
        info "running: flatpak run $PT_APP $APPID -q -f dotnet48"
        info "This takes a while and can look stuck. Wine error lines are normal."
        pt_status=0
        flatpak run "$PT_APP" "$APPID" -q -f dotnet48 || pt_status=$?
        if dotnet_ok; then
            ok ".NET 4.8 installed: $CLR"
            (( pt_status == 0 )) || info "(Protontricks exited with status $pt_status, but .NET 4.8 is in place)"
        else
            die ".NET 4.8 did not install (Protontricks exit status $pt_status)." \
                "clr.dll: $( [[ -f $CLR ]] && echo present || echo missing ), registry Release: $(net_release)" \
                "If the output shows 'FDICopy failed' or 'Failed to extract cabinet'," \
                "check that step 5 finished, then run this script again." \
                "The .NET installer's own logs are .html/.txt files in the prefix's Temp folder:" \
                "  $PFX/drive_c/users/steamuser/AppData/Local/Temp" \
                "See 'Troubleshooting the Steam Deck setup' in README.md."
        fi
    fi
fi

# ------------------------------------- 7. Windows 10 + WPF software mode ---
step "7. Windows version and WPF rendering"

# True if a .reg file has a value line under a section whose name ends with $2.
reg_has() { # file, section suffix (as written in the .reg file), value line prefix
    [[ -f $1 ]] || return 1
    # awk -v turns \\ into \, so double every backslash to match the file literally.
    local sec=${2//\\/\\\\} val=${3//\\/\\\\}
    awk -v sec="$sec]" -v val="$val" '
        /^\[/ { in_sec = (index($0, sec) > 0) }
        in_sec && index($0, val) == 1 { found = 1 }
        END { exit found ? 0 : 1 }
    ' "$1"
}

if reg_has "$PFX/system.reg" 'Windows NT\\CurrentVersion' '"ProductName"="Microsoft Windows 10'; then
    ok "prefix already reports Windows 10"
elif (( DRY_RUN )); then
    run flatpak run "$PT_APP" "$APPID" -q win10
else
    info "setting the prefix to Windows 10 (the Downgrader complains about Windows 7)"
    flatpak run "$PT_APP" "$APPID" -q win10 || warn "protontricks win10 returned an error; check the output above"
    if reg_has "$PFX/system.reg" 'Windows NT\\CurrentVersion' '"ProductName"="Microsoft Windows 10'; then
        ok "prefix now reports Windows 10"
    else
        warn "couldn't confirm Windows 10 in the registry. Run: flatpak run $PT_APP $APPID -q win10"
    fi
fi

if reg_has "$PFX/user.reg" 'Microsoft\\Avalon.Graphics' '"DisableHWAcceleration"=dword:00000001'; then
    ok "WPF hardware acceleration already off"
elif (( DRY_RUN )); then
    run flatpak run "$PT_APP" -c "$WPF_CMD" "$APPID"
else
    flatpak run "$PT_APP" -c "$WPF_CMD" "$APPID" || warn "couldn't set the WPF registry value; see README troubleshooting"
    ok "WPF apps will use software rendering (fixes black Downgrader windows under DXVK)"
fi

# ------------------------------------------------------- 8. next steps ---
step "8. Next steps (not run by this script)"
downgrader=""
if [[ -d $HOME/Downloads ]]; then
    downgrader=$(find "$HOME/Downloads" -maxdepth 3 -type f -iname '*downgrader*.exe' 2>/dev/null | head -n 1 || true)
fi
[[ -n $downgrader ]] || downgrader="/path/to/GTAIVDowngrader.exe"

cat <<EOF

Run the GTA IV Downgrader (https://github.com/ClonkAndre/GTAIVDowngrader)
inside the game's prefix. This step has not been tested on a Deck yet.
Paste this as ONE line:

  flatpak run --command=protontricks-launch $PT_APP --appid $APPID "$downgrader"

In the Downgrader:
  - game folder: $(win_path "$GTA_DIR")
  - version:     1.0.8.0
  - tick:        ASI loader, ScriptHook, ScriptHookDotNet

Then copy ClaudeMenu into the game (one command at a time):

  mkdir -p "$GTA_DIR/scripts"
  cp "$REPO_DIR/scripts/ClaudeMenu.cs" "$REPO_DIR/scripts/ClaudeMenu.ini" "$GTA_DIR/scripts/"

Steam > GTA IV > Properties > General > Launch Options:

  WINEDLLOVERRIDES="dinput8,xlive,dsound=n,b" %command%

Keep GTA IV on Proton ${proton_version:-8.0}. Switching Proton can undo step 5;
if that happens, run this script again.
EOF
