using System;
using UnityEngine;

namespace BepInEx
{
   public abstract class BaseUnityPlugin : MonoBehaviour
   {
      public Configuration.ConfigFile Config => throw new NotImplementedException();

      public Logging.ManualLogSource Logger => throw new NotImplementedException();
   }

   [AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
   public sealed class BepInPlugin : Attribute
   {
      public BepInPlugin( string guid, string name, string version )
      {
      }
   }

   public static class Paths
   {
      public static string GameRootPath => throw new NotImplementedException();

      public static string BepInExRootPath => throw new NotImplementedException();
   }
}

namespace BepInEx.Configuration
{
   public sealed class ConfigFile
   {
      public ConfigEntry<T> Bind<T>( string section, string key, T defaultValue, string description ) => throw new NotImplementedException();

      public void Save() => throw new NotImplementedException();
   }

   public struct KeyboardShortcut
   {
      public KeyboardShortcut( KeyCode mainKey, params KeyCode[] modifiers )
      {
         throw new NotImplementedException();
      }

      public bool IsDown() => throw new NotImplementedException();

      public override string ToString() => throw new NotImplementedException();
   }

   public sealed class ConfigEntry<T>
   {
      public T Value
      {
         get => throw new NotImplementedException();
         set => throw new NotImplementedException();
      }
   }
}

namespace BepInEx.Logging
{
   public sealed class ManualLogSource
   {
      public void LogInfo( object data ) => throw new NotImplementedException();

      public void LogWarning( object data ) => throw new NotImplementedException();

      public void LogError( object data ) => throw new NotImplementedException();

      public void LogDebug( object data ) => throw new NotImplementedException();
   }
}
