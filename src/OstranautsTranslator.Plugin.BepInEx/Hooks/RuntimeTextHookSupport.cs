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
   private static readonly IReadOnlyDictionary<string, string> KnownTextOverrides = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Ostranauts" ] = "星际拾荒者",
      [ "OSTRANAUTS" ] = "星际拾荒者",
      [ "RCS" ] = "姿控",
      [ "OPT" ] = "光学",
      [ "Signal:" ] = "信号：",
      [ "SIGNAL:" ] = "信号：",
      [ "No Target Selected" ] = "未选择目标",
      [ "DELTA-V:" ] = "速度增量：",
      [ "Sensors" ] = "传感器",
      [ "MESCAFORM" ] = "梅斯卡福姆",
      [ "CHARLIE" ] = "查理",
      [ "MARTIN" ] = "马丁",
      [ "CREW ORDERS & BUILDING" ] = "船员指令与建造",
      [ "EDIT" ] = "编辑",
      [ "PAUSE" ] = "暂停",
      [ "AUTOTASK" ] = "自动任务",
      [ "AUTOPAUSE" ] = "自动暂停",
      [ "TOGGLE AFFECTED ITEM TYPE(S)" ] = "切换受影响物品类型",
      [ "TIME / ZOOM" ] = "时间 / 缩放",
      [ "QUICK ZOOM" ] = "快速缩放",
      [ "ZOOM RANGE:" ] = "缩放范围：",
      [ "RESET" ] = "重置",
      [ "REV" ] = "倒退",
      [ "FWD" ] = "前进",
      [ "PLA" ] = "行星",
      [ "INR" ] = "内圈",
      [ "OUT" ] = "外圈",
      [ "SHOW ZONES" ] = "显示区域",
      [ "NO WAKE ZONES" ] = "无尾流区",
      [ "DISPLAY CONTROLS" ] = "显示控制",
      [ "TRACKING MODE" ] = "跟踪模式",
      [ "SHIP LABELS" ] = "船只标签",
      [ "FOCUS" ] = "焦点",
      [ "NAV MODE" ] = "导航模式",
      [ "NAV MODE: PAN" ] = "导航模式：平移",
      [ "NAV MODE: RCS" ] = "导航模式：RCS",
      [ "NAV CONTROLS" ] = "导航控制",
      [ "COMMS CONTROLS" ] = "通讯控制",
      [ "MAP CONTROLS" ] = "地图控制",
      [ "RCS MANEUVERS" ] = "RCS机动",
      [ "RESERVES" ] = "储备",
      [ "TARGET DATA" ] = "目标数据",
      [ "MAP" ] = "地图",
      [ "TRANSPONDER/IFF" ] = "应答机/敌我识别",
      [ "Mooring Control" ] = "系泊控制",
      [ "STATUS:" ] = "状态：",
      [ "INVALID" ] = "无效",
      [ "FUEL:" ] = "燃料：",
      [ "POWER:" ] = "电力：",
      [ "TARGET" ] = "目标",
      [ "VALID" ] = "有效",
      [ "POINT OF REF:" ] = "参考点：",
      [ "PORT-AZIKIWE - CHARLIE - MARTIN" ] = "阿齐基韦港 - 查理 - 马丁",
      [ "WALL" ] = "墙体",
      [ "FLOOR" ] = "地板",
      [ "CONDUIT" ] = "导管",
      [ "CAN" ] = "容器",
      [ "EQUIP" ] = "装备",
      [ "LOOSE" ] = "散放",
      [ "HULL" ] = "船体",
      [ "HVAC" ] = "暖通",
      [ "POWR" ] = "电力",
      [ "SENS" ] = "传感",
      [ "CTRL" ] = "控制",
      [ "FURN" ] = "家具",
      [ "APPS" ] = "设备",
      [ "MISC" ] = "其他",
   };

   public static void TranslateCurrentText( object instance, string hookName )
   {
      if( instance == null ) return;

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

      if( KnownTextOverrides.TryGetValue( value, out var overrideText ) )
      {
         return overrideText;
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