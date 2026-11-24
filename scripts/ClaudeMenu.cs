// =============================================================================
//  ClaudeMenu - a fully controller-driven trainer / mod menu for GTA IV
//  Built for Steam Deck (Proton + Steam Input) and any XInput gamepad.
//
//  Runtime : ScriptHookDotNet (v1.7.x) for GTA IV / EFLC
//  Install : drop this file (or the compiled ClaudeMenu.net.dll) and
//            ClaudeMenu.ini into <GTA IV folder>\scripts\
//
//  Default controls
//    Open menu ........ LB + RB + X   (hold LB+RB, then press X)
//    Close menu ....... LB + RB + B   (or B on the main page)
//    Move ............. D-pad Up/Down (or left stick)  - hold to scroll
//    Page up/down ..... LT / RT
//    Select / toggle .. A
//    Change value ..... D-pad Left/Right (or left stick)
//    Back ............. B
//  Every binding can be changed in-game (Settings > Rebind) or in the INI.
//
//  NOTE: this file is intentionally a single source file written in C# 4
//  syntax, because ScriptHookDotNet compiles each loose .cs file in the
//  scripts folder on its own with the .NET 4 compiler.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GTA;
using GTA.Native;

namespace ClaudeMenu
{
    // -------------------------------------------------------------------------
    //  Button bit flags. The low 16 bits are the real XInput flags; the high
    //  bits are "virtual" buttons (triggers, stick directions, keyboard).
    // -------------------------------------------------------------------------
    internal static class Btn
    {
        public const uint Up = 0x0001;
        public const uint Down = 0x0002;
        public const uint Left = 0x0004;
        public const uint Right = 0x0008;
        public const uint Start = 0x0010;
        public const uint Back = 0x0020;
        public const uint LS = 0x0040;
        public const uint RS = 0x0080;
        public const uint LB = 0x0100;
        public const uint RB = 0x0200;
        public const uint A = 0x1000;
        public const uint B = 0x2000;
        public const uint X = 0x4000;
        public const uint Y = 0x8000;
        public const uint LT = 0x00010000;
        public const uint RT = 0x00020000;

        // Navigation-only virtual inputs (never used in combos)
        public const uint NavUp = 0x00100000;
        public const uint NavDown = 0x00200000;
        public const uint NavLeft = 0x00400000;
        public const uint NavRight = 0x00800000;
        public const uint NavAccept = 0x01000000;
        public const uint NavBack = 0x02000000;
        public const uint NavToggle = 0x04000000;   // keyboard open/close key

        public const uint ComboMask = 0x0003FFFF;

        private static readonly string[] Names = { "UP", "DOWN", "LEFT", "RIGHT", "START", "BACK", "L3", "R3", "LB", "RB", "", "", "A", "B", "X", "Y", "LT", "RT" };

        public static uint Parse(string text, uint fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            uint mask = 0;
            string[] parts = text.ToUpperInvariant().Split(new char[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                switch (p)
                {
                    case "DPADUP": case "DUP": p = "UP"; break;
                    case "DPADDOWN": case "DDOWN": p = "DOWN"; break;
                    case "DPADLEFT": case "DLEFT": p = "LEFT"; break;
                    case "DPADRIGHT": case "DRIGHT": p = "RIGHT"; break;
                    case "MENU": case "OPTIONS": p = "START"; break;
                    case "SELECT": case "VIEW": case "SHARE": p = "BACK"; break;
                    case "LS": case "LSB": case "LTHUMB": p = "L3"; break;
                    case "RS": case "RSB": case "RTHUMB": p = "R3"; break;
                    case "L1": p = "LB"; break;
                    case "R1": p = "RB"; break;
                    case "L2": p = "LT"; break;
                    case "R2": p = "RT"; break;
                }
                int idx = Array.IndexOf(Names, p);
                if (idx < 0 || Names[idx].Length == 0) return fallback;
                mask |= (1u << idx);
            }
            return mask == 0 ? fallback : mask;
        }

        public static string ToText(uint mask)
        {
            // Show shoulder/trigger buttons first, the way people say combos.
            int[] order = { 8, 9, 16, 17, 6, 7, 4, 5, 0, 1, 2, 3, 12, 13, 14, 15 };
            List<string> parts = new List<string>();
            foreach (int i in order)
                if ((mask & (1u << i)) != 0) parts.Add(Names[i]);
            return parts.Count == 0 ? "(none)" : string.Join("+", parts.ToArray());
        }
    }

    // -------------------------------------------------------------------------
    //  XInput reader. Under Proton, Steam Input exposes the Steam Deck's
    //  built-in controls as an Xbox 360 pad, so reading XInput directly is the
    //  most reliable way to get raw button state, independent of game bindings.
    // -------------------------------------------------------------------------
    internal static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Gamepad
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct State
        {
            public uint dwPacketNumber;
            public Gamepad Pad;
        }

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState13(uint index, out State state);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState14(uint index, out State state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint GetState910(uint index, out State state);

        private static int dllChoice = 0;   // 0 = 1_3, 1 = 1_4, 2 = 9_1_0, 3 = none

        public static bool Available { get { return dllChoice < 3; } }

        public static bool TryGetState(uint index, out State state)
        {
            state = new State();
            while (dllChoice < 3)
            {
                try
                {
                    uint r;
                    if (dllChoice == 0) r = GetState13(index, out state);
                    else if (dllChoice == 1) r = GetState14(index, out state);
                    else r = GetState910(index, out state);
                    return r == 0;
                }
                catch (DllNotFoundException) { dllChoice++; }
                catch (EntryPointNotFoundException) { dllChoice++; }
            }
            return false;
        }
    }

    internal static class Keyboard
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static bool broken;

        public static bool IsDown(int vk)
        {
            if (broken || vk <= 0) return false;
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch (Exception) { broken = true; return false; }
        }
    }

    // -------------------------------------------------------------------------
    //  Input state with edge detection and hold-to-repeat.
    // -------------------------------------------------------------------------
    internal sealed class InputState
    {
        public uint Held;
        public uint Prev;

        private int padIndex = -1;
        private int nextScan;
        private readonly Dictionary<uint, int> repeatAt = new Dictionary<uint, int>();

        public int RepeatDelay = 350;
        public int RepeatRate = 70;
        public bool StickNavigation = true;
        public bool KeyboardEnabled = true;
        public int KeyToggle = 0x73;   // F4
        public bool Connected { get { return padIndex >= 0; } }

        public void Update(int now)
        {
            Prev = Held;
            uint held = 0;

            XInput.State st;
            if (padIndex >= 0)
            {
                if (!XInput.TryGetState((uint)padIndex, out st)) padIndex = -1;
                else held = ReadPad(st);
            }
            if (padIndex < 0 && now >= nextScan)
            {
                nextScan = now + 1000;
                for (uint i = 0; i < 4; i++)
                {
                    if (XInput.TryGetState(i, out st)) { padIndex = (int)i; held = ReadPad(st); break; }
                }
            }

            // Copy real buttons onto the navigation virtual buttons.
            if ((held & Btn.Up) != 0) held |= Btn.NavUp;
            if ((held & Btn.Down) != 0) held |= Btn.NavDown;
            if ((held & Btn.Left) != 0) held |= Btn.NavLeft;
            if ((held & Btn.Right) != 0) held |= Btn.NavRight;
            if ((held & Btn.A) != 0) held |= Btn.NavAccept;
            if ((held & Btn.B) != 0) held |= Btn.NavBack;

            if (KeyboardEnabled)
            {
                if (Keyboard.IsDown(0x26) || Keyboard.IsDown(0x68)) held |= Btn.NavUp;      // Up / Num8
                if (Keyboard.IsDown(0x28) || Keyboard.IsDown(0x62)) held |= Btn.NavDown;    // Down / Num2
                if (Keyboard.IsDown(0x25) || Keyboard.IsDown(0x64)) held |= Btn.NavLeft;    // Left / Num4
                if (Keyboard.IsDown(0x27) || Keyboard.IsDown(0x66)) held |= Btn.NavRight;   // Right / Num6
                if (Keyboard.IsDown(0x0D) || Keyboard.IsDown(0x65)) held |= Btn.NavAccept;  // Enter / Num5
                if (Keyboard.IsDown(0x08) || Keyboard.IsDown(0x60)) held |= Btn.NavBack;    // Backspace / Num0
                if (Keyboard.IsDown(KeyToggle)) held |= Btn.NavToggle;
            }

            Held = held;
        }

        private uint ReadPad(XInput.State st)
        {
            uint held = st.Pad.wButtons;
            if (st.Pad.bLeftTrigger > 60) held |= Btn.LT;
            if (st.Pad.bRightTrigger > 60) held |= Btn.RT;
            if (StickNavigation)
            {
                const int dz = 20000;
                if (st.Pad.sThumbLY > dz) held |= Btn.NavUp;
                if (st.Pad.sThumbLY < -dz) held |= Btn.NavDown;
                if (st.Pad.sThumbLX < -dz) held |= Btn.NavLeft;
                if (st.Pad.sThumbLX > dz) held |= Btn.NavRight;
            }
            return held;
        }

        public bool IsHeld(uint b) { return (Held & b) != 0; }
        public bool Pressed(uint b) { return (Held & b) != 0 && (Prev & b) == 0; }
        public bool AnyComboHeld { get { return (Held & Btn.ComboMask) != 0; } }

        /// <summary>True on the frame a whole combination becomes held.</summary>
        public bool ComboPressed(uint mask)
        {
            if (mask == 0) return false;
            return (Held & mask) == mask && (Prev & mask) != mask;
        }

        public bool ComboHeld(uint mask) { return mask != 0 && (Held & mask) == mask; }

        /// <summary>Pressed, then auto-repeats while held.</summary>
        public bool Repeat(uint b, int now)
        {
            if ((Held & b) == 0) { repeatAt.Remove(b); return false; }
            if ((Prev & b) == 0) { repeatAt[b] = now + RepeatDelay; return true; }
            int at;
            if (repeatAt.TryGetValue(b, out at) && now >= at) { repeatAt[b] = now + RepeatRate; return true; }
            return false;
        }
    }

    // -------------------------------------------------------------------------
    //  Safe native wrapper: a bad native name or argument never kills the
    //  script; it is logged once and then skipped.
    // -------------------------------------------------------------------------
    internal static class N
    {
        private static readonly HashSet<string> broken = new HashSet<string>();

        public static bool Call(string name, params Parameter[] args)
        {
            if (broken.Contains(name)) return false;
            try { Function.Call(name, args); return true; }
            catch (Exception ex) { Fail(name, ex); return false; }
        }

        public static T Get<T>(string name, T fallback, params Parameter[] args)
        {
            if (broken.Contains(name)) return fallback;
            try { return Function.Call<T>(name, args); }
            catch (Exception ex) { Fail(name, ex); return fallback; }
        }

        public static bool IsBroken(string name) { return broken.Contains(name); }

        private static void Fail(string name, Exception ex)
        {
            broken.Add(name);
            Log.Write("Native " + name + " failed and was disabled: " + ex.Message);
        }
    }

    internal static class Log
    {
        public static string PathName;

        public static void Write(string msg)
        {
            try
            {
                if (PathName == null) return;
                File.AppendAllText(PathName, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + msg + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }

    // -------------------------------------------------------------------------
    //  Configuration (ClaudeMenu.ini)
    // -------------------------------------------------------------------------
    internal sealed class Config
    {
        public uint OpenCombo = Btn.LB | Btn.RB | Btn.X;
        public uint CloseCombo = Btn.LB | Btn.RB | Btn.B;
        public bool FreezePlayer = true;
        public bool StickNavigation = true;
        public bool KeyboardFallback = true;
        public int KeyToggle = 0x73;
        public int Position = 0;       // 0 = left, 1 = center, 2 = right
        public int Theme = 0;
        public float Scale = 1.0f;
        public int MaxVisible = 11;
        public bool InfiniteSprint = false;
        public string[] Slots = new string[6];
        public List<string> Locations = new List<string>();

        public string FilePath;

        public void Load(string path)
        {
            FilePath = path;
            if (!File.Exists(path)) { Save(); return; }
            try
            {
                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "opencombo": OpenCombo = Btn.Parse(val, OpenCombo); break;
                        case "closecombo": CloseCombo = Btn.Parse(val, CloseCombo); break;
                        case "freezeplayerwhileopen": FreezePlayer = ParseBool(val, FreezePlayer); break;
                        case "sticknavigation": StickNavigation = ParseBool(val, StickNavigation); break;
                        case "keyboardfallback": KeyboardFallback = ParseBool(val, KeyboardFallback); break;
                        case "keyboardtogglekey": KeyToggle = ParseInt(val, KeyToggle); break;
                        case "position": Position = Clamp(ParseInt(val, Position), 0, 2); break;
                        case "theme": Theme = Clamp(ParseInt(val, Theme), 0, Themes.Count - 1); break;
                        case "scale": Scale = Math.Max(0.6f, Math.Min(1.6f, ParseFloat(val, Scale))); break;
                        case "maxvisibleitems": MaxVisible = Clamp(ParseInt(val, MaxVisible), 4, 18); break;
                        case "location": Locations.Add(val); break;
                        default:
                            if (key.StartsWith("slot"))
                            {
                                int n;
                                if (int.TryParse(key.Substring(4), out n) && n >= 1 && n <= Slots.Length) Slots[n - 1] = val;
                            }
                            break;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Config load failed: " + ex.Message); }
        }

        public bool Save()
        {
            if (FilePath == null) return false;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; ClaudeMenu settings - edit with the game closed, or use Settings in the menu.");
                sb.AppendLine("; Buttons: A B X Y LB RB LT RT START BACK L3 R3 UP DOWN LEFT RIGHT (join with +)");
                sb.AppendLine("[Controls]");
                sb.AppendLine("OpenCombo=" + Btn.ToText(OpenCombo));
                sb.AppendLine("CloseCombo=" + Btn.ToText(CloseCombo));
                sb.AppendLine("FreezePlayerWhileOpen=" + (FreezePlayer ? "true" : "false"));
                sb.AppendLine("StickNavigation=" + (StickNavigation ? "true" : "false"));
                sb.AppendLine("; Keyboard: arrows/Enter/Backspace navigate, toggle key is a Windows virtual-key code (115 = F4)");
                sb.AppendLine("KeyboardFallback=" + (KeyboardFallback ? "true" : "false"));
                sb.AppendLine("KeyboardToggleKey=" + KeyToggle.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("[Display]");
                sb.AppendLine("; Position: 0 = left, 1 = center, 2 = right");
                sb.AppendLine("Position=" + Position.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Theme=" + Theme.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Scale=" + Scale.ToString("0.00", CultureInfo.InvariantCulture));
                sb.AppendLine("MaxVisibleItems=" + MaxVisible.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
                sb.AppendLine("[Teleport]");
                sb.AppendLine("; Saved slots are written by the menu (Teleport > Save position).");
                for (int i = 0; i < Slots.Length; i++)
                    if (!string.IsNullOrEmpty(Slots[i])) sb.AppendLine("Slot" + (i + 1) + "=" + Slots[i]);
                sb.AppendLine("; Add your own named locations, one per line:  Location=Name|X|Y|Z");
                foreach (string loc in Locations) sb.AppendLine("Location=" + loc);
                File.WriteAllText(FilePath, sb.ToString());
                return true;
            }
            catch (Exception ex) { Log.Write("Config save failed: " + ex.Message); return false; }
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        private static bool ParseBool(string s, bool fallback)
        {
            s = s.ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
            if (s == "false" || s == "0" || s == "no" || s == "off") return false;
            return fallback;
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public static float ParseFloat(string s, float fallback)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }

    internal struct Col
    {
        public byte R, G, B, A;
        public static Col FromArgb(int a, int r, int g, int b)
        {
            Col c; c.A = (byte)a; c.R = (byte)r; c.G = (byte)g; c.B = (byte)b; return c;
        }
        public static Col White { get { return FromArgb(255, 255, 255, 255); } }
    }

    internal sealed class Theme
    {
        public string Name;
        public Col Accent;
        public Col Header;
        public Theme(string name, Col accent, Col header) { Name = name; Accent = accent; Header = header; }
    }

    internal static class Themes
    {
        public static readonly List<Theme> All = new List<Theme>
        {
            new Theme("Liberty Blue", Col.FromArgb(230, 52, 120, 220), Col.FromArgb(240, 24, 64, 130)),
            new Theme("Niko Green", Col.FromArgb(230, 70, 160, 80), Col.FromArgb(240, 30, 90, 40)),
            new Theme("Crimson", Col.FromArgb(230, 200, 50, 60), Col.FromArgb(240, 110, 20, 30)),
            new Theme("Taxi Yellow", Col.FromArgb(230, 230, 180, 40), Col.FromArgb(240, 140, 100, 10)),
            new Theme("Purple", Col.FromArgb(230, 140, 80, 210), Col.FromArgb(240, 70, 30, 120)),
            new Theme("Mono", Col.FromArgb(230, 150, 150, 150), Col.FromArgb(240, 50, 50, 50)),
        };
        public static int Count { get { return All.Count; } }
    }

    // -------------------------------------------------------------------------
    //  Menu model
    // -------------------------------------------------------------------------
    internal abstract class MenuItem
    {
        public string Label;
        public string Help;
        public virtual string Value { get { return null; } }
        public virtual bool Adjustable { get { return false; } }
        public virtual void Activate(MenuScript m) { }
        public virtual void Adjust(MenuScript m, int dir) { }
    }

    internal sealed class ActionItem : MenuItem
    {
        private readonly Action action;
        public ActionItem(string label, Action action, string help) { Label = label; this.action = action; Help = help; }
        public ActionItem(string label, Action action) : this(label, action, null) { }
        public override void Activate(MenuScript m) { action(); }
    }

    internal sealed class ToggleItem : MenuItem
    {
        private readonly Func<bool> get;
        private readonly Action<bool> set;
        public ToggleItem(string label, Func<bool> get, Action<bool> set, string help) { Label = label; this.get = get; this.set = set; Help = help; }
        public ToggleItem(string label, Func<bool> get, Action<bool> set) : this(label, get, set, null) { }
        public override string Value { get { return get() ? "[ON]" : "[OFF]"; } }
        public override bool Adjustable { get { return true; } }
        public override void Activate(MenuScript m) { set(!get()); }
        public override void Adjust(MenuScript m, int dir) { set(dir > 0); }
    }

    internal sealed class SubmenuItem : MenuItem
    {
        public readonly Menu Target;
        public SubmenuItem(Menu target, string help) { Target = target; Label = target.Title; Help = help; }
        public SubmenuItem(Menu target) : this(target, null) { }
        public override string Value { get { return ">"; } }
        public override void Activate(MenuScript m) { m.Push(Target); }
    }

    /// <summary>Cycles through options with Left/Right; A applies (or applies live).</summary>
    internal sealed class ListItem : MenuItem
    {
        private readonly string[] options;
        private readonly Action<int> apply;
        private readonly bool live;
        private readonly Func<int> current;
        public int Index;

        public ListItem(string label, string[] options, Func<int> current, Action<int> apply, bool live, string help)
        {
            Label = label; this.options = options; this.current = current; this.apply = apply; this.live = live; Help = help;
            if (current != null) Index = current();
        }

        public void Sync() { if (current != null) Index = current(); }
        public override string Value { get { return "< " + options[Index] + " >"; } }
        public override bool Adjustable { get { return true; } }
        public override void Adjust(MenuScript m, int dir)
        {
            Index = (Index + dir + options.Length) % options.Length;
            if (live) apply(Index);
        }
        public override void Activate(MenuScript m) { apply(Index); }
    }

    internal sealed class NumberItem : MenuItem
    {
        private readonly int min, max, step;
        private readonly Func<int> current;
        private readonly Action<int> apply;
        private readonly bool live;
        private readonly string suffix;
        public int Number;

        public NumberItem(string label, int min, int max, int step, Func<int> current, Action<int> apply, bool live, string suffix, string help)
        {
            Label = label; this.min = min; this.max = max; this.step = step; this.current = current; this.apply = apply; this.live = live; this.suffix = suffix ?? ""; Help = help;
            Number = current != null ? current() : min;
        }

        public void Sync() { if (current != null) Number = current(); }
        public override string Value { get { return "< " + Number.ToString(CultureInfo.InvariantCulture) + suffix + " >"; } }
        public override bool Adjustable { get { return true; } }
        public override void Adjust(MenuScript m, int dir)
        {
            Number += dir * step;
            if (Number > max) Number = min;
            if (Number < min) Number = max;
            if (live) apply(Number);
        }
        public override void Activate(MenuScript m) { apply(Number); }
    }

    internal sealed class Menu
    {
        public string Title;
        public List<MenuItem> Items = new List<MenuItem>();
        public int Selected;
        public int Scroll;
        public Action<Menu> OnEnter;

        public Menu(string title) { Title = title; }
        public Menu Add(MenuItem item) { Items.Add(item); return this; }
    }

    // -------------------------------------------------------------------------
    //  The script
    // -------------------------------------------------------------------------
    public class MenuScript : Script
    {
        private const string Version = "1.0.0";

        private readonly Config cfg = new Config();
        private readonly InputState input = new InputState();
        private readonly Stack<Menu> stack = new Stack<Menu>();
        private Menu root;

        private bool open;
        private bool weFroze;
        private bool waitRelease;      // swallow buttons until released after opening
        private int now;

        // Rebind capture
        private int captureTarget;     // 0 none, 1 open combo, 2 close combo
        private bool captureArmed;
        private uint captureUnion;
        private int captureDeadline;

        // Feature state
        private bool godMode, neverWanted, infAmmo, policeIgnore, everyoneIgnore, noRagdollBike, noDragOut, seatbelt;
        private bool vehGod, autoRepair, warpIntoSpawned = true, deleteOldSpawn = true;
        private bool freezeTime, showCoords, infSprint;
        private int frozenHour = 12, frozenMinute;
        private int runSpeed = 10;          // x0.1
        private int weatherIndex = -1;
        private int timeScaleIndex;
        private int densityIndex = 3;
        private Vehicle lastSpawned;
        private int nextSlowTick;
        private readonly Random rng = new Random();

        private static readonly string[] WeatherNames = { "Extra Sunny", "Sunny", "Sunny & Windy", "Cloudy", "Rain", "Drizzle", "Foggy", "Thunderstorm" };
        private static readonly float[] TimeScales = { 1.0f, 0.75f, 0.5f, 0.25f, 0.1f };
        private static readonly string[] TimeScaleNames = { "Normal", "75%", "50%", "25%", "10%" };
        private static readonly float[] Densities = { 0.0f, 0.25f, 0.5f, 1.0f, 1.5f, 2.0f };
        private static readonly string[] DensityNames = { "None", "Low", "Half", "Normal", "High", "Max" };

        private static readonly int[] WeaponIds = { 1, 2, 3, 4, 5, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        private static readonly string[] WeaponNames = { "Baseball Bat", "Pool Cue", "Knife", "Grenades", "Molotovs", "Pistol", "Combat Pistol", "Pump Shotgun", "Combat Shotgun", "Micro SMG", "SMG", "Assault Rifle", "Carbine Rifle", "Combat Sniper", "Sniper Rifle", "RPG" };

        public MenuScript()
        {
            string dir = ResolveScriptsDir();
            Log.PathName = Path.Combine(dir, "ClaudeMenu.log");
            try { File.WriteAllText(Log.PathName, "ClaudeMenu " + Version + " started" + Environment.NewLine); } catch (Exception) { }

            cfg.Load(Path.Combine(dir, "ClaudeMenu.ini"));
            ApplyInputConfig();
            infSprint = cfg.InfiniteSprint;
            BuildMenus();

            Interval = 0;
            Tick += OnTick;
            Log.Write("Open combo " + Btn.ToText(cfg.OpenCombo) + ", close combo " + Btn.ToText(cfg.CloseCombo));
        }

        private static string ResolveScriptsDir()
        {
            try
            {
                string d = Path.Combine(Directory.GetCurrentDirectory(), "scripts");
                if (Directory.Exists(d)) return d;
                d = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");
                if (Directory.Exists(d)) return d;
                return Directory.GetCurrentDirectory();
            }
            catch (Exception) { return "."; }
        }

        private void ApplyInputConfig()
        {
            input.StickNavigation = cfg.StickNavigation;
            input.KeyboardEnabled = cfg.KeyboardFallback;
            input.KeyToggle = cfg.KeyToggle;
        }

        // ---------------------------------------------------------------- tick
        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                now = Environment.TickCount;
                input.Update(now);

                bool paused = N.Get<bool>("IS_PAUSE_MENU_ACTIVE", false);

                if (!paused)
                {
                    if (!open)
                    {
                        if (input.ComboPressed(cfg.OpenCombo) || input.Pressed(Btn.NavToggle)) Open();
                    }
                    else
                    {
                        HandleMenuInput();
                    }
                }

                if (open && !paused) Draw();
                RunFeatures();
            }
            catch (Exception ex)
            {
                Log.Write("Tick error: " + ex);
            }
        }

        private void Open()
        {
            open = true;
            waitRelease = true;
            stack.Clear();
            stack.Push(root);
            Enter(root);
            if (cfg.FreezePlayer)
            {
                weFroze = N.Call("SET_PLAYER_CONTROL", PlayerIndex(), false);
            }
            N.Call("PLAY_AUDIO_EVENT", "FRONTEND_MENU_HIGHLIGHT_DOWN_UP");
        }

        private void Close()
        {
            open = false;
            captureTarget = 0;
            if (weFroze)
            {
                N.Call("SET_PLAYER_CONTROL", PlayerIndex(), true);
                weFroze = false;
            }
        }

        internal void Push(Menu m)
        {
            stack.Push(m);
            Enter(m);
        }

        private void Enter(Menu m)
        {
            if (m.OnEnter != null) m.OnEnter(m);
            foreach (MenuItem it in m.Items)
            {
                ListItem li = it as ListItem; if (li != null) li.Sync();
                NumberItem ni = it as NumberItem; if (ni != null) ni.Sync();
            }
            if (m.Selected >= m.Items.Count) m.Selected = Math.Max(0, m.Items.Count - 1);
        }

        private void Back()
        {
            if (stack.Count <= 1) { Close(); return; }
            stack.Pop();
        }

        private void HandleMenuInput()
        {
            // Close combo always wins, even mid-capture.
            if (captureTarget == 0 && input.ComboPressed(cfg.CloseCombo)) { Close(); return; }
            if (input.Pressed(Btn.NavToggle)) { Close(); return; }

            if (captureTarget != 0) { HandleCapture(); return; }

            // After opening, ignore the opening chord until everything is let go,
            // so the X of LB+RB+X (or anything else) can't trigger an item.
            if (waitRelease)
            {
                if ((input.Held & (Btn.ComboMask | Btn.NavAccept | Btn.NavBack)) == 0) waitRelease = false;
                else return;
            }

            // While LB+RB are held the user is probably entering a combo: don't treat B as Back.
            bool shouldersHeld = input.IsHeld(Btn.LB) && input.IsHeld(Btn.RB);

            Menu m = stack.Peek();
            int count = m.Items.Count;
            if (count > 0)
            {
                if (input.Repeat(Btn.NavUp, now)) { m.Selected = (m.Selected - 1 + count) % count; Beep(); }
                if (input.Repeat(Btn.NavDown, now)) { m.Selected = (m.Selected + 1) % count; Beep(); }
                if (input.Repeat(Btn.LT, now)) { m.Selected = Math.Max(0, m.Selected - cfg.MaxVisible); Beep(); }
                if (input.Repeat(Btn.RT, now)) { m.Selected = Math.Min(count - 1, m.Selected + cfg.MaxVisible); Beep(); }

                MenuItem item = m.Items[m.Selected];
                if (item.Adjustable)
                {
                    if (input.Repeat(Btn.NavLeft, now)) { item.Adjust(this, -1); Beep(); }
                    if (input.Repeat(Btn.NavRight, now)) { item.Adjust(this, 1); Beep(); }
                }
                if (input.Pressed(Btn.NavAccept))
                {
                    N.Call("PLAY_AUDIO_EVENT", "FRONTEND_MENU_SELECT");
                    item.Activate(this);
                    if (!open) return;
                }
            }

            if (!shouldersHeld && input.Pressed(Btn.NavBack))
            {
                N.Call("PLAY_AUDIO_EVENT", "FRONTEND_MENU_BACK");
                Back();
            }
        }

        private void Beep() { N.Call("PLAY_AUDIO_EVENT", "FRONTEND_MENU_HIGHLIGHT_DOWN_UP"); }

        // --------------------------------------------------------- rebinding
        private void StartCapture(int target)
        {
            captureTarget = target;
            captureArmed = false;
            captureUnion = 0;
            captureDeadline = now + 10000;
        }

        private void HandleCapture()
        {
            if (now > captureDeadline) { captureTarget = 0; Notify("Rebind cancelled (timed out)"); return; }
            uint held = input.Held & Btn.ComboMask;
            if (!captureArmed)
            {
                if (held == 0 && !input.IsHeld(Btn.NavAccept)) captureArmed = true;
                return;
            }
            if (held != 0) { captureUnion |= held; return; }
            if (captureUnion == 0) return;

            uint combo = captureUnion;
            int target = captureTarget;
            captureTarget = 0;

            int bits = 0;
            for (uint v = combo; v != 0; v >>= 1) bits += (int)(v & 1);
            if (bits < 2) { Notify("Use at least 2 buttons so the game doesn't trigger it by accident"); return; }

            uint other = target == 1 ? cfg.CloseCombo : cfg.OpenCombo;
            if (combo == other) { Notify("That combo is already used"); return; }

            if (target == 1) cfg.OpenCombo = combo; else cfg.CloseCombo = combo;
            cfg.Save();
            Notify((target == 1 ? "Open" : "Close") + " combo set to " + Btn.ToText(combo));
        }

        // ------------------------------------------------------------ drawing
        private float S(float v) { return v * cfg.Scale; }

        private void Draw()
        {
            Menu m = stack.Peek();
            Theme th = Themes.All[cfg.Theme];

            float w = S(0.26f);
            float headerH = S(0.055f);
            float subH = S(0.032f);
            float rowH = S(0.033f);
            float pad = S(0.008f);
            float x = cfg.Position == 0 ? 0.04f : (cfg.Position == 1 ? 0.5f - w / 2f : 0.96f - w);
            float y = 0.10f;

            int count = m.Items.Count;
            int visible = Math.Min(cfg.MaxVisible, count);
            if (m.Selected < m.Scroll) m.Scroll = m.Selected;
            if (m.Selected >= m.Scroll + visible) m.Scroll = m.Selected - visible + 1;
            if (m.Scroll > Math.Max(0, count - visible)) m.Scroll = Math.Max(0, count - visible);

            // Header
            Rect(x, y, w, headerH, th.Header);
            Text("CLAUDE MENU", x + pad, y + S(0.010f), S(0.50f), Col.White, false);
            TextRight("v" + Version, x + w - pad, y + S(0.020f), S(0.25f), Col.FromArgb(200, 255, 255, 255));
            y += headerH;

            // Sub header: breadcrumb + position
            Rect(x, y, w, subH, Col.FromArgb(235, 10, 10, 10));
            Text(Breadcrumb(), x + pad, y + S(0.006f), S(0.28f), th.Accent, false);
            TextRight(count == 0 ? "0/0" : (m.Selected + 1) + "/" + count, x + w - pad, y + S(0.006f), S(0.28f), Col.White);
            y += subH;

            // Items
            for (int i = m.Scroll; i < m.Scroll + visible; i++)
            {
                MenuItem it = m.Items[i];
                bool sel = i == m.Selected;
                Rect(x, y, w, rowH, sel ? th.Accent : Col.FromArgb(190, 0, 0, 0));
                Col tc = sel ? Col.White : Col.FromArgb(255, 225, 225, 225);
                Text(it.Label, x + pad, y + S(0.006f), S(0.30f), tc, false);
                string v = it.Value;
                if (v != null) TextRight(v, x + w - pad, y + S(0.006f), S(0.30f), tc);
                y += rowH;
            }

            if (count > visible)
            {
                Rect(x, y, w, S(0.006f), Col.FromArgb(190, 0, 0, 0));
                float barW = w * visible / count;
                float barX = x + (w - barW) * m.Scroll / Math.Max(1, count - visible);
                Rect(barX, y + S(0.0015f), barW, S(0.003f), th.Accent);
                y += S(0.006f);
            }

            // Footer: help text or capture prompt, then button hints
            string help = null;
            if (captureTarget != 0)
            {
                int left = Math.Max(0, (captureDeadline - now) / 1000);
                help = captureArmed
                    ? "Hold the new " + (captureTarget == 1 ? "OPEN" : "CLOSE") + " combo, then release (" + left + "s)"
                    : "Release all buttons...";
            }
            else if (count > 0 && m.Items[m.Selected].Help != null) help = m.Items[m.Selected].Help;

            if (help != null)
            {
                Rect(x, y, w, rowH, Col.FromArgb(235, 20, 20, 20));
                Text(help, x + pad, y + S(0.007f), S(0.26f), Col.FromArgb(255, 255, 230, 140), false);
                y += rowH;
            }

            Rect(x, y, w, subH, Col.FromArgb(235, 10, 10, 10));
            string hints = "A Select   B Back   D-pad Move/Change   Close " + Btn.ToText(cfg.CloseCombo);
            Text(hints, x + pad, y + S(0.008f), S(0.22f), Col.FromArgb(220, 200, 200, 200), false);
        }

        private string Breadcrumb()
        {
            Menu[] arr = stack.ToArray();   // top first
            if (arr.Length == 1) return arr[0].Title.ToUpperInvariant();
            if (arr.Length == 2) return arr[0].Title.ToUpperInvariant();
            return arr[1].Title.ToUpperInvariant() + " / " + arr[0].Title.ToUpperInvariant();
        }

        private static void Rect(float x, float y, float w, float h, Col c)
        {
            // DRAW_RECT takes the centre point.
            N.Call("DRAW_RECT", x + w / 2f, y + h / 2f, w, h, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }

        private static void SetupText(float scale, Col c)
        {
            N.Call("SET_TEXT_FONT", 0);
            N.Call("SET_TEXT_SCALE", scale, scale);
            N.Call("SET_TEXT_COLOUR", (int)c.R, (int)c.G, (int)c.B, (int)c.A);
            N.Call("SET_TEXT_DROPSHADOW", false, 0, 0, 0, 0);
            N.Call("SET_TEXT_EDGE", 1, 0, 0, 0, 205);
        }

        private static void Text(string s, float x, float y, float scale, Col c, bool center)
        {
            SetupText(scale, c);
            if (center) N.Call("SET_TEXT_CENTRE", true);
            N.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", s);
        }

        private static void TextRight(string s, float right, float y, float scale, Col c)
        {
            if (!N.IsBroken("SET_TEXT_RIGHT_JUSTIFY") && !N.IsBroken("SET_TEXT_WRAP"))
            {
                SetupText(scale, c);
                if (N.Call("SET_TEXT_RIGHT_JUSTIFY", true) && N.Call("SET_TEXT_WRAP", 0.0f, right))
                {
                    N.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", 0.0f, y, "STRING", s);
                    return;
                }
            }
            // Fallback: estimate the width.
            float approx = s.Length * scale * 0.028f;
            Text(s, right - approx, y, scale, c, false);
        }

        private static void Notify(string msg)
        {
            N.Call("PRINT_STRING_WITH_LITERAL_STRING_NOW", "STRING", msg, 2500, 1);
        }

        // ------------------------------------------------------------ helpers
        private static int PlayerIndex()
        {
            int id = N.Get<int>("GET_PLAYER_ID", 0);
            return N.Get<int>("CONVERT_INT_TO_PLAYERINDEX", id, id);
        }

        private static Ped PlayerPed()
        {
            Player p = Game.LocalPlayer;
            return p == null ? null : p.Character;
        }

        private static Vehicle CurrentVehicle()
        {
            Ped ped = PlayerPed();
            if (ped == null || !ped.Exists() || !ped.isInVehicle()) return null;
            Vehicle v = ped.CurrentVehicle;
            return (v != null && v.Exists()) ? v : null;
        }

        private static Vehicle NeedVehicle()
        {
            Vehicle v = CurrentVehicle();
            if (v == null) Notify("You need to be in a vehicle");
            return v;
        }

        // ------------------------------------------------- per-frame features
        private void RunFeatures()
        {
            Ped ped = PlayerPed();
            if (ped == null || !ped.Exists()) return;
            int pi = PlayerIndex();

            if (neverWanted) N.Call("CLEAR_WANTED_LEVEL", pi);
            if (freezeTime) N.Call("SET_TIME_OF_DAY", frozenHour, frozenMinute);
            if (densityIndex != 3)
            {
                N.Call("SET_CAR_DENSITY_MULTIPLIER", Densities[densityIndex]);
                N.Call("SET_PED_DENSITY_MULTIPLIER", Densities[densityIndex]);
            }
            if (showCoords)
            {
                Vector3 p = ped.Position;
                string s = string.Format(CultureInfo.InvariantCulture, "X {0:0.0}  Y {1:0.0}  Z {2:0.0}  H {3:0}", p.X, p.Y, p.Z, ped.Heading);
                Text(s, 0.40f, 0.95f, 0.28f, Col.White, false);
            }

            // Things that don't need to run every frame.
            if (now < nextSlowTick) return;
            nextSlowTick = now + 250;

            if (godMode)
            {
                N.Call("SET_CHAR_INVINCIBLE", ped, true);
                if (ped.Health < 200) ped.Health = 200;
            }
            if (infAmmo)
            {
                for (int i = 3; i < WeaponIds.Length; i++)
                    if (N.Get<bool>("HAS_CHAR_GOT_WEAPON", false, ped, WeaponIds[i]))
                        N.Call("SET_CHAR_AMMO", ped, WeaponIds[i], 9999);
            }
            if (infSprint) N.Call("SET_PLAYER_NEVER_GETS_TIRED", pi, true);

            Vehicle v = CurrentVehicle();
            if (v != null && (vehGod || autoRepair))
            {
                if (vehGod)
                {
                    N.Call("SET_CAR_CAN_BE_DAMAGED", v, false);
                    N.Call("SET_CAR_CAN_BE_VISIBLY_DAMAGED", v, false);
                    N.Call("SET_CAN_BURST_CAR_TYRES", v, false);
                }
                if (autoRepair && v.Health < 900) v.Repair();
            }
        }

        // -------------------------------------------------------------- menus
        private void BuildMenus()
        {
            root = new Menu("Main Menu");
            root.Add(new SubmenuItem(BuildPlayerMenu()));
            root.Add(new SubmenuItem(BuildVehicleMenu()));
            root.Add(new SubmenuItem(BuildWeaponMenu()));
            root.Add(new SubmenuItem(BuildTeleportMenu()));
            root.Add(new SubmenuItem(BuildWorldMenu()));
            root.Add(new SubmenuItem(BuildSettingsMenu()));
            root.Add(new ActionItem("Close Menu", Close));
        }

        private Menu BuildPlayerMenu()
        {
            Menu m = new Menu("Player");
            m.Add(new ToggleItem("God Mode", () => godMode, v =>
            {
                godMode = v;
                Ped p = PlayerPed();
                if (p != null) N.Call("SET_CHAR_INVINCIBLE", p, v);
            }, "Take no damage"));
            m.Add(new ActionItem("Heal & Armor", () =>
            {
                Ped p = PlayerPed(); if (p == null) return;
                p.Health = 200;
                N.Call("ADD_ARMOUR_TO_CHAR", p, 100);
                Notify("Health and armor restored");
            }));
            m.Add(new ToggleItem("Never Wanted", () => neverWanted, v => neverWanted = v));
            m.Add(new NumberItem("Wanted Level", 0, 6, 1, () => Math.Max(0, Game.LocalPlayer.WantedLevel), v =>
            {
                int pi = PlayerIndex();
                if (v == 0) N.Call("CLEAR_WANTED_LEVEL", pi);
                else { N.Call("ALTER_WANTED_LEVEL", pi, v); N.Call("APPLY_WANTED_LEVEL_CHANGE_NOW", pi); }
            }, false, " stars", "Left/Right to choose, A to apply"));
            m.Add(new ToggleItem("Police Ignore You", () => policeIgnore, v => { policeIgnore = v; N.Call("SET_POLICE_IGNORE_PLAYER", PlayerIndex(), v); }));
            m.Add(new ToggleItem("Everyone Ignores You", () => everyoneIgnore, v => { everyoneIgnore = v; N.Call("SET_EVERYONE_IGNORE_PLAYER", PlayerIndex(), v); }));
            m.Add(new ToggleItem("Unlimited Sprint", () => infSprint, v => { infSprint = v; N.Call("SET_PLAYER_NEVER_GETS_TIRED", PlayerIndex(), v); }));
            m.Add(new NumberItem("Move Speed", 5, 30, 1, () => runSpeed, v =>
            {
                runSpeed = v;
                Ped p = PlayerPed();
                if (p != null) N.Call("SET_CHAR_MOVE_ANIM_SPEED_MULTIPLIER", p, v / 10f);
            }, true, "", "Animation speed x0.1 (10 = normal)"));
            m.Add(new ToggleItem("Never Fall Off Bike", () => noRagdollBike, v =>
            {
                noRagdollBike = v; Ped p = PlayerPed();
                if (p != null) N.Call("SET_CHAR_CAN_BE_KNOCKED_OFF_BIKE", p, !v);
            }));
            m.Add(new ToggleItem("Can't Be Dragged Out", () => noDragOut, v =>
            {
                noDragOut = v; Ped p = PlayerPed();
                if (p != null) N.Call("SET_CHAR_CANT_BE_DRAGGED_OUT", p, v);
            }));
            m.Add(new ToggleItem("Seatbelt", () => seatbelt, v =>
            {
                seatbelt = v; Ped p = PlayerPed();
                if (p != null) N.Call("SET_CHAR_WILL_FLY_THROUGH_WINDSCREEN", p, !v);
            }, "Don't fly through the windscreen"));
            int[] amounts = { 1000, 10000, 100000, 1000000, 10000000 };
            string[] amountNames = { "$1,000", "$10,000", "$100,000", "$1,000,000", "$10,000,000" };
            m.Add(new ListItem("Add Money", amountNames, null, v =>
            {
                N.Call("ADD_SCORE", PlayerIndex(), amounts[v]);
                Notify("+" + amountNames[v]);
            }, false, "Left/Right to choose, A to add"));
            return m;
        }

        private Menu BuildVehicleMenu()
        {
            Menu m = new Menu("Vehicle");
            m.Add(new SubmenuItem(BuildSpawnerMenu(), "Spawn any vehicle"));
            m.Add(new ActionItem("Repair Vehicle", () => { Vehicle v = NeedVehicle(); if (v != null) { v.Repair(); Notify("Repaired"); } }));
            m.Add(new ActionItem("Wash Vehicle", () => { Vehicle v = NeedVehicle(); if (v != null) N.Call("SET_VEHICLE_DIRT_LEVEL", v, 0.0f); }));
            m.Add(new ActionItem("Flip Upright", () => { Vehicle v = NeedVehicle(); if (v != null) N.Call("SET_CAR_ON_GROUND_PROPERLY", v); }));
            m.Add(new ToggleItem("Vehicle God Mode", () => vehGod, v =>
            {
                vehGod = v;
                Vehicle car = CurrentVehicle();
                if (car != null && !v)
                {
                    N.Call("SET_CAR_CAN_BE_DAMAGED", car, true);
                    N.Call("SET_CAR_CAN_BE_VISIBLY_DAMAGED", car, true);
                    N.Call("SET_CAN_BURST_CAR_TYRES", car, true);
                }
            }, "Applies to whatever you're driving"));
            m.Add(new ToggleItem("Auto Repair", () => autoRepair, v => autoRepair = v));
            m.Add(new NumberItem("Speed Boost", 10, 100, 10, () => 40, v =>
            {
                Vehicle car = NeedVehicle();
                if (car != null) N.Call("SET_CAR_FORWARD_SPEED", car, (float)v);
            }, false, " m/s", "Choose speed, press A to launch"));
            m.Add(new ActionItem("Random Colour", () =>
            {
                Vehicle v = NeedVehicle();
                if (v != null) N.Call("CHANGE_CAR_COLOUR", v, rng.Next(0, 134), rng.Next(0, 134));
            }));
            m.Add(new ActionItem("Delete Vehicle", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                Ped p = PlayerPed();
                if (p != null) p.Position = v.Position + new Vector3(0f, 0f, 1.5f);
                v.Delete();
            }));
            return m;
        }

        private static readonly string[][] VehicleClasses =
        {
            new[] { "Sports", "INFERNUS", "TURISMO", "COMET", "BANSHEE", "SULTANRS", "COQUETTE", "SUPERGT", "F620", "FEROCI", "BLISTA" },
            new[] { "Muscle", "SABREGT", "SABRE", "STALION", "DUKES", "VIGERO", "RUINER", "BUCCANEER", "FACTION", "VOODOO" },
            new[] { "Sedans", "ADMIRAL", "ORACLE", "SCHAFTER", "PRES", "PMP600", "SULTAN", "STRETCH", "WASHINGTON", "INTRUDER", "COGNOSCENTI" },
            new[] { "SUVs & Trucks", "CAVALCADE", "PATRIOT", "HUNTLEY", "LANDSTALKER", "REBLA", "FXT", "BOBCAT", "YANKEE" },
            new[] { "Emergency", "POLICE", "POLICE2", "POLPATRIOT", "NOOSE", "NSTOCKADE", "FBI", "AMBULANCE", "FIRETRUK", "PSTOCKADE" },
            new[] { "Commercial", "BUS", "PHANTOM", "BENSON", "FLATBED", "MULE", "BURRITO", "TRASH", "TAXI", "CABBY", "BIFF" },
            new[] { "Motorcycles", "NRG900", "PCJ", "SANCHEZ", "FAGGIO", "HELLFURY", "ZOMBIEB", "BATI", "HAKUCHOU", "DOUBLE", "HEXER" },
            new[] { "Helicopters", "MAVERICK", "POLMAV", "TOURMAV", "ANNIHILATOR", "BUZZARD", "SWIFT", "SKYLIFT" },
            new[] { "Boats", "JETMAX", "SQUALO", "PREDATOR", "DINGHY", "MARQUIS", "REEFER", "TROPIC", "TUGA", "SMUGGLER" },
        };

        private Menu BuildSpawnerMenu()
        {
            Menu m = new Menu("Spawn Vehicle");
            m.Add(new ToggleItem("Warp Into Spawned", () => warpIntoSpawned, v => warpIntoSpawned = v));
            m.Add(new ToggleItem("Replace Last Spawned", () => deleteOldSpawn, v => deleteOldSpawn = v, "Delete your previous spawn"));
            foreach (string[] cls in VehicleClasses)
            {
                Menu sub = new Menu(cls[0]);
                for (int i = 1; i < cls.Length; i++)
                {
                    string model = cls[i];
                    sub.Add(new ActionItem(Pretty(model), () => SpawnVehicle(model), "Model: " + model));
                }
                m.Add(new SubmenuItem(sub));
            }
            return m;
        }

        private static string Pretty(string model)
        {
            if (model.Length <= 3) return model;
            return model.Substring(0, 1) + model.Substring(1).ToLowerInvariant();
        }

        private void SpawnVehicle(string model)
        {
            Ped ped = PlayerPed();
            if (ped == null) return;
            int hash = N.Get<int>("GET_HASH_KEY", 0, model);
            if (hash == 0 || !N.Get<bool>("IS_MODEL_IN_CDIMAGE", false, hash))
            {
                Notify(model + " isn't available in this game/episode");
                return;
            }
            N.Call("REQUEST_MODEL", hash);
            int deadline = Environment.TickCount + 3000;
            while (!N.Get<bool>("HAS_MODEL_LOADED", false, hash))
            {
                if (Environment.TickCount > deadline) { Notify("Timed out loading " + model); return; }
                Draw();
                Wait(0);
            }

            float h = ped.Heading;
            Vehicle old = CurrentVehicle();
            Vector3 basePos = old != null ? old.Position : ped.Position;

            // Step out of the current vehicle first if we're going to warp or delete it.
            bool oldIsLastSpawn = old != null && lastSpawned != null && lastSpawned.Exists() && N.Get<bool>("IS_CHAR_IN_CAR", false, ped, lastSpawned);
            if (old != null && (warpIntoSpawned || (deleteOldSpawn && oldIsLastSpawn)))
                ped.Position = basePos + new Vector3(0f, 0f, 2.5f);

            if (deleteOldSpawn && lastSpawned != null && lastSpawned.Exists())
            {
                if (!N.Get<bool>("IS_CHAR_IN_CAR", false, ped, lastSpawned)) lastSpawned.Delete();
            }
            lastSpawned = null;

            double rad = h * Math.PI / 180.0;
            float dist = (old != null && !warpIntoSpawned) ? 7.0f : 5.0f;
            Vector3 pos = basePos + new Vector3((float)(-Math.Sin(rad) * dist), (float)(Math.Cos(rad) * dist), 0.5f);

            Vehicle car = World.CreateVehicle(new Model(hash), pos);
            N.Call("MARK_MODEL_AS_NO_LONGER_NEEDED", hash);
            if (car == null || !car.Exists()) { Notify("Couldn't spawn " + model); return; }

            car.Heading = h;
            if (warpIntoSpawned) ped.WarpIntoVehicle(car, VehicleSeat.Driver);
            lastSpawned = car;
            Notify("Spawned " + Pretty(model));
        }

        private Menu BuildWeaponMenu()
        {
            Menu m = new Menu("Weapons");
            m.Add(new ActionItem("Give All Weapons", () =>
            {
                Ped p = PlayerPed(); if (p == null) return;
                // Give in an order that leaves a sensible "last" weapon equipped.
                for (int i = 0; i < WeaponIds.Length; i++) N.Call("GIVE_WEAPON_TO_CHAR", p, WeaponIds[i], 9999, false);
                Notify("All weapons given");
            }));
            m.Add(new ToggleItem("Infinite Ammo", () => infAmmo, v => infAmmo = v, "Keeps reserve ammo full"));
            m.Add(new ActionItem("Refill Ammo", () =>
            {
                Ped p = PlayerPed(); if (p == null) return;
                for (int i = 3; i < WeaponIds.Length; i++)
                    if (N.Get<bool>("HAS_CHAR_GOT_WEAPON", false, p, WeaponIds[i])) N.Call("SET_CHAR_AMMO", p, WeaponIds[i], 9999);
            }));
            m.Add(new ActionItem("Remove All Weapons", () => { Ped p = PlayerPed(); if (p != null) N.Call("REMOVE_ALL_CHAR_WEAPONS", p); }));

            Menu single = new Menu("Give Single Weapon");
            for (int i = 0; i < WeaponIds.Length; i++)
            {
                int id = WeaponIds[i];
                string name = WeaponNames[i];
                single.Add(new ActionItem(name, () =>
                {
                    Ped p = PlayerPed(); if (p == null) return;
                    N.Call("GIVE_WEAPON_TO_CHAR", p, id, 9999, false);
                    N.Call("SET_CURRENT_CHAR_WEAPON", p, id, true);
                    Notify(name + " given");
                }));
            }
            m.Add(new SubmenuItem(single));
            return m;
        }

        private Menu BuildTeleportMenu()
        {
            Menu m = new Menu("Teleport");
            m.OnEnter = RebuildTeleport;
            return m;
        }

        private void RebuildTeleport(Menu m)
        {
            m.Items.Clear();
            m.Add(new ActionItem("To Map Waypoint", TeleportToWaypoint, "Set a waypoint on the map first"));
            m.Add(new ActionItem("Forward 10m", () => Nudge(10f, 0f)));
            m.Add(new ActionItem("Up 25m", () => Nudge(0f, 25f)));

            Menu save = new Menu("Save Position");
            for (int i = 0; i < cfg.Slots.Length; i++)
            {
                int slot = i;
                save.Add(new ActionItem("Save to Slot " + (slot + 1), () =>
                {
                    Ped p = PlayerPed(); if (p == null) return;
                    Vector3 pos = p.Position;
                    cfg.Slots[slot] = string.Format(CultureInfo.InvariantCulture, "Slot {0}|{1:0.00}|{2:0.00}|{3:0.00}", slot + 1, pos.X, pos.Y, pos.Z);
                    Notify(cfg.Save() ? "Saved to slot " + (slot + 1) : "Saved (couldn't write INI)");
                    RebuildTeleport(m);
                }, cfg.Slots[slot] == null ? "Empty" : "Overwrites current"));
            }
            m.Add(new SubmenuItem(save));

            for (int i = 0; i < cfg.Slots.Length; i++)
            {
                string label; Vector3 pos;
                if (cfg.Slots[i] != null && TryParseLocation(cfg.Slots[i], out label, out pos))
                {
                    Vector3 p2 = pos;
                    m.Add(new ActionItem("Load Slot " + (i + 1), () => TeleportTo(p2, false)));
                }
            }
            foreach (string loc in cfg.Locations)
            {
                string label; Vector3 pos;
                if (TryParseLocation(loc, out label, out pos))
                {
                    Vector3 p2 = pos;
                    m.Add(new ActionItem(label, () => TeleportTo(p2, false), "From ClaudeMenu.ini"));
                }
            }
            if (m.Selected >= m.Items.Count) m.Selected = 0;
        }

        private static bool TryParseLocation(string s, out string label, out Vector3 pos)
        {
            label = null; pos = new Vector3();
            string[] p = s.Split('|');
            if (p.Length < 4) return false;
            float x = Config.ParseFloat(p[1], float.NaN), y = Config.ParseFloat(p[2], float.NaN), z = Config.ParseFloat(p[3], float.NaN);
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) return false;
            label = p[0].Trim();
            pos = new Vector3(x, y, z);
            return true;
        }

        private void TeleportToWaypoint()
        {
            Blip wp = Game.GetWaypoint();
            if (wp == null || !wp.Exists()) { Notify("No waypoint set"); return; }
            Vector3 target = wp.Position;
            TeleportTo(target, true);
        }

        private void TeleportTo(Vector3 target, bool findGround)
        {
            Ped ped = PlayerPed(); if (ped == null) return;
            Vehicle car = CurrentVehicle();

            if (findGround)
            {
                // Load the area, then probe for the ground height.
                N.Call("REQUEST_COLLISION_AT_POSN", target.X, target.Y, 100.0f);
                N.Call("LOAD_ALL_OBJECTS_NOW");
                float z = target.Z;
                bool found = false;
                for (int attempt = 0; attempt < 30 && !found; attempt++)
                {
                    Pointer pz = new Pointer(typeof(float));
                    if (N.Call("GET_GROUND_Z_FOR_3D_COORD", target.X, target.Y, 1000.0f, pz))
                    {
                        float g = (float)pz;
                        if (g > 0.5f) { z = g + 1.0f; found = true; }
                    }
                    if (!found) Wait(50);
                }
                if (!found)
                {
                    // Fall back to ScriptHookDotNet's own grounding teleport.
                    Game.LocalPlayer.TeleportTo(target.X, target.Y);
                    Notify("Teleported");
                    return;
                }
                target = new Vector3(target.X, target.Y, z);
            }

            if (car != null) car.Position = target; else ped.Position = target;
            Notify("Teleported");
        }

        private void Nudge(float forward, float up)
        {
            Ped ped = PlayerPed(); if (ped == null) return;
            double rad = ped.Heading * Math.PI / 180.0;
            Vector3 off = new Vector3((float)(-Math.Sin(rad) * forward), (float)(Math.Cos(rad) * forward), up);
            Vehicle car = CurrentVehicle();
            if (car != null) car.Position = car.Position + off; else ped.Position = ped.Position + off;
        }

        private Menu BuildWorldMenu()
        {
            Menu m = new Menu("World");
            m.Add(new NumberItem("Hour", 0, 23, 1, () => World.CurrentDayTime.Hours, v =>
            {
                frozenHour = v; frozenMinute = 0;
                N.Call("SET_TIME_OF_DAY", v, 0);
            }, true, ":00", "Changes as you scroll"));
            m.Add(new ToggleItem("Freeze Time", () => freezeTime, v =>
            {
                freezeTime = v;
                TimeSpan t = World.CurrentDayTime;
                frozenHour = t.Hours; frozenMinute = t.Minutes;
            }));
            m.Add(new ListItem("Weather", WeatherNames, () => Math.Max(0, weatherIndex), v =>
            {
                weatherIndex = v;
                N.Call("FORCE_WEATHER_NOW", v);
            }, true, "Changes as you scroll"));
            m.Add(new ActionItem("Reset Weather", () => { weatherIndex = -1; N.Call("RELEASE_WEATHER"); Notify("Weather back to normal"); }));
            m.Add(new ListItem("Game Speed", TimeScaleNames, () => timeScaleIndex, v =>
            {
                timeScaleIndex = v;
                N.Call("SET_TIME_SCALE", TimeScales[v]);
            }, true, "Slow motion"));
            m.Add(new ListItem("Traffic & Ped Density", DensityNames, () => densityIndex, v => densityIndex = v, true, null));
            m.Add(new ActionItem("Clear Area", () =>
            {
                Ped p = PlayerPed(); if (p == null) return;
                Vector3 pos = p.Position;
                N.Call("CLEAR_AREA", pos.X, pos.Y, pos.Z, 60.0f, true);
                Notify("Area cleared");
            }, "Removes nearby cars, peds and fires"));
            m.Add(new ToggleItem("Show Coordinates", () => showCoords, v => showCoords = v));
            return m;
        }

        private Menu BuildSettingsMenu()
        {
            Menu m = new Menu("Settings");
            m.Add(new ActionItem("Rebind Open Combo", () => StartCapture(1), "Current: " + Btn.ToText(cfg.OpenCombo)));
            m.Add(new ActionItem("Rebind Close Combo", () => StartCapture(2), "Current: " + Btn.ToText(cfg.CloseCombo)));
            m.Add(new ToggleItem("Freeze Player While Open", () => cfg.FreezePlayer, v =>
            {
                cfg.FreezePlayer = v;
                if (!v && weFroze) { N.Call("SET_PLAYER_CONTROL", PlayerIndex(), true); weFroze = false; }
                if (v && !weFroze) weFroze = N.Call("SET_PLAYER_CONTROL", PlayerIndex(), false);
            }, "Stops menu buttons also moving Niko"));
            m.Add(new ToggleItem("Left Stick Navigation", () => cfg.StickNavigation, v => { cfg.StickNavigation = v; ApplyInputConfig(); }));
            m.Add(new ListItem("Menu Position", new[] { "Left", "Center", "Right" }, () => cfg.Position, v => cfg.Position = v, true, null));
            List<string> themeNames = new List<string>();
            foreach (Theme t in Themes.All) themeNames.Add(t.Name);
            m.Add(new ListItem("Theme", themeNames.ToArray(), () => cfg.Theme, v => cfg.Theme = v, true, null));
            m.Add(new NumberItem("Menu Size", 70, 150, 10, () => (int)Math.Round(cfg.Scale * 100), v => cfg.Scale = v / 100f, true, "%", "Bigger is easier to read on the Deck"));
            m.Add(new ToggleItem("Keyboard Fallback", () => cfg.KeyboardFallback, v => { cfg.KeyboardFallback = v; ApplyInputConfig(); }, "F4 / arrows / Enter / Backspace"));
            m.Add(new ActionItem("Save Settings", () => Notify(cfg.Save() ? "Settings saved" : "Couldn't write ClaudeMenu.ini")));
            m.Add(new ActionItem("Controller Status", () =>
            {
                Notify(input.Connected ? "Controller connected (XInput)" : (XInput.Available ? "No controller found - check Steam Input" : "XInput DLL not found"));
            }));
            // Refresh the help text showing current combos each time Settings opens.
            m.OnEnter = menu =>
            {
                menu.Items[0].Help = "Current: " + Btn.ToText(cfg.OpenCombo);
                menu.Items[1].Help = "Current: " + Btn.ToText(cfg.CloseCombo);
            };
            return m;
        }
    }
}
