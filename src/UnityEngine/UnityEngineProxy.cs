using System;

namespace UnityEngine;

public enum HideFlags
{
   None = 0,
   HideAndDontSave = 61,
}

public struct Rect
{
   public float x { get; set; }

   public float y { get; set; }

   public float width { get; set; }

   public float height { get; set; }

   public Rect( float x, float y, float width, float height )
   {
      this.x = x;
      this.y = y;
      this.width = width;
      this.height = height;
   }
}

public struct Vector2
{
   public float x;
   public float y;

   public Vector2( float x, float y )
   {
      this.x = x;
      this.y = y;
   }
}

public struct Matrix4x4
{
   public float m00;
   public float m10;
   public float m20;
   public float m30;
   public float m01;
   public float m11;
   public float m21;
   public float m31;
   public float m02;
   public float m12;
   public float m22;
   public float m32;
   public float m03;
   public float m13;
   public float m23;
   public float m33;

   public static Matrix4x4 identity { get; }
}

public enum EventType
{
   MouseDown = 0,
   MouseUp = 1,
   MouseDrag = 3,
}

public class Event
{
   public static Event current { get; }

   public EventType type { get; }

   public Vector2 mousePosition { get; }

   public void Use() => throw new NotImplementedException();
}

public static class Screen
{
   public static int width { get; }

   public static int height { get; }
}

public enum KeyCode
{
   None = 0,
   Alpha0 = 48,
   Keypad0 = 256,
   F6 = 287,
   RightAlt = 307,
   LeftAlt = 308,
}

public class Object
{
   public int GetInstanceID() => throw new NotImplementedException();

   public HideFlags hideFlags { get; set; }

   public static void DontDestroyOnLoad( Object target ) => throw new NotImplementedException();

   public static T Instantiate<T>( T original ) where T : Object => throw new NotImplementedException();
}

public class Component : Object
{
}

public class Behaviour : Component
{
}

public class MonoBehaviour : Behaviour
{
}

public class GameObject : Object
{
   public GameObject()
   {
   }

   public GameObject( string name )
   {
   }

   public Component AddComponent( Type componentType ) => throw new NotImplementedException();

   public T AddComponent<T>() where T : Component => throw new NotImplementedException();
}

public class Texture : Object
{
}

public class Material : Object
{
   public bool HasProperty( string propertyName ) => throw new NotImplementedException();

   public Texture GetTexture( string propertyName ) => throw new NotImplementedException();

   public void SetTexture( string propertyName, Texture value ) => throw new NotImplementedException();

   public float GetFloat( string propertyName ) => throw new NotImplementedException();

   public void SetFloat( string propertyName, float value ) => throw new NotImplementedException();
}

public sealed class AssetBundle : Object
{
   public static AssetBundle LoadFromFile( string path ) => throw new NotImplementedException();

   public Object[] LoadAllAssets() => throw new NotImplementedException();

   public Object[] LoadAllAssets( Type type ) => throw new NotImplementedException();

   public void Unload( bool unloadAllLoadedObjects ) => throw new NotImplementedException();
}

public static class Resources
{
   public static Object Load( string path ) => throw new NotImplementedException();

   public static Object[] FindObjectsOfTypeAll( Type type ) => throw new NotImplementedException();
}

public static class Input
{
   public static bool GetKey( KeyCode key ) => throw new NotImplementedException();

   public static bool GetKeyDown( KeyCode key ) => throw new NotImplementedException();
}

public static class GUI
{
   public delegate void WindowFunction( int id );

   public static GUISkin skin { get; set; }

   public static Matrix4x4 matrix { get; set; }

   public static void Box( Rect position, string text ) => throw new NotImplementedException();

   public static void Label( Rect position, string text ) => throw new NotImplementedException();

   public static bool Button( Rect position, string text ) => throw new NotImplementedException();

   public static Rect Window( int id, Rect clientRect, WindowFunction func, string text ) => throw new NotImplementedException();

   public static void DragWindow() => throw new NotImplementedException();

   public static void DragWindow( Rect position ) => throw new NotImplementedException();
}

public static class GUIUtility
{
   public static void ScaleAroundPivot( Vector2 scale, Vector2 pivotPoint ) => throw new NotImplementedException();
}

public sealed class GUISkin
{
   public GUIStyle box { get; set; }

   public GUIStyle button { get; set; }

   public GUIStyle label { get; set; }

   public GUIStyle window { get; set; }
}

public class GUIStyle
{
   public int fontSize { get; set; }

   public bool richText { get; set; }
}
