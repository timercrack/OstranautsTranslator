using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using OstranautsTranslator.Plugin.BepInEx.Fonts;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx.Hooks;

internal static class RuntimeTypeResolver
{
   private static readonly Dictionary<string, Type> Cache = new Dictionary<string, Type>( StringComparer.Ordinal );

   public static Type FindLoadedType( string typeName )
   {
      if( string.IsNullOrEmpty( typeName ) ) return null;

      if( Cache.TryGetValue( typeName, out var cachedType ) )
      {
         return cachedType;
      }

      var resolvedType = Type.GetType( typeName, false )
         ?? Type.GetType( typeName + ", UnityEngine.UIElementsModule", false )
         ?? Type.GetType( typeName + ", UnityEngine.UI", false )
         ?? Type.GetType( typeName + ", Unity.TextMeshPro", false );

      if( resolvedType == null )
      {
         foreach( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
         {
            resolvedType = assembly.GetType( typeName, false );
            if( resolvedType != null ) break;

            Type[] types;
            try
            {
               types = assembly.GetTypes();
            }
            catch( ReflectionTypeLoadException e )
            {
               types = e.Types;
            }

            if( types == null ) continue;

            foreach( var candidateType in types )
            {
               if( candidateType == null ) continue;
               if( string.Equals( candidateType.FullName, typeName, StringComparison.Ordinal )
                  || string.Equals( candidateType.Name, typeName, StringComparison.Ordinal ) )
               {
                  resolvedType = candidateType;
                  break;
               }
            }

            if( resolvedType != null ) break;
         }
      }

      Cache[ typeName ] = resolvedType;
      return resolvedType;
   }
}

internal static class RuntimeTextHookHelper
{
   private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
   private static readonly ConditionalWeakTable<object, RescanTextState> RescanTextStates = new ConditionalWeakTable<object, RescanTextState>();

   public static void TranslateCurrentText( object instance, string hookName )
   {
      if( instance == null ) return;

      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( instance ) )
      {
         return;
      }

      if( IsGuiContentInstance( instance ) )
      {
         TranslateGuiContent( instance, hookName );
         return;
      }

      var textProperty = GetStringProperty( instance.GetType(), "text" );
      if( textProperty == null )
      {
         ApplyTmpFontIfNeeded( instance );
         return;
      }

      var originalText = textProperty.GetValue( instance, null ) as string;
      if( !string.IsNullOrEmpty( originalText ) )
      {
         if( RuntimeTextComponentBypassHelper.TryTranslateFixedText( instance, originalText, out var fixedText ) )
         {
            if( !string.Equals( fixedText, originalText, StringComparison.Ordinal ) )
            {
               textProperty.SetValue( instance, fixedText, null );
            }

            ApplyTmpFontIfNeeded( instance );
            return;
         }

         var translatedText = TranslateTextValue( originalText, hookName );
         if( !string.Equals( translatedText, originalText, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( instance, translatedText, null );
         }
      }

      ApplyTmpFontIfNeeded( instance );
   }

   public static void TranslateCurrentTextIfChanged( object instance, string hookName )
   {
      if( instance == null ) return;

      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( instance ) )
      {
         return;
      }

      if( IsGuiContentInstance( instance ) )
      {
         TranslateGuiContent( instance, hookName );
         return;
      }

      var textProperty = GetStringProperty( instance.GetType(), "text" );
      if( textProperty == null )
      {
         ApplyTmpFontIfNeeded( instance );
         return;
      }

      var currentText = textProperty.GetValue( instance, null ) as string ?? string.Empty;
      var state = RescanTextStates.GetOrCreateValue( instance );
      if( string.Equals( state.LastSeenText, currentText, StringComparison.Ordinal ) )
      {
         ApplyTmpFontIfNeeded( instance );
         return;
      }

      TranslateCurrentText( instance, hookName );
      state.LastSeenText = textProperty.GetValue( instance, null ) as string ?? string.Empty;
   }

   public static string TranslateTextValue( string value, string hookName )
   {
      if( string.IsNullOrEmpty( value ) ) return value;

      var translated = OstranautsTranslatorPlugin.Translate( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      foreach( var candidate in EnumerateCaseVariants( value ) )
      {
         translated = OstranautsTranslatorPlugin.Translate( candidate, hookName + ".case" );
         if( !string.Equals( translated, candidate, StringComparison.Ordinal ) )
         {
            return translated;
         }
      }

      return value;
   }

   public static void TranslateGuiContent( object content, string hookName )
   {
      if( content == null ) return;

      var textProperty = GetStringProperty( content.GetType(), "text" );
      if( textProperty != null )
      {
         var text = textProperty.GetValue( content, null ) as string;
         if( !string.IsNullOrEmpty( text ) )
         {
            textProperty.SetValue( content, TranslateTextValue( text, hookName + ".text" ), null );
         }
      }

      var tooltipProperty = GetStringProperty( content.GetType(), "tooltip" );
      if( tooltipProperty != null )
      {
         var tooltip = tooltipProperty.GetValue( content, null ) as string;
         if( !string.IsNullOrEmpty( tooltip ) )
         {
            tooltipProperty.SetValue( content, TranslateTextValue( tooltip, hookName + ".tooltip" ), null );
         }
      }
   }

   public static void TranslateGuiContentArray( Array contents, string hookName )
   {
      if( contents == null ) return;

      for( var i = 0; i < contents.Length; i++ )
      {
         TranslateGuiContent( contents.GetValue( i ), hookName + "[" + i + "]" );
      }
   }

   public static bool IsGuiContentInstance( object instance )
   {
      var guiContentType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" );
      return guiContentType != null && instance != null && guiContentType.IsInstanceOfType( instance );
   }

   public static void TranslateStringBuilder( ref StringBuilder builder, string hookName )
   {
      if( builder == null || builder.Length == 0 ) return;

      var originalText = builder.ToString();
      var translatedText = TranslateTextValue( originalText, hookName );
      if( string.Equals( translatedText, originalText, StringComparison.Ordinal ) ) return;

      builder.Length = 0;
      builder.Append( translatedText );
   }

   public static void TranslateCharArray( ref char[] value, string hookName )
   {
      if( value == null || value.Length == 0 ) return;

      var originalText = new string( value );
      var translatedText = TranslateTextValue( originalText, hookName );
      if( string.Equals( translatedText, originalText, StringComparison.Ordinal ) ) return;

      value = translatedText.ToCharArray();
   }

   public static void TranslateCharArraySegment( ref char[] value, ref int startIndex, ref int length, string hookName )
   {
      var originalText = ReadCharArraySegment( value, startIndex, length );
      if( originalText == null ) return;

      var translatedText = TranslateTextValue( originalText, hookName );
      if( string.Equals( translatedText, originalText, StringComparison.Ordinal ) ) return;

      value = translatedText.ToCharArray();
      startIndex = 0;
      length = value.Length;
   }

   public static void TranslateIntArraySegment( ref int[] value, ref int startIndex, ref int length, string hookName )
   {
      var originalText = ReadIntArraySegment( value, startIndex, length );
      if( originalText == null ) return;

      var translatedText = TranslateTextValue( originalText, hookName );
      if( string.Equals( translatedText, originalText, StringComparison.Ordinal ) ) return;

      value = ConvertToIntArray( translatedText );
      startIndex = 0;
      length = value.Length;
   }

   public static void TranslateHierarchy( GameObject root, string hookName )
   {
      if( root == null ) return;

      var getComponentsInChildren = typeof( GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren == null ) return;

      if( getComponentsInChildren.Invoke( root, new object[] { typeof( Component ), true } ) is not IEnumerable components ) return;

      var useRescanCache = string.Equals( hookName, "RuntimeScan", StringComparison.Ordinal )
         || hookName.StartsWith( "RuntimeScan.", StringComparison.Ordinal );

      foreach( var component in components )
      {
         if( component == null ) continue;
         var componentHookName = hookName + "." + component.GetType().Name;
         if( useRescanCache )
         {
            TranslateCurrentTextIfChanged( component, componentHookName );
            continue;
         }

         TranslateCurrentText( component, componentHookName );
      }
   }

   public static void TranslateHierarchyIfChanged( GameObject root, string hookName )
   {
      if( root == null ) return;

      var getComponentsInChildren = typeof( GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren == null ) return;

      if( getComponentsInChildren.Invoke( root, new object[] { typeof( Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( component == null ) continue;
         TranslateCurrentTextIfChanged( component, hookName + "." + component.GetType().Name );
      }
   }

   public static void TranslateObjectHierarchy( UnityEngine.Object target, string hookName )
   {
      switch( target )
      {
         case GameObject gameObject:
            TranslateHierarchy( gameObject, hookName );
            break;
         case Component component:
            TranslateHierarchy( GetGameObject( component ), hookName );
            break;
      }
   }

   public static void TranslateObjectHierarchyIfChanged( UnityEngine.Object target, string hookName )
   {
      switch( target )
      {
         case GameObject gameObject:
            TranslateHierarchyIfChanged( gameObject, hookName );
            break;
         case Component component:
            TranslateHierarchyIfChanged( GetGameObject( component ), hookName );
            break;
      }
   }

   public static GameObject GetGameObject( object component )
   {
      if( component == null ) return null;

      var property = component.GetType().GetProperty( "gameObject", InstanceFlags );
      return property?.GetValue( component, null ) as GameObject;
   }

   public static object GetParentTransform( GameObject gameObject )
   {
      if( gameObject == null ) return null;

      var transformProperty = typeof( GameObject ).GetProperty( "transform", InstanceFlags );
      var transform = transformProperty?.GetValue( gameObject, null );
      if( transform == null ) return null;

      var parentProperty = transform.GetType().GetProperty( "parent", InstanceFlags );
      return parentProperty?.GetValue( transform, null );
   }

   private static string ReadCharArraySegment( char[] value, int startIndex, int length )
   {
      if( value == null ) return null;

      var start = startIndex < 0 ? 0 : startIndex;
      var count = length < 0 ? 0 : length;
      if( start >= value.Length ) return string.Empty;
      if( start + count > value.Length ) count = value.Length - start;

      return new string( value, start, count );
   }

   private static string ReadIntArraySegment( int[] value, int startIndex, int length )
   {
      if( value == null ) return null;

      var start = startIndex < 0 ? 0 : startIndex;
      var count = length < 0 ? 0 : length;
      if( start >= value.Length ) return string.Empty;
      if( start + count > value.Length ) count = value.Length - start;

      var chars = new char[ count ];
      for( var i = 0; i < count; i++ )
      {
         chars[ i ] = (char)value[ start + i ];
      }

      return new string( chars );
   }

   private static int[] ConvertToIntArray( string value )
   {
      var buffer = new int[ value.Length ];
      for( var i = 0; i < value.Length; i++ )
      {
         buffer[ i ] = value[ i ];
      }

      return buffer;
   }

   private static PropertyInfo GetStringProperty( Type type, string propertyName )
   {
      for( var current = type; current != null; current = current.BaseType )
      {
         var property = current.GetProperty( propertyName, InstanceFlags );
         if( property != null && property.PropertyType == typeof( string ) && property.CanRead && property.CanWrite )
         {
            return property;
         }
      }

      return null;
   }

   private static void ApplyTmpFontIfNeeded( object instance )
   {
      var tmpTextType = TmpTypeResolver.Get( "TMPro.TMP_Text" );
      if( tmpTextType != null && tmpTextType.IsInstanceOfType( instance ) )
      {
         TmpFontManager.ApplyOverrideFont( instance );
      }
   }

   private static IEnumerable<string> EnumerateCaseVariants( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) yield break;

      var hasLetter = false;
      var hasLower = false;
      foreach( var ch in value )
      {
         if( !char.IsLetter( ch ) ) continue;
         hasLetter = true;
         if( char.IsLower( ch ) )
         {
            hasLower = true;
            break;
         }
      }

      if( !hasLetter || hasLower ) yield break;

      var lower = value.ToLowerInvariant();
      var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase( lower );
      if( !string.Equals( title, value, StringComparison.Ordinal ) )
      {
         yield return title;
      }

      if( !string.Equals( lower, value, StringComparison.Ordinal ) && !string.Equals( lower, title, StringComparison.Ordinal ) )
      {
         yield return lower;
      }
   }

   private sealed class RescanTextState
   {
      public string LastSeenText { get; set; } = string.Empty;
   }
}

internal static class RuntimeTextRescanner
{
   public static void Tick()
   {
      // Periodic full-scene rescans were causing repeated global hierarchy walks
      // and visible frame drops. Keep rescans explicit and rely on targeted hooks
      // for SetActive/Instantiate/AddComponent/SetParent to handle new UI.
   }

   public static void ForceScanAll()
   {
      var roots = Resources.FindObjectsOfTypeAll( typeof( GameObject ) );
      foreach( var candidate in roots )
      {
         var root = candidate as GameObject;
         if( root == null ) continue;
         if( RuntimeTextHookHelper.GetParentTransform( root ) != null ) continue;

         RuntimeTextHookHelper.TranslateHierarchy( root, "RuntimeScan" );
      }
   }
}