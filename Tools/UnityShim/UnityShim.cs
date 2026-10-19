// Минимальный шим UnityEngine — только для компиляционной проверки Unity-слоя
// вне редактора Unity. В сам Unity-проект не входит (лежит вне Assets).
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMax => x + width;
        public float yMax => y + height;
        public bool Contains(Vector2 p) => p.x >= x && p.x <= xMax && p.y >= y && p.y <= yMax;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f);
        public static Color clear => new Color(0, 0, 0, 0);
        public static Color Lerp(Color a, Color b, float t) => new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
    }

    public class Object
    {
        public string name;
        public static void DontDestroyOnLoad(Object o) { }
        public static void Destroy(Object o) { }
        public static T FindObjectOfType<T>() where T : Object => null;
    }

    public class Texture : Object { }

    public enum TextureFormat { RGBA32, ARGB32, RGB24 }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public class Texture2D : Texture
    {
        public int width, height;
        public FilterMode filterMode;
        public Color32[] pixels;
        public Texture2D(int w, int h) { width = w; height = h; }
        public Texture2D(int w, int h, TextureFormat f, bool mip) { width = w; height = h; }
        public void SetPixels32(Color32[] data) { pixels = (Color32[])data.Clone(); }
        public Color32[] GetPixels32() => pixels;
        public void Apply() { }
    }

    public class Transform : Object
    {
        public Vector3 position;
        public Vector3 localScale = new Vector3(1, 1, 1);
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform = new Transform();
    }

    public class Behaviour : Component { public bool enabled = true; }

    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public Transform transform = new Transform();
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this };
            return c;
        }
        public T GetComponent<T>() where T : Component => null;
        public static GameObject Find(string name) => null;
    }

    public class RectOffset
    {
        public int left, right, top, bottom;
        public RectOffset() { }
        public RectOffset(int l, int r, int t, int b) { left = l; right = r; top = t; bottom = b; }
    }

    public class GUIStyleState
    {
        public Color textColor = Color.white;
        public Texture2D background;
    }

    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }

    public class GUIStyle
    {
        public int fontSize;
        public FontStyle fontStyle;
        public TextAnchor alignment;
        public bool wordWrap;
        public bool richText = true;
        public GUIStyleState normal = new GUIStyleState();
        public GUIStyleState hover = new GUIStyleState();
        public GUIStyleState active = new GUIStyleState();
        public RectOffset padding = new RectOffset();
        public RectOffset margin = new RectOffset();
        public GUIStyle() { }
        public GUIStyle(GUIStyle o)
        {
            if (o == null) return;
            fontSize = o.fontSize; fontStyle = o.fontStyle; alignment = o.alignment;
            wordWrap = o.wordWrap; richText = o.richText; normal = o.normal; hover = o.hover; active = o.active;
            padding = o.padding; margin = o.margin;
        }
    }

    public class GUISkin : Object
    {
        public GUIStyle label = new GUIStyle();
        public GUIStyle box = new GUIStyle();
        public GUIStyle button = new GUIStyle();
        public GUIStyle window = new GUIStyle();
        public GUIStyle textField = new GUIStyle();
    }

    public class GUIContent
    {
        public static readonly GUIContent none = new GUIContent();
        public string text;
        public GUIContent() { }
        public GUIContent(string t) { text = t; }
    }

    public enum EventType { MouseDown, MouseUp, MouseDrag, Repaint, Layout, KeyDown, ScrollWheel, Used }
    public enum KeyCode { Space = 32, Escape = 27, Tab = 9, Return = 13, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51 }

    public class Event
    {
        public static Event current = new Event();
        public EventType type;
        public int button;
        public Vector2 mousePosition;
        public KeyCode keyCode;
        public void Use() { }
    }

    public class GUILayoutOption { }

    public static class GUI
    {
        public static GUISkin skin = new GUISkin();
        public static Color color = Color.white;
        public static bool enabled = true;
        public static void Label(Rect r, string text, GUIStyle style = null) { }
        public static void Box(Rect r, GUIContent content, GUIStyle style = null) { }
        public static bool Button(Rect r, string text, GUIStyle style = null) => false;
        public static void DrawTexture(Rect r, Texture tex, ScaleMode mode) { }
        public static void DrawTexture(Rect r, Texture tex) { }
        public static void DrawTextureWithTexCoords(Rect r, Texture tex, Rect coords) { }
    }

    public static class GUILayout
    {
        public static void BeginArea(Rect r) { }
        public static void BeginArea(Rect r, GUIStyle style) { }
        public static void EndArea() { }
        public static void BeginHorizontal(params GUILayoutOption[] o) { }
        public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] o) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] o) { }
        public static void BeginVertical(GUIStyle style, params GUILayoutOption[] o) { }
        public static void EndVertical() { }
        public static void Label(string text, GUIStyle style = null, params GUILayoutOption[] o) { }
        public static bool Button(string text, GUIStyle style = null, params GUILayoutOption[] o) => false;
        public static void Space(float px) { }
        public static void FlexibleSpace() { }
        public static Vector2 BeginScrollView(Vector2 pos, params GUILayoutOption[] o) => pos;
        public static Vector2 BeginScrollView(Vector2 pos, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] o) => pos;
        public static void EndScrollView() { }
        public static GUILayoutOption Width(float w) => new GUILayoutOption();
        public static GUILayoutOption Height(float h) => new GUILayoutOption();
        public static GUILayoutOption ExpandWidth(bool v) => new GUILayoutOption();
    }

    public static class Screen
    {
        public static int width = 1920;
        public static int height = 1080;
    }

    public static class Application
    {
        public static string dataPath = "/tmp/unity/Assets";
        public static string persistentDataPath = "/tmp/unity/persistent";
        public static string streamingAssetsPath = "/tmp/unity/StreamingAssets";
    }

    public static class Debug
    {
        public static void Log(object msg) => Console.WriteLine("[Unity] " + msg);
        public static void LogWarning(object msg) => Console.WriteLine("[Unity:warn] " + msg);
        public static void LogError(object msg) => Console.Error.WriteLine("[Unity:error] " + msg);
    }

    public static class Time
    {
        public static float time = 0f;
        public static float deltaTime = 0.016f;
    }

    public static class Input
    {
        public static Vector2 mousePosition;
        public static Vector2 mouseScrollDelta;
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetMouseButtonDown(int b) => false;
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Abs(float v) => Math.Abs(v);
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }
}
