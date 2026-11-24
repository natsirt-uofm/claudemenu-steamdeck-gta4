// Compile-only stand-in for the parts of ScriptHookDotNet's API that ClaudeMenu
// uses. It lets CI (or you, without the game) check that ClaudeMenu.cs builds
// with a C# 4 compiler. It does NOT do anything at runtime - never ship it.
using System;

namespace GTA
{
    public struct Vector3
    {
        public float X, Y, Z;
        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
    }

    public enum VehicleSeat { None = -3, AnyPassenger = -2, Driver = -1, RightFront = 0, LeftRear = 1, RightRear = 2 }

    public class Model
    {
        public Model(int hash) { }
        public Model(string name) { }
    }

    public abstract class HandleObject
    {
        public bool Exists() { return true; }
    }

    public class Ped : HandleObject
    {
        public Vector3 Position { get; set; }
        public float Heading { get; set; }
        public int Health { get; set; }
        public bool isInVehicle() { return false; }
        public Vehicle CurrentVehicle { get { return null; } }
        public void WarpIntoVehicle(Vehicle v, VehicleSeat seat) { }
    }

    public class Vehicle : HandleObject
    {
        public Vector3 Position { get; set; }
        public float Heading { get; set; }
        public int Health { get; set; }
        public void Repair() { }
        public void Delete() { }
    }

    public class Blip : HandleObject
    {
        public Vector3 Position { get { return new Vector3(); } }
    }

    public class Player
    {
        public Ped Character { get { return null; } }
        public int WantedLevel { get; set; }
        public void TeleportTo(float x, float y) { }
    }

    public static class Game
    {
        public static Player LocalPlayer { get { return null; } }
        public static Blip GetWaypoint() { return null; }
    }

    public static class World
    {
        public static TimeSpan CurrentDayTime { get; set; }
        public static Vehicle CreateVehicle(Model model, Vector3 position) { return null; }
    }

    public abstract class Script
    {
        public int Interval { get; set; }
        public event EventHandler Tick;
        public void Wait(int ms) { }
        protected void RaiseTick() { if (Tick != null) Tick(this, EventArgs.Empty); }
    }
}

namespace GTA.Native
{
    public class Parameter
    {
        public static implicit operator Parameter(int v) { return new Parameter(); }
        public static implicit operator Parameter(float v) { return new Parameter(); }
        public static implicit operator Parameter(bool v) { return new Parameter(); }
        public static implicit operator Parameter(string v) { return new Parameter(); }
        public static implicit operator Parameter(GTA.Ped v) { return new Parameter(); }
        public static implicit operator Parameter(GTA.Vehicle v) { return new Parameter(); }
        public static implicit operator Parameter(Pointer v) { return new Parameter(); }
    }

    public class Pointer
    {
        public Pointer(Type t) { }
        public static explicit operator float(Pointer p) { return 0f; }
        public static explicit operator int(Pointer p) { return 0; }
    }

    public static class Function
    {
        public static void Call(string name, params Parameter[] args) { }
        public static T Call<T>(string name, params Parameter[] args) { return default(T); }
    }
}
