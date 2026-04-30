using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx.Input;

internal static class UnityInput
{
   private static IInputSystem _current;
   private static string _initializationError;

   public static IInputSystem Current
   {
      get
      {
         if( _current == null )
         {
            _current = CreateInputSystem();
         }

         return _current;
      }
   }

   public static string BackendName => Current.Name;

   public static bool IsAvailable => !( Current is NullInputSystem );

   public static string InitializationError => _initializationError;

   private static IInputSystem CreateInputSystem()
   {
      Exception legacyException = null;

      try
      {
         return new LegacyInputSystem();
      }
      catch( Exception ex )
      {
         legacyException = ex;
      }

      try
      {
         return new NewInputSystem();
      }
      catch( Exception ex )
      {
         _initializationError = BuildInitializationErrorMessage( legacyException, ex );
         return new NullInputSystem();
      }
   }

   private static string BuildInitializationErrorMessage( Exception legacyException, Exception newInputException )
   {
      var parts = new List<string>();

      if( legacyException != null )
      {
         parts.Add( $"Legacy Input failed: {legacyException.Message}" );
      }

      if( newInputException != null )
      {
         parts.Add( $"New Input System failed: {newInputException.Message}" );
      }

      return string.Join( " | ", parts.Where( part => !string.IsNullOrWhiteSpace( part ) ) );
   }
}

internal interface IInputSystem
{
   string Name { get; }

   bool GetKey( KeyCode key );

   bool GetKeyDown( KeyCode key );
}

internal sealed class NullInputSystem : IInputSystem
{
   public string Name => nameof( NullInputSystem );

   public bool GetKey( KeyCode key )
   {
      return false;
   }

   public bool GetKeyDown( KeyCode key )
   {
      return false;
   }
}

internal sealed class LegacyInputSystem : IInputSystem
{
   public string Name => nameof( LegacyInputSystem );

   [MethodImpl( MethodImplOptions.NoInlining )]
   public LegacyInputSystem()
   {
      UnityEngine.Input.GetKeyDown( KeyCode.F6 );
   }

   public bool GetKey( KeyCode key )
   {
      return UnityEngine.Input.GetKey( key );
   }

   public bool GetKeyDown( KeyCode key )
   {
      return UnityEngine.Input.GetKeyDown( key );
   }
}

internal sealed class NewInputSystem : IInputSystem
{
   private readonly Type _keyboardType;
   private readonly PropertyInfo _keyboardCurrentProperty;
   private readonly Dictionary<KeyCode, PropertyInfo> _buttonProperties = new Dictionary<KeyCode, PropertyInfo>();

   public string Name => nameof( NewInputSystem );

   public NewInputSystem()
   {
      var inputSystemAssembly = AppDomain.CurrentDomain
         .GetAssemblies()
         .FirstOrDefault( assembly => string.Equals( assembly.GetName().Name, "Unity.InputSystem", StringComparison.Ordinal ) )
         ?? Assembly.Load( "Unity.InputSystem" );

      _keyboardType = inputSystemAssembly.GetType( "UnityEngine.InputSystem.Keyboard", throwOnError: true );
      _keyboardCurrentProperty = _keyboardType.GetProperty( "current", BindingFlags.Public | BindingFlags.Static )
         ?? throw new InvalidOperationException( "Keyboard.current property was not found." );

      _ = ResolveButtonProperty( KeyCode.F6 );
   }

   public bool GetKey( KeyCode key )
   {
      return ReadButtonState( key, "isPressed" );
   }

   public bool GetKeyDown( KeyCode key )
   {
      return ReadButtonState( key, "wasPressedThisFrame" );
   }

   private bool ReadButtonState( KeyCode key, string statePropertyName )
   {
      var keyboard = _keyboardCurrentProperty.GetValue( null, null );
      if( keyboard == null )
      {
         return false;
      }

      var buttonProperty = ResolveButtonProperty( key );
      var buttonControl = buttonProperty.GetValue( keyboard, null );
      if( buttonControl == null )
      {
         return false;
      }

      var stateProperty = buttonControl.GetType().GetProperty( statePropertyName, BindingFlags.Public | BindingFlags.Instance )
         ?? throw new InvalidOperationException( $"Keyboard button state property '{statePropertyName}' was not found." );

      return stateProperty.GetValue( buttonControl, null ) is bool value && value;
   }

   private PropertyInfo ResolveButtonProperty( KeyCode key )
   {
      if( _buttonProperties.TryGetValue( key, out var property ) )
      {
         return property;
      }

      var propertyName = GetKeyboardPropertyName( key );
      property = _keyboardType.GetProperty( propertyName, BindingFlags.Public | BindingFlags.Instance )
         ?? throw new InvalidOperationException( $"Keyboard button property '{propertyName}' was not found for key '{key}'." );

      _buttonProperties[ key ] = property;
      return property;
   }

   private static string GetKeyboardPropertyName( KeyCode key )
   {
      switch( key )
      {
         case KeyCode.F6:
            return "f6Key";
         case KeyCode.Alpha0:
            return "digit0Key";
         case KeyCode.Keypad0:
            return "numpad0Key";
         case KeyCode.LeftAlt:
            return "leftAltKey";
         case KeyCode.RightAlt:
            return "rightAltKey";
         default:
            throw new NotSupportedException( $"The key '{key}' is not supported by the OstranautsTranslator new-input hotkey bridge." );
      }
   }
}