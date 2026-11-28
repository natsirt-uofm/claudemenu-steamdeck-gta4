// =============================================================================
//  ClaudeMenu - an all-in-one, controller-first trainer for GTA IV
//  Built for the Steam Deck (Proton + Steam Input) and any XInput gamepad.
//
//  Runtime : ScriptHookDotNet v1.7.x on GTA IV 1.0.8.0 / 1.0.7.0 (or EFLC)
//  Install : drop this file and ClaudeMenu.ini into <GTA IV folder>\scripts\
//
//  Default controls
//    Open menu ........ LB + RB + X   (hold LB+RB, then press X)
//    Close menu ....... LB + RB + B   (or B on the main page)
//    Move ............. D-pad Up/Down (or left stick), hold to scroll faster
//    Page up/down ..... LT / RT
//    Select / toggle .. A
//    Change value ..... D-pad Left/Right (or left stick)
//    Back ............. B
//    Nitro (driving) .. L3 (click the left stick); map a back grip to it
//  Every binding can be changed in-game (Settings) or in the INI.
//
//  Rules this file follows (learned on a real Deck; please keep them):
//   - One loose .cs file, C# 4 syntax. ScriptHookDotNet compiles it in-game and
//     only references System, System.Drawing, System.Windows.Forms, System.Xml
//     and ScriptHookDotNet: no System.Core (no HashSet, no LINQ).
//   - All drawing happens in PerFrameDrawing with GTA.Graphics. Drawing natives
//     called from Tick run but never show up. Game logic stays in Tick, which
//     hands a finished FrameView to the drawing code.
//   - Every native goes through N.Call / N.Get: a failing native is logged and
//     switched off instead of crashing the script.
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
    #region Input: buttons, XInput, keyboard

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

        public static int Count(uint mask)
        {
            int bits = 0;
            for (uint v = mask; v != 0; v >>= 1) bits += (int)(v & 1);
            return bits;
        }
    }

    // -------------------------------------------------------------------------
    //  XInput reader. Under Proton, Steam Input exposes the Steam Deck's
    //  built-in controls as an Xbox 360 pad, so reading XInput directly gives
    //  raw button state independent of the game's bindings.
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
    //  Input state with edge detection, hold-to-repeat (faster the longer you
    //  hold) and the raw analog values for driving and the free camera.
    // -------------------------------------------------------------------------
    internal sealed class InputState
    {
        public uint Held;
        public uint Prev;

        // Raw analog values (0 when no pad). Triggers 0..255, sticks -32768..32767.
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;

        private int padIndex = -1;
        private int nextScan;
        private readonly Dictionary<uint, int> repeatAt = new Dictionary<uint, int>();
        private readonly Dictionary<uint, int> holdStart = new Dictionary<uint, int>();

        public int RepeatDelay = 350;
        public int RepeatRate = 70;
        public int FastRepeatRate = 30;
        public bool StickNavigation = true;
        public bool KeyboardEnabled = true;
        public int KeyToggle = 0x73;   // F4
        public bool Connected { get { return padIndex >= 0; } }

        public void Update(int now)
        {
            Prev = Held;
            uint held = 0;
            LeftTrigger = RightTrigger = 0;
            LX = LY = RX = RY = 0;

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
            LeftTrigger = st.Pad.bLeftTrigger;
            RightTrigger = st.Pad.bRightTrigger;
            LX = st.Pad.sThumbLX; LY = st.Pad.sThumbLY;
            RX = st.Pad.sThumbRX; RY = st.Pad.sThumbRY;
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

        /// <summary>Stick value as -1..1 with a dead zone.</summary>
        public static float Axis(short raw)
        {
            const float dz = 7000f;
            float v = raw;
            if (v > -dz && v < dz) return 0f;
            float s = v > 0 ? (v - dz) / (32767f - dz) : (v + dz) / (32768f - dz);
            return s < -1f ? -1f : (s > 1f ? 1f : s);
        }

        /// <summary>Trigger value as 0..1 with a small dead zone.</summary>
        public static float Trigger(byte raw) { return raw < 12 ? 0f : (raw - 12) / 243f; }

        public bool IsHeld(uint b) { return (Held & b) != 0; }
        public bool Pressed(uint b) { return (Held & b) != 0 && (Prev & b) == 0; }

        /// <summary>True on the frame a whole combination becomes held.</summary>
        public bool ComboPressed(uint mask)
        {
            if (mask == 0) return false;
            return (Held & mask) == mask && (Prev & mask) != mask;
        }

        public bool ComboHeld(uint mask) { return mask != 0 && (Held & mask) == mask; }

        /// <summary>Pressed, then auto-repeats while held, speeding up after a second.</summary>
        public bool Repeat(uint b, int now)
        {
            if ((Held & b) == 0) { repeatAt.Remove(b); holdStart.Remove(b); return false; }
            if ((Prev & b) == 0) { repeatAt[b] = now + RepeatDelay; holdStart[b] = now; return true; }
            int at, start;
            if (repeatAt.TryGetValue(b, out at) && now >= at)
            {
                holdStart.TryGetValue(b, out start);
                repeatAt[b] = now + (now - start > 1200 ? FastRepeatRate : RepeatRate);
                return true;
            }
            return false;
        }
    }

    #endregion

    #region Natives, logging, helpers

    // -------------------------------------------------------------------------
    //  Safe native wrapper: a bad native name or argument never kills the
    //  script; it is logged once and then skipped.
    // -------------------------------------------------------------------------
    internal static class N
    {
        private static readonly Dictionary<string, bool> broken = new Dictionary<string, bool>();

        public static bool Call(string name, params Parameter[] args)
        {
            if (broken.ContainsKey(name)) return false;
            try { Function.Call(name, args); return true; }
            catch (Exception ex) { Fail(name, ex); return false; }
        }

        public static T Get<T>(string name, T fallback, params Parameter[] args)
        {
            if (broken.ContainsKey(name)) return fallback;
            try { return Function.Call<T>(name, args); }
            catch (Exception ex) { Fail(name, ex); return fallback; }
        }

        public static bool IsBroken(string name) { return broken.ContainsKey(name); }

        // Pointer helpers for natives that return values through out-parameters.
        public static int OutInt(string name, int fallback, Parameter first)
        {
            Pointer p = new Pointer(typeof(int));
            return Call(name, first, p) ? (int)p : fallback;
        }

        public static float OutFloat(string name, float fallback, Parameter first)
        {
            Pointer p = new Pointer(typeof(float));
            return Call(name, first, p) ? (float)p : fallback;
        }

        private static void Fail(string name, Exception ex)
        {
            broken[name] = true;
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

    internal static class U
    {
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static string F(float v, string fmt) { return v.ToString(fmt, CultureInfo.InvariantCulture); }
        public static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }
        public static string Money(int v) { return (v < 0 ? "-$" : "$") + Math.Abs((long)v).ToString("#,0", CultureInfo.InvariantCulture); }

        public static float ParseFloat(string s, float fallback)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public static bool ParseBool(string s, bool fallback)
        {
            s = s.ToLowerInvariant();
            if (s == "true" || s == "1" || s == "yes" || s == "on") return true;
            if (s == "false" || s == "0" || s == "no" || s == "off") return false;
            return fallback;
        }

        public static string B(bool v) { return v ? "true" : "false"; }

        // Vector helpers kept local so the API stub stays small.
        public static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        public static Vector3 Add(Vector3 a, Vector3 b) { return new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vector3 Sub(Vector3 a, Vector3 b) { return new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vector3 Mul(Vector3 a, float s) { return new Vector3(a.X * s, a.Y * s, a.Z * s); }
        public static float Dot(Vector3 a, Vector3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static float Len(Vector3 a) { return (float)Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z); }
        public static Vector3 Norm(Vector3 a) { float l = Len(a); return l < 1e-5f ? new Vector3() : Mul(a, 1f / l); }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return Add(a, Mul(Sub(b, a), t)); }

        /// <summary>IV heading (degrees, 0 = north/+Y, counter-clockwise) to a flat direction.</summary>
        public static Vector3 HeadingDir(float heading)
        {
            double r = heading * Math.PI / 180.0;
            return new Vector3((float)-Math.Sin(r), (float)Math.Cos(r), 0f);
        }
    }

    #endregion

    #region Configuration (ClaudeMenu.ini) and vehicle presets

    /// <summary>Tuning that the performance code applies to a vehicle.</summary>
    internal sealed class Tuning
    {
        public float Power = 1f;        // engine power multiplier (1 = stock)
        public float Brakes = 1f;       // brake multiplier
        public int Grip;                // grip assist 0 (off) .. 10
        public float Handbrake = 1f;    // handbrake multiplier
        public int TopSpeedKmh;         // 0 = no limit

        public bool IsStock { get { return Power <= 1.001f && Brakes <= 1.001f && Grip == 0 && Handbrake <= 1.001f && TopSpeedKmh == 0; } }

        public void CopyFrom(Tuning t)
        {
            Power = t.Power; Brakes = t.Brakes; Grip = t.Grip; Handbrake = t.Handbrake; TopSpeedKmh = t.TopSpeedKmh;
        }

        public string Summary()
        {
            if (IsStock) return "stock";
            List<string> s = new List<string>();
            if (Power > 1.001f) s.Add("PWR " + U.F(Power, "0.0") + "x");
            if (Brakes > 1.001f) s.Add("BRK " + U.F(Brakes, "0.0") + "x");
            if (Grip > 0) s.Add("GRIP " + Grip);
            if (Handbrake > 1.001f) s.Add("HB " + U.F(Handbrake, "0.0") + "x");
            if (TopSpeedKmh > 0) s.Add("MAX " + TopSpeedKmh);
            return string.Join("  ", s.ToArray());
        }
    }

    /// <summary>A saved vehicle: model, paint, extras, livery and tuning.</summary>
    internal sealed class Preset
    {
        public string Name;
        public string ModelName;     // spawn name if known (e.g. INFERNUS), else display name
        public int ModelHash;
        public int[] Colors = new int[4];
        public int ExtrasOn;         // bit n set = extra n turned on (1..9)
        public int Livery = -1;      // -1 = model has none / leave alone
        public int Dirt;
        public readonly Tuning Tune = new Tuning();

        public string Serialize()
        {
            string[] f =
            {
                Clean(Name), Clean(ModelName), U.I(ModelHash),
                U.I(Colors[0]), U.I(Colors[1]), U.I(Colors[2]), U.I(Colors[3]),
                U.I(ExtrasOn), U.I(Livery), U.I(Dirt),
                U.F(Tune.Power, "0.0#"), U.F(Tune.Brakes, "0.0#"), U.I(Tune.Grip), U.F(Tune.Handbrake, "0.0#"), U.I(Tune.TopSpeedKmh),
            };
            return string.Join("|", f);
        }

        public static Preset Parse(string s)
        {
            string[] f = s.Split('|');
            if (f.Length < 10) return null;
            Preset p = new Preset();
            p.Name = f[0].Trim();
            p.ModelName = f[1].Trim();
            p.ModelHash = U.ParseInt(f[2], 0);
            if (p.ModelHash == 0 || p.Name.Length == 0) return null;
            for (int i = 0; i < 4; i++) p.Colors[i] = U.Clamp(U.ParseInt(f[3 + i], 0), 0, 255);
            p.ExtrasOn = U.ParseInt(f[7], 0);
            p.Livery = U.ParseInt(f[8], -1);
            p.Dirt = U.Clamp(U.ParseInt(f[9], 0), 0, 15);
            if (f.Length >= 15)
            {
                p.Tune.Power = U.Clamp(U.ParseFloat(f[10], 1f), 1f, 10f);
                p.Tune.Brakes = U.Clamp(U.ParseFloat(f[11], 1f), 1f, 10f);
                p.Tune.Grip = U.Clamp(U.ParseInt(f[12], 0), 0, 10);
                p.Tune.Handbrake = U.Clamp(U.ParseFloat(f[13], 1f), 1f, 5f);
                p.Tune.TopSpeedKmh = U.Clamp(U.ParseInt(f[14], 0), 0, 500);
            }
            return p;
        }

        private static string Clean(string s) { return (s ?? "").Replace("|", "/").Replace("\r", "").Replace("\n", ""); }
    }

    internal sealed class Config
    {
        // [Controls]
        public uint OpenCombo = Btn.LB | Btn.RB | Btn.X;
        public uint CloseCombo = Btn.LB | Btn.RB | Btn.B;
        public uint NitroCombo = Btn.LS;
        public uint JumpButton = Btn.X;          // GTA IV's jump on an Xbox pad (Super Jump listens for it)
        public uint RunButton = Btn.A;           // GTA IV's sprint on an Xbox pad (Super Run listens for it)
        public bool FreezePlayer = true;
        public bool CameraWhileOpen = true;      // right stick still turns the camera while the menu is open
        public bool StickNavigation = true;
        public bool KeyboardFallback = true;
        public int KeyToggle = 0x73;

        // [Display]
        public int Position = 0;       // 0 = left, 1 = center, 2 = right
        public int Theme = 0;
        public float Scale = 1.0f;
        public int MaxVisible = 11;
        public bool SaveOnClose = false;
        public bool Speedometer = true;
        public int SpeedUnits = 0;     // 0 = km/h, 1 = mph
        public int Opacity = 85;       // menu panel opacity in percent (40..100)

        // [Performance]
        public float ForceScale = 1.0f;   // advanced: scales every speed change the tuning code makes
        public int BoostMethod = 0;       // 0 smooth (every frame), 1 pulses (5 per second), 2 force

        // [Teleport] / [Vehicles]
        public string[] Slots = new string[6];
        public List<string> Locations = new List<string>();
        public List<Preset> Presets = new List<Preset>();

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
                        case "nitrobutton": NitroCombo = Btn.Parse(val, NitroCombo); break;
                        case "jumpbutton": JumpButton = Btn.Parse(val, JumpButton); break;
                        case "runbutton": RunButton = Btn.Parse(val, RunButton); break;
                        case "camerawhileopen": CameraWhileOpen = U.ParseBool(val, CameraWhileOpen); break;
                        case "opacity": Opacity = U.Clamp(U.ParseInt(val, Opacity), 40, 100); break;
                        case "boostmethod":
                            string bm = val.ToLowerInvariant();
                            BoostMethod = bm.StartsWith("puls") || bm == "1" ? 1 : (bm.StartsWith("force") || bm == "2" ? 2 : 0);
                            break;
                        case "freezeplayerwhileopen": FreezePlayer = U.ParseBool(val, FreezePlayer); break;
                        case "sticknavigation": StickNavigation = U.ParseBool(val, StickNavigation); break;
                        case "keyboardfallback": KeyboardFallback = U.ParseBool(val, KeyboardFallback); break;
                        case "keyboardtogglekey": KeyToggle = U.ParseInt(val, KeyToggle); break;
                        case "position": Position = U.Clamp(U.ParseInt(val, Position), 0, 2); break;
                        case "theme": Theme = U.Clamp(U.ParseInt(val, Theme), 0, Themes.Count - 1); break;
                        case "scale": Scale = U.Clamp(U.ParseFloat(val, Scale), 0.6f, 1.6f); break;
                        case "maxvisibleitems": MaxVisible = U.Clamp(U.ParseInt(val, MaxVisible), 4, 18); break;
                        case "saveonclose": SaveOnClose = U.ParseBool(val, SaveOnClose); break;
                        case "speedometer": Speedometer = U.ParseBool(val, Speedometer); break;
                        case "speedunits": SpeedUnits = val.ToLowerInvariant().StartsWith("mph") || val == "1" ? 1 : 0; break;
                        case "forcescale": ForceScale = U.Clamp(U.ParseFloat(val, ForceScale), 0.1f, 5f); break;
                        case "location": Locations.Add(val); break;
                        case "preset":
                            Preset p = Preset.Parse(val);
                            if (p != null) Presets.Add(p);
                            break;
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
                sb.AppendLine("; Hold while driving for nitro (the Deck's back grips can be mapped to L3 in Steam Input)");
                sb.AppendLine("NitroButton=" + Btn.ToText(NitroCombo));
                sb.AppendLine("; The game's jump and sprint buttons, used by Super Jump / Super Run (X and A on an Xbox-style pad)");
                sb.AppendLine("JumpButton=" + Btn.ToText(JumpButton));
                sb.AppendLine("RunButton=" + Btn.ToText(RunButton));
                sb.AppendLine("FreezePlayerWhileOpen=" + U.B(FreezePlayer));
                sb.AppendLine("; Keep the right stick on the camera while the menu is open");
                sb.AppendLine("CameraWhileOpen=" + U.B(CameraWhileOpen));
                sb.AppendLine("StickNavigation=" + U.B(StickNavigation));
                sb.AppendLine("; Keyboard: arrows/Enter/Backspace navigate, toggle key is a Windows virtual-key code (115 = F4)");
                sb.AppendLine("KeyboardFallback=" + U.B(KeyboardFallback));
                sb.AppendLine("KeyboardToggleKey=" + U.I(KeyToggle));
                sb.AppendLine();
                sb.AppendLine("[Display]");
                sb.AppendLine("; Position: 0 = left, 1 = center, 2 = right");
                sb.AppendLine("Position=" + U.I(Position));
                sb.AppendLine("Theme=" + U.I(Theme));
                sb.AppendLine("Scale=" + U.F(Scale, "0.00"));
                sb.AppendLine("MaxVisibleItems=" + U.I(MaxVisible));
                sb.AppendLine("SaveOnClose=" + U.B(SaveOnClose));
                sb.AppendLine("Speedometer=" + U.B(Speedometer));
                sb.AppendLine("SpeedUnits=" + (SpeedUnits == 1 ? "mph" : "kmh"));
                sb.AppendLine("; Menu panel opacity in percent (40 = very see-through, 100 = solid)");
                sb.AppendLine("Opacity=" + U.I(Opacity));
                sb.AppendLine();
                sb.AppendLine("[Performance]");
                sb.AppendLine("; How the multipliers are applied: smooth (every frame, like the XMC trainer), pulses (5 per second) or force");
                sb.AppendLine("BoostMethod=" + (BoostMethod == 1 ? "pulses" : (BoostMethod == 2 ? "force" : "smooth")));
                sb.AppendLine("; Advanced: scales every speed change the multipliers make (0.1 - 5.0). Leave at 1.0 unless");
                sb.AppendLine("; they feel far too weak or too strong in your setup.");
                sb.AppendLine("ForceScale=" + U.F(ForceScale, "0.00"));
                sb.AppendLine();
                sb.AppendLine("[Teleport]");
                sb.AppendLine("; Saved slots are written by the menu (Teleport > Save Position).");
                for (int i = 0; i < Slots.Length; i++)
                    if (!string.IsNullOrEmpty(Slots[i])) sb.AppendLine("Slot" + (i + 1) + "=" + Slots[i]);
                sb.AppendLine("; Add your own named locations, one per line:  Location=Name|X|Y|Z");
                foreach (string loc in Locations) sb.AppendLine("Location=" + loc);
                sb.AppendLine();
                sb.AppendLine("[Vehicles]");
                sb.AppendLine("; Presets are written by Vehicles > Presets. You can rename one by editing its first field.");
                sb.AppendLine("; Preset=Name|Model|ModelHash|Primary|Secondary|Specular|Trim|Extras|Livery|Dirt|Power|Brakes|Grip|Handbrake|TopSpeedKmh");
                foreach (Preset p in Presets) sb.AppendLine("Preset=" + p.Serialize());
                File.WriteAllText(FilePath, sb.ToString());
                return true;
            }
            catch (Exception ex) { Log.Write("Config save failed: " + ex.Message); return false; }
        }
    }

    #endregion

    #region Look: colours and themes

    internal struct Col
    {
        public byte R, G, B, A;
        public static Col FromArgb(int a, int r, int g, int b)
        {
            Col c; c.A = (byte)a; c.R = (byte)r; c.G = (byte)g; c.B = (byte)b; return c;
        }
        public static Col White { get { return FromArgb(255, 255, 255, 255); } }
        public Col WithAlpha(int a) { return FromArgb(a, R, G, B); }
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

    #endregion

    #region Game data: colours, vehicles, peds, weapons, locations

    /// <summary>One entry of GTA IV's carcols.dat palette (index, name, RGB).</summary>
    internal sealed class CarColor
    {
        public readonly int Index;
        public readonly string Name;
        public readonly Col Rgb;
        public CarColor(int index, string name, int r, int g, int b) { Index = index; Name = name; Rgb = Col.FromArgb(255, r, g, b); }
    }

    internal static class GameData
    {
        // GTA IV's carcols.dat palette. 134-136 only exist in Episodes from Liberty City.
        public static readonly CarColor[] Colors =
        {
            new CarColor(0, "Black", 10, 10, 10),
            new CarColor(1, "Black Metallic", 37, 37, 39),
            new CarColor(2, "Concord Blue Metallic", 101, 106, 121),
            new CarColor(3, "Pewter Gray Metallic", 88, 89, 90),
            new CarColor(4, "Silver Stone Metallic", 156, 161, 163),
            new CarColor(5, "Winning Silver Metallic", 150, 145, 140),
            new CarColor(6, "Steel Gray Metallic", 81, 84, 89),
            new CarColor(7, "Shadow Silver Metallic", 63, 62, 69),
            new CarColor(8, "Silver Stone Metallic", 165, 169, 167),
            new CarColor(9, "Porcelain Silver Metallic", 151, 149, 146),
            new CarColor(10, "Gray Metallic", 118, 123, 124),
            new CarColor(11, "Anthracite Gray Metallic", 90, 87, 82),
            new CarColor(12, "Astra Silver Metallic", 173, 176, 176),
            new CarColor(13, "Ascot Gray", 132, 137, 136),
            new CarColor(14, "Clear Crystal Blue Frost Metallic", 148, 157, 159),
            new CarColor(15, "Silver Metallic", 164, 167, 165),
            new CarColor(16, "Dark Titanium Metallic", 88, 88, 83),
            new CarColor(17, "Titanium Frost Metallic", 164, 160, 150),
            new CarColor(18, "Police White", 175, 177, 177),
            new CarColor(19, "Medium Gray Metallic", 109, 108, 110),
            new CarColor(20, "Medium Gray Metallic", 100, 104, 106),
            new CarColor(21, "Steel Gray Metallic", 82, 86, 97),
            new CarColor(22, "Slate Gray", 140, 146, 154),
            new CarColor(23, "Gun Metal Metallic", 91, 93, 94),
            new CarColor(24, "Light Blue Grey", 189, 190, 198),
            new CarColor(25, "Securicor Light Gray", 182, 182, 182),
            new CarColor(26, "Arctic White", 100, 100, 100),
            new CarColor(27, "Very Red", 226, 6, 6),
            new CarColor(28, "Torino Red Pearl", 150, 8, 0),
            new CarColor(29, "Formula Red", 107, 0, 0),
            new CarColor(30, "Blaze Red", 97, 16, 9),
            new CarColor(31, "Graceful Red Mica", 74, 10, 10),
            new CarColor(32, "Garnet Red Metallic", 115, 11, 11),
            new CarColor(33, "Desert Red", 87, 7, 7),
            new CarColor(34, "Cabernet Red Metallic", 38, 3, 6),
            new CarColor(35, "Turismo Red", 158, 0, 0),
            new CarColor(36, "Desert Red", 20, 0, 2),
            new CarColor(37, "Currant Red Solid", 15, 4, 4),
            new CarColor(38, "Bright Currant Red Metallic", 15, 8, 10),
            new CarColor(39, "Electric Currant Red Metallic", 57, 25, 29),
            new CarColor(40, "Medium Cabernet Solid", 85, 39, 37),
            new CarColor(41, "Wild Strawberry Metallic", 76, 41, 41),
            new CarColor(42, "Medium Red Solid", 116, 29, 40),
            new CarColor(43, "Bright Red", 109, 40, 55),
            new CarColor(44, "Bright Red", 115, 10, 39),
            new CarColor(45, "Medium Garnet Red Metallic", 100, 13, 27),
            new CarColor(46, "Brilliant Red Metallic", 98, 11, 28),
            new CarColor(47, "Brilliant Red Metallic 2", 115, 24, 39),
            new CarColor(48, "Alabaster Solid", 171, 152, 143),
            new CarColor(49, "Twilight Blue Metallic", 32, 32, 44),
            new CarColor(50, "Torch Red", 68, 98, 79),
            new CarColor(51, "Green", 46, 91, 32),
            new CarColor(52, "Deep Jewel Green", 30, 46, 50),
            new CarColor(53, "Agate Green", 48, 79, 69),
            new CarColor(54, "Petrol Blue Green Metallic", 77, 98, 104),
            new CarColor(55, "Hoods", 94, 112, 114),
            new CarColor(56, "Green", 25, 56, 38),
            new CarColor(57, "Dark Green Metallic", 45, 58, 53),
            new CarColor(58, "Rio Red", 51, 95, 63),
            new CarColor(59, "Securicor Dark Green", 71, 120, 60),
            new CarColor(60, "Seafoam Metallic", 147, 163, 150),
            new CarColor(61, "Pastel Alabaster Solid", 154, 167, 144),
            new CarColor(62, "Midnight Blue", 38, 55, 57),
            new CarColor(63, "Striking Blue", 76, 117, 183),
            new CarColor(64, "Saxony Blue Metallic", 70, 89, 122),
            new CarColor(65, "Jasper Green Metallic", 93, 126, 141),
            new CarColor(66, "Mariner Blue", 59, 78, 120),
            new CarColor(67, "Harbor Blue Metallic", 61, 74, 104),
            new CarColor(68, "Diamond Blue Metallic", 109, 122, 136),
            new CarColor(69, "Surf Blue", 22, 34, 72),
            new CarColor(70, "Nautical Blue Metallic", 39, 47, 75),
            new CarColor(71, "Light Crystal Blue Metallic", 78, 104, 129),
            new CarColor(72, "Medium Regatta Blue Metallic", 106, 122, 140),
            new CarColor(73, "Spinnaker Blue Solid", 111, 130, 151),
            new CarColor(74, "Ultra Blue Metallic", 14, 49, 109),
            new CarColor(75, "Bright Blue Metallic", 57, 90, 131),
            new CarColor(76, "Nassau Blue Metallic", 32, 75, 107),
            new CarColor(77, "Medium Sapphire Blue Metallic", 43, 62, 87),
            new CarColor(78, "Steel Blue Metallic", 54, 65, 85),
            new CarColor(79, "Light Sapphire Blue Metallic", 108, 132, 149),
            new CarColor(80, "Malachite Metallic", 77, 93, 96),
            new CarColor(81, "Medium Maui Blue Metallic", 64, 108, 143),
            new CarColor(82, "Bright Blue Metallic", 19, 69, 115),
            new CarColor(83, "Bright Blue Metallic", 16, 80, 130),
            new CarColor(84, "Blue", 56, 86, 148),
            new CarColor(85, "Dark Sapphire Blue Metallic", 0, 28, 50),
            new CarColor(86, "Light Sapphire Blue Metallic", 89, 110, 135),
            new CarColor(87, "Medium Sapphire Blue Firemist", 34, 52, 87),
            new CarColor(88, "Twilight Blue Metallic", 32, 32, 44),
            new CarColor(89, "Taxi Yellow", 245, 137, 15),
            new CarColor(90, "Race Yellow Solid", 145, 115, 71),
            new CarColor(91, "Pastel Alabaster", 142, 140, 70),
            new CarColor(92, "Oxford White Solid", 170, 173, 142),
            new CarColor(93, "Flax", 174, 155, 127),
            new CarColor(94, "Medium Flax", 150, 129, 108),
            new CarColor(95, "Pueblo Beige", 122, 117, 96),
            new CarColor(96, "Light Ivory", 157, 152, 114),
            new CarColor(97, "Smoke Silver Metallic", 152, 149, 134),
            new CarColor(98, "Bisque Frost Metallic", 156, 141, 113),
            new CarColor(99, "Classic Red", 105, 30, 59),
            new CarColor(100, "Vermilion Solid", 114, 42, 63),
            new CarColor(101, "Vermillion Solid", 124, 27, 68),
            new CarColor(102, "Biston Brown Metallic", 34, 25, 24),
            new CarColor(103, "Light Beechwood Metallic", 127, 105, 86),
            new CarColor(104, "Dark Beechwood Metallic", 71, 53, 50),
            new CarColor(105, "Dark Sable Metallic", 105, 88, 83),
            new CarColor(106, "Medium Beechwood Metallic", 98, 68, 40),
            new CarColor(107, "Woodrose Metallic", 125, 98, 86),
            new CarColor(108, "Sandalwood Frost Metallic", 170, 157, 132),
            new CarColor(109, "Medium Sandalwood Metallic", 123, 113, 94),
            new CarColor(110, "Copper Beige", 171, 146, 118),
            new CarColor(111, "Warm Grey Mica", 99, 92, 90),
            new CarColor(112, "White", 201, 201, 201),
            new CarColor(113, "Frost White", 214, 218, 214),
            new CarColor(114, "Honey Beige Metallic", 159, 157, 148),
            new CarColor(115, "Seafoam Metallic", 147, 163, 150),
            new CarColor(116, "Light Titanium Metallic", 156, 156, 152),
            new CarColor(117, "Light Champagne Metallic", 167, 162, 143),
            new CarColor(118, "Arctic Pearl", 15, 106, 137),
            new CarColor(119, "Light Driftwood Metallic", 161, 153, 131),
            new CarColor(120, "White Diamond Pearl", 163, 173, 198),
            new CarColor(121, "Antelope Beige", 155, 139, 128),
            new CarColor(122, "Currant Blue Metallic", 132, 148, 171),
            new CarColor(123, "Crystal Blue Metallic", 158, 164, 171),
            new CarColor(124, "Temple Curtain Purple", 134, 68, 110),
            new CarColor(125, "Cherry Red", 226, 6, 6),
            new CarColor(126, "Securicor Dark Green", 71, 120, 60),
            new CarColor(127, "Taxi Yellow", 215, 142, 16),
            new CarColor(128, "Police Car Blue", 42, 119, 161),
            new CarColor(129, "Mellow Burgundy", 66, 31, 33),
            new CarColor(130, "Desert Taupe Metallic", 111, 103, 95),
            new CarColor(131, "Lammy Orange", 252, 38, 0),
            new CarColor(132, "Lammy Yellow", 252, 109, 0),
            new CarColor(133, "Very White", 255, 255, 255),
            new CarColor(134, "SuperD Yellow (EFLC)", 245, 180, 0),
            new CarColor(135, "Black (EFLC)", 5, 5, 5),
            new CarColor(136, "Gold (EFLC)", 189, 149, 75),
        };

        public static CarColor ColorAt(int index)
        {
            return index >= 0 && index < Colors.Length ? Colors[index] : null;
        }

        public static string ColorName(int index)
        {
            CarColor c = ColorAt(index);
            return c == null ? "#" + U.I(index) : c.Name;
        }

        // Spawner categories: first entry is the category name, the rest are model
        // names. Models that aren't in the running game/episode are detected at
        // runtime (IS_MODEL_IN_CDIMAGE) and skipped, so EFLC models can sit here safely.
        public static readonly string[][] VehicleClasses =
        {
            new[] { "Sports & Super", "INFERNUS", "TURISMO", "COMET", "BANSHEE", "SULTANRS", "COQUETTE", "SUPERGT", "FELTZER", "F620", "BULLET", "SUPERD", "BUFFALO" },
            new[] { "Muscle", "SABREGT", "SABRE", "SABRE2", "STALION", "DUKES", "VIGERO", "VIGERO2", "RUINER", "BUCCANEER", "FACTION", "VOODOO", "TAMPA", "SLAMVAN" },
            new[] { "Coupes & Compacts", "BLISTA", "FUTO", "FORTUNE", "URANUS", "DILETTANTE", "PEYOTE", "MANANA", "CHAVOS", "HAKUMAI" },
            new[] { "Sedans", "ADMIRAL", "ORACLE", "SCHAFTER", "PRES", "PMP600", "SULTAN", "COGNOSCENTI", "DF8", "EMPEROR", "ESPERANTO", "FEROCI", "INGOT", "INTRUDER", "LOKUS", "MARBELLA", "MERIT", "PINNACLE", "PREMIER", "PRIMO", "SENTINEL", "SOLAIR", "STRATUM", "VINCENT", "VIRGO", "WASHINGTON", "WILLARD", "STRETCH", "ROMERO" },
            new[] { "SUVs & Pickups", "CAVALCADE", "PATRIOT", "HUNTLEY", "LANDSTALKER", "REBLA", "FXT", "HABANERO", "BOBCAT", "RANCHER", "SERRANO" },
            new[] { "Vans & Trucks", "BENSON", "BOXVILLE", "BURRITO", "BURRITO2", "FLATBED", "MULE", "PACKER", "PHANTOM", "PONY", "SPEEDO", "STEED", "YANKEE", "MINIVAN", "MOONBEAM", "PERENNIAL", "MRTASTY", "BIFF", "TRASH", "FORKLIFT", "AIRTUG" },
            new[] { "Emergency", "POLICE", "POLICE2", "POLPATRIOT", "NOOSE", "NSTOCKADE", "FBI", "AMBULANCE", "FIRETRUK", "PSTOCKADE", "APC" },
            new[] { "Public & Service", "BUS", "TAXI", "TAXI2", "CABBY", "STOCKADE", "ROM" },
            new[] { "Motorcycles", "NRG900", "PCJ", "SANCHEZ", "FAGGIO", "HELLFURY", "ZOMBIEB", "BOBBER", "ANGEL", "DAEMON", "DIABOLUS", "DOUBLE", "HAKUCHOU", "HEXER", "INNOVATION", "LYCAN", "NIGHTBLADE", "REVENANT", "WAYFARER", "WOLFSBANE", "AKUMA", "BATI", "BATI2", "VADER" },
            new[] { "Helicopters", "MAVERICK", "POLMAV", "TOURMAV", "ANNIHILATOR", "BUZZARD", "SWIFT", "SKYLIFT" },
            new[] { "Boats", "JETMAX", "SQUALO", "PREDATOR", "DINGHY", "MARQUIS", "REEFER", "TROPIC", "TUGA", "SMUGGLER", "FLOATER", "BLADE" },
        };

        // Player models, as "Label=MODEL". Like the vehicles, any model the running
        // game doesn't have is reported as unavailable instead of being loaded.
        public static readonly string[][] PedClasses =
        {
            new[] { "Story Characters", "Niko Bellic=PLAYER", "Roman Bellic=IG_ROMAN", "Brucie Kibbutz=IG_BRUCIE", "Little Jacob=IG_LILJACOB", "Packie McReary=IG_PACKIE_MC", "Dwayne Forge=IG_DWAYNE", "Phil Bell=IG_PHIL_BELL", "Playboy X=IG_PLAYBOY_X", "Elizabeta Torres=IG_ELIZABETA", "Michelle=IG_MICHELLE", "Vlad Glebov=IG_VLAD", "Mikhail Faustin=IG_FAUSTIN", "Dimitri Rascalov=IG_DMITRI", "Francis McReary=IG_FRANCIS_MC", "Gerald McReary=IG_GERRY_MC", "Derrick McReary=IG_DERRICK_MC", "Ray Boccino=IG_RAY_BOCCINO", "Jimmy Pegorino=IG_JIMMY_PEGORINO", "Bernie Crane=IG_BERNIE_CRANE", "Manny Escuela=IG_MANNY", "Mallorie=IG_MALLORIE", "Ray Bulgarin=IG_BULGARIN" },
            new[] { "Police & Services", "LCPD Officer=M_Y_COP", "LCPD Sergeant=M_M_FATCOP_01", "Traffic Cop=M_Y_COP_TRAFFIC", "State Trooper=M_Y_STROOPER", "NOOSE=M_Y_SWAT", "FIB Agent=M_M_FBI", "Heli Pilot=M_Y_NHELIPILOT", "Security Guard=M_M_ARMOURED", "Firefighter=M_Y_FIREMAN", "Paramedic=M_Y_PMEDIC" },
            new[] { "Gangs", "Mafia=M_Y_GMAF_HI_01", "Russian=M_Y_GRUS_LO_01", "Triad=M_Y_GTRI_LO_01", "Jamaican=M_Y_GJAM_LO_01", "Albanian=M_Y_GALB_LO_01", "Irish=M_Y_GIRI_LO_01", "Korean=M_Y_GKOR_LO_01", "Latino=M_Y_GLAT_LO_01", "Northwood=M_Y_GAFR_LO_01", "Biker=M_Y_GBIK_LO_01" },
            new[] { "Civilians", "Businessman=M_Y_BUSINESS_01", "Executive=M_M_BUSINESS_02", "Gym Guy=M_Y_CLUBFIT", "Jogger=M_Y_RUNNER", "Homeless Man=M_M_TRAMPBLACK", "Businesswoman=F_Y_BUSINESS_01", "Street Walker=F_Y_HOOKER_01" },
        };

        public sealed class WeaponInfo
        {
            public readonly int Id; public readonly string Name; public readonly int Episode;   // -1 = every episode
            public WeaponInfo(int id, string name, int episode) { Id = id; Name = name; Episode = episode; }
            public bool UsesAmmo { get { return Id >= 4 && Id != 24 && Id != 41; } }
            public bool HasClip { get { return Id >= 7 && Id != 28 && Id != 36 && Id != 41; } }
        }

        // IDs from ScriptHookDotNet's Weapon enum. Episode: 0 IV, 1 The Lost and Damned, 2 The Ballad of Gay Tony.
        public static readonly WeaponInfo[] Weapons =
        {
            new WeaponInfo(1, "Baseball Bat", -1), new WeaponInfo(2, "Pool Cue", 0), new WeaponInfo(3, "Knife", -1),
            new WeaponInfo(4, "Grenades", -1), new WeaponInfo(5, "Molotovs", -1),
            new WeaponInfo(7, "Pistol", -1), new WeaponInfo(9, "Combat Pistol", -1),
            new WeaponInfo(10, "Pump Shotgun", -1), new WeaponInfo(11, "Combat Shotgun", -1),
            new WeaponInfo(12, "Micro SMG", -1), new WeaponInfo(13, "SMG", -1),
            new WeaponInfo(14, "Assault Rifle", -1), new WeaponInfo(15, "Carbine Rifle", -1),
            new WeaponInfo(16, "Combat Sniper", -1), new WeaponInfo(17, "Sniper Rifle", -1), new WeaponInfo(18, "Rocket Launcher", -1),
            new WeaponInfo(21, "Grenade Launcher", 1), new WeaponInfo(22, "Assault Shotgun", 1), new WeaponInfo(24, "Pool Cue", 1),
            new WeaponInfo(26, "Sawed-Off Shotgun", 1), new WeaponInfo(27, "Automatic 9mm", 1), new WeaponInfo(28, "Pipe Bombs", 1),
            new WeaponInfo(21, "Grenade Launcher", 2), new WeaponInfo(29, ".44 Pistol", 2), new WeaponInfo(30, "Explosive Shotgun", 2),
            new WeaponInfo(31, "Assault Shotgun", 2), new WeaponInfo(32, "Assault SMG", 2), new WeaponInfo(33, "Gold SMG", 2),
            new WeaponInfo(34, "Advanced MG", 2), new WeaponInfo(35, "Advanced Sniper", 2), new WeaponInfo(36, "Sticky Bombs", 2),
            new WeaponInfo(41, "Parachute", 2),
        };

        public sealed class Place
        {
            public readonly string Area, Name; public readonly float X, Y, Z;
            public Place(string area, string name, float x, float y, float z) { Area = area; Name = name; X = x; Y = y; Z = z; }
        }

        // Well-known Liberty City spots. Coordinates are in-game X/Y/Z (Z is only a
        // hint: teleporting loads the area and finds the ground). Taken from the
        // CLEO Redux TaxiMission script's in-game positions and the GTAIVGuessr
        // location list, which agree with each other.
        public static readonly Place[] Places =
        {
            new Place("Algonquin", "Star Junction", -190.8f, 361.4f, 14.8f),
            new Place("Algonquin", "Middle Park", -170.1f, 1178.3f, 14.2f),
            new Place("Algonquin", "Libertonian Museum", -61.1f, 809.9f, 14.1f),
            new Place("Algonquin", "Majestic Hotel", -172f, 599f, 15f),
            new Place("Algonquin", "The Triangle", -202f, 268f, 15f),
            new Place("Algonquin", "Chinatown (Bank of Liberty)", -62f, -435f, 6f),
            new Place("Algonquin", "Castle Gardens", -156.0f, -801.6f, 4.9f),
            new Place("Algonquin", "Algonquin Heli Tours", 297.0f, -679.7f, 4.2f),
            new Place("Algonquin", "Holland Hospital", -391.7f, 1268.4f, 22.5f),
            new Place("Algonquin", "Happiness Island", -591f, -855f, 5f),
            new Place("Broker & Dukes", "Hove Beach Station", 997.7f, -544.7f, 14.3f),
            new Place("Broker & Dukes", "Funland (Firefly Island)", 950.1f, -660.4f, 13.7f),
            new Place("Broker & Dukes", "Brucie's Autos", 843f, -102f, 6f),
            new Place("Broker & Dukes", "Little Jacob's Hangout", 1475f, 62f, 25f),
            new Place("Broker & Dukes", "Faustin's Mansion", 1348f, -845f, 15f),
            new Place("Broker & Dukes", "Schottler Medical Center", 1190.5f, 195.3f, 31.9f),
            new Place("Broker & Dukes", "Steinway Park", 847.2f, 733.6f, 7.2f),
            new Place("Broker & Dukes", "McReary Residence", 1413f, 616f, 35f),
            new Place("Broker & Dukes", "Francis Intl. Airport", 2357.7f, 371.2f, 6.1f),
            new Place("Bohan", "Bohan Park", 464f, 1920f, 15f),
            new Place("Bohan", "Northern Expressway Bridge", 1041f, 1475f, 20f),
            new Place("Bohan", "Burger Shot Bohan", 1114f, 1636f, 15f),
            new Place("Alderney", "Alderney State Correctional", -1051f, -447f, 5f),
            new Place("Alderney", "Acter Park", -1756f, 251f, 25f),
            new Place("Alderney", "Port Tudor", -1169f, 644f, 5f),
            new Place("Alderney", "International Online", -891.9f, 1039.6f, 20.1f),
            new Place("Alderney", "Leftwood Church", -1057f, 1410f, 20f),
            new Place("Alderney", "Pegorino's House", -1394f, 1436f, 25f),
            new Place("Alderney", "Abandoned Casino", -1163f, 1867f, 10f),
        };

        public static readonly string[] WeatherNames = { "Extra Sunny", "Sunny", "Sunny & Windy", "Cloudy", "Rain", "Drizzle", "Foggy", "Thunderstorm" };
        public static readonly float[] TimeScales = { 1.0f, 0.75f, 0.5f, 0.25f, 0.1f };
        public static readonly string[] TimeScaleNames = { "Normal", "75%", "50%", "25%", "10%" };
        public static readonly float[] Densities = { 0.0f, 0.25f, 0.5f, 1.0f, 1.5f, 2.0f };
        public static readonly string[] DensityNames = { "None", "Low", "Half", "Normal", "High", "Max" };
        public const int NormalDensity = 3;
    }

    #endregion

    #region Menu model

    internal enum RowKind { Normal, Toggle, Submenu, Header, Color, Info }

    internal abstract class MenuItem
    {
        public string Label;
        public string Help;
        /// <summary>Text on the right of the row. Read on the script thread only.</summary>
        public virtual string Value { get { return null; } }
        public virtual RowKind Kind { get { return RowKind.Normal; } }
        public virtual bool Selectable { get { return true; } }
        public virtual bool Adjustable { get { return false; } }
        public virtual bool On { get { return false; } }
        public virtual bool HasSwatch { get { return false; } }
        public virtual Col Swatch { get { return Col.White; } }
        public virtual void Activate(MenuScript m) { }
        public virtual void Adjust(MenuScript m, int dir) { }
        public virtual void Sync() { }
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
        public override RowKind Kind { get { return RowKind.Toggle; } }
        public override bool On { get { return get(); } }
        public override bool Adjustable { get { return true; } }
        public override void Activate(MenuScript m) { set(!get()); }
        public override void Adjust(MenuScript m, int dir) { bool v = dir > 0; if (v != get()) set(v); }
    }

    internal sealed class SubmenuItem : MenuItem
    {
        private readonly Menu target;
        private readonly Func<Menu> builder;
        private readonly Func<string> valueFn;
        public SubmenuItem(Menu target, string help) { this.target = target; Label = target.Title; Help = help; }
        public SubmenuItem(Menu target) : this(target, (string)null) { }
        /// <summary>A submenu row that also shows a value, e.g. the current paint colour.</summary>
        public SubmenuItem(Menu target, Func<string> value, string help) : this(target, help) { valueFn = value; }
        /// <summary>A submenu built fresh every time it's opened.</summary>
        public SubmenuItem(string label, Func<Menu> builder, string help) { Label = label; this.builder = builder; Help = help; }
        public override RowKind Kind { get { return RowKind.Submenu; } }
        public override string Value { get { return valueFn == null ? null : valueFn() + "  >"; } }
        public override void Activate(MenuScript m)
        {
            Menu t = builder != null ? builder() : target;
            if (t != null) m.Push(t);
        }
    }

    /// <summary>Cycles through options with Left/Right; A applies (or it applies live).</summary>
    internal sealed class ListItem : MenuItem
    {
        private readonly string[] options;
        private readonly Action<int> apply;
        private readonly bool live;
        private readonly Func<int> current;
        public int Index;

        public ListItem(string label, string[] options, Func<int> current, Action<int> apply, bool live, string help)
        {
            // The current value is read in Sync() when the menu opens, not here:
            // menus are built in the constructor, before the game is ready.
            Label = label; this.options = options; this.current = current; this.apply = apply; this.live = live; Help = help;
        }

        public override void Sync() { if (current != null) Index = U.Clamp(current(), 0, options.Length - 1); }
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
        private readonly Func<int> maxFn;
        private readonly Func<int> current;
        private readonly Action<int> apply;
        private readonly bool live;
        private readonly string suffix;
        public int Number;

        public NumberItem(string label, int min, int max, int step, Func<int> current, Action<int> apply, bool live, string suffix, string help)
            : this(label, min, max, null, step, current, apply, live, suffix, help) { }

        /// <summary>maxFn, if given, is read each time (e.g. a car's livery count).</summary>
        public NumberItem(string label, int min, int max, Func<int> maxFn, int step, Func<int> current, Action<int> apply, bool live, string suffix, string help)
        {
            Label = label; this.min = min; this.max = max; this.maxFn = maxFn; this.step = step; this.current = current;
            this.apply = apply; this.live = live; this.suffix = suffix ?? ""; Help = help;
            Number = min;
        }

        private int Max { get { return maxFn != null ? Math.Max(min, maxFn()) : max; } }
        public override void Sync() { if (current != null) Number = U.Clamp(current(), min, Max); }
        public override string Value { get { return "< " + U.I(Number) + suffix + " >"; } }
        public override bool Adjustable { get { return true; } }
        public override void Adjust(MenuScript m, int dir)
        {
            int hi = Max;
            Number += dir * step;
            if (Number > hi) Number = min;
            if (Number < min) Number = hi;
            if (live) apply(Number);
        }
        public override void Activate(MenuScript m) { apply(Number); }
    }

    /// <summary>A multiplier or other decimal value shown as "&lt; 2.5x &gt;". Stops at the ends.</summary>
    internal sealed class FloatItem : MenuItem
    {
        private readonly float min, max, step;
        private readonly Func<float> get;
        private readonly Action<float> set;
        private readonly string format, suffix, offText;

        public FloatItem(string label, float min, float max, float step, Func<float> get, Action<float> set, string format, string suffix, string offText, string help)
        {
            Label = label; this.min = min; this.max = max; this.step = step; this.get = get; this.set = set;
            this.format = format; this.suffix = suffix ?? ""; this.offText = offText; Help = help;
        }

        public override string Value
        {
            get
            {
                float v = get();
                string text = (offText != null && v <= min + step * 0.01f) ? offText : U.F(v, format) + suffix;
                return (v > min + step * 0.01f ? "< " : "  ") + text + (v < max - step * 0.01f ? " >" : "  ");
            }
        }
        public override bool Adjustable { get { return true; } }
        public override void Adjust(MenuScript m, int dir)
        {
            float v = (float)Math.Round((get() + dir * step) / step) * step;
            set(U.Clamp(v, min, max));
        }
        public override void Activate(MenuScript m) { set(min); }
    }

    /// <summary>A paint colour row with a swatch. Highlighting previews it; A keeps it.</summary>
    internal sealed class ColorItem : MenuItem
    {
        public readonly CarColor Color;
        private readonly Action<CarColor> confirm;
        public ColorItem(CarColor color, Action<CarColor> confirm) { Color = color; this.confirm = confirm; Label = color.Name; }
        public override RowKind Kind { get { return RowKind.Color; } }
        public override bool HasSwatch { get { return true; } }
        public override Col Swatch { get { return Color.Rgb; } }
        public override string Value { get { return "#" + U.I(Color.Index); } }
        public override void Activate(MenuScript m) { confirm(Color); }
    }

    internal sealed class HeaderItem : MenuItem
    {
        public HeaderItem(string label) { Label = label; }
        public override RowKind Kind { get { return RowKind.Header; } }
        public override bool Selectable { get { return false; } }
    }

    /// <summary>A read-only line such as "Cash: $1,234".</summary>
    internal sealed class InfoItem : MenuItem
    {
        private readonly Func<string> value;
        public InfoItem(string label, Func<string> value) { Label = label; this.value = value; }
        public override RowKind Kind { get { return RowKind.Info; } }
        public override bool Selectable { get { return false; } }
        public override string Value { get { return value(); } }
    }

    internal sealed class Menu
    {
        public string Title;
        public List<MenuItem> Items = new List<MenuItem>();
        public int Selected = -1;
        public int Scroll;
        public Action<Menu> OnEnter;
        public Action<Menu> OnExit;
        public Action<Menu, MenuItem> OnHighlight;

        public Menu(string title) { Title = title; }
        public Menu Add(MenuItem item) { Items.Add(item); return this; }
        public Menu Header(string label) { Items.Add(new HeaderItem(label)); return this; }

        public MenuItem Current { get { return Selected >= 0 && Selected < Items.Count ? Items[Selected] : null; } }

        /// <summary>Keep the selection on a selectable row after the list changed.</summary>
        public void FixSelection()
        {
            if (Selected >= Items.Count) Selected = Items.Count - 1;
            if (Selected < 0) Selected = 0;
            if (Items.Count == 0) { Selected = -1; return; }
            if (!Items[Selected].Selectable) Move(1, false);
        }

        /// <summary>Move to the next selectable row; wraps unless paging.</summary>
        public bool Move(int dir, bool wrap)
        {
            int n = Items.Count;
            if (n == 0) return false;
            int i = Selected < 0 ? (dir > 0 ? -1 : n) : Selected;
            for (int k = 0; k < n; k++)
            {
                i += dir;
                if (i >= n) { if (!wrap) return false; i = 0; }
                if (i < 0) { if (!wrap) return false; i = n - 1; }
                if (Items[i].Selectable) { bool changed = i != Selected; Selected = i; return changed; }
            }
            return false;
        }

        public void Page(int dir, int rows)
        {
            int target = U.Clamp(Selected + dir * rows, 0, Math.Max(0, Items.Count - 1));
            Selected = target;
            if (!Items[Selected].Selectable && !Move(dir, false)) Move(-dir, false);
        }
    }

    #endregion

    #region Frame snapshot (built in Tick, drawn in PerFrameDrawing)

    internal sealed class RowView
    {
        public string Label, Value;
        public RowKind Kind;
        public bool On, HasSwatch;
        public Col Swatch;
    }

    /// <summary>
    /// Everything the drawing code needs for one frame. Tick builds a new one and
    /// swaps the reference; the drawing code never touches menus or natives.
    /// </summary>
    internal sealed class FrameView
    {
        public bool MenuOpen;
        public string Crumb, Counter, Help, Hints;
        public RowView[] Rows = new RowView[0];
        public int SelRow = -1;            // index into Rows, -1 if none
        public int Scroll, Count;
        public Theme Theme;
        public float Scale = 1f;
        public int Position;

        public bool Speedo;
        public string SpeedText, SpeedUnit, TuneText;
        public float SpeedFrac;            // 0..1 for the bar
        public bool NitroShown, NitroActive;
        public float NitroLevel;           // 0..1

        public string Coords, Fps, Overlay, DebugCar, DebugFoot, Chaos;
        public float Opacity = 0.85f;
        public string Toast;
        public int ToastUntil;
    }

    #endregion

    #region Script core: tick, menu navigation, rebinding

    public partial class MenuScript : Script
    {
        private const string Version = "1.4.1";

        private readonly Config cfg = new Config();
        private readonly InputState input = new InputState();
        private readonly Stack<Menu> stack = new Stack<Menu>();
        private readonly Random rng = new Random();
        private Menu root;

        private bool open;
        private bool weFroze;
        private bool waitRelease;      // swallow buttons until released after opening
        private int now;
        private float dt = 1f / 60f;   // game frame time in seconds (follows slow motion)
        private int pi;                // player index, refreshed every tick
        private int nextSlowTick;
        private bool paused;

        // Rebind capture
        private int captureTarget;     // 0 none, 1 open, 2 close, 3 nitro, 4 jump, 5 run
        private bool captureArmed;
        private uint captureUnion;
        private int captureDeadline;

        // Toast message (drawn by our own code, so it always shows)
        private string toastText;
        private int toastUntil;

        // Hand-off to the drawing code
        private volatile FrameView frame;

        public MenuScript()
        {
            string dir = ResolveScriptsDir();
            Log.PathName = Path.Combine(dir, "ClaudeMenu.log");
            try { File.WriteAllText(Log.PathName, "ClaudeMenu " + Version + " started" + Environment.NewLine); } catch (Exception) { }

            cfg.Load(Path.Combine(dir, "ClaudeMenu.ini"));
            ApplyInputConfig();
            BuildMenus();

            Interval = 0;
            Tick += OnTick;
            // Drawing has to happen in ScriptHookDotNet's per-frame drawing event.
            // Drawing natives (DRAW_RECT, DISPLAY_TEXT...) called from Tick run
            // without errors but never show up on screen.
            PerFrameDrawing += new GraphicsEventHandler(OnFrameDraw);
            Log.Write("Open combo " + Btn.ToText(cfg.OpenCombo) + ", close combo " + Btn.ToText(cfg.CloseCombo) + ", nitro " + Btn.ToText(cfg.NitroCombo));
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
                Pointer ft = new Pointer(typeof(float));
                dt = N.Call("GET_FRAME_TIME", ft) ? U.Clamp((float)ft, 0f, 0.1f) : 1f / 60f;
                pi = PlayerIndex();
                paused = N.Get<bool>("IS_PAUSE_MENU_ACTIVE", false);

                if (!paused)
                {
                    if (freecamOn) UpdateFreecam();
                    else if (!open)
                    {
                        if (input.ComboPressed(cfg.OpenCombo) || input.Pressed(Btn.NavToggle)) Open();
                    }
                    else HandleMenuInput();
                }

                RunFeatures();
            }
            catch (Exception ex)
            {
                Log.Write("Tick error: " + ex);
            }

            try { BuildFrame(); }
            catch (Exception ex) { Log.Write("Frame build error: " + ex); }
        }

        private void Open()
        {
            open = true;
            waitRelease = true;
            stack.Clear();
            stack.Push(root);
            Enter(root);
            if (cfg.FreezePlayer) FreezePlayer(true);
            N.Call("PLAY_AUDIO_EVENT", "FRONTEND_MENU_HIGHLIGHT_DOWN_UP");
        }

        internal void Close()
        {
            open = false;
            captureTarget = 0;
            while (stack.Count > 0)
            {
                Menu m = stack.Pop();
                if (m.OnExit != null) m.OnExit(m);
            }
            FreezePlayer(false);
            if (cfg.SaveOnClose) cfg.Save();
        }

        private bool camFreed;   // we told the game to keep camera controls while player controls are off

        /// <summary>
        /// Freeze or unfreeze the player for the menu. With CameraWhileOpen the right
        /// stick keeps turning the camera: SET_CAMERA_CONTROLS_DISABLED_WITH_PLAYER_CONTROLS(false)
        /// before SET_PLAYER_CONTROL(false), as the XMC trainer does.
        /// </summary>
        private void FreezePlayer(bool freeze)
        {
            if (freeze && !weFroze)
            {
                if (cfg.CameraWhileOpen) camFreed = N.Call("SET_CAMERA_CONTROLS_DISABLED_WITH_PLAYER_CONTROLS", false);
                weFroze = N.Call("SET_PLAYER_CONTROL", pi, false);
            }
            else if (!freeze)
            {
                if (weFroze) { N.Call("SET_PLAYER_CONTROL", pi, true); weFroze = false; }
                // Put the game's default back, so missions that take control also lock the camera.
                if (camFreed) { N.Call("SET_CAMERA_CONTROLS_DISABLED_WITH_PLAYER_CONTROLS", true); camFreed = false; }
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
            foreach (MenuItem it in m.Items) it.Sync();
            m.FixSelection();
        }

        /// <summary>Rebuild the current menu's items (after its OnEnter changed them).</summary>
        internal void Refresh(Menu m)
        {
            foreach (MenuItem it in m.Items) it.Sync();
            m.FixSelection();
        }

        private void Back()
        {
            Menu top = stack.Pop();
            if (top.OnExit != null) top.OnExit(top);
            if (stack.Count == 0) { Close(); return; }
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
            if (m.Items.Count > 0)
            {
                int before = m.Selected;
                if (input.Repeat(Btn.NavUp, now)) m.Move(-1, true);
                if (input.Repeat(Btn.NavDown, now)) m.Move(1, true);
                if (input.Repeat(Btn.LT, now)) m.Page(-1, cfg.MaxVisible);
                if (input.Repeat(Btn.RT, now)) m.Page(1, cfg.MaxVisible);
                if (m.Selected != before)
                {
                    Beep();
                    if (m.OnHighlight != null && m.Current != null) m.OnHighlight(m, m.Current);
                }

                MenuItem item = m.Current;
                if (item != null && item.Adjustable)
                {
                    if (input.Repeat(Btn.NavLeft, now)) { item.Adjust(this, -1); Beep(); }
                    if (input.Repeat(Btn.NavRight, now)) { item.Adjust(this, 1); Beep(); }
                }
                if (item != null && item.Selectable && input.Pressed(Btn.NavAccept))
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
        private static readonly string[] CaptureNames = { "", "OPEN", "CLOSE", "NITRO", "JUMP", "RUN" };

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

            // Menu combos need 2+ buttons so normal play can't trigger them.
            if (target <= 2 && Btn.Count(combo) < 2) { Notify("Use at least 2 buttons so the game doesn't trigger it by accident"); return; }
            if (target == 1 && combo == cfg.CloseCombo) { Notify("That combo is already used to close"); return; }
            if (target == 2 && combo == cfg.OpenCombo) { Notify("That combo is already used to open"); return; }

            if (target == 1) cfg.OpenCombo = combo;
            else if (target == 2) cfg.CloseCombo = combo;
            else if (target == 3) cfg.NitroCombo = combo;
            else if (target == 4) cfg.JumpButton = combo;
            else cfg.RunButton = combo;
            cfg.Save();
            Notify(CaptureNames[target].Substring(0, 1) + CaptureNames[target].Substring(1).ToLowerInvariant() + " set to " + Btn.ToText(combo));
        }

        // ------------------------------------------------------------ helpers
        internal void Notify(string msg)
        {
            toastText = msg;
            toastUntil = Environment.TickCount + 2800;
        }

        private static int PlayerIndex()
        {
            int id = N.Get<int>("GET_PLAYER_ID", 0);
            return N.Get<int>("CONVERT_INT_TO_PLAYERINDEX", id, id);
        }

        private static Ped PlayerPed()
        {
            Player p = Game.LocalPlayer;
            if (p == null) return null;
            Ped ped = p.Character;
            return (ped != null && ped.Exists()) ? ped : null;
        }

        private static Vehicle CurrentVehicle()
        {
            Ped ped = PlayerPed();
            if (ped == null || !ped.isInVehicle()) return null;
            Vehicle v = ped.CurrentVehicle;
            return (v != null && v.Exists()) ? v : null;
        }

        private Vehicle NeedVehicle()
        {
            Vehicle v = CurrentVehicle();
            if (v == null) Notify("You need to be in a vehicle");
            return v;
        }

        // HandleObject.Handle is internal in ScriptHookDotNet; scripts can only read
        // it through the public GTA.base.iHandleObject interface.
        private static int H(GTA.@base.iHandleObject o)
        {
            return o == null ? 0 : o.Handle;
        }

        private static bool IsDriver(Vehicle v, Ped ped)
        {
            Pointer p = new Pointer(typeof(int));
            if (!N.Call("GET_DRIVER_OF_CAR", v, p)) return false;   // can't tell: don't touch the car
            return (int)p == H(ped);
        }

        private static string Pretty(string model)
        {
            if (model.Length <= 3) return model;
            return model.Substring(0, 1) + model.Substring(1).ToLowerInvariant();
        }

        private static int Hash(string model) { return N.Get<int>("GET_HASH_KEY", 0, model); }

        /// <summary>Request a model and wait (up to 3 s) for it. False if it isn't in the game.</summary>
        private bool LoadModel(int hash, string what)
        {
            if (hash == 0 || !N.Get<bool>("IS_MODEL_IN_CDIMAGE", false, hash))
            {
                Notify(what + " isn't available in this game/episode");
                return false;
            }
            N.Call("REQUEST_MODEL", hash);
            int deadline = Environment.TickCount + 3000;
            while (!N.Get<bool>("HAS_MODEL_LOADED", false, hash))
            {
                if (Environment.TickCount > deadline) { Notify("Timed out loading " + what); return false; }
                Wait(0);
            }
            return true;
        }

        private void BuildMenus()
        {
            root = new Menu("Main Menu");
            root.Add(new SubmenuItem(BuildPlayerMenu(), "Health, wanted level, abilities, model, cash"));
            root.Add(new SubmenuItem(BuildVehicleMenu(), "Spawner, customs garage, performance tuning, presets"));
            root.Add(new SubmenuItem(BuildWeaponMenu(), "Give weapons, infinite ammo, no reload"));
            root.Add(new SubmenuItem(BuildWorldMenu(), "Time, weather, traffic, gravity, slow motion"));
            root.Add(new SubmenuItem(BuildChaosMenu(), "Carmageddon-style car magnet, car rain, black hole, tornado, riots and more"));
            root.Add(new SubmenuItem(BuildTeleportMenu(), "Waypoint, saved slots, Liberty City landmarks"));
            root.Add(new SubmenuItem(BuildMiscMenu(), "HUD, free camera, speedometer, coordinates"));
            root.Add(new SubmenuItem(BuildSettingsMenu(), "Controls, look and saving"));
            root.Add(new ActionItem("Close Menu", Close));
        }
    }

    #endregion

    #region Drawing (PerFrameDrawing only)

    public partial class MenuScript
    {
        private bool drawFailed;
        private static GTA.Graphics gfx;   // only set while OnFrameDraw runs
        private static readonly Dictionary<int, GTA.Font> fonts = new Dictionary<int, GTA.Font>();

        // Animation state, owned by the drawing code.
        private float animSel = -1f;
        private string animCrumb;
        private int lastDrawTick;

        // Builds the snapshot the drawing code renders. Runs on the script thread,
        // so this is where Value getters (which may call natives) are evaluated.
        private void BuildFrame()
        {
            FrameView f = new FrameView();
            f.Theme = Themes.All[U.Clamp(cfg.Theme, 0, Themes.Count - 1)];
            f.Scale = cfg.Scale;
            f.Position = cfg.Position;
            f.Opacity = cfg.Opacity / 100f;
            f.MenuOpen = open && !paused && !freecamOn && stack.Count > 0;

            if (f.MenuOpen)
            {
                Menu m = stack.Peek();
                int count = m.Items.Count;
                int visible = Math.Min(cfg.MaxVisible, count);
                if (m.Selected >= 0)
                {
                    if (m.Selected < m.Scroll) m.Scroll = m.Selected;
                    if (m.Selected >= m.Scroll + visible) m.Scroll = m.Selected - visible + 1;
                    // Keep a section header visible above the first row of its section.
                    if (m.Selected == m.Scroll && m.Selected > 0 && m.Items[m.Selected - 1].Kind == RowKind.Header) m.Scroll = m.Selected - 1;
                }
                m.Scroll = U.Clamp(m.Scroll, 0, Math.Max(0, count - visible));

                f.Rows = new RowView[visible];
                for (int i = 0; i < visible; i++)
                {
                    MenuItem it = m.Items[m.Scroll + i];
                    RowView r = new RowView();
                    r.Label = it.Label;
                    r.Kind = it.Kind;
                    try
                    {
                        r.Value = it.Value;
                        r.On = it.On;
                        r.HasSwatch = it.HasSwatch;
                        if (r.HasSwatch) r.Swatch = it.Swatch;
                    }
                    catch (Exception ex) { r.Value = "?"; Log.Write("Row '" + it.Label + "': " + ex.Message); }
                    f.Rows[i] = r;
                }
                f.SelRow = m.Selected - m.Scroll;
                f.Scroll = m.Scroll;
                f.Count = count;

                int selIndex = 0, selCount = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!m.Items[i].Selectable) continue;
                    selCount++;
                    if (i <= m.Selected) selIndex = selCount;
                }
                f.Counter = selCount == 0 ? "" : selIndex + "/" + selCount;
                f.Crumb = Breadcrumb();

                if (captureTarget != 0)
                {
                    int left = Math.Max(0, (captureDeadline - now) / 1000);
                    f.Help = captureArmed
                        ? "Hold the new " + CaptureNames[captureTarget] + " button(s), then let go (" + left + "s)"
                        : "Let go of all buttons...";
                }
                else if (m.Current != null) f.Help = m.Current.Help;

                f.Hints = "A select    B back    < > change    LT/RT page    " + Btn.ToText(cfg.CloseCombo) + " close";
            }

            FillHud(f);
            if (now < toastUntil) { f.Toast = toastText; f.ToastUntil = toastUntil; }
            frame = f;
        }

        private string Breadcrumb()
        {
            Menu[] arr = stack.ToArray();   // top first
            if (arr.Length <= 2) return arr[0].Title.ToUpperInvariant();
            return arr[1].Title.ToUpperInvariant() + "  /  " + arr[0].Title.ToUpperInvariant();
        }

        private void OnFrameDraw(object sender, GraphicsEventArgs e)
        {
            if (drawFailed) return;
            FrameView f = frame;
            if (f == null) return;
            try
            {
                gfx = e.Graphics;
                gfx.Scaling = FontScaling.ScreenUnits;
                int t = Environment.TickCount;
                float ddt = U.Clamp((t - lastDrawTick) / 1000f, 0f, 0.1f);
                lastDrawTick = t;
                op = f.Opacity;

                if (f.MenuOpen) DrawMenu(f, ddt);
                else animSel = -1f;
                if (f.Speedo) DrawSpeedo(f);
                if (f.Coords != null) Box(f.Coords, 0.5f, 0.955f, 0.26f, Col.FromArgb(150, 0, 0, 0), Col.White);
                if (f.Fps != null) TextRightS(f.Fps, 0.985f, 0.005f, 0.26f, Col.FromArgb(240, 255, 255, 140));
                if (f.Overlay != null) Box(f.Overlay, 0.5f, 0.90f, 0.28f, Col.FromArgb(170, 0, 0, 0), Col.White);
                if (f.DebugCar != null || f.DebugFoot != null) DrawDebug(f);
                if (f.Chaos != null) Box(f.Chaos, 0.5f, 0.004f, 0.21f, Col.FromArgb(170, 70, 10, 10), Col.FromArgb(255, 255, 200, 120));
                if (f.Toast != null && t < f.ToastUntil) DrawToast(f);
            }
            catch (Exception ex)
            {
                drawFailed = true;
                Log.Write("Drawing failed and was turned off: " + ex);
            }
            finally
            {
                gfx = null;
            }
        }

        // ---- look ------------------------------------------------------------
        // Panels are translucent: their alpha is multiplied by the Menu Opacity
        // setting (op). Text and the selection bar stay solid, with a soft
        // shadow so they read on top of the game.
        private static float op = 0.85f;
        private static Col Glass(int a, int r, int g, int b) { return Col.FromArgb((int)(a * op), r, g, b); }
        private static Col Glass(Col c, int a) { return Col.FromArgb((int)(a * op), c.R, c.G, c.B); }

        private void DrawMenu(FrameView f, float ddt)
        {
            float s = f.Scale;
            Theme th = f.Theme;
            float w = 0.27f * s;
            float headerH = 0.050f * s;
            float subH = 0.030f * s;
            float rowH = 0.034f * s;
            float pad = 0.010f * s;
            float x = f.Position == 0 ? 0.03f : (f.Position == 1 ? 0.5f - w / 2f : 0.97f - w);
            float y = 0.08f;
            Col accent = th.Accent.WithAlpha(255);

            // Title bar: theme colour, see-through, with a thin solid accent line under it.
            Rect(x, y, w, headerH, Glass(th.Header, 230));
            TextVS("CLAUDE MENU", x + pad, y, headerH, 0.42f * s, Col.White);
            TextRightVS("v" + Version, x + w - pad, y, headerH, 0.20f * s, Col.FromArgb(200, 255, 255, 255));
            y += headerH;
            Rect(x, y, w, 0.003f * s, accent);
            y += 0.003f * s;

            // Section bar: where you are + position in the list.
            Rect(x, y, w, subH, Glass(195, 8, 8, 10));
            TextVS(f.Crumb, x + pad, y, subH, 0.24f * s, accent);
            TextRightVS(f.Counter, x + w - pad, y, subH, 0.24f * s, Col.FromArgb(210, 225, 225, 225));
            y += subH;

            // Rows on one see-through panel; the highlight glides to the selected row.
            int n = f.Rows.Length;
            Rect(x, y, w, rowH * n, Glass(180, 0, 0, 0));
            if (f.SelRow >= 0 && f.SelRow < n)
            {
                if (animSel < 0f || animCrumb != f.Crumb) animSel = f.SelRow;
                animSel += (f.SelRow - animSel) * Math.Min(1f, ddt * 18f);
                if (Math.Abs(animSel - f.SelRow) < 0.01f) animSel = f.SelRow;
                float hy = y + animSel * rowH;
                Rect(x, hy, w, rowH, Col.FromArgb((int)(150 + 70 * op), th.Accent.R, th.Accent.G, th.Accent.B));
                Rect(x, hy, 0.0035f * s, rowH, Col.White);
            }
            animCrumb = f.Crumb;

            for (int i = 0; i < n; i++)
            {
                RowView r = f.Rows[i];
                float ry = y + i * rowH;
                bool sel = i == f.SelRow;
                Col tc = sel ? Col.White : Col.FromArgb(255, 232, 232, 232);

                if (r.Kind == RowKind.Header)
                {
                    // Section header: no box, just accent text over a hairline.
                    TextVS(r.Label.ToUpperInvariant(), x + pad, ry + rowH * 0.12f, rowH, 0.22f * s, accent);
                    Rect(x + pad, ry + rowH - 0.004f * s, w - 2f * pad, 0.0012f * s, Glass(accent, 170));
                    continue;
                }

                Col lc = r.Kind == RowKind.Info ? Col.FromArgb(255, 175, 175, 175) : tc;
                TextVS(r.Label, x + pad + (sel ? 0.004f * s : 0f), ry, rowH, 0.29f * s, lc);
                float right = x + w - pad - 0.004f * s;

                if (r.Kind == RowKind.Toggle)
                {
                    // Pill switch: green and "ON", or dim and "OFF".
                    float pw = 0.036f * s, ph = rowH * 0.56f;
                    float px = right - pw, py = ry + (rowH - ph) / 2f;
                    Rect(px, py, pw, ph, r.On ? Col.FromArgb(235, 40, 170, 90) : Col.FromArgb((int)(120 + 80 * op), 60, 60, 66));
                    TextCenterV(r.On ? "ON" : "OFF", px + pw / 2f, py, ph, 0.20f * s, r.On ? Col.White : Col.FromArgb(230, 200, 200, 200));
                }
                else if (r.HasSwatch)
                {
                    float sw = 0.030f * s, sh = rowH * 0.6f;
                    float sx = right - sw, sy = ry + (rowH - sh) / 2f;
                    Rect(sx - 0.0015f, sy - 0.0025f, sw + 0.003f, sh + 0.005f, Col.FromArgb(230, 240, 240, 240));
                    Rect(sx, sy, sw, sh, r.Swatch);
                    if (r.Value != null) TextRightVS(r.Value, sx - 0.008f * s, ry, rowH, 0.22f * s, Col.FromArgb(210, 215, 215, 215));
                }
                else if (r.Kind == RowKind.Submenu)
                {
                    TextRightVS(r.Value ?? ">", right, ry, rowH, 0.27f * s, sel ? Col.White : Col.FromArgb(220, 200, 200, 200));
                }
                else if (r.Value != null)
                {
                    TextRightVS(r.Value, right, ry, rowH, 0.29f * s, tc);
                }
            }

            // Thin scrollbar along the right edge.
            if (f.Count > n && n > 0)
            {
                float trackH = rowH * n;
                float thumbH = Math.Max(rowH * 0.5f, trackH * n / f.Count);
                float thumbY = y + (trackH - thumbH) * f.Scroll / Math.Max(1, f.Count - n);
                Rect(x + w - 0.003f * s, y, 0.003f * s, trackH, Glass(110, 255, 255, 255));
                Rect(x + w - 0.003f * s, thumbY, 0.003f * s, thumbH, accent);
            }
            y += rowH * n;

            // Tooltip for the selected item (wraps to two lines).
            if (!string.IsNullOrEmpty(f.Help))
            {
                // Two lines fit about 46 characters each at this size; give long tips a third line.
                float hh = rowH * (f.Help.Length > 88 ? 2.1f : 1.5f);
                Rect(x, y + 0.002f, w, hh, Glass(210, 14, 14, 18));
                Rect(x, y + 0.002f, 0.0025f * s, hh, Col.FromArgb(255, 255, 214, 110));
                if (gfx != null)
                {
                    System.Drawing.RectangleF area = new System.Drawing.RectangleF(x + pad, y + 0.002f + 0.004f * s, w - 2f * pad, hh - 0.004f * s);
                    System.Drawing.RectangleF shadow = new System.Drawing.RectangleF(area.X + 0.0011f, area.Y + 0.0014f, area.Width, area.Height);
                    gfx.DrawText(f.Help, shadow, TextAlignment.Left | TextAlignment.WordBreak, ToColor(Col.FromArgb(160, 0, 0, 0)), FontFor(0.24f * s));
                    gfx.DrawText(f.Help, area, TextAlignment.Left | TextAlignment.WordBreak, ToColor(Col.FromArgb(255, 255, 232, 160)), FontFor(0.24f * s));
                }
                y += hh + 0.002f;
            }

            // Button hints.
            Rect(x, y + 0.002f, w, subH * 0.85f, Glass(200, 8, 8, 10));
            TextVS(f.Hints, x + pad, y + 0.002f, subH * 0.85f, 0.19f * s, Col.FromArgb(225, 200, 200, 200));
        }

        private void DrawSpeedo(FrameView f)
        {
            Theme th = f.Theme;
            float w = 0.15f, h = f.NitroShown ? 0.112f : 0.094f;
            float x = 0.985f - w, y = 0.86f - h;
            Rect(x, y, w, h, Glass(150, 0, 0, 0));
            Rect(x, y, w, 0.0025f, th.Accent.WithAlpha(255));
            if (f.TuneText != null) TextS(f.TuneText, x + 0.008f, y + 0.008f, 0.18f, Col.FromArgb(235, 255, 214, 110));
            TextRightS(f.SpeedText, x + w - 0.040f, y + 0.018f, 0.78f, Col.White);
            TextS(f.SpeedUnit, x + w - 0.037f, y + 0.047f, 0.23f, Col.FromArgb(225, 215, 215, 215));

            float by = y + h - (f.NitroShown ? 0.030f : 0.015f);
            Rect(x + 0.008f, by, w - 0.016f, 0.006f, Glass(150, 90, 90, 90));
            Rect(x + 0.008f, by, (w - 0.016f) * U.Clamp(f.SpeedFrac, 0f, 1f), 0.006f, th.Accent.WithAlpha(255));
            if (f.NitroShown)
            {
                float ny = by + 0.014f;
                TextS("N2O", x + 0.008f, ny - 0.006f, 0.16f, Col.FromArgb(235, 140, 210, 255));
                float nx = x + 0.033f, nw = w - 0.041f;
                Rect(nx, ny, nw, 0.006f, Glass(150, 90, 90, 90));
                Rect(nx, ny, nw * U.Clamp(f.NitroLevel, 0f, 1f), 0.006f, f.NitroActive ? Col.FromArgb(255, 255, 150, 40) : Col.FromArgb(255, 70, 170, 255));
            }
        }

        private void DrawToast(FrameView f)
        {
            float w = Math.Min(0.62f, 0.03f + f.Toast.Length * 0.0078f);
            float h = 0.038f;
            float x = 0.5f - w / 2f, y = 0.035f;
            Rect(x, y, w, h, Glass(185, 12, 12, 14));
            Rect(x, y, 0.0035f, h, f.Theme.Accent.WithAlpha(255));
            TextCenterVS(f.Toast, 0.5f, y, h, 0.27f, Col.White);
        }

        /// <summary>Live debug lines (Misc / Performance > Live Debug), above the coordinates.</summary>
        private void DrawDebug(FrameView f)
        {
            float y = 0.835f;
            if (f.DebugFoot != null) { Box(f.DebugFoot, 0.5f, y, 0.20f, Col.FromArgb(170, 0, 0, 0), Col.FromArgb(255, 160, 255, 160)); y += 0.032f; }
            if (f.DebugCar != null) Box(f.DebugCar, 0.5f, y, 0.20f, Col.FromArgb(170, 0, 0, 0), Col.FromArgb(255, 160, 220, 255));
        }

        // Drawing helpers. They only work inside OnFrameDraw, where gfx is set.
        // Coordinates are screen units (0..1).
        private static System.Drawing.Color ToColor(Col c)
        {
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        private static GTA.Font FontFor(float scale)
        {
            // scale is the old SET_TEXT_SCALE value; 0.30 is a normal row.
            int key = (int)Math.Round(scale * 1000f);
            GTA.Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new GTA.Font(scale * 0.075f, FontScaling.ScreenUnits);
                fonts[key] = f;
            }
            return f;
        }

        private static float TextTop(float y, float h, float scale) { return y + (h - scale * 0.075f) / 2f; }

        private static void Rect(float x, float y, float w, float h, Col c)
        {
            if (gfx == null) return;
            gfx.DrawRectangle(new System.Drawing.RectangleF(x, y, w, h), ToColor(c));
        }

        private static void Text(string s, float x, float y, float scale, Col c, bool center)
        {
            if (gfx == null || s == null) return;
            if (center)
                gfx.DrawText(s, new System.Drawing.RectangleF(x - 0.5f, y, 1.0f, 0.2f), TextAlignment.Center, ToColor(c), FontFor(scale));
            else
                gfx.DrawText(s, x, y, ToColor(c), FontFor(scale));
        }

        private static void TextRight(string s, float right, float y, float scale, Col c)
        {
            if (gfx == null || s == null) return;
            gfx.DrawText(s, new System.Drawing.RectangleF(right - 0.6f, y, 0.6f, 0.2f), TextAlignment.Right, ToColor(c), FontFor(scale));
        }

        // Vertically centred in a row of height h.
        private static void TextV(string s, float x, float y, float h, float scale, Col c) { Text(s, x, TextTop(y, h, scale), scale, c, false); }
        private static void TextRightV(string s, float right, float y, float h, float scale, Col c) { TextRight(s, right, TextTop(y, h, scale), scale, c); }
        private static void TextCenterV(string s, float cx, float y, float h, float scale, Col c) { Text(s, cx, TextTop(y, h, scale), scale, c, true); }

        // The same with a soft drop shadow, for text on see-through panels.
        private static Col ShadowOf(Col c) { return Col.FromArgb(c.A * 3 / 5, 0, 0, 0); }
        private static void TextS(string s, float x, float y, float scale, Col c) { Text(s, x + 0.0011f, y + 0.0014f, scale, ShadowOf(c), false); Text(s, x, y, scale, c, false); }
        private static void TextRightS(string s, float right, float y, float scale, Col c) { TextRight(s, right + 0.0011f, y + 0.0014f, scale, ShadowOf(c)); TextRight(s, right, y, scale, c); }
        private static void TextVS(string s, float x, float y, float h, float scale, Col c) { TextS(s, x, TextTop(y, h, scale), scale, c); }
        private static void TextRightVS(string s, float right, float y, float h, float scale, Col c) { TextRightS(s, right, TextTop(y, h, scale), scale, c); }
        private static void TextCenterVS(string s, float cx, float y, float h, float scale, Col c)
        {
            float ty = TextTop(y, h, scale);
            Text(s, cx + 0.0011f, ty + 0.0014f, scale, ShadowOf(c), true);
            Text(s, cx, ty, scale, c, true);
        }

        /// <summary>Centred text on a background box sized from the text length.</summary>
        private static void Box(string s, float cx, float y, float scale, Col bg, Col fg)
        {
            float w = Math.Min(0.9f, 0.02f + s.Length * scale * 0.026f);
            float h = scale * 0.075f + 0.012f;
            Rect(cx - w / 2f, y, w, h, Glass(bg, bg.A));
            TextCenterVS(s, cx, y, h, scale, fg);
        }
    }

    #endregion

    #region Per-frame feature dispatch and HUD data

    public partial class MenuScript
    {
        // Wall-clock frame time (free camera, FPS, and every per-frame boost). Stopwatch,
        // because Environment.TickCount only moves every 10-16 ms on Windows/Proton.
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private long lastClockTicks;
        private float realDt = 1f / 60f;
        private float fpsAvg = 60f;

        private void RunFeatures()
        {
            long ticks = clock.ElapsedTicks;
            realDt = U.Clamp((float)((ticks - lastClockTicks) / (double)System.Diagnostics.Stopwatch.Frequency), 0.001f, 0.1f);
            lastClockTicks = ticks;
            fpsAvg += (1f / realDt - fpsAvg) * 0.05f;

            Ped ped = PlayerPed();
            if (ped == null) return;

            RunPlayerFrame(ped);
            RunWeaponFrame(ped);
            RunWorldFrame();
            RunMiscFrame();
            RunVehicleFrame(ped);
            RunChaos(ped);

            // Things that don't need to run every frame.
            if (now < nextSlowTick) return;
            nextSlowTick = now + 250;
            RunPlayerSlow(ped);
            RunWeaponSlow(ped);
            RunWorldSlow();
            RunMiscSlow();
            RunVehicleSlow(ped);
        }

        private void FillHud(FrameView f)
        {
            Ped ped = PlayerPed();
            if (showCoords && ped != null)
            {
                Vector3 p = ped.Position;
                f.Coords = string.Format(CultureInfo.InvariantCulture, "X {0:0.0}   Y {1:0.0}   Z {2:0.0}   H {3:0}", p.X, p.Y, p.Z, ped.Heading);
            }
            if (showFps) f.Fps = "FPS " + U.I((int)Math.Round(fpsAvg));
            if (freecamOn) f.Overlay = "FREE CAMERA   L-stick move   R-stick look   RB/LB up/down   RT faster   A teleport here   B exit";
            if (liveDebug) { f.DebugCar = carDebug; f.DebugFoot = footDebug; }
            f.Chaos = chaosBadge;
            FillSpeedo(f);
        }
    }

    #endregion

    #region Player

    public partial class MenuScript
    {
        private bool godMode, neverWanted, policeIgnore, everyoneIgnore, infSprint;
        private bool noRagdoll, noBikeFall, noDragOut, seatbelt, autoHeal, autoArmour;

        // Super Run / Super Jump. What moves the player in GTA IV on PC isn't
        // documented, and Deck tests ruled out several things:
        // SET_CHAR_MOVE_ANIM_SPEED_MULTIPLIER and on-ground SET_CHAR_VELOCITY froze
        // walking, a single in-air SET_CHAR_VELOCITY was ignored, and XMC's small
        // per-frame APPLY_FORCE_TO_PED did nothing in the 1.4 test. So each has
        // three methods to choose from:
        //  Run   Step (default)  small position steps (SET_CHAR_COORDINATES_NO_OFFSET)
        //                        along the way you're running, only while the game
        //                        itself has you moving faster than 2.5 m/s, so walls
        //                        stop it. Teleports demonstrably work on the Deck.
        //        Anim Speed      SET_CHAR_ALL_ANIMS_SPEED while sprinting (the XMC trainer
        //                        sets it on the player for rapid fire); set on start and stop only.
        //        Push            XMC's APPLY_FORCE_TO_PED every frame.
        //  Jump  Lift (default)  raise the player with SET_CHAR_COORDINATES_NO_OFFSET for
        //                        a moment after the jump (the idea comes from MoveImprove's
        //                        climb-up code), then one velocity write at the top.
        //        Launch          SET_CHAR_VELOCITY upwards, re-applied while rising
        //                        (TORISOUP's "let's fly" effect), at the speed that
        //                        coasts to the chosen height.
        //        Push            XMC's APPLY_FORCE_TO_PED for 0.6 s.
        // A tap on jump is enough, and tapping sprint counts as sprinting.
        private bool superRun, superJump;
        private float runBoost = 5f, jumpBoost = 5f;
        private int runMethod, jumpMethod;             // 0 = Step / Lift (defaults)
        private static readonly string[] RunMethodNames = { "Step", "Anim Speed", "Push" };
        private static readonly string[] JumpMethodNames = { "Lift", "Launch", "Push" };
        private const float LiftSpeed = 14f;           // m/s while Lift raises you
        private const float JumpDrift = 6f;            // m/s forward during a Lift jump when you push the stick
        private const float Gravity = 9.8f;            // m/s/s, for the Launch climb rate and Lift's fall allowance
        private const float MinRunSpeed = 2.5f;        // the game must have you moving at least this fast for Step

        // Run state
        private int lastRunHeld = -100000;
        private bool runActive, animBoosted;
        private int runStartAt;
        private Vector3 runStartPos;
        private bool runWhyLogged;

        // Jump state
        private bool jumpActive, jumpRising;
        private int jumpStartAt, jumpBlockedFrames;
        private float jumpStartZ, jumpPeakZ, jumpLastSetZ;
        private bool jumpInvincible;                   // we made the player invincible for the landing
        private int lastAirborne;
        private int abilityLogLeft = 8;

        private static readonly int[] CashAmounts = { 1000, 10000, 100000, 1000000, 10000000 };
        private static readonly string[] CashNames = { "$1,000", "$10,000", "$100,000", "$1,000,000", "$10,000,000" };

        private Menu BuildPlayerMenu()
        {
            Menu m = new Menu("Player");
            m.OnEnter = menu => { abilityLogLeft = 8; runWhyLogged = false; };
            m.Header("Health");
            m.Add(new ToggleItem("God Mode", () => godMode, v =>
            {
                godMode = v;
                Ped p = PlayerPed();
                if (p != null) N.Call("SET_CHAR_INVINCIBLE", p, v);
            }, "Take no damage"));
            m.Add(new ActionItem("Refill Health & Armour", () =>
            {
                Ped p = PlayerPed(); if (p == null) return;
                N.Call("SET_CHAR_HEALTH", p, 200);
                N.Call("ADD_ARMOUR_TO_CHAR", p, 100);
                Notify("Health and armour refilled");
            }));
            m.Add(new ToggleItem("Auto Heal", () => autoHeal, v => autoHeal = v, "Tops your health back up whenever it drops"));
            m.Add(new ToggleItem("Keep Armour Full", () => autoArmour, v => autoArmour = v));
            m.Add(new ActionItem("Kill Player", () => { Ped p = PlayerPed(); if (p != null) { godMode = false; N.Call("SET_CHAR_INVINCIBLE", p, false); N.Call("SET_CHAR_HEALTH", p, 0); } }, "Handy when you're stuck. Turns God Mode off first"));

            m.Header("Wanted Level");
            m.Add(new ToggleItem("Never Wanted", () => neverWanted, v => neverWanted = v));
            m.Add(new NumberItem("Wanted Level", 0, 6, 1, () => Math.Max(0, N.OutInt("STORE_WANTED_LEVEL", 0, pi)), v =>
            {
                if (v == 0) N.Call("CLEAR_WANTED_LEVEL", pi);
                else { N.Call("ALTER_WANTED_LEVEL", pi, v); N.Call("APPLY_WANTED_LEVEL_CHANGE_NOW", pi); }
                Notify(v == 0 ? "Wanted level cleared" : "Wanted level " + v);
            }, false, " stars", "Left/Right to choose, A to apply"));
            m.Add(new ToggleItem("Police Ignore You", () => policeIgnore, v => { policeIgnore = v; N.Call("SET_POLICE_IGNORE_PLAYER", pi, v); }));
            m.Add(new ToggleItem("Everyone Ignores You", () => everyoneIgnore, v => { everyoneIgnore = v; N.Call("SET_EVERYONE_IGNORE_PLAYER", pi, v); }));

            m.Header("Abilities");
            m.Add(new ToggleItem("Super Run (experimental)", () => superRun, v => superRun = v,
                "Sprint (" + Btn.ToText(cfg.RunButton) + ") while moving to run faster. If it does nothing or stutters, try another Run Method"));
            m.Add(new FloatItem("Run Boost", 1f, 10f, 1f, () => runBoost, v => runBoost = v, "0", "", null, "Step: this many m/s extra. Anim Speed: up to 2.5x"));
            m.Add(new ListItem("Run Method", RunMethodNames, () => runMethod, v => { StopRunBoost(PlayerPed()); runMethod = v; }, true,
                "Step: small position steps (default). Anim Speed: faster animations. Push: physics push (XMC)"));
            m.Add(new ToggleItem("Super Jump (experimental)", () => superJump, SetSuperJump,
                "Just tap jump (" + Btn.ToText(cfg.JumpButton) + "). You're invincible until you land. If it does nothing, try another Jump Method"));
            m.Add(new FloatItem("Jump Boost", 1f, 10f, 1f, () => jumpBoost, v => jumpBoost = v, "0", "", null, "How high: about 1.2 m per point (5 = 6 m)"));
            m.Add(new ListItem("Jump Method", JumpMethodNames, () => jumpMethod, v => jumpMethod = v, true,
                "Lift: raises you (default). Launch: velocity kick (TORISOUP). Push: physics push (XMC)"));
            m.Add(new ToggleItem("Unlimited Sprint", () => infSprint, v => { infSprint = v; N.Call("SET_PLAYER_NEVER_GETS_TIRED", pi, v); }));
            m.Add(new ToggleItem("No Ragdoll", () => noRagdoll, v =>
            {
                noRagdoll = v;
                Ped p = PlayerPed();
                if (p != null) N.Call("UNLOCK_RAGDOLL", p, !v);
            }, "Stops Niko going limp when hit or bumped"));
            m.Add(new ActionItem("Ragdoll Now", () =>
            {
                Ped p = PlayerPed();
                if (p == null || CurrentVehicle() != null) { Notify("Get out of the vehicle first"); return; }
                if (noRagdoll) { noRagdoll = false; N.Call("UNLOCK_RAGDOLL", p, true); }
                N.Call("SWITCH_PED_TO_RAGDOLL", p, 1000, 3000, false, false, false, false);
            }, "Fall over for a few seconds (turns No Ragdoll off)"));
            m.Add(new ToggleItem("Never Fall Off Bike", () => noBikeFall, v => { noBikeFall = v; Ped p = PlayerPed(); if (p != null) N.Call("SET_CHAR_CAN_BE_KNOCKED_OFF_BIKE", p, !v); }));
            m.Add(new ToggleItem("Can't Be Dragged Out", () => noDragOut, v => { noDragOut = v; Ped p = PlayerPed(); if (p != null) N.Call("SET_CHAR_CANT_BE_DRAGGED_OUT", p, v); }));
            m.Add(new ToggleItem("Seatbelt", () => seatbelt, v => { seatbelt = v; Ped p = PlayerPed(); if (p != null) N.Call("SET_CHAR_WILL_FLY_THROUGH_WINDSCREEN", p, !v); }, "Don't fly through the windscreen"));

            m.Header("Appearance & Money");
            m.Add(new SubmenuItem(BuildModelMenu(), "Play as another character"));
            m.Add(new SubmenuItem(BuildCashMenu()));
            return m;
        }

        private Menu BuildCashMenu()
        {
            Menu m = new Menu("Cash");
            m.Add(new InfoItem("Current Cash", () => U.Money(N.OutInt("STORE_SCORE", 0, pi))));
            m.Add(new ListItem("Add Money", CashNames, null, v => { N.Call("ADD_SCORE", pi, CashAmounts[v]); Notify("+" + CashNames[v]); }, false, "Left/Right to choose, A to add"));
            m.Add(new ListItem("Remove Money", CashNames, null, v =>
            {
                int cash = N.OutInt("STORE_SCORE", 0, pi);
                int take = Math.Min(cash, CashAmounts[v]);
                N.Call("ADD_SCORE", pi, -take);
                Notify("-" + U.Money(take));
            }, false, "Left/Right to choose, A to take (never below $0)"));
            m.Add(new ActionItem("Set Cash to $0", () => { N.Call("ADD_SCORE", pi, -N.OutInt("STORE_SCORE", 0, pi)); Notify("Cash set to $0"); }));
            return m;
        }

        private Menu BuildModelMenu()
        {
            Menu m = new Menu("Change Model");
            m.Add(new ActionItem("Back to Niko", () => ChangeModel("Niko Bellic", "PLAYER"), "Restores the default player model"));
            foreach (string[] cls in GameData.PedClasses)
            {
                Menu sub = new Menu(cls[0]);
                for (int i = 1; i < cls.Length; i++)
                {
                    string[] kv = cls[i].Split('=');
                    string label = kv[0], model = kv[1];
                    sub.Add(new ActionItem(label, () => ChangeModel(label, model), "Model: " + model));
                }
                m.Add(new SubmenuItem(sub));
            }
            return m;
        }

        private void ChangeModel(string label, string model)
        {
            Ped ped = PlayerPed();
            if (ped == null) return;
            if (CurrentVehicle() != null) { Notify("Get out of the vehicle first"); return; }
            int hash = Hash(model);
            if (hash != 0 && N.Get<bool>("IS_MODEL_IN_CDIMAGE", false, hash) && !N.Get<bool>("IS_THIS_MODEL_A_PED", true, hash))
            {
                Notify(model + " isn't a character model"); return;
            }
            if (!LoadModel(hash, label)) return;

            // Changing model takes the player's weapons away; note them and give them back.
            int episode = Episode();
            List<int> owned = new List<int>();
            List<int> ammo = new List<int>();
            foreach (GameData.WeaponInfo w in GameData.Weapons)
            {
                if (w.Episode != -1 && w.Episode != episode) continue;
                if (!N.Get<bool>("HAS_CHAR_GOT_WEAPON", false, ped, w.Id)) continue;
                owned.Add(w.Id);
                Pointer a = new Pointer(typeof(int));
                ammo.Add(N.Call("GET_AMMO_IN_CHAR_WEAPON", ped, w.Id, a) ? Math.Max(1, (int)a) : 100);
            }

            N.Call("CHANGE_PLAYER_MODEL", pi, hash);
            N.Call("MARK_MODEL_AS_NO_LONGER_NEEDED", hash);
            ped = PlayerPed();
            if (ped == null) return;
            N.Call("SET_CHAR_DEFAULT_COMPONENT_VARIATION", ped);
            for (int i = 0; i < owned.Count; i++) N.Call("GIVE_WEAPON_TO_CHAR", ped, owned[i], ammo[i], false);
            if (godMode) N.Call("SET_CHAR_INVINCIBLE", ped, true);
            Notify("You are now " + label);
        }

        private void SetSuperJump(bool v)
        {
            superJump = v;
            if (!v) { jumpActive = false; EndJumpInvincibility(PlayerPed()); }
        }

        private void EndJumpInvincibility(Ped ped)
        {
            if (!jumpInvincible) return;
            jumpInvincible = false;
            if (ped != null && !godMode) N.Call("SET_CHAR_INVINCIBLE", ped, false);
        }

        /// <summary>The player's position straight from the game (GET_CHAR_COORDINATES).</summary>
        private static Vector3 CharPos(Ped ped)
        {
            Pointer x = new Pointer(typeof(float)), y = new Pointer(typeof(float)), z = new Pointer(typeof(float));
            return N.Call("GET_CHAR_COORDINATES", ped, x, y, z) ? U.V((float)x, (float)y, (float)z) : ped.Position;
        }

        /// <summary>Flat direction the player is moving in (from the game's velocity), or facing if standing.</summary>
        private static Vector3 MoveDir(Ped ped)
        {
            Pointer vx = new Pointer(typeof(float)), vy = new Pointer(typeof(float)), vz = new Pointer(typeof(float));
            if (N.Call("GET_CHAR_VELOCITY", ped, vx, vy, vz))
            {
                Vector3 v = U.V((float)vx, (float)vy, 0f);
                if (U.Len(v) > 0.5f) return U.Norm(v);
            }
            return U.HeadingDir(ped.Heading);
        }

        private void StopRunBoost(Ped ped)
        {
            if (animBoosted && ped != null) N.Call("SET_CHAR_ALL_ANIMS_SPEED", ped, 1.0f);
            animBoosted = false;
        }

        private void RunPlayerFrame(Ped ped)
        {
            if (neverWanted) N.Call("CLEAR_WANTED_LEVEL", pi);

            footDebug = null;
            if (!superRun && animBoosted) StopRunBoost(ped);
            if (!superRun && !superJump && !jumpInvincible && !liveDebug) return;

            bool blocked = open || freecamOn || paused;
            bool menuChord = input.IsHeld(Btn.LB) && input.IsHeld(Btn.RB);   // probably opening the menu
            bool onFoot = N.Get<bool>("IS_CHAR_ON_FOOT", false, ped);
            bool inAir = onFoot && N.Get<bool>("IS_CHAR_IN_AIR", false, ped);
            float sx = InputState.Axis(input.LX), sy = InputState.Axis(input.LY);
            float stick = Math.Min(1f, (float)Math.Sqrt(sx * sx + sy * sy));
            if (!input.Connected && cfg.KeyboardFallback && (Keyboard.IsDown(0x57) || Keyboard.IsDown(0x41) || Keyboard.IsDown(0x53) || Keyboard.IsDown(0x44))) stick = 1f;   // WASD
            bool runHeld = !blocked && !menuChord && input.ComboHeld(cfg.RunButton);
            if (runHeld) lastRunHeld = now;
            bool sprinting = !blocked && now - lastRunHeld < 450;   // tapping sprint counts too
            bool jumpPressed = !blocked && !menuChord && (Btn.Count(cfg.JumpButton) == 1 ? input.Pressed(cfg.JumpButton) : input.ComboPressed(cfg.JumpButton));
            float gameSpeed = (superRun || liveDebug) ? N.OutFloat("GET_CHAR_SPEED", 0f, ped) : 0f;
            string act = null;

            // ---- Super Run -----------------------------------------------------------
            bool canRun = superRun && onFoot && !inAir && sprinting && stick > 0.3f;
            if (canRun && !runActive) { runActive = true; runStartAt = now; runStartPos = CharPos(ped); }
            if (canRun)
            {
                if (runMethod == 0)
                {
                    if (gameSpeed > MinRunSpeed)
                    {
                        // A small step along the way you're already running; the animation keeps playing.
                        float step = runBoost * realDt;
                        Vector3 d = MoveDir(ped), p = CharPos(ped);
                        N.Call("SET_CHAR_COORDINATES_NO_OFFSET", ped, p.X + d.X * step, p.Y + d.Y * step, p.Z);
                        act = "run step " + U.F(step, "0.00") + " m";
                    }
                    else
                    {
                        act = "run: game speed " + U.F(gameSpeed, "0.0") + " (blocked?)";
                        if (!runWhyLogged && now - runStartAt > 1000)
                        {
                            runWhyLogged = true;
                            Log.Write("Super run [step]: the game has you at " + U.F(gameSpeed, "0.0") + " m/s, so no steps (blocked, or not really running?)");
                        }
                    }
                }
                else if (runMethod == 1)
                {
                    if (!animBoosted) { N.Call("SET_CHAR_ALL_ANIMS_SPEED", ped, 1f + 0.15f * runBoost); animBoosted = true; }
                    act = "run anims x" + U.F(1f + 0.15f * runBoost, "0.00");
                }
                else
                {
                    float force = runBoost * U.Clamp(realDt * 30f, 0.25f, 3f);
                    // APPLY_FORCE_TO_PED(ped, type 1, x, y, z, offset x/y/z, component 1, local force, local offset, scale by mass): XMC's arguments.
                    N.Call("APPLY_FORCE_TO_PED", ped, 1, 0f, force, 0f, 0f, 0f, 0f, 1, 1, 1, 1);
                    act = "run push " + U.F(force, "0.0");
                }
            }
            else if (runActive)
            {
                runActive = false;
                StopRunBoost(ped);
                float secs = (now - runStartAt) / 1000f;
                if (secs > 0.6f && abilityLogLeft > 0)
                {
                    Vector3 p = CharPos(ped);
                    float dist = U.Len(U.V(p.X - runStartPos.X, p.Y - runStartPos.Y, 0f));
                    abilityLogLeft--;
                    Log.Write("Super run [" + RunMethodNames[runMethod].ToLowerInvariant() + "]: " + U.F(dist / secs, "0.0") + " m/s average over " + U.F(secs, "0.0") + " s (a normal sprint is about 7)");
                }
            }
            else if (superRun && sprinting && stick > 0.3f && !runWhyLogged && (!onFoot || inAir))
            {
                runWhyLogged = true;
                Log.Write("Super run: sprinting, but the game says on foot " + YN(onFoot) + ", in the air " + YN(inAir));
            }

            // ---- Super Jump ----------------------------------------------------------
            float height = 1.2f * jumpBoost;
            if (superJump && jumpPressed)
            {
                if (onFoot && !inAir && !jumpActive)
                {
                    Vector3 p = CharPos(ped);
                    jumpActive = jumpRising = true;
                    jumpStartAt = now;
                    jumpStartZ = jumpPeakZ = jumpLastSetZ = p.Z;
                    jumpBlockedFrames = 0;
                    if (!jumpInvincible && !godMode) { N.Call("SET_CHAR_INVINCIBLE", ped, true); jumpInvincible = true; }
                }
                else if (abilityLogLeft > 0 && !jumpActive)
                {
                    abilityLogLeft--;
                    Log.Write("Super jump: jump pressed, but the game says on foot " + YN(onFoot) + ", in the air " + YN(inAir));
                }
            }
            if (jumpActive)
            {
                Vector3 p = CharPos(ped);
                if (p.Z > jumpPeakZ) jumpPeakZ = p.Z;
                float climbed = p.Z - jumpStartZ;
                float reached = jumpMethod == 0 ? jumpLastSetZ - jumpStartZ : climbed;   // Lift goes by its own path
                // Stop trying after the time the climb should take, plus a margin.
                float riseSecs = jumpMethod == 1 ? (float)Math.Sqrt(2f * height / Gravity) : height / LiftSpeed;
                int maxMs = (int)(riseSecs * 1000f) + 500;
                bool wasRising = jumpRising;
                if (jumpRising && (reached >= height - 0.01f || now - jumpStartAt > maxMs)) jumpRising = false;
                if (wasRising && !jumpRising && jumpMethod == 0)
                {
                    // One velocity write at the top: drop the fall speed gravity built up while lifting, and
                    // if you're pushing the stick, a forward kick so you carry on in an arc.
                    Vector3 d = MoveDir(ped);
                    float kx = 0f, ky = 0f;
                    if (stick > 0.3f) { kx = d.X * JumpDrift; ky = d.Y * JumpDrift; }
                    else
                    {
                        Pointer vx = new Pointer(typeof(float)), vy = new Pointer(typeof(float)), vz = new Pointer(typeof(float));
                        if (N.Call("GET_CHAR_VELOCITY", ped, vx, vy, vz)) { kx = (float)vx; ky = (float)vy; }
                    }
                    N.Call("SET_CHAR_VELOCITY", ped, kx, ky, stick > 0.3f ? 1f : 0f);
                }
                if (jumpRising)
                {
                    if (jumpMethod == 0)
                    {
                        // Something above you, or the game keeps pulling you back: give up after a few frames.
                        // Allow for the fall gravity adds between frames (more as the lift goes on, and at low fps).
                        float slack = 0.3f + Gravity * ((now - jumpStartAt) / 1000f) * realDt;
                        if (p.Z < jumpLastSetZ - slack) jumpBlockedFrames++; else jumpBlockedFrames = 0;
                        if (jumpBlockedFrames >= 4)
                        {
                            jumpRising = false;
                            Log.Write("Super jump [lift]: the game kept pulling you back down (something above you?)");
                        }
                        else
                        {
                            // Up our own path from where we last put you, not from where gravity has dragged you
                            // since, so the height doesn't depend on whether a teleport clears the fall speed.
                            jumpLastSetZ += Math.Min(height - (jumpLastSetZ - jumpStartZ), LiftSpeed * realDt);
                            float hx = p.X, hy = p.Y;
                            if (stick > 0.3f)
                            {
                                // Pushing the stick: drift forward as you rise, like a real jump.
                                Vector3 d = MoveDir(ped);
                                hx += d.X * JumpDrift * realDt; hy += d.Y * JumpDrift * realDt;
                            }
                            N.Call("SET_CHAR_COORDINATES_NO_OFFSET", ped, hx, hy, jumpLastSetZ);
                            act = (act == null ? "" : act + ", ") + "jump lift " + U.F(climbed, "0.0") + "/" + U.F(height, "0.0") + " m";
                        }
                    }
                    else if (jumpMethod == 1)
                    {
                        // Keep your sideways momentum and set the climb rate (TORISOUP re-applies it every tick
                        // while rising). sqrt(2 g h) is the speed that coasts up exactly the height still to go,
                        // so letting go at the top doesn't throw you higher than Jump Boost says.
                        Pointer vx = new Pointer(typeof(float)), vy = new Pointer(typeof(float)), vz = new Pointer(typeof(float));
                        float hx = 0f, hy = 0f;
                        if (N.Call("GET_CHAR_VELOCITY", ped, vx, vy, vz)) { hx = (float)vx; hy = (float)vy; }
                        float climb = Math.Max(3f, (float)Math.Sqrt(2f * Gravity * Math.Max(0f, height - climbed)));
                        N.Call("SET_CHAR_VELOCITY", ped, hx, hy, climb);
                        act = (act == null ? "" : act + ", ") + "jump launch " + U.F(climb, "0.0") + " m/s, " + U.F(climbed, "0.0") + "/" + U.F(height, "0.0") + " m";
                    }
                    else if (now - jumpStartAt < 600)
                    {
                        float force = jumpBoost * U.Clamp(realDt * 30f, 0.25f, 3f);
                        N.Call("APPLY_FORCE_TO_PED", ped, 1, 0f, 0f, force, 0f, 0f, 0f, 1, 1, 1, 1);
                        act = (act == null ? "" : act + ", ") + "jump push " + U.F(force, "0.0");
                    }
                }
            }

            // Invincibility (and the height log) ends a moment after landing.
            if (inAir || jumpRising) lastAirborne = now;
            else if (now - lastAirborne > 1200)
            {
                if (jumpActive)
                {
                    jumpActive = false;
                    if (abilityLogLeft > 0)
                    {
                        abilityLogLeft--;
                        Log.Write("Super jump [" + JumpMethodNames[jumpMethod].ToLowerInvariant() + "]: rose " + U.F(jumpPeakZ - jumpStartZ, "0.0") + " m (a normal jump is about 1)");
                    }
                }
                EndJumpInvincibility(ped);
            }

            if (liveDebug && CurrentVehicle() == null)
                footDebug = "FOOT  on foot " + YN(onFoot) + "  air " + YN(inAir) + "  stick " + U.F(stick, "0.00")
                          + "  sprint " + YN(sprinting) + "  game speed " + U.F(gameSpeed, "0.0")
                          + "  run " + RunMethodNames[runMethod].ToLowerInvariant() + " / jump " + JumpMethodNames[jumpMethod].ToLowerInvariant()
                          + "  " + (act ?? "-");
        }

        private void RunPlayerSlow(Ped ped)
        {
            if (godMode)
            {
                N.Call("SET_CHAR_INVINCIBLE", ped, true);
                if (ped.Health < 200) N.Call("SET_CHAR_HEALTH", ped, 200);
            }
            if (autoHeal && N.OutInt("GET_CHAR_HEALTH", 200, ped) < 200) N.Call("SET_CHAR_HEALTH", ped, 200);
            if (autoArmour)
            {
                int armour = N.OutInt("GET_CHAR_ARMOUR", 100, ped);
                if (armour < 100) N.Call("ADD_ARMOUR_TO_CHAR", ped, 100 - armour);
            }
            if (infSprint) N.Call("SET_PLAYER_NEVER_GETS_TIRED", pi, true);
            if (noRagdoll) N.Call("UNLOCK_RAGDOLL", ped, false);
            if (noBikeFall) N.Call("SET_CHAR_CAN_BE_KNOCKED_OFF_BIKE", ped, false);
        }
    }

    #endregion

    #region Weapons

    public partial class MenuScript
    {
        private bool infAmmo, noReload, fastReload, explosiveHits;
        private int nextHitScan, lastShotAt = -100000;
        private bool hitScanBroken;

        private static int Episode() { return N.Get<int>("GET_CURRENT_EPISODE", 0); }

        private static List<GameData.WeaponInfo> WeaponsForEpisode()
        {
            int ep = Episode();
            List<GameData.WeaponInfo> list = new List<GameData.WeaponInfo>();
            foreach (GameData.WeaponInfo w in GameData.Weapons)
                if (w.Episode == -1 || w.Episode == ep) list.Add(w);
            return list;
        }

        private Menu BuildWeaponMenu()
        {
            Menu m = new Menu("Weapons");
            m.Add(new ActionItem("Give All Weapons", () =>
            {
                Ped p = PlayerPed(); if (p == null) return;
                foreach (GameData.WeaponInfo w in WeaponsForEpisode())
                    N.Call("GIVE_WEAPON_TO_CHAR", p, w.Id, w.UsesAmmo ? 9999 : 1, false);
                Notify("All weapons given");
            }, "Every weapon in the episode you're playing"));

            Menu single = new Menu("Give One Weapon");
            single.OnEnter = menu =>
            {
                menu.Items.Clear();
                foreach (GameData.WeaponInfo w in WeaponsForEpisode())
                {
                    GameData.WeaponInfo wi = w;
                    menu.Add(new ActionItem(wi.Name, () =>
                    {
                        Ped p = PlayerPed(); if (p == null) return;
                        N.Call("GIVE_WEAPON_TO_CHAR", p, wi.Id, wi.UsesAmmo ? 9999 : 1, false);
                        N.Call("SET_CURRENT_CHAR_WEAPON", p, wi.Id, true);
                        Notify(wi.Name + " given");
                    }));
                }
            };
            m.Add(new SubmenuItem(single, "Pick one weapon and equip it"));

            m.Header("Ammo");
            m.Add(new ToggleItem("Infinite Ammo", () => infAmmo, v => { infAmmo = v; if (v) N.Call("ENABLE_MAX_AMMO_CAP", false); }, "Keeps reserve ammo full for every weapon you carry"));
            m.Add(new ToggleItem("No Reload", () => noReload, v => noReload = v, "Keeps the current magazine full, so you never reload"));
            m.Add(new ToggleItem("Fast Reload", () => fastReload, v => { fastReload = v; N.Call("SET_PLAYER_FAST_RELOAD", pi, v); }));
            m.Add(new ActionItem("Refill Ammo", () => { Ped p = PlayerPed(); if (p != null) { RefillAmmo(p); Notify("Ammo refilled"); } }));

            m.Header("Extras");
            m.Add(new ToggleItem("Explosive Hits", () => explosiveHits, v =>
            {
                explosiveHits = v;
                if (v) { Ped p = PlayerPed(); if (p != null) ScanHits(p, false); }
            }, "People and vehicles you shoot explode. GTA IV has no bullet-impact native, so walls and the ground don't"));
            m.Add(new ActionItem("Remove All Weapons", () => { Ped p = PlayerPed(); if (p != null) N.Call("REMOVE_ALL_CHAR_WEAPONS", p); }));
            return m;
        }

        private void RefillAmmo(Ped p)
        {
            foreach (GameData.WeaponInfo w in WeaponsForEpisode())
                if (w.UsesAmmo && N.Get<bool>("HAS_CHAR_GOT_WEAPON", false, p, w.Id)) N.Call("SET_CHAR_AMMO", p, w.Id, 9999);
        }

        private void RunWeaponFrame(Ped ped)
        {
            if (explosiveHits && N.Get<bool>("IS_CHAR_SHOOTING", false, ped)) lastShotAt = now;

            if (noReload)
            {
                Pointer wp = new Pointer(typeof(int));
                if (N.Call("GET_CURRENT_CHAR_WEAPON", ped, wp))
                {
                    int w = (int)wp;
                    if (w >= 7 && w != 28 && w != 36 && w != 41)
                    {
                        Pointer maxP = new Pointer(typeof(int)), curP = new Pointer(typeof(int));
                        if (N.Call("GET_MAX_AMMO_IN_CLIP", ped, w, maxP) && N.Call("GET_AMMO_IN_CLIP", ped, w, curP))
                        {
                            int max = (int)maxP;
                            if (max > 0 && (int)curP < max) N.Call("SET_AMMO_IN_CLIP", ped, w, max);
                        }
                    }
                }
            }

            if (explosiveHits && !hitScanBroken && now >= nextHitScan)
            {
                nextHitScan = now + 100;
                ScanHits(ped, now - lastShotAt < 1500);
            }
        }

        /// <summary>
        /// Explode peds/vehicles the player damaged since the last scan. With
        /// explode = false it only clears the "damaged by" flags.
        /// </summary>
        private void ScanHits(Ped ped, bool explode)
        {
            try
            {
                Vector3 pos = ped.Position;
                Vehicle mine = CurrentVehicle();
                foreach (Ped p in World.GetPeds(pos, 60f))
                {
                    if (p == null || !p.Exists() || H(p) == H(ped)) continue;
                    if (!N.Get<bool>("HAS_CHAR_BEEN_DAMAGED_BY_CHAR", false, p, ped, false)) continue;
                    if (explode) Boom(p.Position);
                    N.Call("CLEAR_CHAR_LAST_DAMAGE_ENTITY", p);
                }
                foreach (Vehicle v in World.GetVehicles(pos, 60f))
                {
                    if (v == null || !v.Exists() || (mine != null && H(v) == H(mine))) continue;
                    if (!N.Get<bool>("HAS_CAR_BEEN_DAMAGED_BY_CHAR", false, v, ped)) continue;
                    if (explode) Boom(v.Position);
                    N.Call("CLEAR_CAR_LAST_DAMAGE_ENTITY", v);
                }
            }
            catch (Exception ex)
            {
                hitScanBroken = true;
                explosiveHits = false;
                Log.Write("Explosive hits turned off: " + ex.Message);
                Notify("Explosive Hits isn't supported here (see log)");
            }
        }

        private static void Boom(Vector3 p)
        {
            // ADD_EXPLOSION(x, y, z, type 0 = grenade, radius, sound, no visuals, camera shake)
            N.Call("ADD_EXPLOSION", p.X, p.Y, p.Z, 0, 4.0f, true, false, 0.4f);
        }

        private void RunWeaponSlow(Ped ped)
        {
            if (infAmmo)
            {
                N.Call("ENABLE_MAX_AMMO_CAP", false);
                RefillAmmo(ped);
            }
            if (fastReload) N.Call("SET_PLAYER_FAST_RELOAD", pi, true);
        }
    }

    #endregion

    #region World

    public partial class MenuScript
    {
        private bool freezeTime, gravityOff;
        private int frozenHour = 12, frozenMinute;
        private int weatherIndex = -1, timeScaleIndex;
        private int carDensity = GameData.NormalDensity, pedDensity = GameData.NormalDensity, parkedDensity = GameData.NormalDensity;

        private static readonly string[] ClearNames = { "Everything", "Vehicles", "Pedestrians", "Police" };

        private int CurrentHour()
        {
            Pointer h = new Pointer(typeof(int)), mi = new Pointer(typeof(int));
            return N.Call("GET_TIME_OF_DAY", h, mi) ? (int)h : frozenHour;
        }

        private int CurrentMinute()
        {
            Pointer h = new Pointer(typeof(int)), mi = new Pointer(typeof(int));
            return N.Call("GET_TIME_OF_DAY", h, mi) ? (int)mi : frozenMinute;
        }

        private Menu BuildWorldMenu()
        {
            Menu m = new Menu("World");
            m.Header("Time");
            m.Add(new NumberItem("Hour", 0, 23, 1, CurrentHour, v =>
            {
                frozenHour = v;
                N.Call("SET_TIME_OF_DAY", v, CurrentMinute());
            }, true, ":00", "Changes as you scroll"));
            m.Add(new NumberItem("Minute", 0, 55, 5, () => CurrentMinute() / 5 * 5, v =>
            {
                frozenMinute = v;
                N.Call("SET_TIME_OF_DAY", CurrentHour(), v);
            }, true, "", "Changes as you scroll"));
            m.Add(new ToggleItem("Freeze Time", () => freezeTime, v =>
            {
                freezeTime = v;
                frozenHour = CurrentHour(); frozenMinute = CurrentMinute();
            }));

            m.Header("Weather");
            m.Add(new ListItem("Weather", GameData.WeatherNames, () => Math.Max(0, weatherIndex), v =>
            {
                weatherIndex = v;
                N.Call("FORCE_WEATHER_NOW", v);
            }, true, "Changes as you scroll"));
            m.Add(new ActionItem("Reset Weather", () => { weatherIndex = -1; N.Call("RELEASE_WEATHER"); Notify("Weather back to normal"); }));

            m.Header("Population");
            m.Add(new ListItem("Traffic", GameData.DensityNames, () => carDensity, v => carDensity = v, true, "Moving traffic"));
            m.Add(new ListItem("Pedestrians", GameData.DensityNames, () => pedDensity, v => pedDensity = v, true, null));
            m.Add(new ListItem("Parked Cars", GameData.DensityNames, () => parkedDensity, v => parkedDensity = v, true, null));
            m.Add(new ListItem("Clear Area", ClearNames, null, v =>
            {
                Ped p = PlayerPed(); if (p == null) return;
                Vector3 pos = p.Position;
                const float r = 80f;
                if (v == 0) N.Call("CLEAR_AREA", pos.X, pos.Y, pos.Z, r, true);
                else if (v == 1) N.Call("CLEAR_AREA_OF_CARS", pos.X, pos.Y, pos.Z, r);
                else if (v == 2) N.Call("CLEAR_AREA_OF_CHARS", pos.X, pos.Y, pos.Z, r);
                else N.Call("CLEAR_AREA_OF_COPS", pos.X, pos.Y, pos.Z, r);
                Notify("Cleared " + ClearNames[v].ToLowerInvariant() + " within 80 m");
            }, false, "Left/Right to choose, A to clear"));

            m.Header("Physics");
            m.Add(new ListItem("Game Speed", GameData.TimeScaleNames, () => timeScaleIndex, v =>
            {
                timeScaleIndex = v;
                N.Call("SET_TIME_SCALE", GameData.TimeScales[v]);
            }, true, "Slow motion"));
            m.Add(new ToggleItem("Gravity", () => !gravityOff, v =>
            {
                gravityOff = !v;
                N.Call("SET_GRAVITY_OFF", gravityOff);
            }, "GTA IV can only switch gravity on or off; there's no low-gravity setting"));

            m.Header("Map");
            m.Add(new ActionItem("Unlock All Islands", () =>
            {
                N.Call("SET_INT_STAT", 363, 3);   // STAT_ISLANDS_UNLOCKED, as ScriptHookDotNet's World.UnlockAllIslands does
                Notify("All islands unlocked");
            }, "No wanted level for visiting locked islands. Becomes permanent if you save the game"));
            return m;
        }

        private void RunWorldFrame()
        {
            if (freezeTime) N.Call("SET_TIME_OF_DAY", frozenHour, frozenMinute);
            if (carDensity != GameData.NormalDensity) N.Call("SET_RANDOM_CAR_DENSITY_MULTIPLIER", GameData.Densities[carDensity]);
            if (parkedDensity != GameData.NormalDensity) N.Call("SET_PARKED_CAR_DENSITY_MULTIPLIER", GameData.Densities[parkedDensity]);
            if (pedDensity != GameData.NormalDensity) N.Call("SET_PED_DENSITY_MULTIPLIER", GameData.Densities[pedDensity]);
        }

        private void RunWorldSlow()
        {
            if (timeScaleIndex != 0) N.Call("SET_TIME_SCALE", GameData.TimeScales[timeScaleIndex]);
            if (gravityOff) N.Call("SET_GRAVITY_OFF", true);
        }
    }

    #endregion

    #region Teleport

    public partial class MenuScript
    {
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
            m.Add(new ActionItem("Forward 10 m", () => Nudge(10f, 0f)));
            m.Add(new ActionItem("Up 25 m", () => Nudge(0f, 25f)));

            m.Header("Liberty City");
            string area = null;
            Menu areaMenu = null;
            foreach (GameData.Place pl in GameData.Places)
            {
                if (pl.Area != area)
                {
                    area = pl.Area;
                    areaMenu = new Menu(area);
                    m.Add(new SubmenuItem(areaMenu, "Landmarks in " + area));
                }
                GameData.Place p = pl;
                areaMenu.Add(new ActionItem(p.Name, () => TeleportTo(new Vector3(p.X, p.Y, p.Z), true), "Islands still locked in the story give you a wanted level (World > Unlock All Islands)"));
            }

            m.Header("Saved Positions");
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
                }, cfg.Slots[slot] == null ? "Empty" : "Overwrites what's there"));
            }
            m.Add(new SubmenuItem(save, "Remember where you are (kept in ClaudeMenu.ini)"));
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
        }

        private static bool TryParseLocation(string s, out string label, out Vector3 pos)
        {
            label = null; pos = new Vector3();
            string[] p = s.Split('|');
            if (p.Length < 4) return false;
            float x = U.ParseFloat(p[1], float.NaN), y = U.ParseFloat(p[2], float.NaN), z = U.ParseFloat(p[3], float.NaN);
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
            TeleportTo(new Vector3(target.X, target.Y, 0f), true);
        }

        /// <summary>
        /// Teleport the player (or their vehicle). With findGround the area is
        /// loaded and the ground height probed; target.Z is used if probing fails.
        /// </summary>
        private void TeleportTo(Vector3 target, bool findGround)
        {
            Ped ped = PlayerPed(); if (ped == null) return;
            Vehicle car = CurrentVehicle();

            if (findGround)
            {
                N.Call("REQUEST_COLLISION_AT_POSN", target.X, target.Y, 100.0f);
                N.Call("LOAD_ALL_OBJECTS_NOW");
                bool found = false;
                float z = target.Z;
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
                if (!found && target.Z <= 0.5f)
                {
                    // Fall back to ScriptHookDotNet's own grounding teleport.
                    Game.LocalPlayer.TeleportTo(target.X, target.Y);
                    Notify("Teleported");
                    return;
                }
                target = new Vector3(target.X, target.Y, found ? z : target.Z + 1.0f);
            }

            if (car != null) car.Position = target; else ped.Position = target;
            Notify("Teleported");
        }

        private void Nudge(float forward, float up)
        {
            Ped ped = PlayerPed(); if (ped == null) return;
            Vector3 off = U.Add(U.Mul(U.HeadingDir(ped.Heading), forward), U.V(0f, 0f, up));
            Vehicle car = CurrentVehicle();
            if (car != null) car.Position = U.Add(car.Position, off); else ped.Position = U.Add(ped.Position, off);
        }
    }

    #endregion

    #region Misc: HUD, free camera

    public partial class MenuScript
    {
        private bool hideHud, hideRadar, radarHiddenByUs, showCoords, showFps;

        // Free camera
        private bool freecamOn, freecamBroken, freecamWait;
        private Camera cam;
        private Vector3 camPos;
        private float camPitch, camYaw;

        private Menu BuildMiscMenu()
        {
            Menu m = new Menu("Misc");
            m.Header("HUD");
            m.Add(new ToggleItem("Hide HUD & Radar", () => hideHud, v => hideHud = v, "Clean screen for screenshots. The menu still shows"));
            m.Add(new ToggleItem("Hide Radar Only", () => hideRadar, v =>
            {
                hideRadar = v;
                if (!v && radarHiddenByUs) { N.Call("DISPLAY_RADAR", true); radarHiddenByUs = false; }
            }));
            m.Add(new ToggleItem("Show Coordinates", () => showCoords, v => showCoords = v));
            m.Add(new ToggleItem("Show FPS", () => showFps, v => showFps = v));
            m.Add(new ToggleItem("Live Debug", () => liveDebug, v => liveDebug = v, "Shows what Super Run/Jump and the car multipliers see each frame"));

            m.Header("Camera");
            m.Add(new ActionItem("Free Camera", StartFreecam, "Fly the camera around. A teleports you there, B goes back"));

            m.Header("Driving HUD");
            m.Add(new ToggleItem("Speedometer", () => cfg.Speedometer, v => cfg.Speedometer = v, "Shown bottom-right while you're in a vehicle"));
            m.Add(new ListItem("Speed Units", new[] { "km/h", "mph" }, () => cfg.SpeedUnits, v => cfg.SpeedUnits = v, true, null));
            return m;
        }

        private void RunMiscFrame()
        {
            if (hideHud || freecamOn) N.Call("HIDE_HUD_AND_RADAR_THIS_FRAME");
        }

        private void RunMiscSlow()
        {
            if (hideRadar) { N.Call("DISPLAY_RADAR", false); radarHiddenByUs = true; }
        }

        private void StartFreecam()
        {
            if (freecamBroken) { Notify("Free camera isn't working in this setup (see log)"); return; }
            Ped ped = PlayerPed(); if (ped == null) return;
            Close();
            try
            {
                Vector3 pos, rot;
                Camera game = Game.CurrentCamera;
                if (game != null) { pos = game.Position; rot = game.Rotation; }
                else { pos = U.Add(ped.Position, U.V(0f, 0f, 2f)); rot = U.V(0f, 0f, ped.Heading); }
                camPos = pos; camPitch = U.Clamp(rot.X, -89f, 89f); camYaw = rot.Z;
                cam = new Camera();
                cam.Position = camPos;
                cam.Rotation = U.V(camPitch, 0f, camYaw);
                cam.Activate();
                freecamOn = true;
                freecamWait = true;
                N.Call("SET_PLAYER_CONTROL", pi, false);
            }
            catch (Exception ex)
            {
                freecamBroken = true;
                Log.Write("Free camera failed: " + ex);
                Notify("Free camera isn't working in this setup (see log)");
                StopFreecam(false);
            }
        }

        private void UpdateFreecam()
        {
            if (freecamWait)
            {
                if ((input.Held & (Btn.ComboMask | Btn.NavAccept | Btn.NavBack)) == 0) freecamWait = false;
                return;
            }
            if (input.Pressed(Btn.NavBack)) { StopFreecam(false); return; }
            if (input.Pressed(Btn.NavAccept)) { StopFreecam(true); return; }

            float rd = realDt;
            camYaw -= InputState.Axis(input.RX) * 120f * rd;
            camPitch = U.Clamp(camPitch + InputState.Axis(input.RY) * 90f * rd, -89f, 89f);

            double p = camPitch * Math.PI / 180.0, y = camYaw * Math.PI / 180.0;
            Vector3 fwd = U.V((float)(-Math.Sin(y) * Math.Cos(p)), (float)(Math.Cos(y) * Math.Cos(p)), (float)Math.Sin(p));
            Vector3 right = U.V((float)Math.Cos(y), (float)Math.Sin(y), 0f);
            float mf = InputState.Axis(input.LY), ms = InputState.Axis(input.LX), mu = 0f;
            if (input.IsHeld(Btn.RB)) mu += 1f;
            if (input.IsHeld(Btn.LB)) mu -= 1f;
            if (input.IsHeld(Btn.NavUp) && !input.Connected) mf += 1f;
            if (input.IsHeld(Btn.NavDown) && !input.Connected) mf -= 1f;
            float speed = 12f * (1f + 4f * InputState.Trigger(input.RightTrigger)) * (1f - 0.75f * InputState.Trigger(input.LeftTrigger));
            Vector3 move = U.Add(U.Add(U.Mul(fwd, mf), U.Mul(right, ms)), U.V(0f, 0f, mu));
            camPos = U.Add(camPos, U.Mul(move, speed * rd));

            try
            {
                cam.Position = camPos;
                cam.Rotation = U.V(camPitch, 0f, camYaw);
            }
            catch (Exception ex)
            {
                freecamBroken = true;
                Log.Write("Free camera failed: " + ex);
                StopFreecam(false);
            }
        }

        private void StopFreecam(bool teleport)
        {
            freecamOn = false;
            try
            {
                if (cam != null) { cam.Deactivate(); cam.Delete(); }
            }
            catch (Exception ex) { Log.Write("Free camera cleanup: " + ex.Message); }
            cam = null;
            N.Call("SET_PLAYER_CONTROL", pi, true);
            if (teleport) TeleportTo(U.V(camPos.X, camPos.Y, camPos.Z), true);
        }
    }

    #endregion

    #region Settings

    public partial class MenuScript
    {
        private Menu BuildSettingsMenu()
        {
            Menu m = new Menu("Settings");
            m.Header("Controls");
            ActionItem rebOpen = new ActionItem("Rebind Open Combo", () => StartCapture(1));
            ActionItem rebClose = new ActionItem("Rebind Close Combo", () => StartCapture(2));
            ActionItem rebNitro = new ActionItem("Rebind Nitro Button", () => StartCapture(3));
            ActionItem rebJump = new ActionItem("Set Jump Button", () => StartCapture(4));
            ActionItem rebRun = new ActionItem("Set Sprint Button", () => StartCapture(5));
            m.Add(rebOpen); m.Add(rebClose); m.Add(rebNitro); m.Add(rebJump); m.Add(rebRun);
            m.Add(new ToggleItem("Freeze Player While Open", () => cfg.FreezePlayer, v =>
            {
                cfg.FreezePlayer = v;
                FreezePlayer(v && open);
            }, "Stops menu buttons also moving Niko"));
            m.Add(new ToggleItem("Camera Moves While Open", () => cfg.CameraWhileOpen, v =>
            {
                cfg.CameraWhileOpen = v;
                if (open && weFroze) { FreezePlayer(false); FreezePlayer(true); }
            }, "The right stick keeps turning the camera while the menu is open"));
            m.Add(new ToggleItem("Left Stick Navigation", () => cfg.StickNavigation, v => { cfg.StickNavigation = v; ApplyInputConfig(); }));
            m.Add(new ToggleItem("Keyboard Fallback", () => cfg.KeyboardFallback, v => { cfg.KeyboardFallback = v; ApplyInputConfig(); }, "F4 / arrows / Enter / Backspace"));

            m.Header("Look");
            m.Add(new ListItem("Menu Position", new[] { "Left", "Center", "Right" }, () => cfg.Position, v => cfg.Position = v, true, null));
            List<string> themeNames = new List<string>();
            foreach (Theme t in Themes.All) themeNames.Add(t.Name);
            m.Add(new ListItem("Theme", themeNames.ToArray(), () => cfg.Theme, v => cfg.Theme = v, true, null));
            m.Add(new NumberItem("Menu Size", 70, 150, 10, () => (int)Math.Round(cfg.Scale * 100), v => cfg.Scale = v / 100f, true, "%", "Bigger is easier to read on the Deck's 7\" screen"));
            m.Add(new NumberItem("Rows Shown", 6, 16, 1, () => cfg.MaxVisible, v => cfg.MaxVisible = v, true, "", null));
            m.Add(new NumberItem("Menu Opacity", 40, 100, 10, () => cfg.Opacity, v => cfg.Opacity = v, true, "%", "Lower lets the game show through the menu"));

            m.Header("Saving");
            m.Add(new ToggleItem("Save When Menu Closes", () => cfg.SaveOnClose, v => { cfg.SaveOnClose = v; cfg.Save(); }, "Writes ClaudeMenu.ini every time you close the menu"));
            m.Add(new ActionItem("Save Settings Now", () => Notify(cfg.Save() ? "Settings saved" : "Couldn't write ClaudeMenu.ini")));
            m.Add(new ActionItem("Controller Status", () =>
            {
                Notify(input.Connected ? "Controller connected (XInput)" : (XInput.Available ? "No controller found - check Steam Input" : "XInput DLL not found"));
            }));
            m.Add(new InfoItem("Version", () => Version));

            // Refresh the help text showing current bindings each time Settings opens.
            m.OnEnter = menu =>
            {
                rebOpen.Help = "Current: " + Btn.ToText(cfg.OpenCombo);
                rebClose.Help = "Current: " + Btn.ToText(cfg.CloseCombo);
                rebNitro.Help = "Current: " + Btn.ToText(cfg.NitroCombo) + ". One button is fine";
                rebJump.Help = "The game's jump button, which Super Jump listens for. Current: " + Btn.ToText(cfg.JumpButton);
                rebRun.Help = "The game's sprint button, which Super Run listens for. Current: " + Btn.ToText(cfg.RunButton);
            };
            return m;
        }
    }

    #endregion

    #region Vehicles: state, quick actions, spawner and search

    public partial class MenuScript
    {
        /// <summary>What ClaudeMenu remembers about one vehicle (keyed by its handle).</summary>
        private sealed class VehState
        {
            public int Model;
            public int Kind;                         // 0 unknown, 1 car/bike (tunable), 2 other
            public readonly Tuning Tune = new Tuning();
            public readonly bool[] Proofs = new bool[5];   // bullet, fire, explosion, collision, melee
            public bool TyresProof, EngineOff;
            public readonly bool[] DoorOpen = new bool[6];
        }

        private readonly Dictionary<int, VehState> vehStates = new Dictionary<int, VehState>();
        private int nextVehPrune;

        private bool warpIntoSpawned = true, replaceLastSpawn = true;
        private Vehicle lastSpawned;
        private readonly List<Vehicle> spawned = new List<Vehicle>();
        private bool vehGod, autoRepair;

        // Model catalog, filled the first time the spawner opens (needs the game running).
        private bool catalogReady;
        private readonly Dictionary<string, string> friendly = new Dictionary<string, string>();
        private readonly Dictionary<string, bool> available = new Dictionary<string, bool>();
        private readonly Dictionary<int, string> modelByHash = new Dictionary<int, string>();

        private static readonly string[] DoorNames = { "Front Left", "Front Right", "Rear Left", "Rear Right", "Hood", "Trunk" };
        private static readonly string[] WindowNames = { "Front Left", "Front Right", "Rear Left", "Rear Right", "Windscreen", "Rear Window" };
        private static readonly string[] TyreNames = { "Front Left", "Front Right", "Middle Left", "Middle Right", "Rear Left", "Rear Right" };
        private static readonly string[] PaintSlots = { "Primary", "Secondary", "Specular", "Trim" };

        private VehState StateFor(Vehicle v)
        {
            int model = N.OutInt("GET_CAR_MODEL", 0, v);
            VehState st;
            if (!vehStates.TryGetValue(H(v), out st) || st.Model != model)
            {
                // New vehicle, or the handle was reused by a different model: start fresh.
                st = new VehState();
                st.Model = model;
                vehStates[H(v)] = st;
            }
            if (st.Kind == 0 && model != 0)
                st.Kind = (N.Get<bool>("IS_THIS_MODEL_A_CAR", false, model) || N.Get<bool>("IS_THIS_MODEL_A_BIKE", false, model)) ? 1 : 2;
            return st;
        }

        private string ModelLabel(int hash)
        {
            string name;
            if (modelByHash.TryGetValue(hash, out name))
            {
                string f;
                return friendly.TryGetValue(name, out f) ? f : Pretty(name);
            }
            string gxt = N.Get<string>("GET_DISPLAY_NAME_FROM_VEHICLE_MODEL", null, hash);
            if (string.IsNullOrEmpty(gxt)) return "Vehicle";
            string text = N.Get<string>("GET_STRING_FROM_TEXT_FILE", null, gxt);
            return IsGoodText(text) ? text : Pretty(gxt);
        }

        private static bool IsGoodText(string s)
        {
            return !string.IsNullOrEmpty(s) && s.Length < 40 && s.IndexOf("NULL", StringComparison.Ordinal) < 0;
        }

        private void PrepareCatalog()
        {
            if (catalogReady) return;
            catalogReady = true;
            foreach (string[] cls in GameData.VehicleClasses)
            {
                for (int i = 1; i < cls.Length; i++)
                {
                    string model = cls[i];
                    if (available.ContainsKey(model)) continue;
                    int hash = Hash(model);
                    bool ok = hash != 0 && N.Get<bool>("IS_MODEL_IN_CDIMAGE", false, hash);
                    available[model] = ok;
                    if (hash != 0) modelByHash[hash] = model;
                    string name = Pretty(model);
                    if (ok)
                    {
                        string gxt = N.Get<string>("GET_DISPLAY_NAME_FROM_VEHICLE_MODEL", null, hash);
                        string text = string.IsNullOrEmpty(gxt) ? null : N.Get<string>("GET_STRING_FROM_TEXT_FILE", null, gxt);
                        if (IsGoodText(text)) name = text;
                    }
                    friendly[model] = name;
                }
            }
        }

        private string Friendly(string model)
        {
            string f;
            return friendly.TryGetValue(model, out f) ? f : Pretty(model);
        }

        private bool IsAvailable(string model)
        {
            bool ok;
            return !available.TryGetValue(model, out ok) || ok;
        }

        private Menu BuildVehicleMenu()
        {
            Menu m = new Menu("Vehicles");
            m.Add(new SubmenuItem(BuildSpawnerMenu(), "Spawn any car, bike, heli or boat in the game"));
            m.Add(new SubmenuItem(BuildCustomsMenu(), "Paint with live preview, extras, livery, doors, tyres, proofs"));
            m.Add(new SubmenuItem(BuildPerformanceMenu(), "Engine power and brake multipliers, grip, top speed, nitro"));
            m.Add(new SubmenuItem(BuildPresetsMenu(), "Save the car you're in (paint, extras, tuning) and spawn it again later"));

            m.Header("Quick Actions");
            m.Add(new ActionItem("Repair", () => { Vehicle v = NeedVehicle(); if (v != null) { RepairVehicle(v); Notify("Repaired"); } }));
            m.Add(new ActionItem("Wash", () => { Vehicle v = NeedVehicle(); if (v != null) WashVehicle(v); }));
            m.Add(new ActionItem("Flip Upright", () => { Vehicle v = NeedVehicle(); if (v != null) N.Call("SET_CAR_ON_GROUND_PROPERLY", v); }));
            m.Add(new ToggleItem("Vehicle God Mode", () => vehGod, v =>
            {
                vehGod = v;
                Vehicle car = CurrentVehicle();
                if (car != null && !v)
                {
                    N.Call("SET_CAR_CAN_BE_DAMAGED", car, true);
                    N.Call("SET_CAR_CAN_BE_VISIBLY_DAMAGED", car, true);
                    N.Call("SET_CAN_BURST_CAR_TYRES", car, !StateFor(car).TyresProof);
                }
            }, "Applies to whatever you're driving"));
            m.Add(new ToggleItem("Auto Repair", () => autoRepair, v => autoRepair = v, "Fixes the vehicle as soon as it takes damage"));
            m.Add(new ActionItem("Delete Current Vehicle", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                DeleteVehicle(v);
            }));
            return m;
        }

        private void RepairVehicle(Vehicle v)
        {
            N.Call("FIX_CAR", v);
            N.Call("SET_ENGINE_HEALTH", v, 1000.0f);
            N.Call("EXTINGUISH_CAR_FIRE", v);
            for (int i = 0; i < 6; i++) N.Call("FIX_CAR_TYRE", v, i);
        }

        private void WashVehicle(Vehicle v)
        {
            N.Call("SET_VEHICLE_DIRT_LEVEL", v, 0.0f);
            N.Call("WASH_VEHICLE_TEXTURES", v, 255);
        }

        private void DeleteVehicle(Vehicle v)
        {
            Ped p = PlayerPed();
            if (p != null && N.Get<bool>("IS_CHAR_IN_CAR", false, p, v)) p.Position = U.Add(v.Position, U.V(0f, 0f, 2.0f));
            vehStates.Remove(H(v));
            spawned.Remove(v);
            if (lastSpawned != null && H(lastSpawned) == H(v)) lastSpawned = null;
            v.Delete();
        }

        private Menu BuildSpawnerMenu()
        {
            Menu m = new Menu("Spawn Vehicle");
            m.OnEnter = menu => PrepareCatalog();
            m.Header("Options");
            m.Add(new ListItem("Spawn", new[] { "Driving it", "In front of you" }, () => warpIntoSpawned ? 0 : 1, v => warpIntoSpawned = v == 0, true, "Put you in the driver's seat, or park it in front of you"));
            m.Add(new ToggleItem("Replace Last Spawned", () => replaceLastSpawn, v => replaceLastSpawn = v, "Delete your previous spawn so they don't pile up"));
            m.Add(new SubmenuItem(BuildSearchMenu(), "Pick letters with the D-pad to find a model by name"));

            m.Header("Categories");
            foreach (string[] cls in GameData.VehicleClasses)
            {
                string[] c = cls;
                m.Add(new SubmenuItem(c[0], () => BuildCategory(c), (c.Length - 1) + " models"));
            }

            m.Header("Spawned Vehicles");
            m.Add(new InfoItem("Spawned This Session", () => U.I(CountSpawned())));
            m.Add(new ActionItem("Delete Last Spawned", () =>
            {
                if (lastSpawned != null && lastSpawned.Exists()) { DeleteVehicle(lastSpawned); Notify("Deleted"); }
                else Notify("Nothing to delete");
            }));
            m.Add(new ActionItem("Delete All Spawned", () =>
            {
                int n = 0;
                foreach (Vehicle v in spawned.ToArray())
                    if (v != null && v.Exists()) { DeleteVehicle(v); n++; }
                spawned.Clear();
                lastSpawned = null;
                Notify("Deleted " + n + " vehicle" + (n == 1 ? "" : "s"));
            }));
            return m;
        }

        private int CountSpawned()
        {
            int n = 0;
            foreach (Vehicle v in spawned) if (v != null && v.Exists()) n++;
            return n;
        }

        private Menu BuildCategory(string[] cls)
        {
            PrepareCatalog();
            Menu sub = new Menu(cls[0]);
            for (int i = 1; i < cls.Length; i++)
            {
                string model = cls[i];
                bool ok = IsAvailable(model);
                sub.Add(new ActionItem(Friendly(model) + (ok ? "" : "  (n/a)"), () => SpawnByName(model),
                    ok ? "Model: " + model : model + " isn't in this game/episode"));
            }
            return sub;
        }

        // ---- search: controller-only text entry --------------------------------
        private string searchText = "";

        private sealed class LetterItem : MenuItem
        {
            private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            private readonly Action<char> add;
            private int index;
            public LetterItem(Action<char> add) { this.add = add; Label = "Add Letter"; Help = "Left/Right picks a letter, A adds it"; }
            public override string Value { get { return "< " + Chars[index] + " >"; } }
            public override bool Adjustable { get { return true; } }
            public override void Adjust(MenuScript m, int dir) { index = (index + dir + Chars.Length) % Chars.Length; }
            public override void Activate(MenuScript m) { add(Chars[index]); }
        }

        private Menu BuildSearchMenu()
        {
            Menu m = new Menu("Search");
            m.Add(new InfoItem("Search For", () => searchText.Length == 0 ? "(nothing yet)" : searchText + "_"));
            m.Add(new LetterItem(c => { if (searchText.Length < 16) searchText += c; RebuildSearch(m); }));
            m.Add(new ActionItem("Delete Last Letter", () => { if (searchText.Length > 0) searchText = searchText.Substring(0, searchText.Length - 1); RebuildSearch(m); }));
            m.Add(new ActionItem("Clear", () => { searchText = ""; RebuildSearch(m); }));
            m.OnEnter = menu => { PrepareCatalog(); RebuildSearch(menu); };
            return m;
        }

        private const int SearchFixedRows = 4;

        private void RebuildSearch(Menu m)
        {
            int keep = m.Selected;
            m.Items.RemoveRange(SearchFixedRows, m.Items.Count - SearchFixedRows);
            if (searchText.Length == 0)
            {
                m.Header("Results");
                m.Add(new InfoItem("Add a letter or two", () => ""));
            }
            else
            {
                List<MenuItem> results = new List<MenuItem>();
                string q = searchText.ToUpperInvariant();
                foreach (string[] cls in GameData.VehicleClasses)
                {
                    for (int i = 1; i < cls.Length && results.Count < 40; i++)
                    {
                        string model = cls[i];
                        if (!IsAvailable(model)) continue;
                        string fname = Friendly(model);
                        if (model.IndexOf(q, StringComparison.Ordinal) < 0 && fname.ToUpperInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                        bool dup = false;
                        foreach (MenuItem r in results) if (r.Help == "Model: " + model) dup = true;
                        if (dup) continue;
                        results.Add(new ActionItem(fname, () => SpawnByName(model), "Model: " + model));
                    }
                }
                m.Header("Results (" + results.Count + (results.Count == 40 ? "+" : "") + ")");
                if (results.Count == 0) m.Add(new InfoItem("No matches", () => ""));
                foreach (MenuItem r in results) m.Add(r);
            }
            m.Selected = keep;
            Refresh(m);
        }

        // ---- spawning ------------------------------------------------------------
        private void SpawnByName(string model)
        {
            int hash = Hash(model);
            if (hash != 0) modelByHash[hash] = model;
            SpawnVehicle(hash, Friendly(model), null);
        }

        private Vehicle SpawnVehicle(int hash, string label, Preset preset)
        {
            Ped ped = PlayerPed();
            if (ped == null) return null;
            if (hash != 0 && N.Get<bool>("IS_MODEL_IN_CDIMAGE", false, hash) && !N.Get<bool>("IS_THIS_MODEL_A_VEHICLE", true, hash))
            {
                Notify(label + " isn't a vehicle model"); return null;
            }
            if (!LoadModel(hash, label)) return null;

            float h = ped.Heading;
            Vehicle old = CurrentVehicle();
            Vector3 basePos = old != null ? old.Position : ped.Position;

            // Step out of the current vehicle first if we're going to warp or delete it.
            bool oldIsLastSpawn = old != null && lastSpawned != null && lastSpawned.Exists() && H(old) == H(lastSpawned);
            if (old != null && (warpIntoSpawned || (replaceLastSpawn && oldIsLastSpawn)))
                ped.Position = U.Add(basePos, U.V(0f, 0f, 2.5f));

            if (replaceLastSpawn && lastSpawned != null && lastSpawned.Exists() && !N.Get<bool>("IS_CHAR_IN_CAR", false, ped, lastSpawned))
                DeleteVehicle(lastSpawned);
            lastSpawned = null;

            float dist = (old != null && !warpIntoSpawned) ? 7.0f : 5.0f;
            Vector3 pos = U.Add(U.Add(basePos, U.Mul(U.HeadingDir(h), dist)), U.V(0f, 0f, 0.5f));

            Vehicle car = World.CreateVehicle(new Model(hash), pos);
            N.Call("MARK_MODEL_AS_NO_LONGER_NEEDED", hash);
            if (car == null || !car.Exists()) { Notify("Couldn't spawn " + label); return null; }

            car.Heading = h;
            N.Call("SET_CAR_ON_GROUND_PROPERLY", car);
            if (warpIntoSpawned) ped.WarpIntoVehicle(car, VehicleSeat.Driver);
            lastSpawned = car;
            spawned.Add(car);
            if (preset != null) ApplyPreset(car, preset);
            Notify("Spawned " + label);
            return car;
        }

        private void RunVehicleSlow(Ped ped)
        {
            Vehicle v = CurrentVehicle();
            if (v != null)
            {
                VehState st = StateFor(v);
                if (vehGod)
                {
                    N.Call("SET_CAR_CAN_BE_DAMAGED", v, false);
                    N.Call("SET_CAR_CAN_BE_VISIBLY_DAMAGED", v, false);
                    N.Call("SET_CAN_BURST_CAR_TYRES", v, false);
                }
                if (autoRepair && (v.Health < 990 || N.Get<float>("GET_ENGINE_HEALTH", 1000f, v) < 990f)) RepairVehicle(v);
                if (st.EngineOff) N.Call("SET_CAR_ENGINE_ON", v, false, true);
                Tuning tu = TuneFor(st);
                hudTune = tu.IsStock ? null : tu.Summary();
            }

            // Forget vehicles that no longer exist, so a reused handle never inherits tuning.
            if (now >= nextVehPrune)
            {
                nextVehPrune = now + 3000;
                List<int> gone = new List<int>();
                foreach (int handle in vehStates.Keys)
                    if (!N.Get<bool>("DOES_VEHICLE_EXIST", true, handle)) gone.Add(handle);
                foreach (int handle in gone) vehStates.Remove(handle);
                spawned.RemoveAll(x => x == null || !x.Exists());
            }
        }
    }

    #endregion

    #region Vehicles: customs garage

    public partial class MenuScript
    {
        // Paint picker state (only one picker is open at a time).
        private Vehicle pickerVeh;
        private int pickerSlot, pickerOriginal;

        private static int[] GetPaint(Vehicle v)
        {
            int[] c = new int[4];
            Pointer a = new Pointer(typeof(int)), b = new Pointer(typeof(int));
            if (N.Call("GET_CAR_COLOURS", v, a, b)) { c[0] = (int)a; c[1] = (int)b; }
            Pointer d = new Pointer(typeof(int)), e = new Pointer(typeof(int));
            if (N.Call("GET_EXTRA_CAR_COLOURS", v, d, e)) { c[2] = (int)d; c[3] = (int)e; }
            return c;
        }

        private static void SetPaint(Vehicle v, int[] c)
        {
            N.Call("CHANGE_CAR_COLOUR", v, c[0], c[1]);
            N.Call("SET_EXTRA_CAR_COLOURS", v, c[2], c[3]);
        }

        private static void SetPaintSlot(Vehicle v, int slot, int index)
        {
            int[] c = GetPaint(v);
            c[slot] = index;
            SetPaint(v, c);
        }

        private string PaintName(int slot)
        {
            Vehicle v = CurrentVehicle();
            return v == null ? "-" : GameData.ColorName(GetPaint(v)[slot]);
        }

        private Menu BuildCustomsMenu()
        {
            Menu m = new Menu("Customs Garage");
            m.OnEnter = menu => { if (CurrentVehicle() == null) Notify("Get in a vehicle to customise it"); };

            m.Header("Paint");
            for (int s = 0; s < 4; s++)
            {
                int slot = s;
                m.Add(new SubmenuItem(BuildPaintPicker(slot), () => PaintName(slot),
                    slot < 2 ? "Scroll to preview, A to keep, B to go back without changing"
                             : (slot == 2 ? "The shine/pearl colour (IV's third colour slot)" : "Trim and details (IV's fourth colour slot)")));
            }
            m.Add(new ActionItem("Random Paint", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                SetPaint(v, new[] { rng.Next(0, 134), rng.Next(0, 134), rng.Next(0, 134), rng.Next(0, 134) });
            }));

            m.Header("Body");
            m.Add(new SubmenuItem(BuildExtrasMenu(), "Turn the model's optional parts on or off"));
            m.Add(new NumberItem("Livery", 0, 0, () => LiveryCount() - 1, 1, () =>
            {
                Vehicle v = CurrentVehicle();
                return v == null ? 0 : Math.Max(0, N.OutInt("GET_CAR_LIVERY", 0, v));
            }, v =>
            {
                Vehicle car = NeedVehicle(); if (car == null) return;
                if (LiveryCount() <= 1) { Notify("This model has no liveries"); return; }
                N.Call("SET_CAR_LIVERY", car, v);
            }, true, "", "Only some models have liveries (e.g. taxis, police, some bikes)"));
            m.Add(new NumberItem("Dirt", 0, 15, 1, () =>
            {
                Vehicle v = CurrentVehicle();
                return v == null ? 0 : (int)Math.Round(N.OutFloat("GET_VEHICLE_DIRT_LEVEL", 0f, v));
            }, v =>
            {
                Vehicle car = NeedVehicle(); if (car != null) N.Call("SET_VEHICLE_DIRT_LEVEL", car, (float)v);
            }, true, "", "0 is spotless, 15 is filthy"));
            m.Add(new SubmenuItem(BuildDoorsMenu(), "Open, close or break off doors, hood and trunk"));
            m.Add(new SubmenuItem(BuildWindowsMenu()));

            m.Header("Condition");
            m.Add(new ActionItem("Repair", () => { Vehicle v = NeedVehicle(); if (v != null) { RepairVehicle(v); Notify("Repaired"); } }));
            m.Add(new ActionItem("Wash", () => { Vehicle v = NeedVehicle(); if (v != null) WashVehicle(v); }));
            m.Add(new SubmenuItem(BuildTyresMenu()));
            m.Add(new ToggleItem("Engine Off", () => { Vehicle v = CurrentVehicle(); return v != null && StateFor(v).EngineOff; }, on =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                StateFor(v).EngineOff = on;
                N.Call("SET_CAR_ENGINE_ON", v, !on, true);
            }, "Kills the engine and keeps it off until you switch this back"));

            m.Header("Protection");
            string[] proofNames = { "Bulletproof", "Fireproof", "Explosion-proof", "Collision-proof", "Melee-proof" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                m.Add(new ToggleItem(proofNames[idx], () => { Vehicle v = CurrentVehicle(); return v != null && StateFor(v).Proofs[idx]; }, on =>
                {
                    Vehicle v = NeedVehicle(); if (v == null) return;
                    VehState st = StateFor(v);
                    st.Proofs[idx] = on;
                    N.Call("SET_CAR_PROOFS", v, st.Proofs[0], st.Proofs[1], st.Proofs[2], st.Proofs[3], st.Proofs[4]);
                }, "SET_CAR_PROOFS, for the vehicle you're in"));
            }
            return m;
        }

        private int LiveryCount()
        {
            Vehicle v = CurrentVehicle();
            return v == null ? 0 : N.OutInt("GET_NUM_CAR_LIVERIES", 0, v);
        }

        private Menu BuildPaintPicker(int slot)
        {
            Menu pm = new Menu(PaintSlots[slot] + " Colour");
            foreach (CarColor c in GameData.Colors)
            {
                pm.Add(new ColorItem(c, chosen =>
                {
                    if (pickerVeh == null || !pickerVeh.Exists()) { Notify("Get in a vehicle first"); return; }
                    SetPaintSlot(pickerVeh, pickerSlot, chosen.Index);
                    pickerOriginal = chosen.Index;
                    Notify(PaintSlots[pickerSlot] + ": " + chosen.Name);
                }));
            }
            pm.OnEnter = menu =>
            {
                pickerSlot = slot;
                pickerVeh = CurrentVehicle();
                if (pickerVeh == null) { Notify("Get in a vehicle to paint it"); return; }
                pickerOriginal = GetPaint(pickerVeh)[slot];
                if (pickerOriginal >= 0 && pickerOriginal < menu.Items.Count) menu.Selected = pickerOriginal;
            };
            pm.OnHighlight = (menu, item) =>
            {
                ColorItem ci = item as ColorItem;
                if (ci != null && pickerVeh != null && pickerVeh.Exists()) SetPaintSlot(pickerVeh, pickerSlot, ci.Color.Index);
            };
            pm.OnExit = menu =>
            {
                // Leaving without pressing A puts the original colour back.
                if (pickerVeh != null && pickerVeh.Exists() && GetPaint(pickerVeh)[pickerSlot] != pickerOriginal)
                    SetPaintSlot(pickerVeh, pickerSlot, pickerOriginal);
                pickerVeh = null;
            };
            return pm;
        }

        private Menu BuildExtrasMenu()
        {
            Menu m = new Menu("Extras");
            for (int i = 1; i <= 9; i++)
            {
                int id = i;
                m.Add(new ToggleItem("Extra " + id, () =>
                {
                    Vehicle v = CurrentVehicle();
                    return v != null && N.Get<bool>("IS_VEHICLE_EXTRA_TURNED_ON", false, v, id);
                }, on =>
                {
                    Vehicle v = NeedVehicle(); if (v != null) N.Call("TURN_OFF_VEHICLE_EXTRA", v, id, !on);
                }, "Roof racks, light bars, spoilers... IV can't tell which extras a model has, so missing ones do nothing"));
            }
            return m;
        }

        private Menu BuildDoorsMenu()
        {
            Menu m = new Menu("Doors");
            for (int i = 0; i < DoorNames.Length; i++)
            {
                int d = i;
                m.Add(new ToggleItem(DoorNames[d], () => { Vehicle v = CurrentVehicle(); return v != null && StateFor(v).DoorOpen[d]; }, on =>
                {
                    Vehicle v = NeedVehicle(); if (v == null) return;
                    StateFor(v).DoorOpen[d] = on;
                    N.Call(on ? "OPEN_CAR_DOOR" : "SHUT_CAR_DOOR", v, d);
                }));
            }
            m.Add(new ActionItem("Open All", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                VehState st = StateFor(v);
                for (int d = 0; d < 6; d++) { N.Call("OPEN_CAR_DOOR", v, d); st.DoorOpen[d] = true; }
            }));
            m.Add(new ActionItem("Close All", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                N.Call("CLOSE_ALL_CAR_DOORS", v);
                VehState st = StateFor(v);
                for (int d = 0; d < 6; d++) st.DoorOpen[d] = false;
            }));
            m.Add(new ListItem("Break Off", DoorNames, null, d =>
            {
                Vehicle v = NeedVehicle(); if (v != null) N.Call("BREAK_CAR_DOOR", v, d, false);
            }, false, "Left/Right to choose, A to rip it off. Repair puts it back"));
            return m;
        }

        private Menu BuildWindowsMenu()
        {
            Menu m = new Menu("Windows");
            m.Add(new ListItem("Smash", WindowNames, null, w => { Vehicle v = NeedVehicle(); if (v != null) N.Call("SMASH_CAR_WINDOW", v, w); }, false, "Left/Right to choose, A to smash"));
            m.Add(new ListItem("Remove", WindowNames, null, w => { Vehicle v = NeedVehicle(); if (v != null) N.Call("REMOVE_CAR_WINDOW", v, w); }, false, "Takes the glass out completely"));
            m.Add(new ActionItem("Repair Windows", () => { Vehicle v = NeedVehicle(); if (v != null) { N.Call("FIX_CAR", v); Notify("Repaired"); } },
                "GTA IV has no native to roll windows down or up; repairing the car restores them"));
            return m;
        }

        private Menu BuildTyresMenu()
        {
            Menu m = new Menu("Tyres");
            m.Add(new ActionItem("Burst All", () => { Vehicle v = NeedVehicle(); if (v != null) for (int i = 0; i < 6; i++) N.Call("BURST_CAR_TYRE", v, i); }));
            m.Add(new ActionItem("Fix All", () => { Vehicle v = NeedVehicle(); if (v != null) for (int i = 0; i < 6; i++) N.Call("FIX_CAR_TYRE", v, i); }));
            m.Add(new ListItem("Burst One", TyreNames, null, t => { Vehicle v = NeedVehicle(); if (v != null) N.Call("BURST_CAR_TYRE", v, t); }, false, "Bikes use Front Left and Rear Left"));
            m.Add(new ToggleItem("Bulletproof Tyres", () => { Vehicle v = CurrentVehicle(); return v != null && StateFor(v).TyresProof; }, on =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                StateFor(v).TyresProof = on;
                N.Call("SET_CAN_BURST_CAR_TYRES", v, !on);
            }));
            return m;
        }
    }

    #endregion

    #region Vehicles: performance multipliers, nitro and speedometer
    //
    //  GTA IV has no native for engine power, torque or brake force, and editing
    //  handling data in memory is version-specific and unsafe from a loose script.
    //  The multipliers copy what working GTA IV trainers do instead:
    //
    //   - The XMC mod menu's "NFS mode" calls SET_CAR_FORWARD_SPEED(car, speed * 1.02)
    //     every frame while RT is held, with the speed read from the game just
    //     before. It never sets the car slower than it's going, and never
    //     multiplies by the frame time.
    //   - Forward speed comes from GET_CAR_SPEED_VECTOR(car, &v, true): the car's
    //     own frame, Y = forward, X = sideways. (Rockstar's scripts use flag 0 for
    //     world space; LotsofEds' downforce mod reads .Y of the flag-1 vector.)
    //
    //  Deck history: per-frame boosts scaled by GET_FRAME_TIME did nothing as
    //  forces, and pinned the car as speed writes. If GET_FRAME_TIME reads ~0
    //  on Proton both results follow, so the boost now uses the wall clock and
    //  the live debug line shows both values.
    //
    //  Each frame the wanted forward speed is computed:
    //    Engine power  + (0.0022 x speed + 0.03) x (mult - 1) x throttle  per 30 fps frame
    //    Nitro         + 0.25 / 0.4 / 0.7 m/s per 30 fps frame while held
    //    Brakes        - 0.15 x (mult - 1) x brake, never below 0.3 m/s
    //    Handbrake     - 0.12 x (mult - 1) while RB is held
    //    Top speed     the result is clamped to the limit
    //  and applied with the chosen Boost Method:
    //    Smooth  SET_CAR_FORWARD_SPEED every frame (XMC's approach, the default)
    //    Pulses  the change summed over 200 ms, then one SET_CAR_FORWARD_SPEED
    //    Force   APPLY_FORCE_TO_CAR(type 3, local) every frame, as ScriptHookDotNet's
    //            ApplyForceRelative and LotsofEds' downforce mod do
    //  Speed-ups only happen while moving forward (>= 1 m/s), on the ground,
    //  with the handbrake released, the menu closed, and you driving. With every
    //  setting at stock (the default) none of this touches the car.
    //
    //  Grip assist pushes against the sideways slide with APPLY_FORCE_TO_CAR and
    //  stays experimental (off by default).
    //
    //  ALL OF THIS IS UNTESTED ON THE DECK until the owner confirms it.
    //

    public partial class MenuScript
    {
        private static readonly string[] BoostMethodNames = { "Smooth", "Pulses", "Force" };
        private const float PowerGrowth = 0.0022f;   // x speed, per (mult - 1), per 30 fps frame
        private const float PowerAdd = 0.03f;        // m/s, per (mult - 1), per 30 fps frame
        private const float BrakeDrop = 0.15f;       // m/s, per (mult - 1), per 30 fps frame
        private const float HandbrakeDrop = 0.12f;   // m/s, per (mult - 1), per 30 fps frame
        private const float MinBoostSpeed = 1.0f;    // m/s; no speed-ups below this (or in reverse)
        private const int PulseMs = 200;
        private static readonly float[] NitroAdd = { 0.25f, 0.4f, 0.7f };   // m/s per 30 fps frame
        private static readonly string[] NitroNames = { "Mild", "Strong", "Insane" };

        private bool sameTuneEverywhere = true;   // one tuning for everything you drive (per-vehicle is optional)
        private bool nitroEnabled, infiniteNitro; // nitro is off by default: it changes the car's speed
        private bool gripAssist;                  // experimental grip force, off by default
        private bool liveDebug;                   // on-screen values for testing
        private int nitroPower = 1;
        private readonly Tuning globalTune = new Tuning();

        // Per-frame state
        private float thrAvg, brkAvg;
        private float pulseDv;
        private int lastPulse;
        private int boostLogLeft = 8;
        private float nitroLevel = 1f;
        private int lastNitroUse;
        private bool nitroActive;

        // HUD values (written in Tick, copied into the FrameView)
        private bool hudInVehicle, hudTunable;
        private float hudSpeed;
        private string hudTune;
        private string carDebug, footDebug;

        private Tuning TuneFor(VehState st) { return sameTuneEverywhere ? globalTune : st.Tune; }

        /// <summary>The tuning the Performance menu edits right now.</summary>
        private Tuning EditTune()
        {
            if (sameTuneEverywhere) return globalTune;
            Vehicle v = CurrentVehicle();
            return v == null ? globalTune : StateFor(v).Tune;
        }

        private Menu BuildPerformanceMenu()
        {
            Menu m = new Menu("Performance");
            // Each visit re-arms the "Boost" log lines, so every test run gets fresh ones.
            m.OnEnter = menu => { boostLogLeft = 8; };
            m.Add(new InfoItem("Tuning", () =>
            {
                if (sameTuneEverywhere) return "every vehicle";
                Vehicle v = CurrentVehicle();
                return v == null ? "(get in a vehicle)" : ModelLabel(StateFor(v).Model);
            }));

            m.Header("Multipliers (experimental)");
            m.Add(new FloatItem("Engine Power", 1f, 10f, 0.5f, () => EditTune().Power, v => EditTune().Power = v, "0.0", "x", null,
                "Extra speed while you hold RT and are moving forward. A resets to 1.0x"));
            m.Add(new FloatItem("Brakes", 1f, 10f, 0.5f, () => EditTune().Brakes, v => EditTune().Brakes = v, "0.0", "x", null,
                "Stronger braking (LT) while moving forward. Never pushes you into reverse"));
            m.Add(new FloatItem("Handbrake", 1f, 5f, 0.5f, () => EditTune().Handbrake, v => EditTune().Handbrake = v, "0.0", "x", null,
                "Stronger stopping while the handbrake (RB) is held"));
            m.Add(new FloatItem("Top Speed Limit", 0f, 400f, 10f, () => EditTune().TopSpeedKmh, v => EditTune().TopSpeedKmh = (int)Math.Round(v), "0", " km/h", "Off",
                "Power and nitro won't take you past this; above it you're held at the limit"));

            m.Header("Nitro (experimental)");
            m.Add(new ToggleItem("Nitro", () => nitroEnabled, v => nitroEnabled = v, "Off by default. Hold " + Btn.ToText(cfg.NitroCombo) + " while driving. Rebind in Settings"));
            m.Add(new ListItem("Nitro Power", NitroNames, () => nitroPower, v => nitroPower = v, true, null));
            m.Add(new ToggleItem("Infinite Nitro", () => infiniteNitro, v => infiniteNitro = v));

            m.Header("How It's Applied");
            m.Add(new ListItem("Boost Method", BoostMethodNames, () => cfg.BoostMethod, v => { cfg.BoostMethod = v; pulseDv = 0f; }, true,
                "Smooth: every frame (XMC trainer). Pulses: 5 per second. Force: physics push. Try another if one does nothing"));
            m.Add(new ToggleItem("Grip Assist (experimental)", () => gripAssist, v => gripAssist = v, "Uses physics forces, which showed no effect in an earlier Deck test. Off by default"));
            m.Add(new FloatItem("Grip Strength", 1f, 10f, 1f, () => Math.Max(1, EditTune().Grip), v => EditTune().Grip = (int)Math.Round(v), "0", "", null,
                "How hard Grip Assist fights sliding (needs Grip Assist on)"));
            m.Add(new ToggleItem("Live Debug", () => liveDebug, v => liveDebug = v, "Shows what the multipliers see and do each frame, for testing"));

            m.Header("Apply To");
            m.Add(new ToggleItem("Same Tuning on Every Vehicle", () => sameTuneEverywhere, v =>
            {
                if (v && !sameTuneEverywhere)
                {
                    Vehicle car = CurrentVehicle();
                    if (car != null) globalTune.CopyFrom(StateFor(car).Tune);
                }
                sameTuneEverywhere = v;
            }, "On (default): one tuning for everything you drive. Off: each vehicle keeps its own"));
            m.Add(new ActionItem("Reset to Stock", () => { EditTune().CopyFrom(new Tuning()); Notify("Tuning reset"); }));

            m.Header("Display");
            m.Add(new ToggleItem("Speedometer", () => cfg.Speedometer, v => cfg.Speedometer = v));
            m.Add(new ListItem("Speed Units", new[] { "km/h", "mph" }, () => cfg.SpeedUnits, v => cfg.SpeedUnits = v, true, null));
            return m;
        }

        private void RunVehicleFrame(Ped ped)
        {
            nitroActive = false;
            carDebug = null;
            Vehicle v = CurrentVehicle();
            if (v == null)
            {
                hudInVehicle = false;
                pulseDv = 0f;
                RefillNitro();
                return;
            }

            float speed = N.OutFloat("GET_CAR_SPEED", 0f, v);
            hudInVehicle = true;
            hudSpeed = speed;

            VehState st = StateFor(v);
            hudTunable = st.Kind == 1;
            Tuning tu = TuneFor(st);
            bool driver = IsDriver(v, ped);

            bool blocked = open || freecamOn || paused;
            bool kb = !input.Connected && cfg.KeyboardFallback;
            float thr = blocked ? 0f : Math.Max(InputState.Trigger(input.RightTrigger), kb && Keyboard.IsDown(0x57) ? 1f : 0f);   // W
            float brk = blocked ? 0f : Math.Max(InputState.Trigger(input.LeftTrigger), kb && Keyboard.IsDown(0x53) ? 1f : 0f);    // S
            bool hb = !blocked && (input.IsHeld(Btn.RB) || (kb && Keyboard.IsDown(0x20)));                                          // Space
            bool nitroHeld = !blocked && nitroEnabled && input.ComboHeld(cfg.NitroCombo);
            // Ease in when the trigger is pressed (no jolt), but stop the moment it's released.
            float k = Math.Min(1f, realDt * 12f);
            thrAvg = thr < 0.05f ? 0f : thrAvg + (thr - thrAvg) * k;
            brkAvg = brk < 0.05f ? 0f : brkAvg + (brk - brkAvg) * k;

            bool wantsGrip = gripAssist && tu.Grip > 0;
            if (st.Kind != 1 || !driver || (tu.IsStock && !nitroHeld && !wantsGrip))
            {
                pulseDv = 0f;
                RefillNitro();
                if (liveDebug) carDebug = "CAR  driver " + YN(driver) + "  tunable " + YN(st.Kind == 1) + "  stock " + YN(tu.IsStock) + "  speed " + U.F(speed, "0.0") + "  RT " + U.F(thrAvg, "0.00") + "  (nothing to apply)";
                return;
            }

            // Signed speeds in the car's own frame: Y forward, X sideways.
            Pointer lp = new Pointer(typeof(Vector3));
            bool haveLocal = N.Call("GET_CAR_SPEED_VECTOR", v, lp, true);
            Vector3 local = haveLocal ? (Vector3)lp : new Vector3(0f, speed, 0f);
            float fwd = local.Y;
            bool inAir = N.Get<bool>("IS_CAR_IN_AIR_PROPER", false, v);
            // This frame as a fraction of a 30 fps frame (XMC's tuning was made at 30 fps), in game time.
            float f30 = U.Clamp(realDt * 30f, 0.25f, 3f) * GameData.TimeScales[timeScaleIndex];

            float add = 0f;
            string why = null;
            bool grounded = !blocked && !inAir;
            if (grounded && !hb && fwd >= MinBoostSpeed)
            {
                if (tu.Power > 1.001f && thrAvg > 0.05f)
                {
                    add += (fwd * PowerGrowth + PowerAdd) * (tu.Power - 1f) * thrAvg * f30;
                    why = "power";
                }
                if (nitroHeld && nitroLevel > 0f)
                {
                    add += NitroAdd[nitroPower] * f30;
                    nitroActive = true;
                    lastNitroUse = now;
                    if (!infiniteNitro) nitroLevel = Math.Max(0f, nitroLevel - realDt / 3.5f);
                    why = "nitro";
                }
            }
            if (!nitroActive) RefillNitro();
            if (grounded && fwd > MinBoostSpeed)
            {
                if (tu.Brakes > 1.001f && brkAvg > 0.05f)
                {
                    add -= BrakeDrop * (tu.Brakes - 1f) * brkAvg * f30;
                    why = "brakes";
                }
                if (tu.Handbrake > 1.001f && hb)
                {
                    add -= HandbrakeDrop * (tu.Handbrake - 1f) * f30;
                    why = "handbrake";
                }
                if (add < 0f) add = Math.Max(add, 0.3f - fwd);   // never below 0.3 m/s forward
            }
            float target = fwd + add * cfg.ForceScale;
            if (tu.TopSpeedKmh > 0 && grounded && fwd > 0f)
            {
                float cap = tu.TopSpeedKmh / 3.6f;
                if (target > cap && (add > 0f || fwd > cap + 0.3f)) { target = Math.Max(cap, 0.3f); why = "top speed limit"; }
            }

            string did = ApplyBoost(v, fwd, target, why, grounded && !hb);

            // Experimental grip: push against the sideways slide (local X), as a physics force.
            if (wantsGrip && grounded && speed > 3f && Math.Abs(local.X) > 0.05f)
            {
                float push = -local.X * Math.Min(1f, tu.Grip * 0.03f * f30) * cfg.ForceScale;
                N.Call("APPLY_FORCE_TO_CAR", v, 3, push, 0f, 0f, 0f, 0f, 0f, 0, 1, 1, 1);
                if (did == null) did = "grip " + U.F(push, "0.00");
            }

            if (liveDebug)
                carDebug = "CAR  driver Y  air " + YN(inAir) + "  RT " + U.F(thrAvg, "0.00") + "  LT " + U.F(brkAvg, "0.00") + "  RB " + YN(hb)
                         + "  fwd " + U.F(fwd, "0.0") + "  side " + U.F(local.X, "0.0") + " m/s" + (haveLocal ? "" : " (no vector)")
                         + "  frame " + U.F(realDt * 1000f, "0") + " ms (game says " + U.F(dt * 1000f, "0") + ")"
                         + "  " + BoostMethodNames[cfg.BoostMethod].ToLowerInvariant() + ": " + (did ?? "-");
        }

        /// <summary>Move the car's forward speed from fwd to target with the chosen method. Returns what it did.</summary>
        private string ApplyBoost(Vehicle v, float fwd, float target, string why, bool pulsesAllowed)
        {
            float delta = target - fwd;
            string did = null;
            if (cfg.BoostMethod == 1)
            {
                // Pulses: sum the change, apply it at most every PulseMs.
                if (!pulsesAllowed && delta > 0f) pulseDv = 0f;
                else if (Math.Abs(delta) > 0.0005f) pulseDv += delta;
                if (now - lastPulse >= PulseMs)
                {
                    lastPulse = now;
                    if (Math.Abs(pulseDv) > 0.05f)
                    {
                        float t = Math.Max(0.3f, fwd + pulseDv);
                        N.Call("SET_CAR_FORWARD_SPEED", v, t);
                        did = U.F(fwd, "0.0") + " -> " + U.F(t, "0.0") + " (" + (why ?? "pulse") + ")";
                    }
                    pulseDv = 0f;
                }
            }
            else if (Math.Abs(delta) > 0.0005f)
            {
                if (cfg.BoostMethod == 2)
                    N.Call("APPLY_FORCE_TO_CAR", v, 3, 0f, delta, 0f, 0f, 0f, 0f, 0, 1, 1, 1);   // local, scaled by mass
                else
                    N.Call("SET_CAR_FORWARD_SPEED", v, target);
                did = U.F(fwd, "0.00") + " -> " + U.F(target, "0.00") + " (" + (why ?? "?") + ")";
            }
            if (did != null && boostLogLeft > 0)
            {
                boostLogLeft--;
                Log.Write("Boost " + BoostMethodNames[cfg.BoostMethod].ToLowerInvariant() + ": " + did + " m/s");
            }
            return did;
        }

        private static string YN(bool b) { return b ? "Y" : "N"; }

        private void RefillNitro()
        {
            if (nitroLevel < 1f && now - lastNitroUse > 1500) nitroLevel = Math.Min(1f, nitroLevel + realDt / 12f);
        }

        private void FillSpeedo(FrameView f)
        {
            if (!cfg.Speedometer || !hudInVehicle || freecamOn || hideHud) return;
            if (f.MenuOpen && cfg.Position == 2) return;   // would overlap a right-hand menu
            bool mph = cfg.SpeedUnits == 1;
            float shown = hudSpeed * (mph ? 2.23694f : 3.6f);
            f.Speedo = true;
            f.SpeedText = U.I((int)Math.Round(shown));
            f.SpeedUnit = mph ? "mph" : "km/h";
            f.SpeedFrac = hudSpeed * 3.6f / 300f;
            f.TuneText = hudTunable ? hudTune : null;
            f.NitroShown = hudTunable && nitroEnabled;
            f.NitroLevel = nitroLevel;
            f.NitroActive = nitroActive;
        }
    }

    #endregion

    #region Chaos: world-changing modes
    //
    //  Carmageddon-style car magnet, car rain, black hole, tornado, riots and more.
    //  Ideas and force sizes come from GTA IV chaos mods with public source:
    //  ChaosIV (Jumpy Vehicles, Earthquake, launches, coloured traffic, flee,
    //  everyone out, mass breakdown) and TORISOUP's Inferno "Parupunte" script
    //  (repulsion field: ragdoll, then a push of 3 per 100 ms tick; Crazy City:
    //  car speed +/-30 every 500 ms; floaty cars: up 0.9 per tick). Both run on
    //  a 100 ms tick, so this does too.
    //
    //  Rules: every mode is off by default; nothing here ever touches the player
    //  or the player's vehicle; mission characters and vehicles are skipped
    //  (IS_PED_A_MISSION_PED / IS_CAR_A_MISSION_CAR), so story missions keep
    //  their cars and people; each tick handles at most MaxChaosCars cars and
    //  MaxChaosPeds people; "Stop All Chaos" switches everything off and undoes
    //  what can be undone (riot relationships, mad drivers, invisible cars).
    //

    public partial class MenuScript
    {
        private const int ChaosTickMs = 100;
        private const int MaxChaosCars = 40, MaxChaosPeds = 30;
        private const int MaxRainCars = 20;

        // Continuous modes (all off by default)
        private bool carMagnet, carRain, jumpyTraffic, floatyCars, crazyTraffic, madDrivers, rainbowTraffic, honkChorus, ghostTraffic;
        private bool blackHole, tornado, earthquake, repulsor, meteors, timeLapse, weatherRoulette;
        private bool riot, riotTargetsYou;
        private float chaosRadius = 70f, chaosStrength = 5f;

        // State
        private int nextChaosTick, lastChaosTick;
        private int chaosSlowAt, crazyAt, rainbowAt, honkAt, meteorAt, riotAt, weatherAt, rumbleAt;
        private Vector3 holeCenter;
        private float tornadoAngle;
        private readonly List<Vehicle> rainCars = new List<Vehicle>();
        private int rainModel, rainRequestedAt, nextRainSpawn;
        private readonly Dictionary<int, Ped> rioters = new Dictionary<int, Ped>();      // by handle
        private readonly Dictionary<int, Vehicle> hiddenCars = new Dictionary<int, Vehicle>();
        private string chaosBadge;

        private static readonly int[] RiotGroups = { 1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 19, 20, 21 };   // civilians, gangs, dealer, criminal, bum, prostitute
        private static readonly int[] RiotWeapons = { 1, 3, 7, 9, 10, 12, 13, 14 };                                      // bat, knife, pistols, shotgun, SMGs, rifle

        private bool AnyChaos
        {
            get
            {
                return carMagnet || carRain || jumpyTraffic || floatyCars || crazyTraffic || madDrivers || rainbowTraffic || honkChorus || ghostTraffic
                    || blackHole || tornado || earthquake || repulsor || meteors || timeLapse || weatherRoulette || riot || TsunamiActive;
            }
        }

        private Menu BuildChaosMenu()
        {
            Menu m = new Menu("Chaos");
            m.Add(new InfoItem("Active", () => { string b = ChaosNames(); return b.Length == 0 ? "nothing" : b; }));
            m.Add(new ActionItem("Stop All Chaos", () => { StopAllChaos(); Notify("Chaos stopped"); }, "Switches every mode off and cleans up"));

            m.Header("Cars Come Alive");
            m.Add(new ToggleItem("Car Magnet", () => carMagnet, v => carMagnet = v,
                "Nearby traffic is pulled toward you and flung at you, Carmageddon style. God Mode recommended"));
            m.Add(new ToggleItem("Car Rain", () => carRain, v => { carRain = v; if (v) PrepareCatalog(); else ReleaseRainModel(); },
                "Random cars drop out of the sky around you. Clean up with the action below"));
            m.Add(new ToggleItem("Jumpy Traffic", () => jumpyTraffic, v => jumpyTraffic = v, "Cars on the ground keep hopping"));
            m.Add(new ToggleItem("Floaty Cars", () => floatyCars, v => floatyCars = v, "Cars around you float like they're on the moon"));
            m.Add(new ToggleItem("Crazy Traffic", () => crazyTraffic, v => crazyTraffic = v, "Every half second nearby cars shoot forward or backward"));
            m.Add(new ToggleItem("Mad Drivers", () => madDrivers, v => { madDrivers = v; N.Call("SWITCH_MAD_DRIVERS", v); }, "GTA IV's own reckless-driver mode"));
            m.Add(new ToggleItem("Rainbow Traffic", () => rainbowTraffic, v => rainbowTraffic = v, "Nearby cars keep changing colour"));
            m.Add(new ToggleItem("Honking Chorus", () => honkChorus, v => honkChorus = v, "Random cars around you lean on the horn"));
            m.Add(new ToggleItem("Invisible Traffic", () => ghostTraffic, v => { ghostTraffic = v; if (!v) RestoreHiddenCars(); }, "Other cars vanish (you can still hit them)"));

            m.Header("Forces of Nature");
            m.Add(new ToggleItem("Black Hole", () => blackHole, v =>
            {
                blackHole = v;
                Ped p = PlayerPed();
                if (v && p != null) holeCenter = U.Add(U.Add(p.Position, U.Mul(U.HeadingDir(p.Heading), 35f)), U.V(0f, 0f, 10f));
            }, "A point 35 m ahead of you sucks in cars and people"));
            m.Add(new ToggleItem("Tornado", () => tornado, v => tornado = v, "A whirlwind circles you, lifting and spinning cars and people"));
            m.Add(new ToggleItem("Earthquake", () => earthquake, v => earthquake = v, "Cars and people get thrown around and the controller rumbles"));
            m.Add(new ToggleItem("Repulsor Field", () => repulsor, v => repulsor = v, "Anything that comes within 12 m of you is shoved away"));
            m.Add(new ToggleItem("Meteor Shower", () => meteors, v => meteors = v, "Explosions rain down around you, never closer than 20 m"));
            m.Add(new ToggleItem("Time Lapse", () => timeLapse, v => timeLapse = v, "Days fly by in a couple of minutes"));
            m.Add(new ToggleItem("Weather Roulette", () => weatherRoulette, v => { weatherRoulette = v; if (!v) N.Call("RELEASE_WEATHER"); }, "The weather changes every few seconds"));

            m.Header("People");
            m.Add(new ToggleItem("Riot", () => riot, v => { riot = v; if (!v) EndRiot(); }, "People around you grab weapons and fight each other"));
            m.Add(new ToggleItem("Rioters Target You Too", () => riotTargetsYou, v => riotTargetsYou = v, "Off: they only fight each other"));
            m.Add(new ActionItem("Everyone Flees", () => ChaosOnce("flee"), "Nearby people run from you"));
            m.Add(new ActionItem("Launch People", () => ChaosOnce("launchPeds"), "Nearby people fly up"));
            m.Add(new ActionItem("Set People on Fire", () => ChaosOnce("ignite")));
            m.Add(new ActionItem("Everyone Out of Their Cars", () => ChaosOnce("exit")));

            m.Header("One-Shots");
            m.Add(new ActionItem("Launch Cars Up", () => ChaosOnce("launchCars")));
            m.Add(new ActionItem("Shockwave", () => ChaosOnce("shockwave"), "Blasts cars and people away from you"));
            m.Add(new ActionItem("Explode Nearby Cars", () => ChaosOnce("explode"), "Every car in range except yours"));
            m.Add(new ActionItem("Mass Breakdown", () => ChaosOnce("breakdown"), "Kills the engine of every car in range"));
            m.Add(new ActionItem("Clean Up Rain Cars", () => { int n = ClearRainCars(true); Notify("Removed " + n + " rain car" + (n == 1 ? "" : "s")); }));

            AddTsunamiItems(m);

            m.Header("Settings");
            m.Add(new FloatItem("Chaos Radius", 30f, 150f, 10f, () => chaosRadius, v => chaosRadius = v, "0", " m", null, "How far around you chaos reaches"));
            m.Add(new FloatItem("Chaos Strength", 1f, 10f, 1f, () => chaosStrength, v => chaosStrength = v, "0", "", null, "How hard things get pushed, and how often things happen"));
            return m;
        }

        private string ChaosNames()
        {
            List<string> n = new List<string>();
            if (carMagnet) n.Add("Car Magnet"); if (carRain) n.Add("Car Rain"); if (jumpyTraffic) n.Add("Jumpy");
            if (floatyCars) n.Add("Floaty"); if (crazyTraffic) n.Add("Crazy Traffic"); if (madDrivers) n.Add("Mad Drivers");
            if (rainbowTraffic) n.Add("Rainbow"); if (honkChorus) n.Add("Honking"); if (ghostTraffic) n.Add("Invisible");
            if (blackHole) n.Add("Black Hole"); if (tornado) n.Add("Tornado"); if (earthquake) n.Add("Earthquake");
            if (repulsor) n.Add("Repulsor"); if (meteors) n.Add("Meteors"); if (timeLapse) n.Add("Time Lapse");
            if (weatherRoulette) n.Add("Weather"); if (riot) n.Add("Riot");
            if (TsunamiActive) n.Add(tsunamiPhase == 1 ? "Tsunami coming" : "Tsunami");
            return string.Join(", ", n.ToArray());
        }

        // ---- entity selection --------------------------------------------------------

        /// <summary>Nearby cars chaos may touch: never yours, never mission cars, at most MaxChaosCars.</summary>
        private List<Vehicle> ChaosCars(Vector3 center, float radius)
        {
            List<Vehicle> list = new List<Vehicle>();
            Vehicle mine = CurrentVehicle();
            int mineH = mine != null ? H(mine) : 0;
            foreach (Vehicle v in World.GetVehicles(center, radius))
            {
                if (list.Count >= MaxChaosCars) break;
                if (v == null || !v.Exists() || (mineH != 0 && H(v) == mineH)) continue;
                if (N.Get<bool>("IS_CAR_A_MISSION_CAR", false, v)) continue;
                list.Add(v);
            }
            return list;
        }

        /// <summary>Nearby people chaos may touch: never you, never mission characters, at most MaxChaosPeds.</summary>
        private List<Ped> ChaosPeds(Vector3 center, float radius, bool onFootOnly)
        {
            List<Ped> list = new List<Ped>();
            Ped me = PlayerPed();
            int meH = me != null ? H(me) : 0;
            foreach (Ped p in World.GetPeds(center, radius))
            {
                if (list.Count >= MaxChaosPeds) break;
                if (p == null || !p.Exists() || H(p) == meH) continue;
                if (N.Get<bool>("IS_PED_A_MISSION_PED", false, p)) continue;
                if (onFootOnly && N.Get<bool>("IS_CHAR_IN_ANY_CAR", false, p)) continue;
                list.Add(p);
            }
            return list;
        }

        // A velocity change on a car, as ScriptHookDotNet's Vehicle.ApplyForce does it (type 3, world, scaled by mass).
        private static void PushCar(Vehicle v, Vector3 dv)
        {
            N.Call("APPLY_FORCE_TO_CAR", v, 3, dv.X, dv.Y, dv.Z, 0f, 0f, 0f, 0, 0, 1, 1);
        }

        // People only fly when they're ragdolls (TORISOUP's repulsion field does ForceRagdoll, then ApplyForce).
        private static void FlingPed(Ped p, Vector3 dv)
        {
            N.Call("SWITCH_PED_TO_RAGDOLL", p, 10000, 1500, false, true, true, false);   // ScriptHookDotNet's Ped.ForceRagdoll arguments
            N.Call("APPLY_FORCE_TO_PED", p, 3, dv.X, dv.Y, dv.Z, 0f, 0f, 0f, 0, 0, 1, 1);
        }

        // ---- per-tick update -----------------------------------------------------------

        private void RunChaos(Ped ped)
        {
            chaosBadge = AnyChaos ? "CHAOS: " + ChaosNames() : null;
            if (paused || now < nextChaosTick) return;
            nextChaosTick = now + ChaosTickMs;
            // Scale pushes by how long it really was since the last tick (the mods these come from assume 100 ms).
            float k = U.Clamp((lastChaosTick == 0 ? ChaosTickMs : now - lastChaosTick) / (float)ChaosTickMs, 0.5f, 3f);
            lastChaosTick = now;
            if (!AnyChaos) return;

            float s = chaosStrength / 5f;   // 1.0 at the default strength
            Vector3 me = ped.Position;
            bool needCars = carMagnet || jumpyTraffic || floatyCars || crazyTraffic || rainbowTraffic || honkChorus || ghostTraffic || earthquake || repulsor || tornado;
            bool needPeds = earthquake || repulsor || tornado;
            List<Vehicle> cars = needCars ? ChaosCars(me, chaosRadius) : null;
            List<Ped> peds = needPeds ? ChaosPeds(me, chaosRadius, true) : null;

            TickTsunami(ped, k);
            if (carMagnet) TickMagnet(cars, me, s, k);
            if (jumpyTraffic)
                foreach (Vehicle v in cars)
                    if (rng.Next(100) < 40 && N.Get<bool>("IS_VEHICLE_ON_ALL_WHEELS", false, v)) PushCar(v, U.V(0f, 0f, (0.6f + 0.6f * s) * k));
            if (floatyCars)
                foreach (Vehicle v in cars) PushCar(v, U.V(0f, 0f, 0.85f * k));   // gravity takes about 0.98 m/s per 100 ms
            if (earthquake) TickEarthquake(cars, peds, s, k);
            if (repulsor) TickRepulsor(cars, peds, me, s, k);
            if (tornado) TickTornado(me, s, k);
            if (blackHole) TickBlackHole(s, k);

            if (crazyTraffic && now >= crazyAt)
            {
                crazyAt = now + 500;
                foreach (Vehicle v in cars) N.Call("SET_CAR_FORWARD_SPEED", v, rng.Next(2) == 0 ? 30f : -30f);
            }
            if (rainbowTraffic && now >= rainbowAt)
            {
                rainbowAt = now + 400;
                int n = 0;
                foreach (Vehicle v in cars) { if (++n > 20) break; N.Call("CHANGE_CAR_COLOUR", v, rng.Next(0, 134), rng.Next(0, 134)); }
            }
            if (honkChorus && now >= honkAt && cars.Count > 0)
            {
                honkAt = now + 300;
                for (int i = 0; i < 2; i++) N.Call("SOUND_CAR_HORN", cars[rng.Next(cars.Count)], rng.Next(400, 1500));
            }
            if (ghostTraffic)
                foreach (Vehicle v in cars)
                    if (!hiddenCars.ContainsKey(H(v))) { N.Call("SET_CAR_VISIBLE", v, false); hiddenCars[H(v)] = v; }

            if (carRain) TickCarRain(me, s);
            if (meteors && now >= meteorAt) { meteorAt = now + (int)(1600f / Math.Max(0.4f, s)); Meteor(me); }
            if (timeLapse)
            {
                Pointer h = new Pointer(typeof(int)), mi = new Pointer(typeof(int));
                if (N.Call("GET_TIME_OF_DAY", h, mi))
                {
                    int total = ((int)h * 60 + (int)mi + 4) % (24 * 60);   // 4 game minutes per 100 ms: a day in 36 s
                    N.Call("SET_TIME_OF_DAY", total / 60, total % 60);
                }
            }
            if (weatherRoulette && now >= weatherAt) { weatherAt = now + 6000; N.Call("FORCE_WEATHER_NOW", rng.Next(GameData.WeatherNames.Length)); }
            if (riot && now >= riotAt) { riotAt = now + 1500; TickRiot(me); }

            // Housekeeping every second: forget cars that are gone.
            if (now >= chaosSlowAt)
            {
                chaosSlowAt = now + 1000;
                rainCars.RemoveAll(x => x == null || !x.Exists());
                PruneGone(hiddenCars);
                PruneGone(rioters);
            }
        }

        private void TickMagnet(List<Vehicle> cars, Vector3 me, float s, float k)
        {
            Vector3 target = U.Add(me, U.V(0f, 0f, 1.5f));
            foreach (Vehicle v in cars)
            {
                Vector3 d = U.Sub(target, v.Position);
                float dist = U.Len(d);
                if (dist < 4f) continue;   // already on top of you
                if (N.OutFloat("GET_CAR_SPEED", 0f, v) > 32f) continue;   // fast enough already
                Vector3 dir = U.Mul(d, 1f / dist);
                // Pull toward you, with a little lift so far-away cars arc in instead of scraping along.
                PushCar(v, U.Mul(U.Add(U.Mul(dir, 2.2f * s), U.V(0f, 0f, dist > 20f ? 0.7f : 0.15f)), k));
            }
        }

        private void TickEarthquake(List<Vehicle> cars, List<Ped> peds, float s, float k)
        {
            foreach (Vehicle v in cars)
                if (rng.Next(100) < 35) PushCar(v, U.V(((float)rng.NextDouble() - 0.5f) * 1.6f * s * k, ((float)rng.NextDouble() - 0.5f) * 1.6f * s * k, (float)rng.NextDouble() * 1.8f * s * k));
            foreach (Ped p in peds)
                if (rng.Next(100) < 4) FlingPed(p, U.V(((float)rng.NextDouble() - 0.5f) * 3f, ((float)rng.NextDouble() - 0.5f) * 3f, 2f));   // people stumble now and then
            if (now >= rumbleAt) { rumbleAt = now + 250; N.Call("SHAKE_PAD", 0, 300, (int)(60 + 15 * chaosStrength)); }
        }

        private void TickRepulsor(List<Vehicle> cars, List<Ped> peds, Vector3 me, float s, float k)
        {
            const float range = 12f;
            foreach (Vehicle v in cars)
            {
                Vector3 d = U.Sub(v.Position, me);
                float dist = U.Len(d);
                if (dist > range || dist < 0.1f) continue;
                PushCar(v, U.Mul(U.Add(U.Mul(d, 3f * s / dist), U.V(0f, 0f, 0.4f)), k));   // Parupunte's repulsion pushes 3 per tick
            }
            foreach (Ped p in peds)
            {
                Vector3 d = U.Sub(p.Position, me);
                float dist = U.Len(d);
                if (dist > range || dist < 0.1f) continue;
                FlingPed(p, U.Mul(U.Add(U.Mul(d, 3f * s / dist), U.V(0f, 0f, 1f)), k));
            }
        }

        private void TickTornado(Vector3 me, float s, float k)
        {
            // The funnel circles you at 30 m, about one lap every 40 seconds.
            tornadoAngle += 0.016f * k;
            Vector3 c = U.Add(me, U.V((float)Math.Cos(tornadoAngle) * 30f, (float)Math.Sin(tornadoAngle) * 30f, 0f));
            const float reach = 22f;
            foreach (Vehicle v in ChaosCars(c, reach)) Swirl(v, null, c, me.Z, s, k, reach);
            foreach (Ped p in ChaosPeds(c, reach, true)) Swirl(null, p, c, me.Z, s, k, reach);
        }

        private void Swirl(Vehicle v, Ped p, Vector3 c, float groundZ, float s, float k, float reach)
        {
            Vector3 pos = v != null ? v.Position : p.Position;
            Vector3 flat = U.V(c.X - pos.X, c.Y - pos.Y, 0f);
            float dist = U.Len(flat);
            if (dist > reach) return;
            Vector3 inward = dist > 0.5f ? U.Mul(flat, 1f / dist) : new Vector3();
            Vector3 around = U.V(-inward.Y, inward.X, 0f);   // counter-clockwise
            float lift = pos.Z - groundZ < 28f ? 1.25f : 0.6f;   // rise, then get flung out near the top
            Vector3 dv = U.Mul(U.Add(U.Add(U.Mul(around, 1.6f * s), U.Mul(inward, 0.6f * s)), U.V(0f, 0f, lift)), k);
            if (v != null) PushCar(v, dv); else FlingPed(p, U.Mul(dv, 0.6f));
        }

        private void TickBlackHole(float s, float k)
        {
            float reach = chaosRadius * 1.3f;
            foreach (Vehicle v in ChaosCars(holeCenter, reach)) Suck(v, null, s, k);
            foreach (Ped p in ChaosPeds(holeCenter, reach, true)) Suck(null, p, s, k);
        }

        private void Suck(Vehicle v, Ped p, float s, float k)
        {
            Vector3 d = U.Sub(holeCenter, v != null ? v.Position : p.Position);
            float dist = U.Len(d);
            if (dist < 0.5f) return;
            Vector3 dir = U.Mul(d, 1f / dist);
            // Stronger close in, a slight swirl so things orbit, and lift to cancel gravity near the core.
            float pull = Math.Min(4f, (0.9f + 25f / Math.Max(dist, 6f)) * s);
            Vector3 dv = U.Add(U.Add(U.Mul(dir, pull), U.Mul(U.V(-dir.Y, dir.X, 0f), 0.5f * s)), U.V(0f, 0f, dist < 25f ? 0.9f : 0.4f));
            if (v != null) PushCar(v, U.Mul(dv, k)); else FlingPed(p, U.Mul(dv, 0.6f * k));
        }

        private void Meteor(Vector3 me)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            float r = 20f + (float)rng.NextDouble() * Math.Max(1f, chaosRadius - 20f);
            float x = me.X + (float)Math.Cos(a) * r, y = me.Y + (float)Math.Sin(a) * r, z = me.Z;
            Pointer gz = new Pointer(typeof(float));
            if (N.Call("GET_GROUND_Z_FOR_3D_COORD", x, y, me.Z + 50f, gz)) { float g = (float)gz; if (g > 0.5f && Math.Abs(g - me.Z) < 40f) z = g; }
            // ADD_EXPLOSION(x, y, z, type (0 grenade, 1 molotov, 2 rocket), radius, sound, no visuals, camera shake)
            N.Call("ADD_EXPLOSION", x, y, z, rng.Next(4) == 0 ? 1 : 2, 5.0f, true, false, 0.3f);
        }

        // ---- car rain: model loading never blocks the game ---------------------------

        private void TickCarRain(Vector3 me, float s)
        {
            if (now < nextRainSpawn) return;
            if (rainModel == 0)
            {
                string pick = RandomRainModel();
                if (pick == null) { nextRainSpawn = now + 2000; return; }
                rainModel = Hash(pick);
                rainRequestedAt = now;
                N.Call("REQUEST_MODEL", rainModel);
                return;
            }
            if (!N.Get<bool>("HAS_MODEL_LOADED", false, rainModel))
            {
                if (now - rainRequestedAt > 4000) ReleaseRainModel();   // try another model
                return;
            }
            double a = rng.NextDouble() * Math.PI * 2;
            float r = 12f + (float)rng.NextDouble() * Math.Max(1f, chaosRadius * 0.6f - 12f);
            Vector3 pos = U.V(me.X + (float)Math.Cos(a) * r, me.Y + (float)Math.Sin(a) * r, me.Z + 35f + (float)rng.NextDouble() * 25f);
            Vehicle car = World.CreateVehicle(new Model(rainModel), pos);
            ReleaseRainModel();
            nextRainSpawn = now + (int)(1500f / Math.Max(0.4f, s));
            if (car == null || !car.Exists()) return;
            car.Heading = (float)(rng.NextDouble() * 360.0);
            rainCars.Add(car);
            while (rainCars.Count > MaxRainCars)
            {
                Vehicle old = rainCars[0];
                rainCars.RemoveAt(0);
                if (old != null && old.Exists() && !PlayerIsIn(old)) old.Delete();
            }
        }

        private string RandomRainModel()
        {
            List<string> ok = new List<string>();
            foreach (string[] cls in GameData.VehicleClasses)
                for (int i = 1; i < cls.Length; i++)
                    if (IsAvailable(cls[i])) ok.Add(cls[i]);
            return ok.Count == 0 ? null : ok[rng.Next(ok.Count)];
        }

        private void ReleaseRainModel()
        {
            if (rainModel != 0) N.Call("MARK_MODEL_AS_NO_LONGER_NEEDED", rainModel);
            rainModel = 0;
        }

        private bool PlayerIsIn(Vehicle v)
        {
            Ped p = PlayerPed();
            return p != null && N.Get<bool>("IS_CHAR_IN_CAR", false, p, v);
        }

        /// <summary>Delete rain cars (or with delete = false, hand them back to the game). Returns how many.</summary>
        private int ClearRainCars(bool delete)
        {
            int n = 0;
            foreach (Vehicle v in rainCars.ToArray())
            {
                if (v == null || !v.Exists()) continue;
                if (delete && !PlayerIsIn(v)) v.Delete(); else v.NoLongerNeeded();
                n++;
            }
            rainCars.Clear();
            return n;
        }

        // ---- riot: per-person relationships, so it can be undone exactly -------------

        private void TickRiot(Vector3 me)
        {
            int added = 0;
            foreach (Ped p in ChaosPeds(me, Math.Min(chaosRadius, 60f), false))
            {
                if (added >= 8) break;
                if (rioters.ContainsKey(H(p)) || N.Get<bool>("IS_CHAR_DEAD", true, p)) continue;
                int w = RiotWeapons[rng.Next(RiotWeapons.Length)];
                N.Call("GIVE_WEAPON_TO_CHAR", p, w, 300, false);
                N.Call("SET_CURRENT_CHAR_WEAPON", p, w, true);
                foreach (int g in RiotGroups) N.Call("SET_CHAR_RELATIONSHIP", p, 5, g);   // 5 = hate
                if (riotTargetsYou) N.Call("SET_CHAR_RELATIONSHIP", p, 5, 0);
                N.Call("TASK_COMBAT_HATED_TARGETS_AROUND_CHAR", p, 40.0f);
                N.Call("SET_CHAR_KEEP_TASK", p, true);
                rioters[H(p)] = p;
                added++;
            }
        }

        private void EndRiot()
        {
            foreach (Ped p in rioters.Values)
            {
                if (p == null || !p.Exists()) continue;
                foreach (int g in RiotGroups) N.Call("CLEAR_CHAR_RELATIONSHIP", p, 5, g);
                N.Call("CLEAR_CHAR_RELATIONSHIP", p, 5, 0);
                N.Call("SET_CHAR_KEEP_TASK", p, false);
            }
            rioters.Clear();
        }

        private void RestoreHiddenCars()
        {
            foreach (Vehicle v in hiddenCars.Values) if (v != null && v.Exists()) N.Call("SET_CAR_VISIBLE", v, true);
            hiddenCars.Clear();
        }

        private static void PruneGone<T>(Dictionary<int, T> map) where T : GTA.@base.HandleObject
        {
            List<int> gone = new List<int>();
            foreach (KeyValuePair<int, T> kv in map) if (kv.Value == null || !kv.Value.Exists()) gone.Add(kv.Key);
            foreach (int h in gone) map.Remove(h);
        }

        // ---- one-shots ------------------------------------------------------------------

        private void ChaosOnce(string what)
        {
            Ped ped = PlayerPed(); if (ped == null) return;
            Vector3 me = ped.Position;
            float s = chaosStrength / 5f;
            int n = 0;
            switch (what)
            {
                case "launchCars":
                    foreach (Vehicle v in ChaosCars(me, chaosRadius)) { PushCar(v, U.V(0f, 0f, 10f + 8f * s)); n++; }
                    Notify(n + " cars launched"); break;
                case "shockwave":
                    foreach (Vehicle v in ChaosCars(me, Math.Min(chaosRadius, 50f)))
                    {
                        Vector3 d = U.Sub(v.Position, me); float dist = Math.Max(1f, U.Len(d));
                        PushCar(v, U.Add(U.Mul(d, (14f * s) / dist * Math.Max(0.3f, 1f - dist / 50f)), U.V(0f, 0f, 4f * s))); n++;
                    }
                    foreach (Ped p in ChaosPeds(me, Math.Min(chaosRadius, 50f), true))
                    {
                        Vector3 d = U.Sub(p.Position, me); float dist = Math.Max(1f, U.Len(d));
                        FlingPed(p, U.Add(U.Mul(d, (12f * s) / dist), U.V(0f, 0f, 5f * s))); n++;
                    }
                    Notify("Shockwave hit " + n + " things"); break;
                case "explode":
                    foreach (Vehicle v in ChaosCars(me, chaosRadius)) { N.Call("EXPLODE_CAR", v, true, false); n++; }
                    Notify(n + " cars exploded"); break;
                case "breakdown":
                    foreach (Vehicle v in ChaosCars(me, chaosRadius)) { N.Call("SET_ENGINE_HEALTH", v, 0f); n++; }
                    Notify(n + " engines killed"); break;
                case "launchPeds":
                    foreach (Ped p in ChaosPeds(me, chaosRadius, true)) { FlingPed(p, U.V(0f, 0f, 14f + 6f * s)); n++; }
                    Notify(n + " people launched"); break;
                case "ignite":
                    foreach (Ped p in ChaosPeds(me, Math.Min(chaosRadius, 50f), false)) { if (n >= 15) break; N.Call("START_CHAR_FIRE", p); n++; }
                    Notify(n + " people set on fire"); break;
                case "flee":
                    foreach (Ped p in ChaosPeds(me, chaosRadius, false)) { N.Call("TASK_SMART_FLEE_CHAR", p, ped, 100.0f, 20000); n++; }
                    Notify(n + " people running"); break;
                case "exit":
                    foreach (Vehicle v in ChaosCars(me, chaosRadius)) { N.Call("TASK_EVERYONE_LEAVE_CAR", v); n++; }
                    Notify("Everyone out of " + n + " cars"); break;
            }
        }

        private void StopAllChaos()
        {
            carMagnet = jumpyTraffic = floatyCars = crazyTraffic = rainbowTraffic = honkChorus = false;
            blackHole = tornado = earthquake = repulsor = meteors = timeLapse = false;
            if (carRain) { carRain = false; ReleaseRainModel(); }
            ClearRainCars(false);   // keep them, but let the game clean them up
            if (madDrivers) { madDrivers = false; N.Call("SWITCH_MAD_DRIVERS", false); }
            if (ghostTraffic) { ghostTraffic = false; RestoreHiddenCars(); }
            if (weatherRoulette) { weatherRoulette = false; N.Call("RELEASE_WEATHER"); }
            if (riot) { riot = false; EndRiot(); }
            EndTsunami();
            chaosBadge = null;
        }
    }

    #endregion

    #region Chaos: tsunami
    //
    //  GTA IV can't raise the sea from a script: there's no water-level native, and
    //  ScriptHookDotNet's memory helper is private, so a real flood would mean
    //  writing to unknown memory addresses. This is the closest honest thing: a
    //  storm-surge WAVE.
    //
    //   Warning  find the real nearest sea (GET_WATER_HEIGHT in 16 directions, water
    //            at two distances so a park pond doesn't count), thunderstorm,
    //            FORCE_WIND(1.0) (Rockstar uses 0..1; it also roughens the real
    //            ocean), people flee from the coast, a countdown.
    //   Wave     a straight front sweeps in from that sea and past you. In the 30 m
    //            band behind the front, cars and people are lifted and carried
    //            toward the wave's speed, cars get tumbled, and Rockstar's car-wash
    //            water effects (water_carwash_mist / _jets) spray along the front.
    //   Behind   cars float and drift in a slow current for a while.
    //   After    weather and wind go back to the game (FORCE_WIND(-1), RELEASE_WEATHER).
    //
    //  Same rules as the rest of Chaos: never you or your vehicle (unless "Wave Hits
    //  Me Too" is on), never mission characters or vehicles, capped counts.
    //

    public partial class MenuScript
    {
        private const int TsunamiWarnMs = 8000;
        private const float WaveBand = 30f, FloodBand = 90f, WaveEnd = 260f;
        private static readonly string[] WaveSizeNames = { "Small", "Big", "Mega" };
        private static readonly float[] WaveLift = { 0.6f, 1.2f, 2.0f };      // extra lift at the crest, m/s per 100 ms
        private static readonly float[] WaveHeight = { 4f, 8f, 15f };        // how high things get carried, m
        private static readonly string[] WaveSpeedNames = { "Slow", "Fast", "Insane" };
        private static readonly float[] WaveSpeeds = { 15f, 22f, 35f };       // m/s
        private static readonly string[] WaveFromNames = { "Nearest Sea", "Behind Me", "Ahead of Me" };

        private int waveSize = 1, waveSpeed = 1, waveFrom;
        private bool waveHitsMe;

        private int tsunamiPhase;          // 0 off, 1 warning, 2 wave
        private int tsunamiWarnUntil, tsunamiSprayAt, tsunamiRumbleAt;
        private Vector3 waveOrigin, waveDir, wavePerp;
        private float waveFront, waveBaseZ;
        private string waveFromText;
        private readonly List<int> sprays = new List<int>();

        private bool TsunamiActive { get { return tsunamiPhase != 0; } }

        private void AddTsunamiItems(Menu m)
        {
            m.Header("Tsunami");
            m.Add(new ActionItem("Send a Tsunami", StartTsunami,
                "A storm-surge wave rolls in from the sea and carries cars and people away. GTA IV can't raise the actual sea"));
            m.Add(new ListItem("Wave Size", WaveSizeNames, () => waveSize, v => waveSize = v, true, "How high it throws things (about 4 / 8 / 15 m)"));
            m.Add(new ListItem("Wave Speed", WaveSpeedNames, () => waveSpeed, v => waveSpeed = v, true, "15 / 22 / 35 m/s"));
            m.Add(new ListItem("Wave Comes From", WaveFromNames, () => waveFrom, v => waveFrom = v, true, "Nearest Sea looks for real water around you"));
            m.Add(new ToggleItem("Wave Hits Me Too", () => waveHitsMe, v => waveHitsMe = v, "Off: it flows around you and your car. On: you get swept away as well"));
            m.Add(new ActionItem("Stop Tsunami", () => { if (TsunamiActive) { EndTsunami(); Notify("Tsunami stopped"); } else Notify("No tsunami running"); }));
        }

        private void StartTsunami()
        {
            Ped ped = PlayerPed(); if (ped == null) return;
            if (TsunamiActive) { Notify("A tsunami is already on its way"); return; }
            Vector3 me = ped.Position;
            float dist;
            Vector3 toSea;
            if (waveFrom == 0 && FindSea(me, out toSea, out dist)) { }
            else
            {
                Vector3 fwd = U.HeadingDir(ped.Heading);
                toSea = waveFrom == 2 ? fwd : U.Mul(fwd, -1f);
                dist = 250f;
                if (waveFrom == 0) Notify("No sea found nearby; the wave comes from behind you");
            }
            waveDir = U.Mul(toSea, -1f);                      // the wave travels from the sea toward you
            wavePerp = U.V(-waveDir.Y, waveDir.X, 0f);
            waveOrigin = me;
            waveBaseZ = me.Z;
            waveFront = -dist;                                // the front starts at the sea
            waveFromText = Compass(toSea);
            tsunamiPhase = 1;
            tsunamiWarnUntil = now + TsunamiWarnMs;

            N.Call("FORCE_WEATHER_NOW", 7);                   // thunderstorm
            N.Call("FORCE_WIND", 1.0f);
            Vector3 coast = U.Add(me, U.Mul(toSea, dist));
            foreach (Ped p in ChaosPeds(me, Math.Min(chaosRadius * 1.5f, 120f), false))
                N.Call("TASK_SMART_FLEE_POINT", p, coast.X, coast.Y, coast.Z, 150.0f, 20000);
            Notify("TSUNAMI WARNING! Wave coming from the " + waveFromText + " (" + U.I((int)dist) + " m)");
        }

        /// <summary>Direction (flat, unit) and distance to the nearest real water, if any within 700 m.</summary>
        private static bool FindSea(Vector3 me, out Vector3 dir, out float dist)
        {
            dir = new Vector3(); dist = 0f;
            float[] steps = { 60f, 120f, 200f, 300f, 420f, 560f, 700f };
            float best = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8.0;
                Vector3 d = U.V((float)Math.Cos(a), (float)Math.Sin(a), 0f);
                for (int j = 0; j + 1 < steps.Length; j++)
                {
                    if (steps[j] >= best) break;
                    // Water here AND a step further out, so ponds and fountains don't count.
                    if (IsWater(me, d, steps[j]) && IsWater(me, d, steps[j + 1]))
                    {
                        best = steps[j]; dir = d;
                        break;
                    }
                }
            }
            if (best == float.MaxValue) return false;
            dist = best;
            return true;
        }

        private static bool IsWater(Vector3 me, Vector3 d, float r)
        {
            Pointer h = new Pointer(typeof(float));
            return N.Get<bool>("GET_WATER_HEIGHT", false, me.X + d.X * r, me.Y + d.Y * r, me.Z + 50f, h);
        }

        private static string Compass(Vector3 d)
        {
            // IV: +Y is north, +X is east.
            string[] names = { "east", "north-east", "north", "north-west", "west", "south-west", "south", "south-east" };
            double a = Math.Atan2(d.Y, d.X);
            int i = (int)Math.Round(a / (Math.PI / 4.0));
            return names[((i % 8) + 8) % 8];
        }

        /// <summary>Runs on the 100 ms chaos tick.</summary>
        private void TickTsunami(Ped ped, float k)
        {
            if (tsunamiPhase == 0) return;
            Vector3 me = ped.Position;
            if (tsunamiPhase == 1)
            {
                int left = (tsunamiWarnUntil - now + 999) / 1000;
                if (left > 0) { if ((tsunamiWarnUntil - now) % 2000 < 110) Notify("Tsunami from the " + waveFromText + " in " + left + "s!"); }
                else { tsunamiPhase = 2; Notify("TSUNAMI!"); }
                if (now >= tsunamiRumbleAt) { tsunamiRumbleAt = now + 500; N.Call("SHAKE_PAD", 0, 200, 40); }
                return;
            }

            float speed = WaveSpeeds[waveSpeed];
            waveFront += speed * 0.1f * k;
            if (waveFront > WaveEnd) { EndTsunami(); Notify("The water calms down"); return; }

            float rel = U.Dot(U.Sub(me, waveOrigin), waveDir);      // where you are along the wave's path
            float gap = waveFront - rel;                            // < 0: still coming; > 0: it has passed you
            if (now >= tsunamiRumbleAt && Math.Abs(gap) < 80f)
            {
                tsunamiRumbleAt = now + 250;
                N.Call("SHAKE_PAD", 0, 300, (int)(60 + 180 * (1f - Math.Abs(gap) / 80f)));
            }
            if (now >= tsunamiSprayAt) { tsunamiSprayAt = now + 300; MoveSpray(me, Math.Abs(gap) < 150f); }

            float reach = Math.Min(150f, chaosRadius * 1.5f);
            foreach (Vehicle v in ChaosCars(me, reach)) Surge(v, null, speed, k);
            foreach (Ped p in ChaosPeds(me, reach, true)) Surge(null, p, speed, k);

            if (waveHitsMe)
            {
                Vehicle mine = CurrentVehicle();
                if (mine != null) Surge(mine, null, speed, k);
                else Surge(null, ped, speed, k);
            }
        }

        /// <summary>Lift and carry one car or person if the wave (or its flood) is where they are.</summary>
        private void Surge(Vehicle v, Ped p, float speed, float k)
        {
            Vector3 pos = v != null ? v.Position : p.Position;
            float a = U.Dot(U.Sub(pos, waveOrigin), waveDir);
            float behind = waveFront - a;                           // metres behind the front
            if (behind < 0f || behind > WaveBand + FloodBand) return;
            float height = pos.Z - waveBaseZ;

            if (behind <= WaveBand)
            {
                // The crest: carry toward the wave's speed, lift up to the wave height.
                float crest = 1f - behind / WaveBand;
                float along = 0f;
                if (v != null)
                {
                    Pointer vp = new Pointer(typeof(Vector3));
                    if (N.Call("GET_CAR_SPEED_VECTOR", v, vp, false)) along = U.Dot((Vector3)vp, waveDir);   // flag 0 = world space
                }
                float target = speed * (0.7f + 0.3f * crest);
                float push = Math.Max(0f, target - along) * 0.35f;
                float lift = 0.98f + (height < WaveHeight[waveSize] ? WaveLift[waveSize] * crest : -0.2f);   // 0.98 cancels gravity per tick
                Vector3 dv = U.Mul(U.Add(U.Mul(waveDir, push), U.V(0f, 0f, lift)), k);
                if (v != null)
                {
                    PushCar(v, dv);
                    // Now and then push at the roof (local offset 0,0,1.2) so cars tumble.
                    if (rng.Next(100) < 12)
                        N.Call("APPLY_FORCE_TO_CAR", v, 3, waveDir.X * 1.5f, waveDir.Y * 1.5f, 0f, 0f, 0f, 1.2f, 0, 0, 1, 1);
                }
                else FlingPed(p, U.Mul(U.Add(U.Mul(waveDir, Math.Min(push, 3f)), U.V(0f, 0f, 0.5f + 0.8f * crest)), k));
            }
            else if (v != null)
            {
                // The flood behind it: cars float a little and drift with a slow current.
                PushCar(v, U.Mul(U.Add(U.Mul(waveDir, 0.25f), U.V(0f, 0f, height < 1.5f ? 0.75f : 0.45f)), k));
            }
        }

        // Spray along the part of the front nearest you, using Rockstar's car-wash water effects.
        private void MoveSpray(Vector3 me, bool show)
        {
            foreach (int id in sprays) N.Call("STOP_PTFX", id);   // what Rockstar's car wash does to end these effects
            sprays.Clear();
            if (!show) return;
            float across = U.Dot(U.Sub(me, waveOrigin), wavePerp);
            Vector3 onFront = U.Add(U.Add(waveOrigin, U.Mul(waveDir, waveFront)), U.Mul(wavePerp, across));
            for (int i = -2; i <= 2; i++)
            {
                Vector3 at = U.Add(onFront, U.Mul(wavePerp, i * 14f));
                string fx = (i & 1) == 0 ? "water_carwash_mist" : "water_carwash_jets";
                // START_PTFX(name, x, y, z, rot x, rot y, rot z, scale): Rockstar passes the scale as a float (1.0).
                int id = N.Get<int>("START_PTFX", 0, fx, at.X, at.Y, waveBaseZ + 1.5f, 0f, 0f, 0f, 3.0f);
                if (id != 0) sprays.Add(id);
            }
        }

        private void EndTsunami()
        {
            if (tsunamiPhase == 0) return;
            tsunamiPhase = 0;
            foreach (int id in sprays) N.Call("STOP_PTFX", id);   // what Rockstar's car wash does to end these effects
            sprays.Clear();
            N.Call("FORCE_WIND", -1.0f);                      // back to the game's wind (Rockstar uses -1)
            if (!weatherRoulette) N.Call("RELEASE_WEATHER");
        }
    }

    #endregion

    #region Vehicles: presets

    public partial class MenuScript
    {
        private int deleteArmedAt = -100000;

        private Menu BuildPresetsMenu()
        {
            Menu m = new Menu("Presets");
            m.OnEnter = RebuildPresets;
            return m;
        }

        private void RebuildPresets(Menu m)
        {
            m.Items.Clear();
            m.Add(new ActionItem("Save Current Vehicle", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                Preset p = CapturePreset(v);
                cfg.Presets.Add(p);
                Notify(cfg.Save() ? "Saved preset \"" + p.Name + "\"" : "Saved for this session (couldn't write INI)");
                RebuildPresets(m);
                Refresh(m);
            }, "Model, paint, extras, livery, dirt and tuning. Rename it in ClaudeMenu.ini"));
            m.Header("Saved (" + cfg.Presets.Count + ")");
            if (cfg.Presets.Count == 0) m.Add(new InfoItem("No presets yet", () => ""));
            foreach (Preset preset in cfg.Presets)
            {
                Preset p = preset;
                m.Add(new SubmenuItem(p.Name, () => BuildPresetMenu(p), p.ModelName + "  |  " + p.Tune.Summary()));
            }
        }

        private Menu BuildPresetMenu(Preset p)
        {
            Menu m = new Menu(p.Name);
            m.Add(new InfoItem("Model", () => p.ModelName));
            m.Add(new InfoItem("Paint", () => GameData.ColorName(p.Colors[0]) + " / " + GameData.ColorName(p.Colors[1])));
            m.Add(new InfoItem("Tuning", () => p.Tune.Summary()));
            m.Add(new ActionItem("Spawn", () => SpawnVehicle(p.ModelHash, p.Name, p), "Spawns it with the paint, extras and tuning"));
            m.Add(new ActionItem("Apply to Current Vehicle", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                ApplyPreset(v, p);
                Notify(N.OutInt("GET_CAR_MODEL", 0, v) == p.ModelHash ? "Preset applied" : "Applied (different model, so extras may not match)");
            }));
            m.Add(new ActionItem("Overwrite with Current Vehicle", () =>
            {
                Vehicle v = NeedVehicle(); if (v == null) return;
                Preset fresh = CapturePreset(v);
                fresh.Name = p.Name;
                int i = cfg.Presets.IndexOf(p);
                if (i >= 0) cfg.Presets[i] = fresh;
                cfg.Save();
                Notify("Preset updated");
                Back();
                ReenterTop();
            }));
            m.Add(new ActionItem("Delete", () =>
            {
                if (now - deleteArmedAt > 3000) { deleteArmedAt = now; Notify("Press A again to delete \"" + p.Name + "\""); return; }
                deleteArmedAt = -100000;
                cfg.Presets.Remove(p);
                cfg.Save();
                Notify("Preset deleted");
                Back();
                ReenterTop();
            }, "Press A twice"));
            return m;
        }

        /// <summary>Re-run the current menu's OnEnter (after its contents changed underneath it).</summary>
        private void ReenterTop()
        {
            if (stack.Count == 0) return;
            Menu top = stack.Peek();
            if (top.OnEnter != null) top.OnEnter(top);
            Refresh(top);
        }

        private Preset CapturePreset(Vehicle v)
        {
            Preset p = new Preset();
            p.ModelHash = N.OutInt("GET_CAR_MODEL", 0, v);
            string spawnName;
            p.ModelName = modelByHash.TryGetValue(p.ModelHash, out spawnName) ? spawnName : ModelLabel(p.ModelHash);
            string label = ModelLabel(p.ModelHash);
            int n = 1;
            while (true)
            {
                string candidate = label + " #" + n;
                bool used = false;
                foreach (Preset q in cfg.Presets) if (q.Name == candidate) used = true;
                if (!used) { p.Name = candidate; break; }
                n++;
            }
            p.Colors = GetPaint(v);
            for (int i = 1; i <= 9; i++)
                if (N.Get<bool>("IS_VEHICLE_EXTRA_TURNED_ON", false, v, i)) p.ExtrasOn |= 1 << i;
            p.Livery = N.OutInt("GET_NUM_CAR_LIVERIES", 0, v) > 1 ? N.OutInt("GET_CAR_LIVERY", -1, v) : -1;
            p.Dirt = U.Clamp((int)Math.Round(N.OutFloat("GET_VEHICLE_DIRT_LEVEL", 0f, v)), 0, 15);
            p.Tune.CopyFrom(TuneFor(StateFor(v)));
            return p;
        }

        private void ApplyPreset(Vehicle v, Preset p)
        {
            SetPaint(v, p.Colors);
            for (int i = 1; i <= 9; i++) N.Call("TURN_OFF_VEHICLE_EXTRA", v, i, (p.ExtrasOn & (1 << i)) == 0);
            if (p.Livery >= 0) N.Call("SET_CAR_LIVERY", v, p.Livery);
            N.Call("SET_VEHICLE_DIRT_LEVEL", v, (float)p.Dirt);
            StateFor(v).Tune.CopyFrom(p.Tune);
            if (sameTuneEverywhere) globalTune.CopyFrom(p.Tune);
        }
    }

    #endregion
}
