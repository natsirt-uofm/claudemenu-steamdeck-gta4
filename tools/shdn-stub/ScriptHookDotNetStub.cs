// Compile-only stand-in for the parts of ScriptHookDotNet's API that ClaudeMenu
// uses. It lets CI (or you, without the game) check that ClaudeMenu.cs builds
// with a C# 4 compiler and the same references ScriptHookDotNet gives a script.
// It does NOT do anything at runtime - never ship it.
//
// Keep it honest: only add members that exist in ScriptHookDotNet 1.7.x
// (github.com/Tomasak/gta4_scripthookdotnet, ScriptHookDotNet/*.h), with the
// same names, types, static/instance-ness AND access level (public, protected,
// internal), so a passing check means something. 1.1.0 failed in game because
// this stub made HandleObject.Handle public while ScriptHookDotNet keeps it
// internal.
using System;

namespace GTA.@base
{
    // interfaces.h
    public interface iBaseObject
    {
        bool Exists();
    }

    // interfaces.h: the only public way to read an object's handle.
    public interface iHandleObject : IEquatable<iHandleObject>, iBaseObject
    {
        int Handle { get; }
        object Metadata { get; }
    }

    // bObject.h
    public abstract class Object
    {
        public bool Exists() { return true; }
    }

    // bHandleObject.h: Handle is declared under "internal:" and implements
    // iHandleObject::Handle explicitly, so scripts can't call obj.Handle.
    public abstract class HandleObject : Object, iHandleObject, IEquatable<HandleObject>
    {
        internal int Handle { get { return 0; } }
        int iHandleObject.Handle { get { return 0; } }
        public object Metadata { get { return null; } }
        public bool Equals(HandleObject other) { return false; }
        public bool Equals(iHandleObject other) { return false; }
    }
}

namespace GTA
{
    // sVector3.h
    public struct Vector3
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vector3 operator *(Vector3 a, float s) { return new Vector3(a.X * s, a.Y * s, a.Z * s); }
        public float Length() { return 0f; }
    }

    // enums.h
    public enum VehicleSeat { None = -3, AnyPassengerSeat = -2, Driver = -1, RightFront = 0, LeftRear = 1, RightRear = 2 }

    // sModel.h
    public struct Model
    {
        public Model(int hash) { }
        public Model(string name) { }
    }

    // Ped.h
    public sealed class Ped : GTA.@base.HandleObject
    {
        public Vector3 Position { get; set; }
        public float Heading { get; set; }
        public int Health { get; set; }
        public bool isInVehicle() { return false; }
        public Vehicle CurrentVehicle { get { return null; } }
        public void WarpIntoVehicle(Vehicle vehicle, VehicleSeat seat) { }
    }

    // Vehicle.h
    public sealed class Vehicle : GTA.@base.HandleObject
    {
        public Vector3 Position { get; set; }
        public float Heading { get; set; }
        public int Health { get; set; }
        public void Repair() { }
        public void Delete() { }
        public void NoLongerNeeded() { }   // Vehicle.h: virtual void NoLongerNeeded() (MARK_CAR_AS_NO_LONGER_NEEDED)
    }

    // Blip.h
    public sealed class Blip : GTA.@base.HandleObject
    {
        public Vector3 Position { get { return new Vector3(); } }
    }

    // Camera.h
    public sealed class Camera : GTA.@base.HandleObject
    {
        public Camera() { }
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }
        public void Activate() { }
        public void Deactivate() { }
        public void Delete() { }
    }

    // Player.h
    public sealed class Player
    {
        public Ped Character { get { return null; } }
        public void TeleportTo(float x, float y) { }
    }

    // Game.h
    public static class Game
    {
        public static Player LocalPlayer { get { return null; } }
        public static Camera CurrentCamera { get { return null; } }
        public static Blip GetWaypoint() { return null; }
    }

    // World.h
    public static class World
    {
        public static Vehicle CreateVehicle(Model model, Vector3 position) { return null; }
        public static Ped[] GetPeds(Vector3 position, float radius) { return new Ped[0]; }
        public static Vehicle[] GetVehicles(Vector3 position, float radius) { return new Vehicle[0]; }
    }

    // Font.h / Graphics.h
    public enum FontScaling { FontSize, ScreenUnits, Pixel }

    // Values are D3DX DT_* flags and are passed straight through, so they combine.
    public enum TextAlignment { Top = 0, Left = 0, Center = 1, Right = 2, VerticalCenter = 4, Bottom = 8, WordBreak = 16, SingleLine = 32, ExpandTabs = 64, NoClip = 256 }

    public class Font
    {
        public Font(float height, FontScaling scaling) { }
    }

    public sealed class Graphics
    {
        public FontScaling Scaling { get; set; }
        public void DrawRectangle(System.Drawing.RectangleF rect, System.Drawing.Color color) { }
        public void DrawText(string text, float x, float y, System.Drawing.Color color, Font font) { }
        public void DrawText(string text, System.Drawing.RectangleF area, TextAlignment alignment, System.Drawing.Color color, Font font) { }
    }

    public class GraphicsEventArgs : EventArgs
    {
        public Graphics Graphics { get { return null; } }
    }

    public delegate void GraphicsEventHandler(object sender, GraphicsEventArgs e);

    // Script.h (Interval and Wait are protected there)
    public abstract class Script
    {
        protected int Interval { get; set; }
        public event EventHandler Tick;
        public event GraphicsEventHandler PerFrameDrawing;
        protected void RaisePerFrameDrawing() { if (PerFrameDrawing != null) PerFrameDrawing(this, new GraphicsEventArgs()); }
        protected void Wait(int ms) { }
        protected void RaiseTick() { if (Tick != null) Tick(this, EventArgs.Empty); }
    }
}

namespace GTA.Native
{
    // nParameter.h: Parameter converts from these types; Pointer is a Parameter
    // whose value the native writes back, read with an explicit cast.
    public class Parameter
    {
        public static implicit operator Parameter(int v) { return new Parameter(); }
        public static implicit operator Parameter(float v) { return new Parameter(); }
        public static implicit operator Parameter(double v) { return new Parameter(); }
        public static implicit operator Parameter(bool v) { return new Parameter(); }
        public static implicit operator Parameter(string v) { return new Parameter(); }
        public static implicit operator Parameter(GTA.Ped v) { return new Parameter(); }
        public static implicit operator Parameter(GTA.Vehicle v) { return new Parameter(); }
        public static implicit operator Parameter(GTA.Model v) { return new Parameter(); }
    }

    public sealed class Pointer : Parameter
    {
        public Pointer(Type type) { }
        public static explicit operator float(Pointer p) { return 0f; }
        public static explicit operator int(Pointer p) { return 0; }
        public static explicit operator GTA.Vector3(Pointer p) { return new GTA.Vector3(); }
    }

    // nFunction.h
    public static class Function
    {
        public static void Call(string name, params Parameter[] args) { }
        public static T Call<T>(string name, params Parameter[] args) { return default(T); }
    }
}
