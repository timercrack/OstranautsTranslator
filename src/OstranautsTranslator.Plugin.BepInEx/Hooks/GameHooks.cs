using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;

namespace OstranautsTranslator.Plugin.BepInEx.Hooks;

internal static class GameTypeResolver
{
   public static Type Get( string typeName )
   {
      return Type.GetType( typeName + ", Assembly-CSharp", false );
   }
}

internal static class RuntimeHookTranslationHelper
{
   private static readonly Dictionary<string, FieldInfo> InstanceFieldCache = new Dictionary<string, FieldInfo>( StringComparer.Ordinal );
   private static readonly Dictionary<string, PropertyInfo> PropertyCache = new Dictionary<string, PropertyInfo>( StringComparer.Ordinal );
   private static readonly ConditionalWeakTable<object, TextComponentState> TextComponentStates = new ConditionalWeakTable<object, TextComponentState>();

   private sealed class TextComponentState
   {
      public string LastSeenText = string.Empty;
   }

   public static void TranslateStringField( object target, string fieldName, string hookName )
   {
      TranslateStringField( target, fieldName, value => OstranautsTranslatorPlugin.Translate( value, hookName + "." + fieldName ) );
   }

   public static void TranslateStringField( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = GetInstanceField( target.GetType(), fieldName );
      if( field == null || field.FieldType != typeof( string ) ) return;

      var value = field.GetValue( target ) as string;
      if( string.IsNullOrEmpty( value ) ) return;

      field.SetValue( target, translator( value ) );
   }

   public static void TranslateStringList( object target, string fieldName, string hookName )
   {
      TranslateStringList( target, fieldName, value => OstranautsTranslatorPlugin.Translate( value, hookName + "." + fieldName ) );
   }

   public static void TranslateStringList( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = GetInstanceField( target.GetType(), fieldName );
      if( field?.GetValue( target ) is not IList values ) return;

      for( var i = 0; i < values.Count; i++ )
      {
         if( values[ i ] is not string value || string.IsNullOrEmpty( value ) ) continue;
         values[ i ] = translator( value );
      }
   }

   public static void TranslateSidePanelList( object target, string fieldName, string hookName )
   {
      TranslateSidePanelList( target, fieldName, value => OstranautsTranslatorPlugin.Translate( value, hookName + "." + fieldName ) );
   }

   public static void TranslateSidePanelList( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = GetInstanceField( target.GetType(), fieldName );
      if( field?.GetValue( target ) is not IEnumerable values ) return;

      foreach( var item in values )
      {
         TranslateStringField( item, "Label", translator );
         TranslateStringField( item, "MainText", translator );
      }
   }

   public static void TranslateTextComponentField( object target, string fieldName, string hookName )
   {
      TranslateTextComponentField( target, fieldName, value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + "." + fieldName ) );
   }

   public static void TranslateTextComponentField( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = GetInstanceField( target.GetType(), fieldName );
      var component = field?.GetValue( target );
      if( component == null ) return;

      var textProperty = GetStringProperty( component.GetType(), "text" );
      if( textProperty == null ) return;

      var value = textProperty.GetValue( component ) as string;
      if( string.IsNullOrEmpty( value ) ) return;

      var translated = translator( value );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         textProperty.SetValue( component, translated );
      }
   }

   public static void TranslateTextComponentFieldIfChanged( object target, string fieldName, string hookName )
   {
      TranslateTextComponentFieldIfChanged( target, fieldName, value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + "." + fieldName ) );
   }

   public static void TranslateTextComponentFieldIfChanged( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = GetInstanceField( target.GetType(), fieldName );
      var component = field?.GetValue( target );
      if( component == null ) return;

      var textProperty = GetStringProperty( component.GetType(), "text" );
      if( textProperty == null ) return;

      var value = textProperty.GetValue( component ) as string ?? string.Empty;
      var state = TextComponentStates.GetOrCreateValue( component );
      if( string.Equals( state.LastSeenText, value, StringComparison.Ordinal ) ) return;

      var translated = translator( value );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         textProperty.SetValue( component, translated );
         value = translated;
      }

      state.LastSeenText = value;
   }

   public static void SetTextComponentField( object target, string fieldName, string value )
   {
      if( target == null ) return;

      var field = GetInstanceField( target.GetType(), fieldName );
      var component = field?.GetValue( target );
      if( component == null ) return;

      var textProperty = GetStringProperty( component.GetType(), "text" );
      if( textProperty == null || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      textProperty.SetValue( component, value );
   }

   public static void SetTextComponentProperty( object component, string value )
   {
      if( component == null ) return;

      var textProperty = GetStringProperty( component.GetType(), "text" );
      if( textProperty == null || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      textProperty.SetValue( component, value );
   }

   public static void TranslateDropdownOptionsField( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = GetInstanceField( target.GetType(), fieldName );
      TranslateDropdownOptions( field?.GetValue( target ), translator );
   }

   public static void TranslateDropdownOptions( object dropdown, Func<string, string> translator )
   {
      if( dropdown == null ) return;

      var optionsProperty = GetProperty( dropdown.GetType(), "options" );
      if( optionsProperty?.GetValue( dropdown ) is not IList options ) return;

      foreach( var option in options )
      {
         if( option == null ) continue;

         var textProperty = GetStringProperty( option.GetType(), "text" );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) continue;

         var text = textProperty.GetValue( option ) as string;
         if( string.IsNullOrWhiteSpace( text ) ) continue;

         textProperty.SetValue( option, translator( text ) );
      }

      GetMethod( dropdown.GetType(), "RefreshShownValue" )?.Invoke( dropdown, null );
   }

   public static FieldInfo GetInstanceField( Type type, string fieldName )
   {
      var cacheKey = type.AssemblyQualifiedName + "|field|" + fieldName;
      if( InstanceFieldCache.TryGetValue( cacheKey, out var cachedField ) )
      {
         return cachedField;
      }

      for( var current = type; current != null; current = current.BaseType )
      {
         var field = current.GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
         if( field != null )
         {
            InstanceFieldCache[ cacheKey ] = field;
            return field;
         }
      }

      InstanceFieldCache[ cacheKey ] = null;
      return null;
   }

   public static PropertyInfo GetProperty( Type type, string propertyName )
   {
      var cacheKey = type.AssemblyQualifiedName + "|property|" + propertyName;
      if( PropertyCache.TryGetValue( cacheKey, out var cachedProperty ) )
      {
         return cachedProperty;
      }

      for( var current = type; current != null; current = current.BaseType )
      {
         var property = current.GetProperty( propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
         if( property != null )
         {
            PropertyCache[ cacheKey ] = property;
            return property;
         }
      }

      PropertyCache[ cacheKey ] = null;
      return null;
   }

   public static PropertyInfo GetStringProperty( Type type, string propertyName )
   {
      var property = GetProperty( type, propertyName );
      return property != null
         && property.PropertyType == typeof( string )
         && property.CanRead
         && property.CanWrite
         ? property
         : null;
   }

   private static MethodInfo GetMethod( Type type, string methodName )
   {
      return type.GetMethod( methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
   }
}

internal static class ChargenBodyRuntimeTranslationHelper
{
   private const string TranslatedModDirectoryName = "OstranautsTranslate";
   private static readonly Regex SimpleValuesArrayRegex = new Regex( "\"aValues\"\\s*:\\s*\\[(?<values>.*?)\\]", RegexOptions.Compiled | RegexOptions.Singleline );
   private static readonly Regex JsonStringRegex = new Regex( "\"(?<value>(?:\\\\.|[^\\\"\\\\])*)\"", RegexOptions.Compiled | RegexOptions.Singleline );
   private static readonly Lazy<IReadOnlyDictionary<string, string>> FirstNameTranslationLookup = new Lazy<IReadOnlyDictionary<string, string>>( () => BuildNameTranslationLookup( "names_first" ) );
   private static readonly Lazy<IReadOnlyDictionary<string, string>> LastNameTranslationLookup = new Lazy<IReadOnlyDictionary<string, string>>( () => BuildNameTranslationLookup( "names_last" ) );

   public static string TranslateFirstName( string value, string hookName )
   {
      return TranslateNamePart( value, isFirstName: true, hookName );
   }

   public static string TranslateLastName( string value, string hookName )
   {
      return TranslateNamePart( value, isFirstName: false, hookName );
   }

   public static bool TryTranslateKnownNameToken( string value, out string translatedValue )
   {
      translatedValue = string.Empty;
      if( string.IsNullOrWhiteSpace( value ) ) return false;

      var trimmed = value.Trim();
      if( trimmed.Length == 0 || ContainsCjkCharacters( trimmed ) ) return false;

      if( FirstNameTranslationLookup.Value.TryGetValue( trimmed, out translatedValue ) && !string.IsNullOrWhiteSpace( translatedValue ) )
      {
         return true;
      }

      if( LastNameTranslationLookup.Value.TryGetValue( trimmed, out translatedValue ) && !string.IsNullOrWhiteSpace( translatedValue ) )
      {
         return true;
      }

      translatedValue = string.Empty;
      return false;
   }

   public static void TranslateGeneratedName( object guiChargenBody, string hookName )
   {
      if( guiChargenBody == null ) return;

      TranslateUi( guiChargenBody, hookName + ".UI" );

      var coUser = GetObjectMember( guiChargenBody, "coUser" );
      var inputField = GetObjectMember( guiChargenBody, "tboxName" );
      if( inputField == null ) return;

      var textProperty = RuntimeHookTranslationHelper.GetStringProperty( inputField.GetType(), "text" );
      if( textProperty == null ) return;

      var currentName = textProperty.GetValue( inputField ) as string;
      if( string.IsNullOrWhiteSpace( currentName ) )
      {
         currentName = GetStringMember( coUser, "strName" );
      }

      if( string.IsNullOrWhiteSpace( currentName ) ) return;

      var translatedName = BuildTranslatedNameFromParts( coUser, currentName, hookName );
      if( string.IsNullOrWhiteSpace( translatedName ) || string.Equals( translatedName, currentName, StringComparison.Ordinal ) )
      {
         translatedName = TooltipRuntimeTranslationHelper.TranslateCondOwnerDisplayName( currentName, coUser, hookName )
            .Replace( '·', ' ' );
      }

      if( string.IsNullOrWhiteSpace( translatedName ) || string.Equals( translatedName, currentName, StringComparison.Ordinal ) ) return;

      textProperty.SetValue( inputField, translatedName );
      SetStringMember( coUser, "strName", translatedName );
      SetStringMember( coUser, "strNameFriendly", translatedName );
      SyncNameParts( GetObjectMember( coUser, "pspec" ), translatedName, false );
      SyncNameParts( coUser?.GetType().GetMethod( "GetComponent", new[] { typeof( Type ) } )?.Invoke( coUser, new object[] { GameTypeResolver.Get( "GUIChargenStack" ) } ), translatedName, true );
   }

   public static void TranslateUi( object guiChargenBody, string hookName )
   {
      var root = RuntimeTextHookHelper.GetGameObject( guiChargenBody as UnityEngine.Object );
      if( root == null ) return;

      RuntimeTextHookHelper.TranslateHierarchyIfChanged( root, hookName );
   }

   private static string BuildTranslatedNameFromParts( object coUser, string fallbackName, string hookName )
   {
      var pspec = GetObjectMember( coUser, "pspec" );
      var firstName = GetStringMember( pspec, "strFirstName" );
      var lastName = GetStringMember( pspec, "strLastName" );

      if( string.IsNullOrWhiteSpace( firstName ) && string.IsNullOrWhiteSpace( lastName ) )
      {
         SplitName( fallbackName, out firstName, out lastName );
      }

      var translatedFirstName = TranslateNamePart( firstName, isFirstName: true, hookName + ".FirstName" );
      var translatedLastName = TranslateNamePart( lastName, isFirstName: false, hookName + ".LastName" );

      if( string.IsNullOrWhiteSpace( translatedFirstName ) && string.IsNullOrWhiteSpace( translatedLastName ) )
      {
         return string.Empty;
      }

      if( string.IsNullOrWhiteSpace( translatedFirstName ) ) return translatedLastName;
      if( string.IsNullOrWhiteSpace( translatedLastName ) ) return translatedFirstName;
      return translatedFirstName + " " + translatedLastName;
   }

   private static string TranslateNamePart( string value, bool isFirstName, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return string.Empty;

      var trimmed = value.Trim();
      if( trimmed.Length == 0 || ContainsCjkCharacters( trimmed )) return trimmed;

      var translationLookup = isFirstName ? FirstNameTranslationLookup.Value : LastNameTranslationLookup.Value;
      if( translationLookup.TryGetValue( trimmed, out var translatedValue ) && !string.IsNullOrWhiteSpace( translatedValue ) )
      {
         return translatedValue;
      }

      var translated = RuntimeTextHookHelper.TranslateTextValue( trimmed, hookName );
      if( !string.Equals( translated, trimmed, StringComparison.Ordinal ) )
      {
         return translated.Replace( '·', ' ' );
      }

      if( trimmed.IndexOf( ' ' ) < 0 ) return trimmed;

      var parts = trimmed.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries );
      var changed = false;
      for( var i = 0; i < parts.Length; i++ )
      {
         var translatedPart = TranslateNamePart( parts[ i ], isFirstName, hookName + ".Part" + ( i + 1 ) );
         if( !string.Equals( translatedPart, parts[ i ], StringComparison.Ordinal ) )
         {
            parts[ i ] = translatedPart;
            changed = true;
         }
      }

      return changed ? string.Join( " ", parts ) : trimmed;
   }

   private static IReadOnlyDictionary<string, string> BuildNameTranslationLookup( string relativeNameDirectory )
   {
      try
      {
         var gameRootPath = Paths.GameRootPath;
         if( string.IsNullOrWhiteSpace( gameRootPath )) return new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

         var fileName = relativeNameDirectory + ".json";
         var sourceFilePath = Path.Combine( gameRootPath, "Ostranauts_Data", "StreamingAssets", "data", relativeNameDirectory, fileName );
         var translatedFilePath = Path.Combine( gameRootPath, "Ostranauts_Data", "Mods", TranslatedModDirectoryName, "data", relativeNameDirectory, fileName );
         if( !File.Exists( sourceFilePath ) || !File.Exists( translatedFilePath ) )
         {
            return new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
         }

         var sourceValues = ExtractSimpleValues( sourceFilePath );
         var translatedValues = ExtractSimpleValues( translatedFilePath );
         var lookup = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
         var pairCount = Math.Min( sourceValues.Count, translatedValues.Count );
         for( var i = 0; i + 1 < pairCount; i += 2 )
         {
            var sourceValue = sourceValues[ i ].Trim();
            var translatedValue = translatedValues[ i ].Trim();
            if( sourceValue.Length == 0 || translatedValue.Length == 0 || ContainsCjkCharacters( sourceValue ) ) continue;
            if( lookup.ContainsKey( sourceValue ) ) continue;
            lookup[ sourceValue ] = translatedValue;
         }

         return lookup;
      }
      catch
      {
         return new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
      }
   }

   private static List<string> ExtractSimpleValues( string filePath )
   {
      var json = File.ReadAllText( filePath );
      var match = SimpleValuesArrayRegex.Match( json );
      if( !match.Success ) return new List<string>();

      var values = new List<string>();
      foreach( Match stringMatch in JsonStringRegex.Matches( match.Groups[ "values" ].Value ) )
      {
         values.Add( DecodeJsonString( stringMatch.Groups[ "value" ].Value ) );
      }

      return values;
   }

   private static string DecodeJsonString( string value )
   {
      if( string.IsNullOrEmpty( value ) ) return string.Empty;

      return Regex.Unescape( value );
   }

   private static bool ContainsCjkCharacters( string value )
   {
      if( string.IsNullOrEmpty( value ) ) return false;

      foreach( var ch in value )
      {
         if( ch >= 0x2E80 && ch <= 0x9FFF )
         {
            return true;
         }
      }

      return false;
   }

   private static void SplitName( string value, out string firstName, out string lastName )
   {
      firstName = string.Empty;
      lastName = string.Empty;
      if( string.IsNullOrWhiteSpace( value ) ) return;

      var lastSpace = value.LastIndexOf( ' ' );
      if( lastSpace >= 0 )
      {
         firstName = value.Substring( 0, lastSpace ).Trim();
         lastName = value.Substring( lastSpace + 1 ).Trim();
         return;
      }

      lastName = value.Trim();
   }

   private static void SyncNameParts( object target, string translatedName, bool includeStackFields )
   {
      if( target == null || string.IsNullOrWhiteSpace( translatedName ) ) return;

      SplitName( translatedName, out var firstName, out var lastName );

      SetStringMember( target, "strFirstName", firstName );
      SetStringMember( target, "strLastName", lastName );
      if( !includeStackFields )
      {
         SetStringMember( target, "strCO", translatedName );
      }
   }

   private static object GetObjectMember( object target, string memberName )
   {
      if( target == null ) return null;

      var property = RuntimeHookTranslationHelper.GetProperty( target.GetType(), memberName );
      if( property != null && property.CanRead ) return property.GetValue( target );

      return RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), memberName )?.GetValue( target );
   }

   private static string GetStringMember( object target, string memberName )
   {
      if( target == null ) return string.Empty;

      var property = RuntimeHookTranslationHelper.GetProperty( target.GetType(), memberName );
      if( property != null && property.CanRead && property.PropertyType == typeof( string ) )
      {
         return property.GetValue( target ) as string ?? string.Empty;
      }

      var field = RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), memberName );
      if( field != null && field.FieldType == typeof( string ) )
      {
         return field.GetValue( target ) as string ?? string.Empty;
      }

      return string.Empty;
   }

   private static void SetStringMember( object target, string memberName, string value )
   {
      if( target == null ) return;

      var property = RuntimeHookTranslationHelper.GetProperty( target.GetType(), memberName );
      if( property != null && property.CanWrite && property.PropertyType == typeof( string ) )
      {
         property.SetValue( target, value );
         return;
      }

      var field = RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), memberName );
      if( field != null && field.FieldType == typeof( string ) )
      {
         field.SetValue( target, value );
      }
   }
}

internal static class PdaRuntimeTranslationHelper
{
   private static readonly IReadOnlyDictionary<string, string> AppTitleMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "HOME" ] = "主页",
      [ "ITEMS" ] = "物品",
      [ "ZONES" ] = "区域",
      [ "GOALS" ] = "目标",
      [ "ROSTER" ] = "成员",
      [ "SOCIAL" ] = "社交",
      [ "GIGS" ] = "委托",
      [ "P.A.S.S" ] = "客运",
      [ "FILES" ] = "文件",
      [ "NAVMAP" ] = "导航图",
      [ "NAVLINK" ] = "导航链路",
      [ "VIZOR" ] = "视镜",
      [ "INSTALL" ] = "安装",
      [ "ORDERS" ] = "指令",
      [ "TASKS" ] = "任务",
      [ "NOTES" ] = "笔记",
      [ "TIMER" ] = "计时器",
      [ "FACTION TIES" ] = "派系关系",
      [ "CLOSE" ] = "关闭",
   };

   private static readonly IReadOnlyDictionary<string, string> ZoneSelectionExactMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "multiple ships selected" ] = "已选择多个船只",
      [ "Zone selected" ] = "已选择区域",
      [ "Zone reduced" ] = "区域已缩减",
      [ "Zone not expanded. Tiles on different ship." ] = "区域未扩展。所选地格位于不同船只。",
      [ "Zone expanded" ] = "区域已扩展",
   };

   private static readonly Regex TilesSelectedRegex = new Regex( @"^(?<count>\d+) tiles selected$", RegexOptions.Compiled | RegexOptions.CultureInvariant );

   private static readonly IReadOnlyDictionary<string, string> TitleByImageName = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "GUIActionCancel" ] = "Cancel",
      [ "GUIActionUninstall" ] = "Uninstall",
      [ "GUIActionScrap" ] = "Scrap",
      [ "GUIActionRepair" ] = "Repair",
      [ "GUIActionDismantle" ] = "Dismantle",
      [ "GUIActionHaul" ] = "Haul",
      [ "GUIActionMine" ] = "Mine",
      [ "GUIActionReload" ] = "Reload",
      [ "GUIBuildHull" ] = "Hull",
      [ "GUIBuildHVAC" ] = "HVAC",
      [ "GUIBuildPower" ] = "Power",
      [ "GUIBuildSensors" ] = "Sensors",
      [ "GUIBuildControls" ] = "Controls",
      [ "GUIBuildFurniture" ] = "Furniture",
      [ "GUIBuildAppliances" ] = "Appliances",
      [ "GUIBuildOther" ] = "Other",
   };

   private static readonly IReadOnlyDictionary<string, string> NotesExactMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "NEW TO-DOs!" ] = "新的待办事项！",
      [ "Eat Breakfast" ] = "吃早餐",
      [ "Organise To-Dos" ] = "整理待办事项",
      [ "Buy new salvage license" ] = "购买新的打捞许可证",
      [ "Make enough to get through the day!" ] = "赚够今天撑下去的钱！",
   };

   public static string TranslateAppTitle( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return AppTitleMap.TryGetValue( value, out var exactText ) ? exactText : value;
   }

   public static string TranslateMenuTitle( string title, string imageName, string hookName )
   {
      if( string.IsNullOrWhiteSpace( title ) ) return title;

      if( !string.IsNullOrWhiteSpace( imageName ) && TitleByImageName.TryGetValue( imageName, out var canonicalTitle ) )
      {
         return RuntimeTextHookHelper.TranslateTextValue( canonicalTitle, hookName + "." + imageName );
      }

      return RuntimeTextHookHelper.TranslateTextValue( title, hookName );
   }

   public static string TranslateSocialFilterLabel( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      const string hidePrefix = "Hide ";
      if( value.StartsWith( hidePrefix, StringComparison.Ordinal ) )
      {
         var suffix = value.Substring( hidePrefix.Length );
         return "隐藏 " + RuntimeTextHookHelper.TranslateTextValue( suffix, hookName + ".suffix" );
      }

      return value;
   }

   public static void TranslateNotesDisplay( object guiPda, string hookName )
   {
      if( guiPda == null ) return;

      var pdaNotes = RuntimeHookTranslationHelper.GetInstanceField( guiPda.GetType(), "pdaNotes" )?.GetValue( guiPda );
      if( pdaNotes == null ) return;

      var inputField = RuntimeHookTranslationHelper.GetInstanceField( pdaNotes.GetType(), "_input" )?.GetValue( pdaNotes );
      if( inputField == null ) return;

      var textProperty = RuntimeHookTranslationHelper.GetStringProperty( inputField.GetType(), "text" );
      if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite ) return;

      var textValue = textProperty.GetValue( inputField ) as string;
      if( string.IsNullOrWhiteSpace( textValue ) ) return;

      var translatedText = TranslateNotesText( textValue, hookName );
      if( string.Equals( translatedText, textValue, StringComparison.Ordinal ) ) return;

      textProperty.SetValue( inputField, translatedText );
   }

   public static void TranslateSocialFilterHierarchy( UnityEngine.GameObject root, string hookName )
   {
      if( root == null ) return;

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren == null ) return;

      if( getComponentsInChildren.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite ) continue;

         var value = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( value ) ) continue;

         var translated = TranslateSocialFilterLabel( value, hookName + "." + component.GetType().Name );
         if( !string.Equals( translated, value, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( component, translated );
         }
      }
   }

   public static string TranslateStandingsLabel( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = value;
      translated = ReplacePrefix( translated, "<b>Status:</b> ", "<b>状态：</b> " );
      translated = ReplacePrefix( translated, "<b>Value:</b> ", "<b>评分：</b> " );
      translated = ReplacePrefix( translated, "<b>Faction Funds:</b> ", "<b>派系资金：</b> " );
      return translated;
   }

   public static string TranslateStandingsFactionName( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return value switch
      {
         "Policia Federal" => "联邦警察",
         "Polícia Federal" => "联邦警察",
         _ => value
      };
   }

   public static string TranslateStandingsReputation( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return value switch
      {
         "Neutral" => "中立",
         "Friendly" => "友好",
         "Hostile" => "敌对",
         _ => value
      };
   }

   public static string TranslateZoneSelectionLabel( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( ZoneSelectionExactMap.TryGetValue( value, out var exactText ) )
      {
         return exactText;
      }

      var match = TilesSelectedRegex.Match( value );
      if( match.Success )
      {
         return match.Groups[ "count" ].Value + " 个地格已选";
      }

      return value;
   }

   public static string TranslateRosterCompanyTitle( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = ReplaceCompanySuffix( value, "'s Company", hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return ReplaceCompanySuffix( value, "'s COMPANY", hookName );
   }

   public static string TranslateSocialContactBody( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return TranslateSocialMultiline(
         value,
         [
            ("<b>Relationship Status:</b> ", "<b>关系状态：</b> "),
            ("<b>Career:</b> ", "<b>职业：</b> "),
            ("<b>Location:</b> ", "<b>位置：</b> "),
            ("<b>Notes:</b> ", "<b>备注：</b> ")
         ],
         [
            ("Deceased", "已故")
         ] );
   }

   public static string TranslateSocialStatusSummary( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return TranslateSocialMultiline(
         value,
         [
            ("Age: ", "年龄："),
            ("Career: ", "职业："),
            ("Homeworld: ", "母星："),
            ("Strata: ", "阶层：")
         ],
         [
            ("They See Us As:", "他们眼中的我们："),
            ("We See Them As:", "我们眼中的他们："),
            ("None", "无"),
            ("N/A", "无"),
            ("None Revealed Yet", "尚未揭示任何内容")
         ] );
   }

   public static string TranslatePersonModuleDescription( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      var normalized = value.Replace( "\r\n", "\n" );
      var lines = normalized.Split( '\n' );
      var changed = false;

      for( var i = 0; i < lines.Length; i++ )
      {
         var line = lines[ i ];

         if( line.StartsWith( "Factions: ", StringComparison.Ordinal ) )
         {
            lines[ i ] = "派系：" + TranslateFactionList( line.Substring( "Factions: ".Length ), hookName + "[" + i + "].factions" );
            continue;
         }

         if( string.Equals( line, "n/a", StringComparison.Ordinal ) || string.Equals( line, "N/A", StringComparison.Ordinal ) )
         {
            lines[ i ] = "无";
            continue;
         }

         lines[ i ] = RuntimeTextHookHelper.TranslateTextValue( line, hookName + "[" + i + "]" );
      }

      return string.Join( "\n", lines );
   }

   private static string TranslateFactionList( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var parts = value.Split( new[] { ", " }, StringSplitOptions.None );
      var changed = false;

      for( var i = 0; i < parts.Length; i++ )
      {
         var part = parts[ i ];
         if( string.IsNullOrWhiteSpace( part ) ) continue;

         var translatedPart = TranslateFactionFriendlyName( part, hookName + "." + i );
         if( !string.Equals( translatedPart, part, StringComparison.Ordinal ) )
         {
            parts[ i ] = translatedPart;
            changed = true;
         }
      }

      return changed ? string.Join( ", ", parts ) : value;
   }

   private static string TranslateFactionFriendlyName( string value, string hookName )
   {
      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return value switch
      {
         "Policia Federal" => "联邦警察",
         "Polícia Federal" => "联邦警察",
         _ => value,
      };
   }

   public static string TranslateFerryDestinationLabel( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      const string regSuffixToken = "<color=orange> (";
      var suffixIndex = value.IndexOf( regSuffixToken, StringComparison.Ordinal );
      if( suffixIndex <= 0 ) return value;

      var destinationName = value.Substring( 0, suffixIndex );
      var translatedDestinationName = RuntimeTextHookHelper.TranslateTextValue( destinationName, hookName + ".destination" );
      return string.Equals( translatedDestinationName, destinationName, StringComparison.Ordinal )
         ? value
         : translatedDestinationName + value.Substring( suffixIndex );
   }

   public static string GetTextComponentFieldValue( object target, string fieldName )
   {
      if( target == null ) return string.Empty;

      var field = RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), fieldName );
      var component = field?.GetValue( target );
      if( component == null ) return string.Empty;

      var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
      if( textProperty == null || !textProperty.CanRead ) return string.Empty;

      return textProperty.GetValue( component ) as string ?? string.Empty;
   }

   private static string ReplacePrefix( string value, string prefix, string replacement )
   {
      return value.StartsWith( prefix, StringComparison.Ordinal )
         ? replacement + value.Substring( prefix.Length )
         : value;
   }

   private static string TranslateNotesText( string value, string hookName )
   {
      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      var result = value;
      foreach( var pair in NotesExactMap )
      {
         result = ReplaceOrdinal( result, pair.Key, pair.Value );
      }

      return result;
   }

   private static string ReplaceOrdinal( string value, string oldValue, string newValue )
   {
      if( string.IsNullOrEmpty( value ) || string.IsNullOrEmpty( oldValue ) ) return value;

      var index = value.IndexOf( oldValue, StringComparison.Ordinal );
      if( index < 0 ) return value;

      var builder = new StringBuilder( value.Length );
      var cursor = 0;
      while( index >= 0 )
      {
         builder.Append( value, cursor, index - cursor );
         builder.Append( newValue );
         cursor = index + oldValue.Length;
         index = value.IndexOf( oldValue, cursor, StringComparison.Ordinal );
      }

      builder.Append( value, cursor, value.Length - cursor );
      return builder.ToString();
   }

   private static string ReplaceCompanySuffix( string value, string suffix, string hookName )
   {
      if( !value.EndsWith( suffix, StringComparison.Ordinal ) ) return value;

      var companyName = value.Substring( 0, value.Length - suffix.Length );
      var translatedCompanyName = RuntimeTextHookHelper.TranslateTextValue( companyName, hookName + ".companyName" );
      return translatedCompanyName + " 的公司";
   }

   private static string TranslateSocialMultiline(
      string value,
      (string Prefix, string Replacement)[] prefixes,
      (string Source, string Replacement)[] exacts )
   {
      var normalized = value.Replace( "\r\n", "\n" );
      var lines = normalized.Split( '\n' );
      var changed = false;

      for( var i = 0; i < lines.Length; i++ )
      {
         var line = lines[ i ];
         var translatedLine = line;

         foreach( var exact in exacts )
         {
            if( string.Equals( translatedLine, exact.Source, StringComparison.Ordinal ) )
            {
               translatedLine = exact.Replacement;
               break;
            }
         }

         foreach( var prefix in prefixes )
         {
            translatedLine = ReplacePrefix( translatedLine, prefix.Prefix, prefix.Replacement );
         }

         if( !string.Equals( translatedLine, line, StringComparison.Ordinal ) )
         {
            lines[ i ] = translatedLine;
            changed = true;
         }
      }

      return changed ? string.Join( "\n", lines ) : value;
   }
}

internal static class ComputerRuntimeTranslationHelper
{
   private static readonly IReadOnlyDictionary<string, string> OverlayVariableExactMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "_None" ] = "无",
      [ "_Power" ] = "电力",
      [ "_Damage" ] = "损伤",
      [ "_Mass" ] = "质量",
      [ "_Price" ] = "价格",
      [ "_Heat" ] = "温度",
      [ "_Pressure" ] = "压力",
   };

   private static readonly IReadOnlyDictionary<string, string> GradientExactMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Default" ] = "默认",
      [ "Clean" ] = "纯净",
      [ "Monochrome" ] = "单色",
      [ "Tricolor" ] = "三色",
      [ "Rainbow" ] = "彩虹",
      [ "Oldred" ] = "复古红",
      [ "Opacity" ] = "透明度",
      [ "Golden" ] = "金色",
      [ "InverseRainbow" ] = "反向彩虹",
      [ "HeatMap" ] = "热力图",
      [ "Glitch" ] = "故障",
      [ "Disco" ] = "迪斯科",
   };

   public static void TranslateVizHierarchy( object guiPda, string hookName )
   {
      if( guiPda == null ) return;

      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( guiPda as UnityEngine.Object, hookName );

      var pdaVisualisers = RuntimeHookTranslationHelper.GetInstanceField( guiPda.GetType(), "pdaVisualisers" )?.GetValue( guiPda );
      TranslateVizDynamicDisplays( pdaVisualisers, hookName );
   }

   public static void TranslateVizDynamicDisplays( object pdaVisualisers, string hookName )
   {
      if( pdaVisualisers == null ) return;

      TranslateVizOverlayVariableDisplay( pdaVisualisers, hookName + ".OverlayVariable" );
      TranslateVizGradientDisplay( pdaVisualisers, hookName + ".Gradient" );
   }

   public static void TranslateVizOverlayVariableDisplay( object pdaVisualisers, string hookName )
   {
      if( pdaVisualisers == null ) return;

      var inputField = RuntimeHookTranslationHelper.GetInstanceField( pdaVisualisers.GetType(), "_txtOverlayVariable" )?.GetValue( pdaVisualisers );
      if( inputField == null ) return;

      var textValue = RuntimeHookTranslationHelper.GetStringProperty( inputField.GetType(), "text" )?.GetValue( inputField ) as string;
      if( string.IsNullOrWhiteSpace( textValue ) ) return;

      if( !OverlayVariableExactMap.TryGetValue( textValue, out var translatedText ) )
      {
         translatedText = RuntimeTextHookHelper.TranslateTextValue( textValue, hookName );
      }

      if( string.Equals( translatedText, textValue, StringComparison.Ordinal ) ) return;

      SetVisibleTextComponents( inputField, textValue, translatedText );
   }

   public static void TranslateVizGradientDisplay( object pdaVisualisers, string hookName )
   {
      if( pdaVisualisers == null ) return;

      var value = PdaRuntimeTranslationHelper.GetTextComponentFieldValue( pdaVisualisers, "_txtOverlayGradient" );
      if( string.IsNullOrWhiteSpace( value ) ) return;

      if( !GradientExactMap.TryGetValue( value, out var translatedText ) )
      {
         translatedText = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      }

      if( string.Equals( translatedText, value, StringComparison.Ordinal ) ) return;

      RuntimeHookTranslationHelper.SetTextComponentField( pdaVisualisers, "_txtOverlayGradient", translatedText );
   }

   private static void SetVisibleTextComponents( object rootComponent, string sourceText, string translatedText )
   {
      if( rootComponent == null || string.IsNullOrWhiteSpace( sourceText ) || string.IsNullOrWhiteSpace( translatedText ) ) return;

      var directTextComponent = RuntimeHookTranslationHelper.GetProperty( rootComponent.GetType(), "textComponent" )?.GetValue( rootComponent )
         ?? RuntimeHookTranslationHelper.GetInstanceField( rootComponent.GetType(), "m_TextComponent" )?.GetValue( rootComponent );
      if( TrySetComponentTextIfMatches( directTextComponent, sourceText, translatedText ) ) return;

      var root = RuntimeTextHookHelper.GetGameObject( rootComponent );
      if( root == null ) return;

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( TrySetComponentTextIfMatches( component, sourceText, translatedText ) ) return;
      }
   }

   private static bool TrySetComponentTextIfMatches( object component, string sourceText, string translatedText )
   {
      if( component == null ) return false;

      var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
      if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite ) return false;

      var currentText = textProperty.GetValue( component ) as string;
      if( !string.Equals( currentText, sourceText, StringComparison.Ordinal ) ) return false;

      textProperty.SetValue( component, translatedText );
      return true;
   }

   public static void TranslateNavPanelHierarchy( UnityEngine.GameObject root, string hookName )
   {
      if( root == null ) return;

      RuntimeTextHookHelper.TranslateHierarchy( root, hookName );

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren == null ) return;

      if( getComponentsInChildren.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite ) continue;

         var value = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( value ) ) continue;

         var translated = TranslateNavPanelText( value, hookName + "." + component.GetType().Name );
         if( !string.Equals( translated, value, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( component, translated );
         }
      }
   }

   private static string TranslateNavPanelText( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( string.Equals( value, "STATUS:\n<color=#25FF78>LINKED</color>", StringComparison.Ordinal ) )
      {
         return "状态：\n<color=#25FF78>已链接</color>";
      }

      if( string.Equals( value, "STATUS:\n<color=#FF5100>UNLINKED</color>", StringComparison.Ordinal ) )
      {
         return "状态：\n<color=#FF5100>未链接</color>";
      }

      if( value.IndexOf( '\n' ) < 0 ) return value;

      var normalized = value.Replace( "\r\n", "\n" );
      var lines = normalized.Split( '\n' );
      var changed = false;

      for( var i = 0; i < lines.Length; i++ )
      {
         var line = lines[ i ];
         if( string.IsNullOrWhiteSpace( line ) ) continue;

         var translatedLine = RuntimeTextHookHelper.TranslateTextValue( line, hookName + "[" + i + "]" );
         if( string.Equals( translatedLine, line, StringComparison.Ordinal ) ) continue;

         lines[ i ] = translatedLine;
         changed = true;
      }

      return changed ? string.Join( "\n", lines ) : value;
   }
}

internal static class LoadingScreenRuntimeTranslationHelper
{
   private static readonly IReadOnlyDictionary<string, string> ExactTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Emptying Scene" ] = "清空场景",
      [ "Init Star System" ] = "初始化恒星系",
      [ "Load Starting Area" ] = "加载起始区域",
      [ "Spawn Player Character" ] = "生成人物角色",
      [ "Init Plot Manager" ] = "初始化剧情管理器",
      [ "Update Game Objects" ] = "更新游戏对象",
      [ "Toggle Power UI" ] = "切换电力界面",
      [ "Init AI Ship Manager" ] = "初始化 AI 舰船管理器",
      [ "Set Camera" ] = "设置镜头",
      [ "Load files" ] = "加载文件",
      [ "Load Ships" ] = "加载船只",
      [ "Load star system" ] = "加载恒星系",
      [ "Init system" ] = "初始化系统",
      [ "Load Market" ] = "加载市场",
      [ "Load ship" ] = "加载飞船",
      [ "Load roster" ] = "加载人员名单",
      [ "Load plot" ] = "加载剧情",
      [ "Load ledger" ] = "加载账本",
      [ "Update COs" ] = "更新对象",
      [ "Load gigs" ] = "加载委托",
      [ "Patching old save data" ] = "修补旧存档数据",
      [ "Loading scene" ] = "加载场景",
      [ "Init ship manager" ] = "初始化舰船管理器",
      [ "Populate tiles" ] = "填充瓦片",
      [ "Parse System Bodies" ] = "解析天体",
      [ "Parse Asteroid Fields" ] = "解析小行星带",
      [ "Parse System Body Hierarchy" ] = "解析天体层级",
      [ "Spawning System Bodies" ] = "生成天体",
      [ "Spawning System Companies" ] = "生成系统公司",
      [ "Spawning System Stations" ] = "生成系统空间站",
      [ "Spawning Asteroid Fields" ] = "生成小行星带",
      [ "Spawning System Derelicts" ] = "生成系统废船",
      [ "Spawning System Ships" ] = "生成系统飞船",
   };

   public static string TranslateProgressText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = OstranautsTranslatorPlugin.Translate( value, "LoadingScreen.SetProgressBar" );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( ExactTextMap.TryGetValue( value, out var exactText ) )
      {
         return exactText;
      }

      return TryTranslateWithPrefix( value, "Spawning Ship: ", "正在生成飞船：" )
         ?? TryTranslateWithPrefix( value, "Spawning Station: ", "正在生成空间站：" )
         ?? value;
   }

   private static string TryTranslateWithPrefix( string value, string englishPrefix, string chinesePrefix )
   {
      if( !value.StartsWith( englishPrefix, StringComparison.Ordinal ) ) return null;

      var suffix = value.Substring( englishPrefix.Length );
      var translatedSuffix = OstranautsTranslatorPlugin.Translate( suffix, "LoadingScreen.SetProgressBar.suffix" );
      return chinesePrefix + translatedSuffix;
   }
}

internal static class SaveLoadRuntimeTranslationHelper
{
   private static readonly IReadOnlyDictionary<string, string> ExactCompositeLabelMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "RST_Portal_Handset" ] = "传送门手持终端",
   };

   public static string TranslateSaveWarning( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( string.Equals( value, "CAUTION: Older saves may experience problems.", StringComparison.Ordinal ) )
      {
         return "注意：较旧的存档可能会出现问题。";
      }

      const string prefix = "CAUTION: Saves older than v";
      const string suffix = " may experience problems.";
      if( value.StartsWith( prefix, StringComparison.Ordinal ) && value.EndsWith( suffix, StringComparison.Ordinal ) )
      {
         var version = value.Substring( prefix.Length, value.Length - prefix.Length - suffix.Length );
         return "注意：早于 v" + version + " 的存档可能会出现问题。";
      }

      return OstranautsTranslatorPlugin.Translate( value, "GUILoadMenu.CreateSaveWarning" );
   }

   public static string TranslateAvailableSpaceWarning( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      const string gbSuffix = " GB free";
      if( value.EndsWith( gbSuffix, StringComparison.Ordinal ) )
      {
         return value.Substring( 0, value.Length - gbSuffix.Length ) + " GB 可用";
      }

      const string lowDiskSuffix = " MB free | <color=red>LOW DISK SPACE!</color>";
      if( value.EndsWith( lowDiskSuffix, StringComparison.Ordinal ) )
      {
         return value.Substring( 0, value.Length - lowDiskSuffix.Length ) + " MB 可用 | <color=red>磁盘空间不足！</color>";
      }

      return OstranautsTranslatorPlugin.Translate( value, "GUISaveLoadBase.GetAvailableSpaceWarning" );
   }

   public static string TranslateConfirmationText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( string.Equals( value, "Are you sure you want to quit to desktop?", StringComparison.Ordinal ) )
      {
         return "确定要退出到桌面吗？";
      }

      return RuntimeTextHookHelper.TranslateTextValue( value, "GUIConfirmationDialogue.Setup.text" );
   }

   public static string TranslateSaveInfoPlayerLine( object saveInfo )
   {
      if( saveInfo == null ) return string.Empty;

      var saveInfoType = saveInfo.GetType();
      var playerName = saveInfoType.GetProperty( "PlayerName", BindingFlags.Instance | BindingFlags.Public )?.GetValue( saveInfo ) as string ?? string.Empty;
      var shipName = saveInfoType.GetProperty( "ShipName", BindingFlags.Instance | BindingFlags.Public )?.GetValue( saveInfo ) as string ?? string.Empty;
      var translatedPlayer = RuntimeTextHookHelper.TranslateTextValue( playerName, "LoadListEntry.Setup.PlayerName" );
      var translatedShip = TranslateCompositeLabel( shipName, "LoadListEntry.Setup.ShipName" );

      if( string.IsNullOrWhiteSpace( translatedShip ) ) return translatedPlayer;
      if( string.IsNullOrWhiteSpace( translatedPlayer ) ) return translatedShip;

      return translatedPlayer + " 的 " + translatedShip;
   }

   public static string TranslateSaveName( object saveInfo )
   {
      if( saveInfo == null ) return string.Empty;

      var saveName = saveInfo.GetType().GetProperty( "SaveName", BindingFlags.Instance | BindingFlags.Public )?.GetValue( saveInfo ) as string ?? string.Empty;
      return TranslateCompositeLabel( saveName, "LoadListEntry.Setup.SaveName" );
   }

   public static string TranslateCompositeLabel( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( value.Contains( " - ", StringComparison.Ordinal ) )
      {
         var parts = value.Split( new[] { " - " }, StringSplitOptions.None );
         var changed = false;
         for( var i = 0; i < parts.Length; i++ )
         {
            var part = parts[ i ];
            var translatedPart = TranslateCompositeToken( part, hookName + "[" + i + "]" );
            if( !string.Equals( translatedPart, part, StringComparison.Ordinal ) )
            {
               parts[ i ] = translatedPart;
               changed = true;
            }
         }

         if( changed )
         {
            return string.Join( " - ", parts );
         }
      }

      var translated = TranslateCompositeToken( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return value;
   }

   private static string TranslateCompositeToken( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( ExactCompositeLabelMap.TryGetValue( value, out var exactText ) )
      {
         return exactText;
      }

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( value.StartsWith( "RST_", StringComparison.Ordinal ) )
      {
         var normalized = value.Substring( 4 ).Replace( '_', ' ' );
         var translatedNormalized = RuntimeTextHookHelper.TranslateTextValue( normalized, hookName + ".normalized" );
         if( !string.Equals( translatedNormalized, normalized, StringComparison.Ordinal ) )
         {
            return translatedNormalized;
         }
      }

      return value;
   }
}

internal static class SaveVersionRuntimeHelper
{
   public static void ApplyCurrentBuildToSaveDto( object saveDto )
   {
      if( saveDto == null ) return;

      var currentBuild = GetCurrentBuild();
      if( string.IsNullOrWhiteSpace( currentBuild ) ) return;

      var gameSaveTupleField = RuntimeHookTranslationHelper.GetInstanceField( saveDto.GetType(), "jGameSave" );
      var gameSaveTuple = gameSaveTupleField?.GetValue( saveDto );
      var jsonGameSave = RuntimeHookTranslationHelper.GetProperty( gameSaveTuple?.GetType(), "Item2" )?.GetValue( gameSaveTuple );
      SetStringMember( jsonGameSave, "strVersion", currentBuild );
   }

   public static void ApplyCurrentBuildToSaveInfo( object saveInfo )
   {
      if( saveInfo == null ) return;

      var currentBuild = GetCurrentBuild();
      if( string.IsNullOrWhiteSpace( currentBuild ) ) return;

      var jsonSaveInfoField = RuntimeHookTranslationHelper.GetInstanceField( saveInfo.GetType(), "_jsonSaveInfo" );
      var jsonSaveInfo = jsonSaveInfoField?.GetValue( saveInfo );
      SetStringMember( jsonSaveInfo, "version", currentBuild );
   }

   private static string GetCurrentBuild()
   {
      var dataHandlerType = GameTypeResolver.Get( "DataHandler" );
      var currentBuild = dataHandlerType?.GetField( "strBuild", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null ) as string;
      if( !string.IsNullOrWhiteSpace( currentBuild ) )
      {
         return currentBuild.Trim();
      }

      return RuntimeGameVersionResolver.TryResolveCurrentGameBuild( out var gameBuildInfo, out _ )
         ? gameBuildInfo.DisplayVersion
         : string.Empty;
   }

   private static void SetStringMember( object target, string memberName, string value )
   {
      if( target == null || string.IsNullOrWhiteSpace( value ) ) return;

      var property = RuntimeHookTranslationHelper.GetProperty( target.GetType(), memberName );
      if( property?.CanWrite == true && property.PropertyType == typeof( string ) )
      {
         property.SetValue( target, value );
         return;
      }

      var field = RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), memberName );
      if( field?.FieldType == typeof( string ) )
      {
         field.SetValue( target, value );
      }
   }
}

internal static class CrewBarRuntimeTranslationHelper
{
   private static readonly HashSet<string> LoggedCrewCardDiagnostics = new HashSet<string>( StringComparer.Ordinal );
   private const string ShiftTooltipTitle = "轮次";

   public static string TranslateCrewDisplayName( object crewMember )
   {
      if( crewMember == null ) return string.Empty;

      var crewType = crewMember.GetType();
      var friendlyName = crewType.GetProperty( "FriendlyName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( crewMember ) as string;
      if( !string.IsNullOrWhiteSpace( friendlyName ) )
      {
         return RuntimeTextHookHelper.TranslateTextValue( friendlyName, "GUICrewCard.SetData.FriendlyName" );
      }

      var rawName = crewType.GetField( "strName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( crewMember ) as string;
      return RuntimeTextHookHelper.TranslateTextValue( rawName ?? string.Empty, "GUICrewCard.SetData.strName" );
   }

   public static void TranslateCrewBarUi()
   {
      var crewSimType = GameTypeResolver.Get( "CrewSim" );
      var crewBar = crewSimType?.GetField( "goCrewBar", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null ) as UnityEngine.GameObject;
      if( crewBar == null ) return;

      RuntimeTextHookHelper.TranslateHierarchy( crewBar, "CrewSim.Start.CrewBar" );
   }

   public static void TranslateShiftIndicator( object target, object crewMember, string hookName )
   {
      if( target == null ) return;

      var translatedShiftName = TranslateShiftName( GetShiftName( crewMember ), hookName + ".shift" );
      RuntimeHookTranslationHelper.SetTextComponentField( target, "_txtShift", translatedShiftName );

      var ttShiftField = RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), "ttShift" );
      var ttShift = ttShiftField?.GetValue( target );
      if( ttShift == null ) return;

      var setDataMethod = AccessTools.Method( ttShift.GetType(), "SetData", new[] { typeof( string ), typeof( string ), typeof( bool ) } );
      setDataMethod?.Invoke( ttShift, new object[] { ShiftTooltipTitle, translatedShiftName, false } );
   }

   public static void LogCrewCardDiagnostic( object crewCard, object crewMember, string translatedName )
   {
      var rawName = crewMember?.GetType().GetField( "strName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( crewMember ) as string ?? string.Empty;
      var friendlyName = crewMember?.GetType().GetProperty( "FriendlyName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( crewMember ) as string ?? string.Empty;
      var key = RuntimeHelpers.GetHashCode( crewCard ) + "|" + rawName + "|" + translatedName;
      if( !LoggedCrewCardDiagnostics.Add( key ) ) return;

      OstranautsTranslatorPlugin.LogDiagnostic(
         "CrewCardDiag"
         + " raw=" + SanitizeDiagnosticValue( rawName )
         + " friendly=" + SanitizeDiagnosticValue( friendlyName )
         + " translated=" + SanitizeDiagnosticValue( translatedName ) );
   }

   private static string SanitizeDiagnosticValue( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return "<empty>";

      return value.Replace( "\r", "\\r" ).Replace( "\n", "\\n" );
   }

   private static string GetShiftName( object crewMember )
   {
      if( crewMember == null ) return string.Empty;

      var shiftField = RuntimeHookTranslationHelper.GetInstanceField( crewMember.GetType(), "jsShiftLast" );
      var shift = shiftField?.GetValue( crewMember );
      if( shift == null ) return string.Empty;

      var shiftNameProperty = RuntimeHookTranslationHelper.GetProperty( shift.GetType(), "strName" );
      if( shiftNameProperty?.PropertyType == typeof( string ) )
      {
         return shiftNameProperty.GetValue( shift ) as string ?? string.Empty;
      }

      var shiftNameField = RuntimeHookTranslationHelper.GetInstanceField( shift.GetType(), "strName" );
      return shiftNameField?.GetValue( shift ) as string ?? string.Empty;
   }

   private static string TranslateShiftName( string shiftName, string hookName )
   {
      if( string.IsNullOrWhiteSpace( shiftName ) || string.Equals( shiftName, "blank", StringComparison.OrdinalIgnoreCase ) )
      {
         return string.Empty;
      }

      if( string.Equals( shiftName, "None", StringComparison.Ordinal ) )
      {
         return "无";
      }

      var translated = RuntimeTextHookHelper.TranslateTextValue( shiftName, hookName );
      if( !string.Equals( translated, shiftName, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return shiftName switch
      {
         "Free" => "空闲",
         "Sleep" => "睡眠",
         "Work" => "工作",
         _ => shiftName,
      };
   }
}

internal static class LogMessageRuntimeTranslationHelper
{
   private static readonly Regex RichTextLeadingYouMixedClauseRegex = new Regex( @"(^|\r?\n)(?<prefix>(?:<[^>]+>|\s)*)You\s*(?:have|has|are|feel|feels|gain|gains|need|needs|suffer(?:s)?(?:\s+from)?)\s*(?=[\u3400-\u9FFF\uF900-\uFAFF])", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex RichTextLeadingYouBeforeCjkRegex = new Regex( @"(^|\r?\n)(?<prefix>(?:<[^>]+>|\s)*)You\s*(?=[\u3400-\u9FFF\uF900-\uFAFF])", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex MixedStandaloneTheRegex = new Regex( @"(?<![A-Za-z])the\s+(?=(?:<[^>]+>)*[\u3400-\u9FFF0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase );

   public static string TranslateMessage( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      const string gainsToken = " gains ";
      var gainsIndex = value.IndexOf( gainsToken, StringComparison.Ordinal );
      if( gainsIndex > 0 && value.EndsWith( ".", StringComparison.Ordinal ) )
      {
         var subject = value.Substring( 0, gainsIndex );
         var payload = value.Substring( gainsIndex + gainsToken.Length, value.Length - gainsIndex - gainsToken.Length - 1 );
         return TranslateToken( subject, "CondOwner.LogMessage.gains.subject" )
            + " 获得了 "
            + TranslateToken( payload, "CondOwner.LogMessage.gains.payload" )
            + "。";
      }

      return NormalizeMixedMessageText( value );
   }

   public static string TranslateLogMarkup( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = OstranautsTranslatorPlugin.Translate( value, hookName );
      return NormalizeMixedMessageText( translated );
   }

   public static string NormalizeMixedMessageText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var normalized = RichTextLeadingYouMixedClauseRegex.Replace( value, match => match.Groups[ 1 ].Value + match.Groups[ "prefix" ].Value + "你" );
      normalized = RichTextLeadingYouBeforeCjkRegex.Replace( normalized, match => match.Groups[ 1 ].Value + match.Groups[ "prefix" ].Value + "你" );
      return MixedStandaloneTheRegex.Replace( normalized, string.Empty );
   }

   private static string TranslateToken( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      var trimmed = value.Trim();
      if( !string.Equals( trimmed, value, StringComparison.Ordinal ) )
      {
         var translatedTrimmed = RuntimeTextHookHelper.TranslateTextValue( trimmed, hookName + ".trim" );
         if( !string.Equals( translatedTrimmed, trimmed, StringComparison.Ordinal ) )
         {
            return value.Replace( trimmed, translatedTrimmed );
         }
      }

      return value;
   }
}

internal static class SettingsRuntimeTranslationHelper
{
   private static readonly IReadOnlyDictionary<string, string> ExactTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
   [ "Off" ] = "关",
      [ "5 mins" ] = "5分钟",
      [ "10 mins" ] = "10分钟",
   [ "15 mins" ] = "15分钟",
      [ "20 mins" ] = "20分钟",
      [ "30 mins" ] = "30分钟",
      [ "60 mins" ] = "60分钟",
      [ "Soft" ] = "柔和",
      [ "Kelvin" ] = "开尔文",
   };

   private static readonly IReadOnlyDictionary<string, string> TurboButtonTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "HYPER" ] = "狂飙",
      [ "PLUS" ] = "增强",
      [ "MEGA" ] = "巨能",
      [ "SUPER" ] = "超级",
      [ "FAST" ] = "迅捷",
      [ "YES" ] = "开启",
      [ "狂飙" ] = "狂飙",
      [ "增强" ] = "增强",
      [ "巨能" ] = "巨能",
      [ "超级" ] = "超级",
      [ "迅捷" ] = "迅捷",
      [ "开启" ] = "开启",
      [ "是" ] = "开启",
   };

   private static readonly string[] DropdownFieldNames =
   {
      "ddDateFormat",
      "ddTempUnits",
      "ddautoSaveInt",
      "ddautoSaveMaxCount",
      "ddFlickerAmount",
   };

   public static void TranslateOptionsUi( object guiOptions )
   {
      var root = RuntimeTextHookHelper.GetGameObject( guiOptions );
      RuntimeTextHookHelper.TranslateHierarchy( root, "GUIOptions.Init" );

      foreach( var fieldName in DropdownFieldNames )
      {
         RuntimeHookTranslationHelper.TranslateDropdownOptionsField( guiOptions, fieldName, TranslateOptionText );
      }

      RuntimeTextHookHelper.TranslateHierarchy( root, "GUIOptions.Init.post" );
      ApplyOptionsUiOverrides( root );
   }

   public static void ApplyOptionsUiOverrides( UnityEngine.GameObject root )
   {
      ReplaceFilePanelPathTexts( root );
      ReplaceKnownVideoOptionLabels( root );
      ReplaceTurboPanelTexts( root );
   }

   public static void TranslateTurboButtonUi( object guiOptions )
   {
      ReplaceTurboPanelTexts( RuntimeTextHookHelper.GetGameObject( guiOptions ) );
   }

   public static string TranslateOptionText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( ExactTextMap.TryGetValue( value, out var exactText ) )
      {
         return exactText;
      }

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, "GUIOptions.Option" );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return value;
   }

   private static void ReplaceKnownVideoOptionLabels( UnityEngine.GameObject root )
   {
      if( root == null ) return;

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) continue;

         var currentText = textProperty.GetValue( component ) as string;
         if( !string.Equals( currentText, "Turbo", StringComparison.Ordinal )
            && !string.Equals( currentText, "涡轮", StringComparison.Ordinal ) ) continue;

         textProperty.SetValue( component, "加速按钮" );
      }
   }

   private static void ReplaceFilePanelPathTexts( UnityEngine.GameObject root )
   {
      if( root == null ) return;

      var persistentDataPath = GetUnityApplicationPath( "persistentDataPath" );
      var streamingAssetsPath = GetUnityApplicationPath( "streamingAssetsPath" );

      SetFilePanelPathText( root, "pnlFiles/btnMods/boxFilePath/txt", GetModsPathText() );
      SetFilePanelPathText( root, "pnlFiles/btnScreenshots/boxFilePath/txt", CombineUnityPath( persistentDataPath, "Screenshots" ) );
      SetFilePanelPathText( root, "pnlFiles/btnManuals/boxFilePath/txt", CombineUnityPath( streamingAssetsPath, "images/manuals" ) );
      SetFilePanelPathText( root, "pnlFiles/btnSave1/boxFilePath/txt", GetSavesPathText() );
      SetFilePanelPathText( root, "pnlFiles/btnSettings/boxFilePath/txt", persistentDataPath );
      SetFilePanelPathText( root, "pnlFiles/btnAssets/boxFilePath/txt", streamingAssetsPath );
   }

   private static void SetFilePanelPathText( UnityEngine.GameObject root, string path, string value )
   {
      if( root == null || string.IsNullOrWhiteSpace( path ) || string.IsNullOrWhiteSpace( value ) ) return;

      var target = FindChildGameObject( root, path );
      if( target == null ) return;

      foreach( var component in EnumerateTextComponents( target ) )
      {
         RuntimeHookTranslationHelper.SetTextComponentProperty( component, value );
      }
   }

   private static string GetModsPathText()
   {
      var dataHandlerType = GameTypeResolver.Get( "DataHandler" );
      var field = dataHandlerType?.GetField( "strModFolder", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );
      return field?.GetValue( null ) as string;
   }

   private static string GetSavesPathText()
   {
      var loadManagerType = GameTypeResolver.Get( "LoadManager" );
      var instance = GetStaticPropertyValue( loadManagerType, "Instance" );
      if( instance != null )
      {
         var savesPathProperty = RuntimeHookTranslationHelper.GetProperty( instance.GetType(), "SavesPath" );
         if( savesPathProperty?.CanRead == true && savesPathProperty.PropertyType == typeof( string ) )
         {
            var savesPath = savesPathProperty.GetValue( instance ) as string;
            if( !string.IsNullOrWhiteSpace( savesPath ) ) return savesPath;
         }
      }

      return CombineUnityPath( GetUnityApplicationPath( "persistentDataPath" ), "Saves" );
   }

   private static string GetUnityApplicationPath( string propertyName )
   {
      if( string.IsNullOrWhiteSpace( propertyName ) ) return string.Empty;

      var applicationType = Type.GetType( "UnityEngine.Application, UnityEngine.CoreModule", false )
         ?? Type.GetType( "UnityEngine.Application, UnityEngine", false );
      var property = applicationType?.GetProperty( propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy );
      return property?.GetValue( null ) as string ?? string.Empty;
   }

   private static string CombineUnityPath( string root, string suffix )
   {
      if( string.IsNullOrWhiteSpace( root ) ) return string.Empty;
      if( string.IsNullOrWhiteSpace( suffix ) ) return root;

      return root.TrimEnd( '/', '\\' ) + "/" + suffix.TrimStart( '/', '\\' );
   }

   private static object GetStaticPropertyValue( Type type, string propertyName )
   {
      for( var current = type; current != null; current = current.BaseType )
      {
         var property = current.GetProperty( propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy );
         if( property?.CanRead == true ) return property.GetValue( null );
      }

      return null;
   }

   private static void ReplaceTurboPanelTexts( UnityEngine.GameObject root )
   {
      if( root == null ) return;

      var turboPanel = FindChildGameObject( root, "pnlVideo/pnlTurbo" );
      if( turboPanel == null ) return;

      var toggleRoot = FindChildGameObject( turboPanel, "chkB" );
      ReplaceTurboTitleText( turboPanel, toggleRoot );
      ReplaceTurboButtonTexts( toggleRoot );
   }

   private static void ReplaceTurboTitleText( UnityEngine.GameObject turboPanel, UnityEngine.GameObject toggleRoot )
   {
      if( turboPanel == null ) return;

      var titleCandidates = new List<object>();

      foreach( var component in EnumerateTextComponents( turboPanel ) )
      {
         var componentGameObject = RuntimeTextHookHelper.GetGameObject( component );
         if( componentGameObject == null ) continue;
         if( IsSameOrDescendant( componentGameObject, toggleRoot ) ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         var currentText = textProperty?.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( currentText ) ) continue;

         titleCandidates.Add( component );
         var normalizedText = currentText.Trim();
         if( !string.Equals( normalizedText, "Turbo", StringComparison.Ordinal )
            && !string.Equals( normalizedText, "涡轮", StringComparison.Ordinal ) ) continue;

         textProperty.SetValue( component, "加速按钮" );
         return;
      }

      if( titleCandidates.Count == 1 )
      {
         RuntimeHookTranslationHelper.SetTextComponentProperty( titleCandidates[ 0 ], "加速按钮" );
      }
   }

   private static void ReplaceTurboButtonTexts( UnityEngine.GameObject toggleRoot )
   {
      if( toggleRoot == null ) return;

      foreach( var component in EnumerateTextComponents( toggleRoot ) )
      {
         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         var currentText = textProperty?.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( currentText ) ) continue;

         if( !TurboButtonTextMap.TryGetValue( currentText.Trim(), out var translatedText ) ) continue;
         textProperty.SetValue( component, translatedText );
      }
   }

   private static IEnumerable EnumerateTextComponents( UnityEngine.GameObject root )
   {
      if( root == null ) yield break;

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) yield break;

      foreach( var component in components )
      {
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) continue;

         yield return component;
      }
   }

   private static UnityEngine.GameObject FindChildGameObject( UnityEngine.GameObject root, string path )
   {
      if( root == null || string.IsNullOrWhiteSpace( path ) ) return null;

      var transformProperty = typeof( UnityEngine.GameObject ).GetProperty( "transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var transform = transformProperty?.GetValue( root, null );
      if( transform == null ) return null;

      var findMethod = transform.GetType().GetMethod( "Find", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof( string ) }, null );
      var childTransform = findMethod?.Invoke( transform, new object[] { path } );
      return RuntimeTextHookHelper.GetGameObject( childTransform );
   }

   private static bool IsSameOrDescendant( UnityEngine.GameObject candidate, UnityEngine.GameObject ancestor )
   {
      if( candidate == null || ancestor == null ) return false;
      if( ReferenceEquals( candidate, ancestor ) ) return true;

      var current = candidate;
      while( current != null )
      {
         if( ReferenceEquals( current, ancestor ) ) return true;

         var transformProperty = typeof( UnityEngine.GameObject ).GetProperty( "transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
         var transform = transformProperty?.GetValue( current, null );
         var parentProperty = transform?.GetType().GetProperty( "parent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
         var parentTransform = parentProperty?.GetValue( transform, null );
         current = RuntimeTextHookHelper.GetGameObject( parentTransform );
      }

      return false;
   }
}

internal static class ControlsRuntimeTranslationHelper
{
   public static void TranslateControlsUi( object guiControls )
   {
      var root = RuntimeTextHookHelper.GetGameObject( guiControls );
      RuntimeTextHookHelper.TranslateHierarchy( root, "GUIControls.Init" );
      RuntimeHookTranslationHelper.SetTextComponentField( guiControls, "txtDescr", TranslateControlsDescription( GetTextComponentFieldValue( guiControls, "txtDescr" ) ) );
   }

   public static void TranslateControlsPage( object guiControls )
   {
      RuntimeHookTranslationHelper.SetTextComponentField( guiControls, "txtDescr", TranslateControlsDescription( GetTextComponentFieldValue( guiControls, "txtDescr" ) ) );
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( guiControls ), "GUIControls.SetPage" );
   }

   public static void TranslateActionKey( object actionKey )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentField( actionKey, "actionLabel", "GUIActionKey.Init" );
   }

   private static string TranslateControlsDescription( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      const string prefix = "Press ";
      const string middle = " on key to begin adding new combo and ";
      const string suffix = " to remove last combo.";

      if( value.StartsWith( prefix, StringComparison.Ordinal )
         && value.EndsWith( suffix, StringComparison.Ordinal ) )
      {
         var middleIndex = value.IndexOf( middle, StringComparison.Ordinal );
         if( middleIndex > prefix.Length )
         {
            var confirmGlyphs = value.Substring( prefix.Length, middleIndex - prefix.Length );
            var cancelGlyphs = value.Substring( middleIndex + middle.Length, value.Length - middleIndex - middle.Length - suffix.Length );
            return "按 " + confirmGlyphs + " 开始为按键添加新组合，按 " + cancelGlyphs + " 删除最后一个组合。";
         }
      }

      return RuntimeTextHookHelper.TranslateTextValue( value, "GUIControls.SetPage.txtDescr" );
   }

   private static string GetTextComponentFieldValue( object target, string fieldName )
   {
      if( target == null ) return string.Empty;

      var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var component = field?.GetValue( target );
      if( component == null ) return string.Empty;

      var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( textProperty == null || !textProperty.CanRead || textProperty.PropertyType != typeof( string ) ) return string.Empty;

      return textProperty.GetValue( component ) as string ?? string.Empty;
   }
}

internal static class MfdRuntimeTranslationHelper
{
   private static readonly IReadOnlyDictionary<string, string> ExactTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "LOCAL CHANNEL" ] = "本地频道",
      [ "MESSAGE LOG" ] = "消息日志",
      [ "PREVIOUS PAGE" ] = "上一页",
      [ "NEXT PAGE" ] = "下一页",
      [ "Docked: " ] = "已对接：",
   };

   public static string TranslateDisplayText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( value.IndexOf( '\n' ) >= 0 )
      {
         var lines = value.Split( '\n' );
         var changed = false;
         for( var i = 0; i < lines.Length; i++ )
         {
            var translatedLine = TranslateDisplaySingleLine( lines[ i ] );
            if( !string.Equals( translatedLine, lines[ i ], StringComparison.Ordinal ) )
            {
               lines[ i ] = translatedLine;
               changed = true;
            }
         }

         if( changed )
         {
            return string.Join( "\n", lines );
         }
      }

      return TranslateDisplaySingleLine( value );
   }

   private static string TranslateDisplaySingleLine( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, "GUIMFDDisplay.ShowMenu" );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = TranslateDirectionalText( value );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( value.StartsWith( "Mode [", StringComparison.Ordinal ) )
      {
         var separatorIndex = value.IndexOf( "] ", StringComparison.Ordinal );
         if( separatorIndex > 0 )
         {
            var modeIndex = value.Substring( 5, separatorIndex - 5 );
            var suffix = value.Substring( separatorIndex + 2 );
            return "模式 [" + modeIndex + "] " + TranslateDisplayText( suffix );
         }
      }

      if( value.StartsWith( "ATC CHANNEL: ", StringComparison.Ordinal ) )
      {
         return "ATC频道：" + value.Substring( "ATC CHANNEL: ".Length );
      }

      if( value.StartsWith( "CONNECTED WITH - ", StringComparison.Ordinal ) )
      {
         return "已连接 - " + value.Substring( "CONNECTED WITH - ".Length );
      }

      if( value.StartsWith( "OPEN CHANNEL TO ", StringComparison.Ordinal ) )
      {
         return "开放频道至 " + value.Substring( "OPEN CHANNEL TO ".Length );
      }

      if( value.StartsWith( "Docked: ", StringComparison.Ordinal ) )
      {
         return "已对接：" + value.Substring( "Docked: ".Length );
      }

      if( value.Equals( "ACTIVE SENSORS:", StringComparison.Ordinal ) )
      {
         return "主动传感器：";
      }

      if( value.Equals( "PASSIVE SENSORS:", StringComparison.Ordinal ) )
      {
         return "被动传感器：";
      }

      if( value.Equals( "NONE", StringComparison.Ordinal ) )
      {
         return "无";
      }

      if( value.Equals( "NO SENSORS FOUND", StringComparison.Ordinal ) )
      {
         return "未发现传感器";
      }

      if( value.Equals( "Back", StringComparison.Ordinal ) )
      {
         return "返回";
      }

      if( value.StartsWith( "LOCKING ", StringComparison.Ordinal ) && value.EndsWith( "%", StringComparison.Ordinal ) )
      {
         return "锁定中 " + value.Substring( "LOCKING ".Length );
      }

      if( value.Equals( "<color=green>LOCKED</color>", StringComparison.Ordinal ) )
      {
         return "<color=green>已锁定</color>";
      }

      if( value.Equals( "<color=white>HOSTILE</color>", StringComparison.Ordinal ) )
      {
         return "<color=white>敌对</color>";
      }

      if( value.Equals( "<color=white>FRIENDLY</color>", StringComparison.Ordinal ) )
      {
         return "<color=white>友方</color>";
      }

      if( value.Equals( "MARK", StringComparison.Ordinal ) )
      {
         return "标记";
      }

      if( value.Equals( "MARK: -", StringComparison.Ordinal ) )
      {
         return "标记：-";
      }

      if( value.Equals( "VIZ", StringComparison.Ordinal ) )
      {
         return "可视化";
      }

      if( value.Equals( "<color=green>VIZ</color>", StringComparison.Ordinal ) )
      {
         return "<color=green>可视化</color>";
      }

      if( value.StartsWith( "OPT ", StringComparison.Ordinal ) )
      {
         return "光学 " + value.Substring( "OPT ".Length );
      }

      if( value.Equals( "Sensors", StringComparison.Ordinal ) )
      {
         return "传感器";
      }

      if( value.StartsWith( "Sensors: <b>", StringComparison.Ordinal ) )
      {
         return "传感器：<b>" + value.Substring( "Sensors: <b>".Length );
      }

      if( value.StartsWith( "Signal:", StringComparison.Ordinal ) )
      {
         return "信号：" + value.Substring( "Signal:".Length );
      }

      if( value.EndsWith( ": <color=green>ON</color>", StringComparison.Ordinal ) )
      {
         var label = value.Substring( 0, value.Length - ": <color=green>ON</color>".Length );
         return TranslateDisplayText( label ) + "：<color=green>开启</color>";
      }

      if( value.EndsWith( ": <color=red>OFF</color>", StringComparison.Ordinal ) )
      {
         var label = value.Substring( 0, value.Length - ": <color=red>OFF</color>".Length );
         return TranslateDisplayText( label ) + "：<color=red>关闭</color>";
      }

      if( value.StartsWith( "<b>Thrusters: ", StringComparison.Ordinal ) )
      {
         return "<b>推进器：" + value.Substring( "<b>Thrusters: ".Length );
      }

      if( value.StartsWith( "Weapons: ", StringComparison.Ordinal ) )
      {
         return "武器：" + value.Substring( "Weapons: ".Length );
      }

      if( value.StartsWith( "Launchers: ", StringComparison.Ordinal ) )
      {
         return "发射器：" + value.Substring( "Launchers: ".Length );
      }

      if( value.StartsWith( "Coilguns: ", StringComparison.Ordinal ) )
      {
         return "线圈炮：" + value.Substring( "Coilguns: ".Length );
      }

      if( value.StartsWith( "PDCs: ", StringComparison.Ordinal ) )
      {
         return "点防炮：" + value.Substring( "PDCs: ".Length );
      }

      if( value.StartsWith( "Connected with ", StringComparison.Ordinal ) )
      {
         var middleIndex = value.IndexOf( " of the ", StringComparison.Ordinal );
         if( middleIndex > 0 )
         {
            var contact = value.Substring( "Connected with ".Length, middleIndex - "Connected with ".Length );
            var target = value.Substring( middleIndex + " of the ".Length );
            return "已连接至 " + contact + " 的 " + RuntimeTextHookHelper.TranslateTextValue( target, "GUIMessageDisplay.ShowPanel.target" );
         }
      }

   return ExactTextMap.TryGetValue( value, out var exactText ) ? exactText : value;
   }

   private static string TranslateDirectionalText( string value )
   {
      var prefix = string.Empty;
      var suffix = string.Empty;
      var core = value;

      if( core.StartsWith( "< ", StringComparison.Ordinal ) )
      {
         prefix = "< ";
         core = core.Substring( 2 );
      }
      else if( core.StartsWith( "<", StringComparison.Ordinal ) )
      {
         prefix = "<";
         core = core.Substring( 1 );
      }

      if( core.EndsWith( " >", StringComparison.Ordinal ) )
      {
         suffix = " >";
         core = core.Substring( 0, core.Length - 2 );
      }
      else if( core.EndsWith( ">", StringComparison.Ordinal ) )
      {
         suffix = ">";
         core = core.Substring( 0, core.Length - 1 );
      }

      if( string.IsNullOrEmpty( prefix ) && string.IsNullOrEmpty( suffix ) )
      {
         return value;
      }

      var translatedCore = TranslateDisplayText( core );
      return string.Equals( translatedCore, core, StringComparison.Ordinal ) ? value : prefix + translatedCore + suffix;
   }
}

internal static class NavStationRuntimeTranslationHelper
{
   public static void TranslateNavStationUi( string hookName )
   {
      var guiRenderTargetsType = GameTypeResolver.Get( "GUIRenderTargets" );
      var goLines = guiRenderTargetsType?.GetField( "goLines", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null ) as UnityEngine.GameObject;
      if( goLines == null ) return;

      RuntimeTextHookHelper.TranslateHierarchy( goLines, hookName );
   }

   public static void TranslateNavModeLabel()
   {
      var guiRenderTargetsType = GameTypeResolver.Get( "GUIRenderTargets" );
      var goLines = guiRenderTargetsType?.GetField( "goLines", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null ) as UnityEngine.GameObject;
      var transform = typeof( UnityEngine.GameObject ).GetProperty( "transform", BindingFlags.Instance | BindingFlags.Public )?.GetValue( goLines );
      var navModeTransform = transform?.GetType().GetMethod( "Find", new[] { typeof( string ) } )?.Invoke( transform, new object[] { "pnlTitle/txtNavMode" } );
      var navModeComponent = navModeTransform?.GetType().GetMethod( "GetComponent", new[] { typeof( Type ) } )?.Invoke( navModeTransform, new object[] { RuntimeTypeResolver.FindLoadedType( "TMPro.TMP_Text" ) } );
      if( navModeComponent == null ) return;

      var textProperty = RuntimeHookTranslationHelper.GetStringProperty( navModeComponent.GetType(), "text" );
      if( textProperty?.GetValue( navModeComponent ) is not string value || string.IsNullOrWhiteSpace( value ) ) return;

      var translated = TranslateNavModeText( value );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         textProperty.SetValue( navModeComponent, translated );
      }
   }

   private static string TranslateNavModeText( string value )
   {
      const string prefix = "NAV MODE: ";
      if( !value.StartsWith( prefix, StringComparison.Ordinal ) ) return value;

      var mode = value.Substring( prefix.Length ) switch
      {
         "RCS" => "姿控",
         "PAN" => "平移",
         var other => RuntimeTextHookHelper.TranslateTextValue( other, "NavStation.NavMode" )
      };

      return "导航模式：" + mode;
   }
}

internal static class ShipRuntimeTranslationHelper
{
   private static readonly string[] ShipInfoFieldNames =
   {
      "make",
      "model",
      "year",
      "origin",
      "designation",
      "publicName",
      "dimensions",
   };

   private static readonly string[] XpdrFieldNames =
   {
      "txtShipName",
      "txtMake",
      "txtModel",
      "txtYear",
      "txtDesignation",
   };

   public static void TranslateShipInfo( object shipInfo, string hookName )
   {
      if( shipInfo == null ) return;

      foreach( var fieldName in ShipInfoFieldNames )
      {
         RuntimeHookTranslationHelper.TranslateStringField( shipInfo, fieldName, value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + "." + fieldName ) );
      }
   }

   public static void TranslateXpdrPanel( object panel, string hookName )
   {
      if( panel == null ) return;

      foreach( var fieldName in XpdrFieldNames )
      {
         RuntimeHookTranslationHelper.TranslateTextComponentField( panel, fieldName, hookName );
      }
   }

   public static void TranslateGuiFriendlyName( object guiData, string hookName )
   {
      if( guiData == null ) return;

      RuntimeHookTranslationHelper.TranslateStringField( guiData, "strFriendlyName", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".strFriendlyName" ) );

      var dictField = RuntimeHookTranslationHelper.GetInstanceField( guiData.GetType(), "dictPropMap" );
      if( dictField?.GetValue( guiData ) is IDictionary<string, string> dict
         && dict.TryGetValue( "strFriendlyName", out var friendlyName )
         && !string.IsNullOrWhiteSpace( friendlyName ) )
      {
         dict[ "strFriendlyName" ] = RuntimeTextHookHelper.TranslateTextValue( friendlyName, hookName + ".dictPropMap.strFriendlyName" );
      }
   }

   public static void TranslateTargetDataPanel( object panel, string hookName )
   {
      if( panel == null ) return;

      var field = RuntimeHookTranslationHelper.GetInstanceField( panel.GetType(), "txtArray" );
      if( field?.GetValue( panel ) is not Array textComponents ) return;

      for( var i = 0; i < textComponents.Length; i++ )
      {
         var component = textComponents.GetValue( i );
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) continue;

         var value = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( value ) ) continue;

         var translated = TranslateTargetDataLine( value, hookName + "[" + i + "]" );
         if( !string.Equals( translated, value, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( component, translated );
         }
      }
   }

   private static string TranslateTargetDataLine( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = ReplacePrefix( value, "Point of Ref:", "参考点：" );
      translated = ReplacePrefix( translated, "VREL: ", "相对速度：" );
      translated = ReplacePrefix( translated, "VREL ", "相对速度 " );
      translated = ReplacePrefix( translated, "VCRS ", "横向速度 " );
      translated = ReplacePrefix( translated, "BRG ", "方位 " );
      translated = ReplacePrefix( translated, "ETA ", "预计到达 " );
      translated = ReplacePrefix( translated, "Claimed by: ", "归属：" );
      translated = ReplaceExact( translated, "F: Unclaimed", "F: 未认领" );
      translated = ReplaceToken( translated, "RNG ", "距离 " );

      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return RuntimeTextHookHelper.TranslateTextValue( value, hookName );
   }

   private static string ReplaceExact( string value, string source, string replacement )
   {
      return string.Equals( value, source, StringComparison.Ordinal ) ? replacement : value;
   }

   private static string ReplacePrefix( string value, string prefix, string replacement )
   {
      return value.StartsWith( prefix, StringComparison.Ordinal )
         ? replacement + value.Substring( prefix.Length )
         : value;
   }

   private static string ReplaceOrdinal( string value, string oldValue, string newValue )
   {
      if( string.IsNullOrEmpty( value ) || string.IsNullOrEmpty( oldValue ) ) return value;

      var index = value.IndexOf( oldValue, StringComparison.Ordinal );
      if( index < 0 ) return value;

      var builder = new StringBuilder( value.Length );
      var cursor = 0;
      while( index >= 0 )
      {
         builder.Append( value, cursor, index - cursor );
         builder.Append( newValue );
         cursor = index + oldValue.Length;
         index = value.IndexOf( oldValue, cursor, StringComparison.Ordinal );
      }

      builder.Append( value, cursor, value.Length - cursor );
      return builder.ToString();
   }

   private static string ReplaceToken( string value, string token, string replacement )
   {
      var index = value.IndexOf( token, StringComparison.Ordinal );
      if( index < 0 ) return value;

      return value.Substring( 0, index ) + replacement + value.Substring( index + token.Length );
   }
}

internal static class TooltipRuntimeTranslationHelper
{
   private static readonly Regex PersonNameTokenPattern = new Regex( "[A-Za-z][A-Za-z'’-]*", RegexOptions.CultureInvariant );
   private static readonly Regex EmbeddedPersonNamePattern = new Regex( "\\b[A-Z][A-Za-z'’-]+(?:\\s+[A-Z][A-Za-z'’-]+){2,}\\b", RegexOptions.CultureInvariant );
   private static readonly HashSet<string> LoggedCrewTooltipDiagnostics = new HashSet<string>( StringComparer.Ordinal );

   private static readonly IReadOnlyDictionary<string, string> ExactPersonNameTokenMap = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
   {
      [ "Camila" ] = "卡米拉",
      [ "Graves" ] = "格雷夫斯",
      [ "Oluwakemi" ] = "奥卢瓦克米",
   };

   public static string TranslateCondOwnerDisplayName( string value, object condOwner, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      foreach( var sourceName in EnumerateKnownDisplayNames( condOwner ) )
      {
         var translatedName = TranslatePersonName( sourceName, hookName + "." + sourceName );
         if( string.Equals( translatedName, sourceName, StringComparison.Ordinal ) ) continue;

         if( value.Contains( sourceName, StringComparison.Ordinal ) )
         {
            return value.Replace( sourceName, translatedName );
         }

         if( string.Equals( value, sourceName, StringComparison.Ordinal ) )
         {
            return translatedName;
         }
      }

      return TranslatePersonName( value, hookName );
   }

   public static string TranslateCondOwnerTooltipText( string value, object condOwner, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = ReplaceCrewName( value, condOwner, hookName );
      var lines = translated.Split( '\n' );
      var translatedNameLine = false;
      for( var i = 0; i < lines.Length; i++ )
      {
         var line = lines[ i ];
         if( string.IsNullOrWhiteSpace( line ) ) continue;

         if( !translatedNameLine )
         {
            lines[ i ] = ReplaceCrewName( line, condOwner, hookName + ".FriendlyName" );
            translatedNameLine = true;
            continue;
         }

         lines[ i ] = TranslateLegacyTooltipLine( line, hookName + "[" + i + "]" );
      }

      return string.Join( "\n", lines );
   }

   public static string TranslateInteractionTooltipText( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var lines = value.Split( '\n' );
      for( var i = 0; i < lines.Length; i++ )
      {
         if( string.IsNullOrWhiteSpace( lines[ i ] ) ) continue;
         lines[ i ] = TranslateLegacyInteractionLine( lines[ i ], hookName + "[" + i + "]" );
      }

      return string.Join( "\n", lines );
   }

   public static string TranslateEmbeddedPersonNames( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = EmbeddedPersonNamePattern.Replace( value, match =>
      {
         var translatedName = TranslatePersonName( match.Value, hookName + ".name" );
         return string.Equals( translatedName, match.Value, StringComparison.Ordinal )
            ? match.Value
            : translatedName;
      } );

      return ReplaceKnownPersonNameTokensInText( translated, hookName );
   }

   private static string ReplaceKnownPersonNameTokensInText( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      return PersonNameTokenPattern.Replace( value, match =>
      {
         return TryTranslatePersonNameTokenFromLookup( match.Value, out var translatedToken )
            ? translatedToken
            : match.Value;
      } );
   }

   private static bool TryTranslatePersonNameTokenFromLookup( string value, out string translatedValue )
   {
      translatedValue = string.Empty;
      if( string.IsNullOrWhiteSpace( value ) ) return false;

      if( ExactPersonNameTokenMap.TryGetValue( value, out var exactTranslated ) )
      {
         translatedValue = exactTranslated;
         return true;
      }

      if( ChargenBodyRuntimeTranslationHelper.TryTranslateKnownNameToken( value, out var lookupTranslated ) )
      {
         translatedValue = lookupTranslated;
         return true;
      }

      return false;
   }

   public static void TranslateCrewTooltip( object tooltip, object crewMember, string hookName )
   {
      if( tooltip == null ) return;

      var tooltipTextField = RuntimeHookTranslationHelper.GetInstanceField( tooltip.GetType(), "tooltipText" );
      var tooltipTextComponent = tooltipTextField?.GetValue( tooltip );
      if( tooltipTextComponent == null ) return;

      var textProperty = RuntimeHookTranslationHelper.GetStringProperty( tooltipTextComponent.GetType(), "text" );
      if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      var value = textProperty.GetValue( tooltipTextComponent ) as string;
      if( string.IsNullOrWhiteSpace( value ) ) return;

      var translated = TranslateCrewTooltipText( value, crewMember, hookName );
      LogCrewTooltipDiagnostic( tooltip, crewMember, hookName, value, translated );
      if( string.Equals( translated, value, StringComparison.Ordinal ) ) return;

      textProperty.SetValue( tooltipTextComponent, translated );
      tooltip.GetType().GetMethod( "TooltipResize2", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.Invoke( tooltip, null );
   }

   private static string TranslateCrewTooltipText( string value, object crewMember, string hookName )
   {
      var translated = ReplaceCrewName( value, crewMember, hookName );
      translated = ReplaceShiftName( translated, crewMember, hookName );

      translated = translated.Replace( ", <b>Captain</b>", "，<b>船长</b>" );
      translated = translated.Replace( ", <b>Crew</b>", "，<b>船员</b>" );
      translated = translated.Replace( ", <b>Active Shift:</b> ", "，<b>当前班次：</b> " );
      translated = translated.Replace( "\n\n<b>Current:</b>", "\n\n<b>当前：</b>" );
      translated = translated.Replace( "\n\n<b>Log:</b>", "\n\n<b>日志：</b>" );
      translated = translated.Replace( "\n\n<b>Planned:</b>", "\n\n<b>计划：</b>" );
      translated = translated.Replace( "\n\n<b>Last Failed Work Attempts:</b>", "\n\n<b>最近失败的工作尝试：</b>" );
      translated = translated.Replace( "\n\n<b>Last Active Pledge:</b>\n", "\n\n<b>最近一次生效誓约：</b>\n" );

      var lines = translated.Split( '\n' );
      for( var i = 0; i < lines.Length; i++ )
      {
         var line = lines[ i ];
         var trimmed = line.Trim();
         if( string.IsNullOrWhiteSpace( trimmed ) ) continue;

         if( string.Equals( trimmed, "none", StringComparison.Ordinal ) )
         {
            lines[ i ] = line.Replace( "none", "无" );
            continue;
         }

         if( trimmed.EndsWith( "s ago", StringComparison.Ordinal ) )
         {
            lines[ i ] = line.Substring( 0, line.LastIndexOf( "s ago", StringComparison.Ordinal ) ) + "秒前";
            continue;
         }

         lines[ i ] = RuntimeTextHookHelper.TranslateTextValue( line, hookName + "[" + i + "]" );
      }

      return string.Join( "\n", lines );
   }

   private static string ReplaceCrewName( string value, object crewMember, string hookName )
   {
      foreach( var sourceName in EnumerateKnownDisplayNames( crewMember ) )
      {
         var translatedName = TranslatePersonName( sourceName, hookName + "." + sourceName );
         if( string.Equals( translatedName, sourceName, StringComparison.Ordinal ) ) continue;

         if( value.Contains( sourceName, StringComparison.Ordinal ) )
         {
            return value.Replace( sourceName, translatedName );
         }
      }

      return value;
   }

   private static string ReplaceShiftName( string value, object crewMember, string hookName )
   {
      var shift = GetObjectMember( crewMember, "jsShiftLast" );
      var shiftName = GetStringMember( shift, "strName" );
      if( string.IsNullOrWhiteSpace( shiftName ) ) return value;

      var translatedShiftName = RuntimeTextHookHelper.TranslateTextValue( shiftName, hookName + ".ShiftName" );
      return string.Equals( translatedShiftName, shiftName, StringComparison.Ordinal )
         ? value
         : value.Replace( shiftName, translatedShiftName );
   }

   private static string TranslateLegacyTooltipLine( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = ReplaceLinePrefix( value, "Mass: ", "质量：" );
      translated = ReplaceLinePrefix( translated, "Mass of stack: ", "堆叠总质量：" );
      translated = ReplaceLinePrefix( translated, "Condition: ", "状态：" );
      translated = ReplaceLinePrefix( translated, "Charge: ", "电量：" );
      translated = ReplaceLinePrefix( translated, "Pressure: ", "压力：" );
      return translated;
   }

   private static string TranslateLegacyInteractionLine( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      var trimmed = value.Trim();
      translated = trimmed switch
      {
         "<b>We need:</b>" => value.Replace( trimmed, "<b>我们需要：</b>" ),
         "<b>We can't be:</b>" => value.Replace( trimmed, "<b>我们不能处于：</b>" ),
         "<b>Effects:</b>" => value.Replace( trimmed, "<b>效果：</b>" ),
         "<b>Tools required:</b>" => value.Replace( trimmed, "<b>所需工具：</b>" ),
         "<b>Items given:</b>" => value.Replace( trimmed, "<b>给予物品：</b>" ),
         "<b>Items consumed:</b>" => value.Replace( trimmed, "<b>消耗物品：</b>" ),
         _ => value,
      };
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( trimmed.StartsWith( "{Is ", StringComparison.Ordinal ) || trimmed.StartsWith( "<Is ", StringComparison.Ordinal )
         || trimmed.StartsWith( "{Has ", StringComparison.Ordinal ) || trimmed.StartsWith( "<Has ", StringComparison.Ordinal ) )
      {
         translated = value.Replace( "{Is ", "{具有 " )
            .Replace( "<Is ", "<具有 " )
            .Replace( "{Has ", "{具有 " )
            .Replace( "<Has ", "<具有 " )
            .Replace( ", and ", "、" )
            .Replace( ", ", "、" )
            .Replace( " and ", " 和 " );
      }

      return translated;
   }

   private static string ReplaceLinePrefix( string value, string prefix, string replacement )
   {
      return value.StartsWith( prefix, StringComparison.Ordinal )
         ? replacement + value.Substring( prefix.Length )
         : value;
   }

   private static object GetObjectMember( object target, string memberName )
   {
      if( target == null ) return null;

      var property = RuntimeHookTranslationHelper.GetProperty( target.GetType(), memberName );
      if( property != null && property.CanRead )
      {
         return property.GetValue( target );
      }

      return RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), memberName )?.GetValue( target );
   }

   private static string GetStringMember( object target, string memberName )
   {
      if( target == null ) return string.Empty;

      var property = RuntimeHookTranslationHelper.GetProperty( target.GetType(), memberName );
      if( property != null && property.CanRead && property.PropertyType == typeof( string ) )
      {
         return property.GetValue( target ) as string ?? string.Empty;
      }

      var field = RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), memberName );
      if( field != null && field.FieldType == typeof( string ) )
      {
         return field.GetValue( target ) as string ?? string.Empty;
      }

      return string.Empty;
   }

   private static string GetDisplayName( object target )
   {
      var displayName = GetStringMember( target, "FriendlyName" );
      if( !string.IsNullOrWhiteSpace( displayName ) ) return displayName;

      displayName = GetStringMember( target, "strNameFriendly" );
      if( !string.IsNullOrWhiteSpace( displayName ) ) return displayName;

      return GetStringMember( target, "strName" );
   }

   private static string GetTranslatedCondOwnerName( object target, string hookName )
   {
      foreach( var displayName in EnumerateKnownDisplayNames( target ) )
      {
         var translatedName = TranslatePersonName( displayName, hookName + "." + displayName );
         if( !string.Equals( translatedName, displayName, StringComparison.Ordinal ) )
         {
            return translatedName;
         }
      }

      return string.Empty;
   }

   private static IEnumerable<string> EnumerateKnownDisplayNames( object target )
   {
      if( target == null ) yield break;

      var seen = new HashSet<string>( StringComparer.Ordinal );
      foreach( var value in new[]
      {
         GetStringMember( target, "FriendlyName" ),
         GetStringMember( target, "strNameFriendly" ),
         GetStringMember( target, "strNameShort" ),
         GetStringMember( target, "strName" ),
      } )
      {
         if( string.IsNullOrWhiteSpace( value ) || !seen.Add( value ) ) continue;
         yield return value;
      }
   }

   private static void LogCrewTooltipDiagnostic( object tooltip, object crewMember, string hookName, string value, string translated )
   {
      var key = RuntimeHelpers.GetHashCode( tooltip ) + "|" + value + "|" + translated;
      if( !LoggedCrewTooltipDiagnostics.Add( key ) ) return;

      OstranautsTranslatorPlugin.LogDiagnostic(
         "CrewTooltipDiag"
         + " hook=" + hookName
         + " window=" + SanitizeDiagnosticValue( RuntimeHookTranslationHelper.GetInstanceField( tooltip.GetType(), "window" )?.GetValue( tooltip )?.ToString() )
         + " text=" + SanitizeDiagnosticValue( value )
         + " translated=" + SanitizeDiagnosticValue( translated )
         + " strName=" + SanitizeDiagnosticValue( GetStringMember( crewMember, "strName" ) )
         + " strNameFriendly=" + SanitizeDiagnosticValue( GetStringMember( crewMember, "strNameFriendly" ) )
         + " strNameShort=" + SanitizeDiagnosticValue( GetStringMember( crewMember, "strNameShort" ) ) );
   }

   private static string SanitizeDiagnosticValue( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return "<empty>";

      return value.Replace( "\r", "\\r" ).Replace( "\n", "\\n" );
   }

   private static string TranslatePersonName( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( TryTranslatePersonNameTokenFromLookup( value, out var exactTranslated ) )
      {
         return exactTranslated;
      }

      var changed = false;
      var translatedTokenCount = 0;
      var tokenCount = 0;

      var translated = PersonNameTokenPattern.Replace( value, match =>
      {
         tokenCount++;

         var translatedToken = TranslatePersonNameToken( match.Value, hookName + ".part" + tokenCount );
         if( string.Equals( translatedToken, match.Value, StringComparison.Ordinal ) ) return match.Value;

         changed = true;
         translatedTokenCount++;
         return translatedToken;
      } );

      if( changed )
      {
         return tokenCount > 1 && translatedTokenCount == tokenCount
            ? Regex.Replace( translated, "\\s+", "·" )
            : translated;
      }

      translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      return !string.Equals( translated, value, StringComparison.Ordinal ) ? translated : value;
   }

   private static string TranslatePersonNameToken( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( TryTranslatePersonNameTokenFromLookup( value, out var translatedValue ) )
      {
         return translatedValue;
      }

      return RuntimeTextHookHelper.TranslateTextValue( value, hookName );
   }
}

internal static class MegaToolTipRuntimeTranslationHelper
{
   private static readonly Regex ItemSentencePattern = new Regex( "^The (?<subject>.+?) is (?<article>an? )?(?<descriptor>.+?) item\\.$", RegexOptions.CultureInvariant );
   private static readonly Regex ItemTokenSentencePattern = new Regex( "^\\[us\\] \\[is\\] (?<article>an? )?(?<descriptor>.+?) item\\.$", RegexOptions.CultureInvariant );
   private static readonly Regex StateSentencePattern = new Regex( "^The (?<subject>.+?) is (?<article>an? )?(?<descriptor>.+?)\\.$", RegexOptions.CultureInvariant );
   private static readonly IReadOnlyDictionary<string, string> FactionFriendlyNameMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "AyoSec" ] = "阿约安全",
      [ "Ayotimiwa Ship Breaking Co." ] = "阿约蒂米瓦拆船公司",
      [ "OKLG Civilian" ] = "OKLG 平民"
   };

   public static string TranslateTooltipTitle( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) ) return translated;

      return value switch
      {
         "Precise Value" => "精确估值",
         "Rough Value" => "粗略估值",
         _ => value,
      };
   }

   public static string TranslateTooltipBody( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) ) return translated;

      translated = value switch
      {
         "A skilled spacer's appraisal of the item's worth." => "熟练太空人对该物品价值的评估。",
         "An unskilled spacer's rough guess of the item's worth." => "不熟练太空人对该物品价值的粗略估计。",
         _ => value,
      };
      if( !string.Equals( translated, value, StringComparison.Ordinal ) ) return translated;

      var match = ItemSentencePattern.Match( value );
      if( match.Success )
      {
         var subject = match.Groups[ "subject" ].Value;
         var descriptor = match.Groups[ "descriptor" ].Value;

         var translatedSubject = RuntimeTextHookHelper.TranslateTextValue( subject, hookName + ".subject" );
         return BuildTranslatedItemSentence( translatedSubject, descriptor, hookName + ".descriptor" );
      }

      match = StateSentencePattern.Match( value );
      if( !match.Success ) return value;

      var translatedStateSubject = RuntimeTextHookHelper.TranslateTextValue( match.Groups[ "subject" ].Value, hookName + ".subject" );
      var translatedDescriptor = TranslateDescriptor( match.Groups[ "descriptor" ].Value, hookName + ".descriptor" );
      return translatedStateSubject + " 是" + translatedDescriptor + "。";
   }

   public static string TranslateItemDescription( string value, object condOwner, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translatedOwnerName = GetTranslatedOwnerName( condOwner, hookName + ".owner" );
      var lines = value.Split( '\n' );
      for( var i = 0; i < lines.Length; i++ )
      {
         var line = lines[ i ];
         if( string.IsNullOrWhiteSpace( line ) ) continue;

         var tokenSentenceMatch = ItemTokenSentencePattern.Match( line );
         if( tokenSentenceMatch.Success && !string.IsNullOrWhiteSpace( translatedOwnerName ) )
         {
            lines[ i ] = BuildTranslatedItemSentence( translatedOwnerName, tokenSentenceMatch.Groups[ "descriptor" ].Value, hookName + "[" + i + "].descriptor" );
            continue;
         }

         if( line.StartsWith( "Factions: ", StringComparison.Ordinal ) )
         {
            lines[ i ] = "派系：" + TranslateFactionList( line.Substring( "Factions: ".Length ), hookName + "[" + i + "].factions" );
            continue;
         }

         if( string.Equals( line, "n/a", StringComparison.Ordinal ) || string.Equals( line, "N/A", StringComparison.Ordinal ) )
         {
            lines[ i ] = "无";
            continue;
         }

         lines[ i ] = RuntimeTextHookHelper.TranslateTextValue( line, hookName + "[" + i + "]" );
      }

      return string.Join( "\n", lines );
   }

   private static string TranslateFactionList( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var parts = value.Split( new[] { ", " }, StringSplitOptions.None );
      var changed = false;

      for( var i = 0; i < parts.Length; i++ )
      {
         var part = parts[ i ];
         if( string.IsNullOrWhiteSpace( part ) ) continue;

         var translatedPart = TranslateFactionFriendlyName( part, hookName + "." + i );
         if( !string.Equals( translatedPart, part, StringComparison.Ordinal ) )
         {
            parts[ i ] = translatedPart;
            changed = true;
         }
      }

      return changed ? string.Join( ", ", parts ) : value;
   }

   private static string TranslateFactionFriendlyName( string value, string hookName )
   {
      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return FactionFriendlyNameMap.TryGetValue( value, out var mapped )
         ? mapped
         : value;
   }

   private static string BuildTranslatedItemSentence( string subject, string descriptor, string hookName )
   {
      var translatedDescriptor = TranslateDescriptor( descriptor, hookName );

      return subject + " 是一种" + translatedDescriptor + "物品。";
   }

   private static string TranslateDescriptor( string descriptor, string hookName )
   {
      var translatedDescriptor = RuntimeTextHookHelper.TranslateTextValue( descriptor, hookName );

      return translatedDescriptor switch
      {
         "destructable" => "可破坏",
         "solid (not liquid/gas/ideal)" => "固体（非液体/气体/理想气体）",
         _ => translatedDescriptor,
      };
   }

   private static string GetTranslatedOwnerName( object condOwner, string hookName )
   {
      if( condOwner == null ) return string.Empty;

      var ownerName = condOwner.GetType().GetProperty( "FriendlyName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( condOwner ) as string;
      if( string.IsNullOrWhiteSpace( ownerName ) )
      {
         ownerName = RuntimeHookTranslationHelper.GetInstanceField( condOwner.GetType(), "strNameFriendly" )?.GetValue( condOwner ) as string;
      }

      if( string.IsNullOrWhiteSpace( ownerName ) ) return string.Empty;

      return RuntimeTextHookHelper.TranslateTextValue( ownerName, hookName );
   }
}

internal static class MessageDisplayRuntimeTranslationHelper
{
   private static readonly IReadOnlyDictionary<string, string> ExactTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Open incoming port" ] = "打开传入端口",
      [ "Build routing table" ] = "构建路由表",
      [ "Load kernel driver" ] = "加载内核驱动",
      [ "Interface message Processor" ] = "接口消息处理器",
      [ "DONE" ] = "完成",
      [ "OPERATIONAL" ] = "运行正常",
      [ "Connection established" ] = "连接已建立",
   };

   public static void TranslateUi( object messageDisplay )
   {
      TranslateStatusMessages( messageDisplay );
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( messageDisplay ), "GUIMessageDisplay" );
      TranslateRenderedStatusText( messageDisplay );
   }

   public static string TranslateStatusText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, "GUIMessageDisplay.Status" );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return ExactTextMap.TryGetValue( value, out var exactText ) ? exactText : value;
   }

   public static string TranslateConversationText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = value.Replace( "Connection established", TranslateStatusText( "Connection established" ) );
      return MfdRuntimeTranslationHelper.TranslateDisplayText( translated );
   }

   public static void TranslateMessageObject( object shipMessage )
   {
      if( shipMessage == null ) return;

      var textProperty = RuntimeHookTranslationHelper.GetStringProperty( shipMessage.GetType(), "MessageText" );
      if( textProperty == null ) return;

      var value = textProperty.GetValue( shipMessage ) as string;
      if( string.IsNullOrWhiteSpace( value ) ) return;

      var translated = TranslateConversationMessageText( value );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         textProperty.SetValue( shipMessage, translated );
      }
   }

   public static string TranslateConversationMessageText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, "GUIMessageDisplay.MessageText" );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = TranslateReadyToProceedText( value );
      translated = TranslateHailsText( translated );
      translated = TranslateConversationResidualText( translated );
      return translated;
   }

   public static string TranslateRenderedStatusMarkup( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      value = ReplaceStatusToken( value, "<color=green>DONE</color>", "<color=green>完成</color>" );
      value = ReplaceStatusToken( value, "<color=green>OPERATIONAL</color>", "<color=green>运行正常</color>" );
      return value;
   }

   public static void TranslateRenderedStatusText( object messageDisplay )
   {
      if( messageDisplay == null ) return;

      var field = messageDisplay.GetType().GetField( "txtStatus", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var component = field?.GetValue( messageDisplay );
      if( component == null ) return;

      var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      if( textProperty.GetValue( component ) is not string value || string.IsNullOrWhiteSpace( value ) ) return;

      var translated = TranslateRenderedStatusMarkup( value );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         textProperty.SetValue( component, translated );
      }
   }

   private static void TranslateStatusMessages( object messageDisplay )
   {
      if( messageDisplay == null ) return;

      var field = messageDisplay.GetType().GetField( "_statusMessages", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( field?.GetValue( messageDisplay ) is not IList values ) return;

      for( var i = 0; i < values.Count; i++ )
      {
         if( values[ i ] is not string value ) continue;
         values[ i ] = TranslateStatusText( value );
      }
   }

   private static string ReplaceStatusToken( string value, string token, string replacement )
   {
      return value.Contains( token, StringComparison.Ordinal )
         ? value.Replace( token, replacement )
         : value;
   }

   private static string TranslateReadyToProceedText( string value )
   {
      const string suffix = ", ready to proceed";
      if( string.Equals( value, "Ready to proceed", StringComparison.Ordinal ) )
      {
         return "准备继续";
      }

      return value.EndsWith( suffix, StringComparison.Ordinal )
         ? value.Substring( 0, value.Length - suffix.Length ) + "，准备继续"
         : value;
   }

   private static string TranslateHailsText( string value )
   {
      const string separator = " hails ";
      var separatorIndex = value.IndexOf( separator, StringComparison.Ordinal );
      if( separatorIndex < 0 ) return value;

      return value.Substring( 0, separatorIndex ) + " 呼叫 " + value.Substring( separatorIndex + separator.Length );
   }

   private static string TranslateConversationResidualText( string value )
   {
      value = ReplaceExactToken( value, "K-Leg: Port Azikiwe", "K-Leg：阿齐基韦港" );
      value = ReplaceExactToken( value, "Port Azikiwe", "阿齐基韦港" );
      value = ReplaceExactToken( value, "阿齐基韦港 这里是", "阿齐基韦港，这里是" );
      value = ReplaceExactToken( value, "RCS maneuver", "姿控机动" );
      value = ReplaceExactToken( value, "RCS maneuvers", "姿控机动" );
      return value;
   }

   private static string ReplaceExactToken( string value, string source, string replacement )
   {
      return value.Contains( source, StringComparison.Ordinal )
         ? value.Replace( source, replacement )
         : value;
   }
}

internal static class ReservesRuntimeTranslationHelper
{
   public static string TranslateFuelText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = ReplaceToken( value, "DELTA-V: ", "速度增量：" );
      translated = ReplaceToken( translated, "FUEL: ", "燃料：" );
      translated = ReplaceToken( translated, "LOW", "低" );
      return string.Equals( translated, value, StringComparison.Ordinal )
         ? RuntimeTextHookHelper.TranslateTextValue( value, "NavModReserves.UpdateUI.txtFuel" )
         : translated;
   }

   private static string ReplaceToken( string value, string token, string replacement )
   {
      var index = value.IndexOf( token, StringComparison.Ordinal );
      if( index < 0 ) return value;

      return value.Substring( 0, index ) + replacement + value.Substring( index + token.Length );
   }
}

internal static class MooringRuntimeTranslationHelper
{
   public static string TranslateTargetStatusText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = ReplaceToken( value, ">VALID<", ">有效<" );
      translated = ReplaceToken( translated, ">INVALID<", ">无效<" );

      return string.Equals( translated, value, StringComparison.Ordinal )
         ? RuntimeTextHookHelper.TranslateTextValue( value, "NavModMooringControl.UpdateText.txtTargetStatus" )
         : translated;
   }

   private static string ReplaceToken( string value, string token, string replacement )
   {
      var index = value.IndexOf( token, StringComparison.Ordinal );
      if( index < 0 ) return value;

      return value.Substring( 0, index ) + replacement + value.Substring( index + token.Length );
   }
}

internal static class DockingRuntimeTranslationHelper
{
   public static string TranslateZoomRangeText( string value )
   {
      return value.StartsWith( "ZOOM RANGE: ", StringComparison.Ordinal )
         ? "缩放范围：" + value.Substring( "ZOOM RANGE: ".Length )
         : value;
   }

   public static string TranslateDockTelemetryText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      if( string.Equals( value, "No Target Selected", StringComparison.Ordinal ) )
      {
         return "未选择目标";
      }

      var translated = value;
      translated = ReplaceLinePrefix( translated, "RNG ", "距离 " );
      translated = ReplaceLinePrefix( translated, "ETA ", "预计到达 " );
      translated = ReplaceLinePrefix( translated, "VCRS ", "横向速度 " );
      translated = ReplaceLinePrefix( translated, "BRG ", "方位 " );
      return translated;
   }

   private static string ReplaceLinePrefix( string value, string sourcePrefix, string replacementPrefix )
   {
      if( value.StartsWith( sourcePrefix, StringComparison.Ordinal ) )
      {
         value = replacementPrefix + value.Substring( sourcePrefix.Length );
      }

      var token = "\n" + sourcePrefix;
      var replacement = "\n" + replacementPrefix;
      var index = value.IndexOf( token, StringComparison.Ordinal );
      while( index >= 0 )
      {
         value = value.Substring( 0, index ) + replacement + value.Substring( index + token.Length );
         index = value.IndexOf( token, index + replacement.Length, StringComparison.Ordinal );
      }

      return value;
   }
}

[HarmonyPatch]
internal static class DataHandler_GetString_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "DataHandler" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "DataHandler" ), "GetString", new[] { typeof( string ), typeof( bool ) } );
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "DataHandler.GetString" );
   }
}

[HarmonyPatch]
internal static class GrammarUtils_GetInflectedString_ConditionOwner_Hook
{
   private static MethodBase _targetMethod;

   private static bool Prepare()
   {
      _targetMethod = ResolveTargetMethod();
      return _targetMethod != null;
   }

   private static MethodBase TargetMethod()
   {
      return _targetMethod ?? ResolveTargetMethod();
   }

   private static MethodBase ResolveTargetMethod()
   {
      var grammarUtilsType = GameTypeResolver.Get( "GrammarUtils" );
      if( grammarUtilsType == null ) return null;

      foreach( var method in grammarUtilsType.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ) )
      {
         if( !string.Equals( method.Name, "GetInflectedString", StringComparison.Ordinal ) ) continue;

         var parameters = method.GetParameters();
         if( parameters.Length != 3 ) continue;
         if( parameters[ 0 ].ParameterType != typeof( string ) ) continue;
         if( !string.Equals( parameters[ 1 ].ParameterType.Name, "Condition", StringComparison.Ordinal ) ) continue;
         if( !string.Equals( parameters[ 2 ].ParameterType.Name, "CondOwner", StringComparison.Ordinal ) ) continue;

         return method;
      }

      return null;
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "GrammarUtils.GetInflectedString(string,Condition,CondOwner)" );
   }
}

[HarmonyPatch]
internal static class GrammarUtils_GetInflectedString_Object_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GrammarUtils" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GrammarUtils" ), "GetInflectedString", new[] { typeof( string ), typeof( object ) } );
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "GrammarUtils.GetInflectedString(string,object)" );
   }
}

[HarmonyPatch]
internal static class GrammarUtils_GetInflectedString_Interaction_Hook
{
   private static MethodBase _targetMethod;

   private static bool Prepare()
   {
      _targetMethod = AccessTools.Method(
         GameTypeResolver.Get( "GrammarUtils" ),
         "GetInflectedString",
         new[] { typeof( string ), GameTypeResolver.Get( "Interaction" ) } );
      return _targetMethod != null;
   }

   private static MethodBase TargetMethod()
   {
      return _targetMethod;
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "GrammarUtils.GetInflectedString(string,Interaction)" );
   }
}

[HarmonyPatch]
internal static class CondOwner_LogMessage_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "CondOwner" ), "LogMessage", new[] { typeof( string ), typeof( string ), typeof( string ), typeof( string ) } );
   }

   private static void Prefix( ref string strMsg, ref string strShort )
   {
      strMsg = LogMessageRuntimeTranslationHelper.TranslateLogMarkup( strMsg, "CondOwner.LogMessage" );
      strMsg = LogMessageRuntimeTranslationHelper.TranslateMessage( strMsg );
      if( !string.IsNullOrEmpty( strShort ) )
      {
         strShort = LogMessageRuntimeTranslationHelper.TranslateLogMarkup( strShort, "CondOwner.LogMessage.short" );
      }
   }
}

[HarmonyPatch]
internal static class Ship_LogAdd_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ship" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ship" ), "LogAdd", new[] { typeof( string ), typeof( double ), typeof( bool ) } );
   }

   private static void Prefix( ref string strEntry )
   {
      strEntry = OstranautsTranslatorPlugin.Translate( strEntry, "Ship.LogAdd" );
   }
}

[HarmonyPatch]
internal static class CondOwner_GetMessageLog_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "CondOwner" ), "GetMessageLog", new[] { typeof( int ) } );
   }

   private static void Postfix( ref string __result )
   {
      if( string.IsNullOrWhiteSpace( __result ) ) return;

      __result = LogMessageRuntimeTranslationHelper.TranslateLogMarkup( __result, "CondOwner.GetMessageLog" );
   }
}

[HarmonyPatch]
internal static class GUISocialCombat2_UpdateCO_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUISocialCombat2" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUISocialCombat2" ), "UpdateCO", new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged(
         __instance,
         "txtMessageLog",
         value => LogMessageRuntimeTranslationHelper.TranslateLogMarkup( value, "GUISocialCombat2.UpdateCO.txtMessageLog" ) );

      SocialCombatRuntimeTranslationHelper.TranslateFixedTexts( __instance, "GUISocialCombat2.UpdateCO.Fixed" );
   }
}

[HarmonyPatch]
internal static class GUISocialCombat2_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUISocialCombat2" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null
         && GameTypeResolver.Get( "Interaction" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var socialCombatType = GameTypeResolver.Get( "GUISocialCombat2" );
      var condOwnerType = GameTypeResolver.Get( "CondOwner" );
      var interactionType = GameTypeResolver.Get( "Interaction" );
      if( socialCombatType == null || condOwnerType == null || interactionType == null ) return null;

      var interactionListType = typeof( List<> ).MakeGenericType( interactionType );
      return AccessTools.Method( socialCombatType, "SetData", new[] { condOwnerType, condOwnerType, typeof( bool ), interactionListType } );
   }

   private static void Postfix( object __instance )
   {
      SocialCombatRuntimeTranslationHelper.TranslateFixedTexts( __instance, "GUISocialCombat2.SetData.Fixed" );
   }
}

internal static class SocialCombatRuntimeTranslationHelper
{
   private static readonly HashSet<string> LoggedDiagnosticRoots = new HashSet<string>( StringComparer.Ordinal );
   private static readonly Dictionary<string, string> ActionsPanelTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Manual" ] = "操作",
      [ "手动" ] = "操作",
      [ "操作" ] = "操作",
      [ "Actions" ] = "操作",
      [ "Action" ] = "操作",
      [ "行动" ] = "操作"
   };

   private static readonly Dictionary<string, string> PreviewPanelTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Preview" ] = "预览",
      [ "Review" ] = "检视",
      [ "预习" ] = "检视"
   };

   private static readonly HashSet<string> ConfirmTokens = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
   {
      "Confirm",
      "确认",
      "确定",
      "Accept",
      "接受"
   };

   private static readonly HashSet<string> ExitTokens = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
   {
      "Exit",
      "退出",
      "Cancel",
      "取消",
      "Close",
      "关闭",
      "确认",
      "确定"
   };

   public static void TranslateFixedTexts( object socialCombat, string hookName )
   {
      var root = RuntimeTextHookHelper.GetGameObject( socialCombat );
      if( root == null ) return;

      NormalizePanelText( root, "pnlActions", hookName + ".Actions", ActionsPanelTextMap );
      NormalizePanelText( root, "pnlPreview", hookName + ".Preview", PreviewPanelTextMap );
      SetButtonText( root, "pnlConfirm", "确认" );
      SetButtonText( root, "pnlExit", "退出" );
      NormalizeKnownRootTexts( root, hookName + ".Root" );
      LogDiagnosticsOnce( root, hookName );
   }

   private static void NormalizeKnownRootTexts( UnityEngine.GameObject root, string hookName )
   {
      foreach( var component in EnumerateTextComponents( root ) )
      {
         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null ) continue;

         var value = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( value ) ) continue;

         var path = GetComponentPath( component );
         var translated = TranslateKnownComponentText( path, value, hookName );
         if( !string.Equals( translated, value, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( component, translated );
         }
      }
   }

   private static void NormalizePanelText( UnityEngine.GameObject root, string panelPath, string hookName, IReadOnlyDictionary<string, string> exactTextMap )
   {
      var panel = FindChildGameObject( root, panelPath );
      if( panel == null ) return;

      foreach( var component in EnumerateTextComponents( panel ) )
      {
         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null ) continue;

         var value = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( value ) ) continue;

         var translated = TranslateFixedText( value, hookName, exactTextMap );
         if( !string.Equals( translated, value, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( component, translated );
         }
      }
   }

   private static string TranslateFixedText( string value, string hookName, IReadOnlyDictionary<string, string> exactTextMap )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var trimmed = value.Trim();
      if( exactTextMap.TryGetValue( trimmed, out var exactText ) )
      {
         return exactText;
      }

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      var normalized = translated?.Trim();
      if( !string.IsNullOrWhiteSpace( normalized ) && exactTextMap.TryGetValue( normalized, out var normalizedText ) )
      {
         return normalizedText;
      }

      return translated;
   }

   private static string TranslateKnownComponentText( string path, string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var trimmed = value.Trim();
      if( IsExitPath( path ) && ( ExitTokens.Contains( trimmed ) || ConfirmTokens.Contains( trimmed ) ) )
      {
         return "退出";
      }

      if( IsConfirmPath( path ) && ( ConfirmTokens.Contains( trimmed ) || ExitTokens.Contains( trimmed ) ) )
      {
         return "确认";
      }

      if( PreviewPanelTextMap.TryGetValue( trimmed, out var previewText ) )
      {
         return previewText;
      }

      if( ActionsPanelTextMap.TryGetValue( trimmed, out var actionText ) )
      {
         return actionText;
      }

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      var normalized = translated?.Trim();
      if( string.IsNullOrWhiteSpace( normalized ) )
      {
         return translated;
      }

      if( IsExitPath( path ) && ( ExitTokens.Contains( normalized ) || ConfirmTokens.Contains( normalized ) ) )
      {
         return "退出";
      }

      if( IsConfirmPath( path ) && ( ConfirmTokens.Contains( normalized ) || ExitTokens.Contains( normalized ) ) )
      {
         return "确认";
      }

      if( PreviewPanelTextMap.TryGetValue( normalized, out previewText ) )
      {
         return previewText;
      }

      if( ActionsPanelTextMap.TryGetValue( normalized, out actionText ) )
      {
         return actionText;
      }

      return translated;
   }

   private static void SetButtonText( UnityEngine.GameObject root, string buttonPath, string value )
   {
      var button = FindChildGameObject( root, buttonPath );
      if( button == null ) return;

      foreach( var component in EnumerateTextComponents( button ) )
      {
         RuntimeHookTranslationHelper.SetTextComponentProperty( component, value );
      }
   }

   private static IEnumerable EnumerateTextComponents( UnityEngine.GameObject root )
   {
      if( root == null ) yield break;

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) yield break;

      foreach( var component in components )
      {
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) continue;

         yield return component;
      }
   }

   private static bool IsConfirmPath( string path )
   {
      return !string.IsNullOrWhiteSpace( path )
         && path.IndexOf( "/pnlConfirm", StringComparison.OrdinalIgnoreCase ) >= 0;
   }

   private static bool IsExitPath( string path )
   {
      return !string.IsNullOrWhiteSpace( path )
         && path.IndexOf( "/pnlExit", StringComparison.OrdinalIgnoreCase ) >= 0;
   }

   private static void LogDiagnosticsOnce( UnityEngine.GameObject root, string hookName )
   {
      var instanceKey = RuntimeHelpers.GetHashCode( root ) + "|SocialCombat";
      if( !LoggedDiagnosticRoots.Add( instanceKey ) ) return;

      var lines = new List<string>();
      foreach( var component in EnumerateAllComponents( root ) )
      {
         if( component == null ) continue;

         var path = GetComponentPath( component );
         if( string.IsNullOrWhiteSpace( path ) ) continue;
         if( !IsDiagnosticTargetPath( path ) ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty?.GetValue( component ) is string text && !string.IsNullOrWhiteSpace( text ) )
         {
            lines.Add( $"text {component.GetType().Name} {path} => {SanitizeDiagnosticValue( text )}" );
         }

         var spriteProperty = RuntimeHookTranslationHelper.GetProperty( component.GetType(), "sprite" );
         var sprite = spriteProperty?.GetValue( component );
         if( sprite != null )
         {
            lines.Add( $"sprite {component.GetType().Name} {path} => {GetUnityObjectName( sprite )}" );
         }

         if( lines.Count >= 120 ) break;
      }

      OstranautsTranslatorPlugin.LogDiagnostic( $"Visible panel SocialCombat from {hookName}: {GetComponentPath( root )}" );
      foreach( var line in lines )
      {
         OstranautsTranslatorPlugin.LogDiagnostic( line );
      }
      OstranautsTranslatorPlugin.LogDiagnostic( $"Visible panel SocialCombat diagnostic lines: {lines.Count}" );
   }

   private static bool IsDiagnosticTargetPath( string path )
   {
      return path.IndexOf( "/pnlActions/", StringComparison.OrdinalIgnoreCase ) >= 0
         || path.IndexOf( "/pnlPreview/", StringComparison.OrdinalIgnoreCase ) >= 0
         || path.IndexOf( "/pnlConfirm/", StringComparison.OrdinalIgnoreCase ) >= 0
         || path.IndexOf( "/pnlExit/", StringComparison.OrdinalIgnoreCase ) >= 0;
   }

   private static IEnumerable EnumerateAllComponents( UnityEngine.GameObject root )
   {
      if( root == null ) yield break;

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) yield break;

      foreach( var component in components )
      {
         if( component != null ) yield return component;
      }
   }

   private static string GetComponentPath( object component )
   {
      var gameObject = component as UnityEngine.GameObject ?? RuntimeTextHookHelper.GetGameObject( component );
      if( gameObject == null ) return null;

      var names = new List<string>();
      var current = gameObject;
      while( current != null )
      {
         names.Add( GetUnityObjectName( current ) );
         var parentTransform = RuntimeTextHookHelper.GetParentTransform( current );
         current = RuntimeTextHookHelper.GetGameObject( parentTransform );
      }

      names.Reverse();
      return string.Join( "/", names.ToArray() );
   }

   private static string GetUnityObjectName( object value )
   {
      if( value == null ) return string.Empty;

      var nameProperty = RuntimeHookTranslationHelper.GetProperty( value.GetType(), "name" );
      return nameProperty?.GetValue( value ) as string ?? value.GetType().Name;
   }

   private static string SanitizeDiagnosticValue( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return string.Empty;

      var normalized = value.Replace( "\r", "\\r" ).Replace( "\n", "\\n" );
      return normalized.Length <= 200 ? normalized : normalized.Substring( 0, 200 ) + "...";
   }

   private static UnityEngine.GameObject FindChildGameObject( UnityEngine.GameObject root, string path )
   {
      if( root == null || string.IsNullOrWhiteSpace( path ) ) return null;

      var transformProperty = typeof( UnityEngine.GameObject ).GetProperty( "transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var transform = transformProperty?.GetValue( root, null );
      if( transform == null ) return null;

      var findMethod = transform.GetType().GetMethod( "Find", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof( string ) }, null );
      var childTransform = findMethod?.Invoke( transform, new object[] { path } );
      return RuntimeTextHookHelper.GetGameObject( childTransform );
   }
}

[HarmonyPatch]
internal static class GUITooltip_SetTooltipCrew_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null
         && RuntimeTypeResolver.FindLoadedType( "GUITooltip+TooltipWindow" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUITooltip" ),
         "SetTooltipCrew",
         new[] { GameTypeResolver.Get( "CondOwner" ), RuntimeTypeResolver.FindLoadedType( "GUITooltip+TooltipWindow" ) } );
   }

   private static void Postfix( object __instance, object __0 )
   {
      TooltipRuntimeTranslationHelper.TranslateCrewTooltip( __instance, __0, "GUITooltip.SetTooltipCrew" );
   }
}

[HarmonyPatch]
internal static class GUITooltip_Update_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUITooltip" ), "Update", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      if( __instance == null ) return;

      var window = RuntimeHookTranslationHelper.GetInstanceField( __instance.GetType(), "window" )?.GetValue( __instance );
      if( !string.Equals( window?.ToString(), "Crew", StringComparison.Ordinal ) ) return;

      var crewMember = RuntimeHookTranslationHelper.GetInstanceField( __instance.GetType(), "tooltipCO" )?.GetValue( __instance );
      if( crewMember == null ) return;

      TooltipRuntimeTranslationHelper.TranslateCrewTooltip( __instance, crewMember, "GUITooltip.Update" );
   }
}

[HarmonyPatch]
internal static class GUITooltip_TooltipTextFormat1_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip" ) != null && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUITooltip" ), "TooltipTextFormat1", new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __0, ref string __result )
   {
      __result = TooltipRuntimeTranslationHelper.TranslateCondOwnerTooltipText( __result, __0, "GUITooltip.TooltipTextFormat1" );
   }
}

[HarmonyPatch]
internal static class GUITooltip_TooltipTextFormat4_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip" ) != null && GameTypeResolver.Get( "Interaction" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUITooltip" ), "TooltipTextFormat4", new[] { GameTypeResolver.Get( "Interaction" ) } );
   }

   private static void Postfix( ref string __result )
   {
      __result = TooltipRuntimeTranslationHelper.TranslateInteractionTooltipText( __result, "GUITooltip.TooltipTextFormat4" );
   }
}

[HarmonyPatch]
internal static class GUIItemToolTip_SetCondOwner_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIItemToolTip" ) != null && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIItemToolTip" ), "SetCondOwner", new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance, object __0 )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "m_txtName", value => TooltipRuntimeTranslationHelper.TranslateCondOwnerDisplayName( value, __0, "GUIItemToolTip.SetCondOwner.m_txtName" ) );
   }
}

[HarmonyPatch]
internal static class MegaToolTip_ItemModule_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.ItemModule" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.ItemModule" ), "SetData", new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance, object __0 )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "_txtFullName", value => TooltipRuntimeTranslationHelper.TranslateCondOwnerDisplayName( value, __0, "MegaToolTip.ItemModule.SetData._txtFullName" ) );
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "_txtDescription", value => MegaToolTipRuntimeTranslationHelper.TranslateItemDescription( value, __0, "MegaToolTip.ItemModule.SetData._txtDescription" ) );
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( RuntimeTextHookHelper.GetGameObject( __instance ), "MegaToolTip.ItemModule.SetData" );
   }
}

[HarmonyPatch]
internal static class MegaToolTip_PersonModule_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.PersonModule" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.PersonModule" ),
         "SetData",
         new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged(
         __instance,
         "_txtDescription",
         value => PdaRuntimeTranslationHelper.TranslatePersonModuleDescription( value, "MegaToolTip.PersonModule.SetData._txtDescription" ) );
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( RuntimeTextHookHelper.GetGameObject( __instance ), "MegaToolTip.PersonModule.SetData" );
   }
}

[HarmonyPatch]
internal static class MegaToolTip_PersonModule_OnUpdateUI_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.PersonModule" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.PersonModule" ), "OnUpdateUI", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged(
         __instance,
         "_txtDescription",
         value => PdaRuntimeTranslationHelper.TranslatePersonModuleDescription( value, "MegaToolTip.PersonModule.OnUpdateUI._txtDescription" ) );
   }
}

[HarmonyPatch]
internal static class TooltipPreviewButton_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.TooltipPreviewButton" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.TooltipPreviewButton" ), "SetData", new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance, object __0 )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "_txtCOName", value => TooltipRuntimeTranslationHelper.TranslateCondOwnerDisplayName( value, __0, "TooltipPreviewButton.SetData._txtCOName" ) );
   }
}

[HarmonyPatch]
internal static class MegaToolTip_StatusbarModule_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.StatusbarModule" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.MegaToolTip.DataModules.StatusbarModule" ), "SetData", new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( RuntimeTextHookHelper.GetGameObject( __instance ), "MegaToolTip.StatusbarModule.SetData" );
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "_txtPower", value => RuntimeTextHookHelper.TranslateTextValue( value, "MegaToolTip.StatusbarModule.SetData._txtPower" ) );
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "_txtPowerRate", value => RuntimeTextHookHelper.TranslateTextValue( value, "MegaToolTip.StatusbarModule.SetData._txtPowerRate" ) );
   }
}

[HarmonyPatch]
internal static class GUITooltip2_SetToolTip_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip2" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUITooltip2" ), "SetToolTip", new[] { typeof( string ), typeof( string ), typeof( bool ), typeof( bool ) } );
   }

   private static void Prefix( ref string strTitle, ref string strBody )
   {
      strTitle = MegaToolTipRuntimeTranslationHelper.TranslateTooltipTitle( strTitle, "GUITooltip2.SetToolTip.title" );
      strBody = MegaToolTipRuntimeTranslationHelper.TranslateTooltipBody( strBody, "GUITooltip2.SetToolTip.body" );
   }
}

[HarmonyPatch]
internal static class GUITooltip2_SetToolTip_1_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip2" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUITooltip2" ), "SetToolTip_1", new[] { typeof( string ), typeof( string ), typeof( string ), typeof( bool ) } );
   }

   private static void Prefix( ref string strSubtitle, ref string strTitle, ref string strBody )
   {
      strSubtitle = OstranautsTranslatorPlugin.Translate( strSubtitle, "GUITooltip2.SetToolTip_1.subtitle" );
      strTitle = MegaToolTipRuntimeTranslationHelper.TranslateTooltipTitle( strTitle, "GUITooltip2.SetToolTip_1.title" );
      strBody = MegaToolTipRuntimeTranslationHelper.TranslateTooltipBody( strBody, "GUITooltip2.SetToolTip_1.body" );
   }
}

[HarmonyPatch]
internal static class GUIMFDDisplay_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.MFD.GUIMFDDisplay" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.MFD.GUIMFDDisplay" ), "Awake" );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIMFDDisplay.Awake" );
   }
}

[HarmonyPatch]
internal static class GUIOrbitDraw_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIOrbitDraw" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIOrbitDraw" ), "Awake", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIOrbitDraw.Awake" );
      NavStationRuntimeTranslationHelper.TranslateNavStationUi( "GUIOrbitDraw.Awake.goLines" );
   }
}

[HarmonyPatch]
internal static class GUIOrbitDraw_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIOrbitDraw" ) != null && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUIOrbitDraw" ),
         "Init",
         new[] { GameTypeResolver.Get( "CondOwner" ), typeof( Dictionary<string, string> ), typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIOrbitDraw.Init" );
      NavStationRuntimeTranslationHelper.TranslateNavStationUi( "GUIOrbitDraw.Init.goLines" );
   }
}

[HarmonyPatch]
internal static class GUIOrbitDraw_UpdateUIs_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIOrbitDraw" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIOrbitDraw" ), "UpdateUIs", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "txtRange", DockingRuntimeTranslationHelper.TranslateZoomRangeText );
   }
}

[HarmonyPatch]
internal static class GUIMFDDisplay_ShowMenu_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.MFD.GUIMFDDisplay" ) != null
         && GameTypeResolver.Get( "Ostranauts.Events.DTOs.MFDDTO" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.MFD.GUIMFDDisplay" ),
         "ShowMenu",
         new[] { typeof( string ), GameTypeResolver.Get( "Ostranauts.Events.DTOs.MFDDTO" ) } );
   }

   private static void Prefix( string id, object mfdDto )
   {
      if( mfdDto == null ) return;

      RuntimeHookTranslationHelper.TranslateStringField( mfdDto, "Title", MfdRuntimeTranslationHelper.TranslateDisplayText );
      RuntimeHookTranslationHelper.TranslateStringList( mfdDto, "Left", MfdRuntimeTranslationHelper.TranslateDisplayText );
      RuntimeHookTranslationHelper.TranslateStringList( mfdDto, "Right", MfdRuntimeTranslationHelper.TranslateDisplayText );
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "LeftPanelData", MfdRuntimeTranslationHelper.TranslateDisplayText );
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "RightPanelData", MfdRuntimeTranslationHelper.TranslateDisplayText );
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "TopPanelData", MfdRuntimeTranslationHelper.TranslateDisplayText );
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "BottomPanelData", MfdRuntimeTranslationHelper.TranslateDisplayText );
   }
}

[HarmonyPatch]
internal static class Objective_Constructors_Hook
{
   private static IEnumerable<MethodBase> TargetMethods()
   {
      var objectiveType = GameTypeResolver.Get( "Ostranauts.Objectives.Objective" );
      var condOwnerType = GameTypeResolver.Get( "CondOwner" );
      var jsonPlotSaveType = GameTypeResolver.Get( "JsonPlotSave" );
      if( objectiveType == null ) yield break;

      var constructor = objectiveType.GetConstructor( new[] { condOwnerType, typeof( string ), typeof( string ) } );
      if( constructor != null ) yield return constructor;

      constructor = objectiveType.GetConstructor( new[] { jsonPlotSaveType } );
      if( constructor != null ) yield return constructor;

      constructor = objectiveType.GetConstructor( new[] { condOwnerType, typeof( string ), typeof( string ), typeof( string ) } );
      if( constructor != null ) yield return constructor;
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateStringField( __instance, "strDisplayName", "Objective.ctor" );
      RuntimeHookTranslationHelper.TranslateStringField( __instance, "strDisplayDesc", "Objective.ctor" );
      RuntimeHookTranslationHelper.TranslateStringField( __instance, "strDisplayDescComplete", "Objective.ctor" );
   }
}

[HarmonyPatch]
internal static class AlarmObjective_Constructors_Hook
{
   private static IEnumerable<MethodBase> TargetMethods()
   {
      var alarmObjectiveType = GameTypeResolver.Get( "Ostranauts.Objectives.AlarmObjective" );
      var condOwnerType = GameTypeResolver.Get( "CondOwner" );
      var alarmType = GameTypeResolver.Get( "Ostranauts.Objectives.AlarmType" );
      if( alarmObjectiveType == null || condOwnerType == null || alarmType == null ) yield break;

      var signatures = new[]
      {
         new[] { alarmType, condOwnerType, typeof( string ) },
         new[] { alarmType, condOwnerType, typeof( string ), typeof( string ) },
         new[] { alarmType, condOwnerType, typeof( string ), typeof( string ), typeof( string ), typeof( string ) },
         new[] { alarmType, condOwnerType, typeof( string ), typeof( string ), typeof( bool ), typeof( string ) },
      };

      foreach( var signature in signatures )
      {
         var constructor = alarmObjectiveType.GetConstructor( signature );
         if( constructor != null ) yield return constructor;
      }
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateStringField( __instance, "strDisplayName", "AlarmObjective.ctor" );
      RuntimeHookTranslationHelper.TranslateStringField( __instance, "strDisplayDesc", "AlarmObjective.ctor" );
      RuntimeHookTranslationHelper.TranslateStringField( __instance, "strDisplayDescComplete", "AlarmObjective.ctor" );
   }
}

[HarmonyPatch]
internal static class TutorialObjectiveRuntimeTranslationHelper
{
   private static readonly Regex ToggleLightSwitchDescriptionRegex = new Regex(
      @"^Press\s+(?<glyph>.+?)\s+on the nearby Power Switch\.\s+Select\s+.+?\s+to turn on the lights\.$",
      RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase );

   public static void TranslateObjectivePanelText( object objectivePanel, string hookName )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentField( objectivePanel, "_txtTitle", value => TranslateObjectiveTitle( value, hookName + "._txtTitle" ) );
      RuntimeHookTranslationHelper.TranslateTextComponentField( objectivePanel, "_txtDescription", value => TranslateObjectiveDescription( value, hookName + "._txtDescription" ) );
   }

   private static string TranslateObjectiveTitle( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      return value switch
      {
         "Toggle the Switch" => "拨动开关",
         "Lights On." => "照明已开启。",
         _ => RuntimeTextHookHelper.TranslateTextValue( value, hookName ),
      };
   }

   private static string TranslateObjectiveDescription( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var toggleLightSwitchMatch = ToggleLightSwitchDescriptionRegex.Match( value );
      if( toggleLightSwitchMatch.Success )
      {
         var glyphs = toggleLightSwitchMatch.Groups[ "glyph" ].Value;
         return "按 " + glyphs + " 点击附近的电源开关。选择“切换电力”以打开照明。";
      }

      return RuntimeTextHookHelper.TranslateTextValue( value, hookName );
   }
}

[HarmonyPatch]
internal static class ObjectivePanel_CompleteObjective_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivePanel" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivePanel" ), "CompleteObjective", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      TutorialObjectiveRuntimeTranslationHelper.TranslateObjectivePanelText( __instance, "ObjectivePanel.CompleteObjective" );
   }
}

[HarmonyPatch]
internal static class ObjectivePanel_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivePanel" ) != null
         && GameTypeResolver.Get( "Ostranauts.Objectives.Objective" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivePanel" ),
         "SetData",
         new[] { GameTypeResolver.Get( "Ostranauts.Objectives.Objective" ), typeof( bool ) } );
   }

   private static void Postfix( object __instance )
   {
      TutorialObjectiveRuntimeTranslationHelper.TranslateObjectivePanelText( __instance, "ObjectivePanel.SetData" );
   }
}

[HarmonyPatch]
internal static class ObjectivePanel_RefreshText_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivePanel" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivePanel" ), "RefreshText", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      TutorialObjectiveRuntimeTranslationHelper.TranslateObjectivePanelText( __instance, "ObjectivePanel.RefreshText" );
   }
}

[HarmonyPatch]
internal static class LoadingScreen_SetProgressBar_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.LoadingScreen" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.LoadingScreen" ), "SetProgressBar", new[] { typeof( float ), typeof( string ) } );
   }

   private static void Prefix( ref string textToDisplay )
   {
      textToDisplay = LoadingScreenRuntimeTranslationHelper.TranslateProgressText( textToDisplay );
   }
}

[HarmonyPatch]
internal static class LoadingScreen_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.LoadingScreen" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.LoadingScreen" ), "Awake", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "LoadingScreen.Awake" );
   }
}

[HarmonyPatch]
internal static class LoadBackground_AssignBackground_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "LoadBackground" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "LoadBackground" ), "AssignBackground", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtAttribution", value => SaveLoadRuntimeTranslationHelper.TranslateCompositeLabel( value, "LoadBackground.AssignBackground.txtAttribution" ) );
   }
}

[HarmonyPatch]
internal static class LoadTip_AssignTip_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "LoadTip" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "LoadTip" ), "AssignTip", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtTip", value => RuntimeTextHookHelper.TranslateTextValue( value, "LoadTip.AssignTip.txtTip" ) );
   }
}

[HarmonyPatch]
internal static class GUILoadMenu_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.Loading.GUILoadMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.Loading.GUILoadMenu" ), "Awake" );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUILoadMenu.Awake" );
   }
}

[HarmonyPatch]
internal static class GUISaveMenu_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.Loading.GUISaveMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.Loading.GUISaveMenu" ), "Awake" );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUISaveMenu.Awake" );
   }
}

[HarmonyPatch]
internal static class GUILoadMenu_CreateSaveWarning_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.Loading.GUILoadMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.Loading.GUILoadMenu" ), "CreateSaveWarning", Type.EmptyTypes );
   }

   private static void Postfix( ref string __result )
   {
      __result = SaveLoadRuntimeTranslationHelper.TranslateSaveWarning( __result );
   }
}

[HarmonyPatch]
internal static class GUISaveLoadBase_GetAvailableSpaceWarning_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.Loading.GUISaveLoadBase" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.Loading.GUISaveLoadBase" ), "GetAvailableSpaceWarning", Type.EmptyTypes );
   }

   private static void Postfix( ref string __result )
   {
      __result = SaveLoadRuntimeTranslationHelper.TranslateAvailableSpaceWarning( __result );
   }
}

[HarmonyPatch]
internal static class GUIConfirmationDialogue_Setup_Hook
{
   private static IEnumerable<MethodBase> TargetMethods()
   {
      var confirmationType = GameTypeResolver.Get( "Ostranauts.UI.Loading.GUIConfirmationDialogue" );
      if( confirmationType == null ) yield break;

      foreach( var method in confirmationType.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
      {
         if( !string.Equals( method.Name, "Setup", StringComparison.Ordinal ) ) continue;

         var parameters = method.GetParameters();
         if( parameters.Length is 2 or 5 or 6 && parameters[ 0 ].ParameterType == typeof( string ) )
         {
            yield return method;
         }
      }
   }

   private static void Prefix( ref string text )
   {
      text = SaveLoadRuntimeTranslationHelper.TranslateConfirmationText( text );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "txtDescription", "GUIConfirmationDialogue.Setup" );
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIConfirmationDialogue.Setup" );
   }
}

[HarmonyPatch]
internal static class LoadListEntry_Setup_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.Loading.LoadListEntry" ) != null
         && GameTypeResolver.Get( "Ostranauts.Core.Models.SaveInfo" ) != null
         && GameTypeResolver.Get( "Ostranauts.UI.Loading.GUISaveLoadEntryMode" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.UI.Loading.LoadListEntry" ),
         "Setup",
         new[]
         {
            GameTypeResolver.Get( "Ostranauts.Core.Models.SaveInfo" ),
            GameTypeResolver.Get( "Ostranauts.UI.Loading.GUISaveLoadEntryMode" )
         } );
   }

   private static void Postfix( object __instance, object saveInfo )
   {
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "_txtPlayerName", SaveLoadRuntimeTranslationHelper.TranslateSaveInfoPlayerLine( saveInfo ) );
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "_txtSaveName", SaveLoadRuntimeTranslationHelper.TranslateSaveName( saveInfo ) );
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "LoadListEntry.Setup" );
   }
}

[HarmonyPatch]
internal static class LoadManager_SaveGameData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.Core.LoadManager" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.Core.LoadManager" ), "SaveGameData", new[] { typeof( string ) } );
   }

   private static void Postfix( object __result )
   {
      SaveVersionRuntimeHelper.ApplyCurrentBuildToSaveDto( __result );
   }
}

[HarmonyPatch]
internal static class LoadManager_SaveGameInfo_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.Core.LoadManager" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.Core.LoadManager" ), "SaveGameInfo", new[] { typeof( string ), typeof( string ), typeof( int ) } );
   }

   private static void Postfix( object __result )
   {
      SaveVersionRuntimeHelper.ApplyCurrentBuildToSaveInfo( __result );
   }
}

[HarmonyPatch]
internal static class DataHandler_GetTip_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "DataHandler" ) != null && GameTypeResolver.Get( "JsonTip" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "DataHandler" ), "GetTip", Type.EmptyTypes );
   }

   private static void Postfix( object __result )
   {
      RuntimeHookTranslationHelper.TranslateStringField( __result, "strBody", value => RuntimeTextHookHelper.TranslateTextValue( value, "DataHandler.GetTip.strBody" ) );
   }
}

[HarmonyPatch]
internal static class GUIPAXIntro_Show_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPAXIntro" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPAXIntro" ), "Show", new[] { typeof( Action ) } );
   }

   private static void Postfix( object __instance )
   {
      LoadingIntroRuntimeTranslationHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPAXIntro.Show" );
   }
}

[HarmonyPatch]
internal static class GUICrewCard_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUICrewCard" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUICrewCard" ),
         "SetData",
         new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance, object co )
   {
      var translatedName = CrewBarRuntimeTranslationHelper.TranslateCrewDisplayName( co );
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "_txtName", translatedName );
      CrewBarRuntimeTranslationHelper.TranslateShiftIndicator( __instance, co, "GUICrewCard.SetData" );
      CrewBarRuntimeTranslationHelper.LogCrewCardDiagnostic( __instance, co, translatedName );
   }
}

[HarmonyPatch]
internal static class GUICrewStatus_UpdateCrewBar_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUICrewStatus" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUICrewStatus" ),
         "UpdateCrewBar",
         new[] { GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance, object co )
   {
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "_lblName", CrewBarRuntimeTranslationHelper.TranslateCrewDisplayName( co ) );
      CrewBarRuntimeTranslationHelper.TranslateShiftIndicator( __instance, co, "GUICrewStatus.UpdateCrewBar" );
   }
}

[HarmonyPatch]
internal static class CanvasManager_ShowCanvasQuit_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "CanvasManager" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "CanvasManager" ), "ShowCanvasQuit", Type.EmptyTypes );
   }

   private static void Postfix()
   {
      var canvasManagerType = GameTypeResolver.Get( "CanvasManager" );
      var instance = canvasManagerType?.GetField( "instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null );
      var canvasField = canvasManagerType?.GetField( "goCanvasQuit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( canvasField?.GetValue( instance ) is UnityEngine.GameObject canvasQuit )
      {
         RuntimeTextHookHelper.TranslateHierarchy( canvasQuit, "CanvasManager.ShowCanvasQuit" );
      }
   }
}

[HarmonyPatch]
internal static class CrewSim_Options_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "CrewSim" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "CrewSim" ), "Options", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      var canvasManagerType = GameTypeResolver.Get( "CanvasManager" );
      var canvasManager = GameTypeResolver.Get( "CrewSim" )?.GetField( "CanvasManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null );
      var canvasField = canvasManagerType?.GetField( "goCanvasQuit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( canvasField?.GetValue( canvasManager ) is not UnityEngine.GameObject canvasQuit ) return;

      var transformProperty = typeof( UnityEngine.GameObject ).GetProperty( "transform", BindingFlags.Instance | BindingFlags.Public );
      var transform = transformProperty?.GetValue( canvasQuit );
      var optionsTransform = transform?.GetType().GetMethod( "Find", new[] { typeof( string ) } )?.Invoke( transform, new object[] { "GUIQuit/prefabGUIOptions" } );
      var optionsGameObject = optionsTransform?.GetType().GetProperty( "gameObject", BindingFlags.Instance | BindingFlags.Public )?.GetValue( optionsTransform ) as UnityEngine.GameObject;
      if( optionsGameObject != null )
      {
         RuntimeTextHookHelper.TranslateHierarchy( optionsGameObject, "CrewSim.Options" );
         SettingsRuntimeTranslationHelper.ApplyOptionsUiOverrides( optionsGameObject );
      }
   }
}

[HarmonyPatch]
internal static class NavModMooringControl_UpdateText_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModMooringControl" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModMooringControl" ), "UpdateText", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtTargetStatus", MooringRuntimeTranslationHelper.TranslateTargetStatusText );

      var tetherField = __instance == null ? null : RuntimeHookTranslationHelper.GetInstanceField( __instance.GetType(), "goTether" );
      if( tetherField?.GetValue( __instance ) is UnityEngine.GameObject tetherGameObject )
      {
         RuntimeTextHookHelper.TranslateHierarchyIfChanged( tetherGameObject, "NavModMooringControl.goTether" );
      }
   }
}

[HarmonyPatch]
internal static class NavModTimeZoom_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModTimeZoom" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModTimeZoom" ), "Awake", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "NavModTimeZoom.Awake" );
      SetButtonLabel( __instance, "btnZoomPlanet", "行星" );
      SetButtonLabel( __instance, "btnZoomInner", "内圈" );
      SetButtonLabel( __instance, "btnZoomOuter", "外圈" );
   }

   private static void SetButtonLabel( object target, string fieldName, string translatedText )
   {
      var field = target?.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var button = field?.GetValue( target );
      if( button == null ) return;

      var gameObject = button.GetType().GetProperty( "gameObject", BindingFlags.Instance | BindingFlags.Public )?.GetValue( button ) as UnityEngine.GameObject;
      if( gameObject == null ) return;

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( gameObject, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( component == null ) continue;
         var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) continue;

         var currentValue = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( currentValue ) ) continue;

         textProperty.SetValue( component, translatedText );
      }
   }
}

[HarmonyPatch]
internal static class GUIDockSys_SetUI_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIDockSys" ) != null && GameTypeResolver.Get( "Ship" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIDockSys" ), "SetUI", new[] { typeof( bool ), GameTypeResolver.Get( "Ship" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtRNGETA", DockingRuntimeTranslationHelper.TranslateDockTelemetryText );
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtBRGVCRS", DockingRuntimeTranslationHelper.TranslateDockTelemetryText );
   }
}

[HarmonyPatch]
internal static class NavModReserves_UpdateUI_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModReserves" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModReserves" ), "UpdateUI", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtFuel", ReservesRuntimeTranslationHelper.TranslateFuelText );
   }
}

[HarmonyPatch]
internal static class MFDMainMenuSensors_BuildMenu_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.MFD.MFDMainMenuSensors" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.MFD.MFDMainMenuSensors" ), "BuildMenu", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateSidePanelList( __instance, "TopPanelData", value => MfdRuntimeTranslationHelper.TranslateDisplayText( value ) );
      RuntimeHookTranslationHelper.TranslateSidePanelList( __instance, "LeftPanelData", value => MfdRuntimeTranslationHelper.TranslateDisplayText( value ) );
      RuntimeHookTranslationHelper.TranslateSidePanelList( __instance, "BottomPanelData", value => MfdRuntimeTranslationHelper.TranslateDisplayText( value ) );
   }
}

[HarmonyPatch]
internal static class ShipInfo_Constructors_Hook
{
   private static IEnumerable<MethodBase> TargetMethods()
   {
      var shipInfoType = GameTypeResolver.Get( "Ostranauts.ShipGUIs.Utilities.ShipInfo" );
      var shipType = GameTypeResolver.Get( "Ship" );
      if( shipInfoType == null ) yield break;

      var shipConstructor = shipInfoType.GetConstructor( new[] { shipType, typeof( bool ) } );
      if( shipConstructor != null ) yield return shipConstructor;

      var gpmConstructor = shipInfoType.GetConstructor( new[] { typeof( string ), typeof( string ) } );
      if( gpmConstructor != null ) yield return gpmConstructor;
   }

   private static void Postfix( object __instance )
   {
      ShipRuntimeTranslationHelper.TranslateShipInfo( __instance, "ShipInfo.ctor" );
   }
}

[HarmonyPatch]
internal static class ShipInfo_RevealTargetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.Utilities.ShipInfo" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.Utilities.ShipInfo" ), "RevealTargetData", new[] { typeof( float ) } );
   }

   private static void Postfix( object __instance )
   {
      ShipRuntimeTranslationHelper.TranslateShipInfo( __instance, "ShipInfo.RevealTargetData" );
   }
}

[HarmonyPatch]
internal static class GUIData_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIData" ) != null && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUIData" ),
         "SetData",
         new[] { GameTypeResolver.Get( "CondOwner" ), typeof( Dictionary<string, string> ), typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      ShipRuntimeTranslationHelper.TranslateGuiFriendlyName( __instance, "GUIData.SetData" );
   }
}

[HarmonyPatch]
internal static class GUIXPDR_SetTextXPDR_Hook
{
   private static IEnumerable<MethodBase> TargetMethods()
   {
      var signatures = new[] { typeof( bool ), typeof( string ) };
      var guiXpdrType = GameTypeResolver.Get( "GUIXPDR" );
      var navModTransponderType = GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModTransponder" );

      var guiXpdrMethod = AccessTools.Method( guiXpdrType, "SetTextXPDR", signatures );
      if( guiXpdrMethod != null ) yield return guiXpdrMethod;

      var navModMethod = AccessTools.Method( navModTransponderType, "SetTextXPDR", signatures );
      if( navModMethod != null ) yield return navModMethod;
   }

   private static void Postfix( object __instance )
   {
      ShipRuntimeTranslationHelper.TranslateXpdrPanel( __instance, __instance.GetType().Name + ".SetTextXPDR" );
   }
}

[HarmonyPatch]
internal static class NavModTargetData_ApplyText_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModTargetData" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModTargetData" ), "ApplyText", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      ShipRuntimeTranslationHelper.TranslateTargetDataPanel( __instance, "NavModTargetData.ApplyText" );
   }
}

[HarmonyPatch]
internal static class CrewSim_Start_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "CrewSim" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "CrewSim" ), "Start", Type.EmptyTypes );
   }

   private static void Postfix()
   {
      CrewBarRuntimeTranslationHelper.TranslateCrewBarUi();
   }
}

[HarmonyPatch]
internal static class GUIOptions_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIOptions" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIOptions" ), "Init", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      SettingsRuntimeTranslationHelper.TranslateOptionsUi( __instance );
   }
}

[HarmonyPatch]
internal static class GUIOptions_State_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIOptions" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return GameTypeResolver.Get( "GUIOptions" )?.GetProperty( "State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetSetMethod( true );
   }

   private static void Postfix( object __instance, object value )
   {
      if( __instance == null || value == null ) return;

      var filesField = RuntimeHookTranslationHelper.GetInstanceField( __instance.GetType(), "cgFiles" );
      var filesCanvasGroup = filesField?.GetValue( __instance );
      if( filesCanvasGroup == null || !ReferenceEquals( value, filesCanvasGroup ) ) return;

      var root = RuntimeTextHookHelper.GetGameObject( __instance );
      if( root == null ) return;

      SettingsRuntimeTranslationHelper.ApplyOptionsUiOverrides( root );
   }
}

[HarmonyPatch]
internal static class GUIOptions_TurboB_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIOptions" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIOptions" ), "TurboB", new[] { typeof( bool ) } );
   }

   private static void Postfix( object __instance )
   {
      SettingsRuntimeTranslationHelper.TranslateTurboButtonUi( __instance );
   }
}

[HarmonyPatch]
internal static class GUIControls_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.InputControl.GUIControls" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return GameTypeResolver.Get( "Ostranauts.InputControl.GUIControls" )?
         .GetMethod( "Init", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
   }

   private static void Postfix( object __instance )
   {
      ControlsRuntimeTranslationHelper.TranslateControlsUi( __instance );
   }
}

[HarmonyPatch]
internal static class GUIControls_SetPage_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.InputControl.GUIControls" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.InputControl.GUIControls" ), "SetPage", new[] { typeof( int ) } );
   }

   private static void Postfix( object __instance )
   {
      ControlsRuntimeTranslationHelper.TranslateControlsPage( __instance );
   }
}

[HarmonyPatch]
internal static class GUIActionKey_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.InputControl.GUIActionKey" ) != null
         && GameTypeResolver.Get( "Ostranauts.InputControl.IInputCommand" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.InputControl.GUIActionKey" ),
         "Init",
         new[] { GameTypeResolver.Get( "Ostranauts.InputControl.IInputCommand" ), typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      ControlsRuntimeTranslationHelper.TranslateActionKey( __instance );
   }
}

[HarmonyPatch]
internal static class GUITimeBar_Start_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUITimeBar" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUITimeBar" ), "Start", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUITimeBar.Start" );
   }
}

[HarmonyPatch]
internal static class GUITimeBar_OnUpdatedTimeScale_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUITimeBar" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.UI.CrewBar.GUITimeBar" ), "OnUpdatedTimeScale", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUITimeBar.OnUpdatedTimeScale" );
   }
}

[HarmonyPatch]
internal static class NavModControlToggle_ToggleNavMode_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModControlToggle" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.NavModControlToggle" ), "ToggleNavMode", new[] { typeof( bool ) } );
   }

   private static void Postfix()
   {
      NavStationRuntimeTranslationHelper.TranslateNavStationUi( "NavModControlToggle.ToggleNavMode" );
      NavStationRuntimeTranslationHelper.TranslateNavModeLabel();
   }
}

internal static class InfoRuntimeTranslationHelper
{
   public static void TranslateNodeData( object info, string hookName )
   {
      if( info == null ) return;

      var mapNodes = RuntimeHookTranslationHelper.GetInstanceField( info.GetType(), "mapNodes" )?.GetValue( info ) as IDictionary;
      if( mapNodes == null ) return;

      foreach( DictionaryEntry entry in mapNodes )
      {
         TranslateNodeEntryData( entry.Value, hookName );
      }
   }

   public static void TranslateDisplayedNode( object info, string hookName )
   {
      if( info == null ) return;

      RuntimeHookTranslationHelper.TranslateTextComponentField( info, "mainWindowTitle", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".mainWindowTitle" ) );
      RuntimeHookTranslationHelper.TranslateTextComponentField( info, "mainWindowBodyText", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".mainWindowBodyText" ) );
      RefreshMainWindowLayout( info );
   }

   public static void TranslateInfoHierarchy( string hookName )
   {
      var infoInstance = GetInfoInstance();
      if( infoInstance == null ) return;

      RuntimeTextHookHelper.TranslateObjectHierarchy( infoInstance, hookName );
   }

   private static void TranslateNodeEntryData( object node, string hookName )
   {
      if( node == null ) return;

      RuntimeHookTranslationHelper.TranslateStringField( node, "label", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".label" ) );

      var mainWindowData = RuntimeHookTranslationHelper.GetInstanceField( node.GetType(), "mainWindowData" )?.GetValue( node );
      if( mainWindowData == null ) return;

      RuntimeHookTranslationHelper.TranslateStringField( mainWindowData, "title", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".title" ) );
      RuntimeHookTranslationHelper.TranslateStringField( mainWindowData, "body", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".body" ) );
   }

   private static UnityEngine.Object GetInfoInstance()
   {
      var infoType = GameTypeResolver.Get( "Info" );
      return infoType?.GetField( "instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null ) as UnityEngine.Object;
   }

   private static void RefreshMainWindowLayout( object info )
   {
      RefreshBodyLayout( info );
      info.GetType().GetMethod( "RepositionTitle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.Invoke( info, null );
      info.GetType().GetMethod( "RepositionTitleBG", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.Invoke( info, null );
   }

   private static void RefreshBodyLayout( object info )
   {
      var bodyComponent = RuntimeHookTranslationHelper.GetInstanceField( info.GetType(), "mainWindowBodyText" )?.GetValue( info );
      if( bodyComponent == null ) return;

      bodyComponent.GetType().GetMethod( "ForceMeshUpdate", new[] { typeof( bool ), typeof( bool ) } )?.Invoke( bodyComponent, new object[] { false, false } );

      var rectTransform = bodyComponent.GetType().GetProperty( "rectTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( bodyComponent );
      var preferredHeight = bodyComponent.GetType().GetProperty( "preferredHeight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( bodyComponent );
      if( rectTransform != null && preferredHeight is float height )
      {
         var axisType = rectTransform.GetType().Assembly.GetType( "UnityEngine.RectTransform+Axis", false );
         var setSizeMethod = rectTransform.GetType().GetMethod( "SetSizeWithCurrentAnchors", axisType == null ? null : new[] { axisType, typeof( float ) } );
         var verticalAxis = axisType == null ? 1 : Enum.ToObject( axisType, 1 );
         setSizeMethod?.Invoke( rectTransform, new[] { verticalAxis, (object)height } );
         RuntimeHookTranslationHelper.GetInstanceField( info.GetType(), "lastBodyTextPreferredHeight" )?.SetValue( info, height );
      }

      var bodyScrollBar = RuntimeHookTranslationHelper.GetInstanceField( info.GetType(), "bodyScrollBar" )?.GetValue( info );
      bodyScrollBar?.GetType().GetMethod( "AfterNewTextDraw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.Invoke( bodyScrollBar, null );
   }
}

[HarmonyPatch]
internal static class MainMenuRuntimeTranslationHelper
{
   private static readonly Dictionary<int, int> PendingRescans = new Dictionary<int, int>();
   private static readonly HashSet<string> LoggedVisiblePanels = new HashSet<string>( StringComparer.Ordinal );
   private const int StartupWarmupTicks = 180;

   public static void TranslateNow( object mainMenu, string hookName )
   {
      if( mainMenu == null ) return;

      TranslateMainMenu( mainMenu, hookName );
   }

   public static void QueueMainMenuRescan( object mainMenu )
   {
      if( mainMenu == null ) return;

      var instanceId = RuntimeHelpers.GetHashCode( mainMenu );
      TranslateMainMenu( mainMenu, "MainMenu.Init" );
      if( HasInfoInstance() ) PendingRescans.Remove( instanceId );
      else PendingRescans[ instanceId ] = 1;
   }

   public static void Tick( object mainMenu )
   {
      if( mainMenu == null ) return;

      var instanceId = RuntimeHelpers.GetHashCode( mainMenu );
      if( !PendingRescans.TryGetValue( instanceId, out var remainingTicks ) )
      {
         remainingTicks = StartupWarmupTicks;
      }

      TranslateMainMenu( mainMenu, "MainMenu.Deferred" );
      remainingTicks--;
      if( remainingTicks <= 0 )
      {
         PendingRescans.Remove( instanceId );
         return;
      }

      PendingRescans[ instanceId ] = remainingTicks;
   }

   private static void TranslateMainMenu( object mainMenu, string hookName )
   {
      var mainMenuGameObject = RuntimeTextHookHelper.GetGameObject( mainMenu );
      RuntimeTextHookHelper.TranslateHierarchy( mainMenuGameObject, hookName );
      TranslateTextHierarchy( mainMenuGameObject, hookName + ".Special", ManualPanelRuntimeTranslationHelper.TranslateText );
      TranslateManualPanel( mainMenu, hookName + ".Manual" );
      InfoRuntimeTranslationHelper.TranslateInfoHierarchy( hookName + ".Info" );
      LogVisiblePanelDiagnostics( mainMenu, hookName );
   }

   private static void TranslateManualPanel( object mainMenu, string hookName )
   {
      if( mainMenu == null ) return;

      var manualField = RuntimeHookTranslationHelper.GetInstanceField( mainMenu.GetType(), "cgManual" );
      var manualCanvasGroup = manualField?.GetValue( mainMenu ) as UnityEngine.Object;
      if( manualCanvasGroup == null ) return;

      var manualGameObject = RuntimeTextHookHelper.GetGameObject( manualCanvasGroup );
      if( manualGameObject == null ) return;

      TranslateTextHierarchy( manualGameObject, hookName, ManualPanelRuntimeTranslationHelper.TranslateText );
   }

   private static void TranslateTextHierarchy( UnityEngine.GameObject root, string hookName, Func<string, string> translator )
   {
      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null ) continue;

         var value = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( value ) ) continue;

         var translated = translator( value );
         if( !string.Equals( translated, value, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( component, translated );
         }
      }
   }

   private static bool HasInfoInstance()
   {
      var infoType = GameTypeResolver.Get( "Info" );
      return infoType?.GetField( "instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null ) != null;
   }

   private static void LogVisiblePanelDiagnostics( object mainMenu, string hookName )
   {
      if( mainMenu == null ) return;

      LogVisibleCanvasGroupPanel( mainMenu, "cgManual", "Manual", hookName );
      LogVisibleCanvasGroupPanel( mainMenu, "cgInfo", "Info", hookName );
      LogVisibleCanvasGroupPanel( mainMenu, "cgCredits", "Credits", hookName );
      LogVisibleCanvasGroupPanel( mainMenu, "cgWarning", "Warning", hookName );
      LogVisibleCanvasGroupPanel( mainMenu, "cgOptions", "Options", hookName );
   }

   private static void LogVisibleCanvasGroupPanel( object mainMenu, string fieldName, string panelName, string hookName )
   {
      var canvasGroupField = RuntimeHookTranslationHelper.GetInstanceField( mainMenu.GetType(), fieldName );
      var canvasGroup = canvasGroupField?.GetValue( mainMenu );
      if( canvasGroup == null ) return;

      var alphaProperty = RuntimeHookTranslationHelper.GetProperty( canvasGroup.GetType(), "alpha" );
      if( alphaProperty?.GetValue( canvasGroup ) is not float alpha || alpha <= 0.01f ) return;

      var instanceKey = RuntimeHelpers.GetHashCode( mainMenu ) + "|" + panelName;
      if( !LoggedVisiblePanels.Add( instanceKey ) ) return;

      var root = RuntimeTextHookHelper.GetGameObject( canvasGroup );
      if( root == null ) return;

      LogPanelDiagnostics( root, panelName, hookName );
   }

   private static void LogPanelDiagnostics( UnityEngine.GameObject root, string panelName, string hookName )
   {
      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      var lines = new List<string>();
      foreach( var component in components )
      {
         if( component == null ) continue;

         var path = GetComponentPath( component );
         if( string.IsNullOrWhiteSpace( path ) ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty?.GetValue( component ) is string text && !string.IsNullOrWhiteSpace( text ) )
         {
            lines.Add( $"text {component.GetType().Name} {path} => {SanitizeDiagnosticValue( text )}" );
         }

         var spriteProperty = RuntimeHookTranslationHelper.GetProperty( component.GetType(), "sprite" );
         var sprite = spriteProperty?.GetValue( component );
         if( sprite != null )
         {
            lines.Add( $"sprite {component.GetType().Name} {path} => {GetUnityObjectName( sprite )}" );
         }

         var textureProperty = RuntimeHookTranslationHelper.GetProperty( component.GetType(), "texture" );
         var texture = textureProperty?.GetValue( component );
         if( texture != null )
         {
            lines.Add( $"texture {component.GetType().Name} {path} => {GetUnityObjectName( texture )}" );
         }

         if( lines.Count >= 60 ) break;
      }

      OstranautsTranslatorPlugin.LogDiagnostic( $"Visible panel {panelName} from {hookName}: {GetComponentPath( root)}" );
      foreach( var line in lines )
      {
         OstranautsTranslatorPlugin.LogDiagnostic( line );
      }
      OstranautsTranslatorPlugin.LogDiagnostic( $"Visible panel {panelName} diagnostic lines: {lines.Count}" );
   }

   private static string GetComponentPath( object component )
   {
      var gameObject = component as UnityEngine.GameObject ?? RuntimeTextHookHelper.GetGameObject( component );
      if( gameObject == null ) return null;

      var names = new List<string>();
      var current = gameObject;
      while( current != null )
      {
         names.Add( GetUnityObjectName( current ) );
         var parentTransform = RuntimeTextHookHelper.GetParentTransform( current );
         current = RuntimeTextHookHelper.GetGameObject( parentTransform );
      }

      names.Reverse();
      return string.Join( "/", names.ToArray() );
   }

   private static string GetUnityObjectName( object value )
   {
      if( value == null ) return string.Empty;

      var nameProperty = RuntimeHookTranslationHelper.GetProperty( value.GetType(), "name" );
      return nameProperty?.GetValue( value ) as string ?? value.GetType().Name;
   }

   private static string SanitizeDiagnosticValue( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return string.Empty;

      var normalized = value.Replace( "\r", "\\r" ).Replace( "\n", "\\n" );
      return normalized.Length <= 200 ? normalized : normalized.Substring( 0, 200 ) + "...";
   }
}

internal static class ManualPanelRuntimeTranslationHelper
{
   public static string TranslateText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, "MainMenu.Manual" );

      translated = ReplaceToken( translated, "WELCOME TO", "欢迎来到" );
      translated = ReplaceToken( translated, "EARLY ACCESS!", "抢先体验！" );
      translated = ReplaceToken( translated, "WHAT'S NEW?", "更新内容" );
      translated = ReplaceToken( translated, "WE LOVE FEEDBACK!", "我们欢迎反馈！" );
      translated = ReplaceToken( translated, "Ship-to-ship Combat", "舰对舰战斗" );
      translated = ReplaceToken( translated, "Fire & Smoke", "火灾与烟雾" );
      translated = ReplaceToken( translated, "Asteroid Mining", "小行星采矿" );
      translated = ReplaceToken( translated, "New Region: Ceres", "新区域：谷神星" );
      translated = ReplaceToken( translated, "Steam Deck™ & Gamepad Support", "Steam Deck™ 与手柄支持" );
      translated = ReplaceToken( translated, "Performance Improvements & more...", "性能改进及更多内容……" );
      translated = ReplaceToken( translated, "We're active on Discord and other communities,", "我们活跃于 Discord 及其他社区，" );
      translated = ReplaceToken( translated, "where players help us shape the game experience.", "玩家会在那里帮助我们塑造游戏体验。" );
      translated = ReplaceToken( translated, "You can find links to these resources on the main", "你可以在主菜单中找到这些资源的链接，" );
      translated = ReplaceToken( translated, "menu, and in-game through the Options menu.", "也可以在游戏内通过选项菜单访问。" );
      return translated;
   }

   private static string ReplaceToken( string value, string source, string replacement )
   {
      return value.Contains( source, StringComparison.Ordinal )
         ? value.Replace( source, replacement )
         : value;
   }
}

internal static class LoadingIntroRuntimeTranslationHelper
{
   public static void TranslateHierarchy( UnityEngine.GameObject root, string hookName )
   {
      if( root == null ) return;

      RuntimeTextHookHelper.TranslateHierarchy( root, hookName );

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( root, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components ) return;

      foreach( var component in components )
      {
         if( component == null ) continue;

         var textProperty = RuntimeHookTranslationHelper.GetStringProperty( component.GetType(), "text" );
         if( textProperty == null ) continue;

         var value = textProperty.GetValue( component ) as string;
         if( string.IsNullOrWhiteSpace( value ) ) continue;

         var translated = TranslateText( value, hookName + ".Special" );
         if( !string.Equals( translated, value, StringComparison.Ordinal ) )
         {
            textProperty.SetValue( component, translated );
         }
      }
   }

   private static string TranslateText( string value, string hookName )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = ManualPanelRuntimeTranslationHelper.TranslateText( value.Replace( "\r\n", "\n" ) );
      return translated;
   }
}

[HarmonyPatch]
internal static class Info_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Info" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Info" ), "Init", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      InfoRuntimeTranslationHelper.TranslateNodeData( __instance, "Info.Init" );
      InfoRuntimeTranslationHelper.TranslateDisplayedNode( __instance, "Info.Init" );
      InfoRuntimeTranslationHelper.TranslateInfoHierarchy( "Info.Init.Hierarchy" );
   }
}

[HarmonyPatch]
internal static class MainMenu_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "MainMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "MainMenu" ), "Awake", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      MainMenuRuntimeTranslationHelper.TranslateNow( __instance, "MainMenu.Awake" );
   }
}

[HarmonyPatch]
internal static class MainMenu_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "MainMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "MainMenu" ), "Init", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      MainMenuRuntimeTranslationHelper.QueueMainMenuRescan( __instance );
   }
}

[HarmonyPatch]
internal static class MainMenu_Start_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "MainMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "MainMenu" ), "Start", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      MainMenuRuntimeTranslationHelper.TranslateNow( __instance, "MainMenu.Start" );
   }
}

[HarmonyPatch]
internal static class MainMenu_RestartLoop_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "MainMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "MainMenu" ), "RestartLoop", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      MainMenuRuntimeTranslationHelper.TranslateNow( __instance, "MainMenu.RestartLoop" );
   }
}

[HarmonyPatch]
internal static class MainMenu_Update_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "MainMenu" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "MainMenu" ), "Update", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      MainMenuRuntimeTranslationHelper.Tick( __instance );
   }
}

internal static class SplashScreenRuntimeTranslationHelper
{
   public static void TranslateUi( object splashScreen, string hookName )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( splashScreen, "txtTitle", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".txtTitle" ) );
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( splashScreen, "txtBody", value => RuntimeTextHookHelper.TranslateTextValue( value, hookName + ".txtBody" ) );
   }
}

[HarmonyPatch]
internal static class GUISplashScreens_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUISplashScreens" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUISplashScreens" ), "Awake", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      SplashScreenRuntimeTranslationHelper.TranslateUi( __instance, "GUISplashScreens.Awake" );
   }
}

[HarmonyPatch]
internal static class GUISplashScreens_Update_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUISplashScreens" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUISplashScreens" ), "Update", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      SplashScreenRuntimeTranslationHelper.TranslateUi( __instance, "GUISplashScreens.Update" );
   }
}

[HarmonyPatch]
internal static class Info_DrawMainWindow_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Info" ) != null && GameTypeResolver.Get( "InfoNode" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Info" ), "DrawMainWindow", new[] { GameTypeResolver.Get( "InfoNode" ) } );
   }

   private static void Postfix( object __instance )
   {
      InfoRuntimeTranslationHelper.TranslateDisplayedNode( __instance, "Info.DrawMainWindow" );
   }
}

internal static class RotateCommandRuntimeGuardHelper
{
   public static bool ShouldSkipExecute()
   {
      var canvasManagerType = GameTypeResolver.Get( "CanvasManager" );
      var crewSimType = GameTypeResolver.Get( "CrewSim" );

      if( canvasManagerType == null || crewSimType == null ) return false;

      var canvasManager = canvasManagerType.GetField( "instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null );
      if( canvasManager == null ) return true;

      var crewCanvasManager = crewSimType.GetField( "CanvasManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null );
      if( crewCanvasManager == null ) return true;

      var shipCurrentLoaded = crewSimType.GetField( "shipCurrentLoaded", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null );
      if( shipCurrentLoaded == null ) return true;

      var crewSimInstance = crewSimType.GetField( "objInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )?.GetValue( null );
      return crewSimInstance == null;
   }
}

[HarmonyPatch]
internal static class CommandRotateCCW_Execute_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.InputControl.CommandRotateCCW" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.InputControl.CommandRotateCCW" ), "Execute" );
   }

   private static bool Prefix()
   {
      return !RotateCommandRuntimeGuardHelper.ShouldSkipExecute();
   }
}

[HarmonyPatch]
internal static class CommandRotateCW_Execute_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.InputControl.CommandRotateCW" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.InputControl.CommandRotateCW" ), "Execute" );
   }

   private static bool Prefix()
   {
      return !RotateCommandRuntimeGuardHelper.ShouldSkipExecute();
   }
}

[HarmonyPatch]
internal static class GUIMessageDisplay_Awake_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ), "Awake" );
   }

   private static void Postfix( object __instance )
   {
      MessageDisplayRuntimeTranslationHelper.TranslateUi( __instance );
   }
}

[HarmonyPatch]
internal static class GUIMessageDisplay_ShowPanel_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ) != null
         && GameTypeResolver.Get( "Ostranauts.Ships.Comms.ShipMessage" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ),
         "ShowPanel",
         new[] { typeof( string ), typeof( List<> ).MakeGenericType( GameTypeResolver.Get( "Ostranauts.Ships.Comms.ShipMessage" ) ) } );
   }

   private static void Prefix( ref string text )
   {
      text = MessageDisplayRuntimeTranslationHelper.TranslateConversationText( text );
   }

   private static void Postfix( object __instance )
   {
      MessageDisplayRuntimeTranslationHelper.TranslateUi( __instance );
   }
}

[HarmonyPatch]
internal static class GUIMessageDisplay_AddMessage_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ) != null
         && GameTypeResolver.Get( "Ostranauts.Ships.Comms.ShipMessage" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ),
         "AddMessage",
         new[] { GameTypeResolver.Get( "Ostranauts.Ships.Comms.ShipMessage" ) } );
   }

   private static void Prefix( object __0 )
   {
      MessageDisplayRuntimeTranslationHelper.TranslateMessageObject( __0 );
   }
}

[HarmonyPatch]
internal static class GUIMessageDisplay_RedrawStatus_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay" ),
         "RedrawStatus",
         new[] { typeof( List<string> ) } );
   }

   private static void Prefix( object __0 )
   {
      if( __0 is not IList values ) return;

      for( var i = 0; i < values.Count; i++ )
      {
         if( values[ i ] is not string value ) continue;
         values[ i ] = MessageDisplayRuntimeTranslationHelper.TranslateRenderedStatusMarkup( value );
      }
   }

   private static void Postfix( object __instance )
   {
      MessageDisplayRuntimeTranslationHelper.TranslateRenderedStatusText( __instance );
   }
}

[HarmonyPatch]
internal static class GUIPDA_ShowJobPaintUI_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDA" ), "ShowJobPaintUI", new[] { typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDA.ShowJobPaintUI" );
   }
}

[HarmonyPatch]
internal static class GUIPDAApp_UpdateInfo_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDAApp" ) != null
         && GameTypeResolver.Get( "JsonPDAAppIcon" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDAApp" ), "UpdateInfo", new[] { GameTypeResolver.Get( "JsonPDAAppIcon" ) } );
   }

   private static void Prefix( object __0 )
   {
      if( __0 == null ) return;

      var friendlyNameProperty = RuntimeHookTranslationHelper.GetProperty( __0.GetType(), "strFriendlyName" );
      if( friendlyNameProperty == null
         || friendlyNameProperty.PropertyType != typeof( string )
         || !friendlyNameProperty.CanRead
         || !friendlyNameProperty.CanWrite )
      {
         return;
      }

      if( friendlyNameProperty.GetValue( __0 ) is not string value || string.IsNullOrWhiteSpace( value ) ) return;
      friendlyNameProperty.SetValue( __0, PdaRuntimeTranslationHelper.TranslateAppTitle( value, "GUIPDAApp.UpdateInfo.strFriendlyName" ) );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "m_txtName", value => PdaRuntimeTranslationHelper.TranslateAppTitle( value, "GUIPDAApp.UpdateInfo.m_txtName" ) );
   }
}

[HarmonyPatch]
internal static class GUIPDA_BuildFilters_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDA" ), "BuildFilters", new[] { typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      if( __instance == null ) return;

      var filtersField = RuntimeHookTranslationHelper.GetInstanceField( __instance.GetType(), "_socialFilters" );
      if( filtersField?.GetValue( __instance ) is IList filters )
      {
         foreach( var filter in filters )
         {
            var friendlyNameField = RuntimeHookTranslationHelper.GetInstanceField( filter?.GetType(), "FriendlyName" );
            if( friendlyNameField == null || friendlyNameField.FieldType != typeof( string ) ) continue;

            var value = friendlyNameField.GetValue( filter ) as string;
            if( string.IsNullOrWhiteSpace( value ) ) continue;

            friendlyNameField.SetValue( filter, PdaRuntimeTranslationHelper.TranslateSocialFilterLabel( value, "GUIPDA.BuildFilters.FriendlyName" ) );
         }
      }

      var dropdownField = RuntimeHookTranslationHelper.GetInstanceField( __instance.GetType(), "ddFilter" );
      var dropdown = dropdownField?.GetValue( __instance );
      PdaRuntimeTranslationHelper.TranslateSocialFilterHierarchy( RuntimeTextHookHelper.GetGameObject( dropdown ), "GUIPDA.BuildFilters" );
   }
}

[HarmonyPatch]
internal static class StandingsListElement_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.StandingsListElement" ) != null
         && GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.StandingsDTO" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.StandingsListElement" ),
         "Init",
         new[] { GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.StandingsDTO" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtFactionName", value => RuntimeTextHookHelper.TranslateTextValue( value, "StandingsListElement.Init.txtFactionName" ) );
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "txtStatus", PdaRuntimeTranslationHelper.TranslateStandingsLabel( PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "txtStatus" ), "StandingsListElement.Init.txtStatus" ) );
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "txtValue", PdaRuntimeTranslationHelper.TranslateStandingsLabel( PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "txtValue" ), "StandingsListElement.Init.txtValue" ) );
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "txtCurrency", PdaRuntimeTranslationHelper.TranslateStandingsLabel( PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "txtCurrency" ), "StandingsListElement.Init.txtCurrency" ) );
   }
}

[HarmonyPatch]
internal static class GUIPDA_ShowSocials_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDA" ), "ShowSocials", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDA.ShowSocials" );
   }
}

[HarmonyPatch]
internal static class GUIPDA_ShowStandings_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDA" ), "ShowStandings", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDA.ShowStandings" );
   }
}

[HarmonyPatch]
internal static class GUIPDA_SetSelectedFaction_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null
         && GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.StandingsDTO" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUIPDA" ),
         "SetSelectedFaction",
         new[] { GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.StandingsDTO" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.SetTextComponentField(
         __instance,
         "lblFactionName",
         PdaRuntimeTranslationHelper.TranslateStandingsFactionName(
            PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "lblFactionName" ),
            "GUIPDA.SetSelectedFaction.lblFactionName" ) );

      RuntimeHookTranslationHelper.SetTextComponentField(
         __instance,
         "lblFactionRep",
         PdaRuntimeTranslationHelper.TranslateStandingsReputation(
            PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "lblFactionRep" ),
            "GUIPDA.SetSelectedFaction.lblFactionRep" ) );
   }
}

[HarmonyPatch]
internal static class GUIPDA_ToggleNotesUI_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDA" ), "ToggleNotesUI", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      PdaRuntimeTranslationHelper.TranslateNotesDisplay( __instance, "GUIPDA.ToggleNotesUI" );
   }
}

[HarmonyPatch]
internal static class GUIPDA_ToggleVizUI_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDA" ), "ToggleVizUI", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      ComputerRuntimeTranslationHelper.TranslateVizHierarchy( __instance, "GUIPDA.ToggleVizUI" );
   }
}

[HarmonyPatch]
internal static class PDAVisualisers_set_OverlayVariable_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "PDAVisualisers" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return GameTypeResolver.Get( "PDAVisualisers" )?.GetProperty( "OverlayVariable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetSetMethod( true );
   }

   private static void Postfix( object __instance )
   {
      ComputerRuntimeTranslationHelper.TranslateVizOverlayVariableDisplay( __instance, "PDAVisualisers.OverlayVariable" );
   }
}

[HarmonyPatch]
internal static class PDAVisualisers_set_Gradient_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "PDAVisualisers" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return GameTypeResolver.Get( "PDAVisualisers" )?.GetProperty( "Gradient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetSetMethod( true );
   }

   private static void Postfix( object __instance )
   {
      ComputerRuntimeTranslationHelper.TranslateVizGradientDisplay( __instance, "PDAVisualisers.Gradient" );
   }
}

[HarmonyPatch]
internal static class GUIPDAFerry_ShowRequest_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDAFerry" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDAFerry" ), "ShowRequest", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDAFerry.ShowRequest" );
   }
}

[HarmonyPatch]
internal static class GUIPDAFerry_ShowArrival_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDAFerry" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDAFerry" ), "ShowArrival", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDAFerry.ShowArrival" );
   }
}

[HarmonyPatch]
internal static class GUIPDAFerryRow_SetData_Ship_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.GUIPDAFerryRow" ) != null
         && GameTypeResolver.Get( "Ship" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.GUIPDAFerryRow" ),
         "SetData",
         new[] { GameTypeResolver.Get( "Ship" ), typeof( double ), typeof( Action<string, double> ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.SetTextComponentField(
         __instance,
         "txtName",
         PdaRuntimeTranslationHelper.TranslateFerryDestinationLabel(
            PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "txtName" ),
            "GUIPDAFerryRow.SetData.ship.txtName" ) );
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDAFerryRow.SetData.ship" );
   }
}

[HarmonyPatch]
internal static class GUIPDAFerryRow_SetData_String_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.GUIPDAFerryRow" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.GUIPDAFerryRow" ),
         "SetData",
         new[] { typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDAFerryRow.SetData.text" );
   }
}

[HarmonyPatch]
internal static class GUIPDAFerryHeaderRow_SetData_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.GUIPDAFerryHeaderRow" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.PDA.GUIPDAFerryHeaderRow" ),
         "SetData",
         new[] { typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIPDAFerryHeaderRow.SetData" );
   }
}

[HarmonyPatch]
internal static class GUIComputer2_ShowNav_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIComputer2" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var guiComputerType = GameTypeResolver.Get( "GUIComputer2" );
      if( guiComputerType == null ) return null;

      foreach( var method in guiComputerType.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
      {
         if( !string.Equals( method.Name, "ShowNav", StringComparison.Ordinal ) ) continue;

         var parameters = method.GetParameters();
         if( parameters.Length != 2 ) continue;
         if( !string.Equals( parameters[ 0 ].ParameterType.Name, "Transform", StringComparison.Ordinal ) ) continue;
         if( !string.Equals( parameters[ 1 ].ParameterType.Name, "CondOwner", StringComparison.Ordinal ) ) continue;

         return method;
      }

      return null;
   }

   private static void Postfix( object __instance )
   {
      var root = RuntimeTextHookHelper.GetGameObject( __instance );
      var navPanel = FindChildGameObject( root, "MiddleGround/Search/pnlNavStation" );
      ComputerRuntimeTranslationHelper.TranslateNavPanelHierarchy( navPanel, "GUIComputer2.ShowNav" );
   }

   private static UnityEngine.GameObject FindChildGameObject( UnityEngine.GameObject root, string path )
   {
      if( root == null || string.IsNullOrWhiteSpace( path ) ) return null;

      var transformProperty = typeof( UnityEngine.GameObject ).GetProperty( "transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var transform = transformProperty?.GetValue( root, null );
      if( transform == null ) return null;

      var findMethod = transform.GetType().GetMethod( "Find", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof( string ) }, null );
      var childTransform = findMethod?.Invoke( transform, new object[] { path } );
      return RuntimeTextHookHelper.GetGameObject( childTransform );
   }
}

[HarmonyPatch]
internal static class GUISocialsRow_SetContact_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUISocialsRow" ) != null
         && GameTypeResolver.Get( "Social" ) != null
         && GameTypeResolver.Get( "PersonSpec" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUISocialsRow" ),
         "SetContact",
         new[] { GameTypeResolver.Get( "Social" ), GameTypeResolver.Get( "PersonSpec" ), GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "txtName", value => RuntimeTextHookHelper.TranslateTextValue( value, "GUISocialsRow.SetContact.txtName" ) );
      RuntimeHookTranslationHelper.SetTextComponentField(
         __instance,
         "txtBody",
         PdaRuntimeTranslationHelper.TranslateSocialContactBody(
            PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "txtBody" ),
            "GUISocialsRow.SetContact.txtBody" ) );
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUISocialsRow.SetContact" );
   }
}

[HarmonyPatch]
internal static class GUIZones_OnTilesSelected_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.GUIZones" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var condOwnerType = GameTypeResolver.Get( "CondOwner" );
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.ShipGUIs.GUIZones" ),
         "OnTilesSelected",
         new[] { typeof( List<> ).MakeGenericType( condOwnerType ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "_selectedTilesLabel", PdaRuntimeTranslationHelper.TranslateZoneSelectionLabel( PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "_selectedTilesLabel" ), "GUIZones.OnTilesSelected._selectedTilesLabel" ) );
   }
}

[HarmonyPatch]
internal static class GUIZones_ToggleMenuVisibility_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.ShipGUIs.GUIZones" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ostranauts.ShipGUIs.GUIZones" ), "ToggleMenuVisibility", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __instance as UnityEngine.Object, "GUIZones.ToggleMenuVisibility" );
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "_selectedTilesLabel", PdaRuntimeTranslationHelper.TranslateZoneSelectionLabel( PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "_selectedTilesLabel" ), "GUIZones.ToggleMenuVisibility._selectedTilesLabel" ) );
   }
}

[HarmonyPatch]
internal static class GUIPDA_ToggleObjectives_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIPDA" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIPDA" ), "ToggleObjectives", new[] { typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __instance as UnityEngine.Object, "GUIPDA.ToggleObjectives" );
   }
}

[HarmonyPatch]
internal static class ObjectivesApp_SetPage_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivesApp" ) != null
         && GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivesAppPage" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivesApp" ),
         "SetPage",
         new[] { GameTypeResolver.Get( "Ostranauts.Objectives.ObjectivesAppPage" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __instance as UnityEngine.Object, "ObjectivesApp.SetPage" );
   }
}

[HarmonyPatch]
internal static class GUIRoster_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIRoster" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUIRoster" ),
         "Init",
         new[] { GameTypeResolver.Get( "CondOwner" ), typeof( Dictionary<string, string> ), typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "GUIRoster.Init" );
      RuntimeHookTranslationHelper.SetTextComponentField(
         __instance,
         "txtTitleValue",
         PdaRuntimeTranslationHelper.TranslateRosterCompanyTitle(
            PdaRuntimeTranslationHelper.GetTextComponentFieldValue( __instance, "txtTitleValue" ),
            "GUIRoster.Init.txtTitleValue" ) );
   }
}

[HarmonyPatch]
internal static class GUIDuties_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIDuties" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUIDuties" ),
         "Init",
         new[] { GameTypeResolver.Get( "CondOwner" ), typeof( Dictionary<string, string> ), typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      var root = RuntimeTextHookHelper.GetGameObject( __instance );
      RuntimeTextHookHelper.TranslateHierarchy( root, "GUIDuties.Init" );

      var transformProperty = typeof( UnityEngine.GameObject ).GetProperty( "transform", BindingFlags.Instance | BindingFlags.Public );
      var transform = transformProperty?.GetValue( root );
      var findMethod = transform?.GetType().GetMethod( "Find", new[] { typeof( string ) } );
      var titleTransform = findMethod?.Invoke( transform, new object[] { "txtTitleValue" } );
      var tmpTextType = RuntimeTypeResolver.FindLoadedType( "TMPro.TMP_Text" );
      var getComponentMethod = titleTransform?.GetType().GetMethod( "GetComponent", new[] { typeof( Type ) } );
      var titleText = tmpTextType == null ? null : getComponentMethod?.Invoke( titleTransform, new object[] { tmpTextType } );
      var textProperty = titleText?.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( textProperty != null && textProperty.CanRead && textProperty.CanWrite )
      {
         var currentText = textProperty.GetValue( titleText ) as string ?? string.Empty;
         textProperty.SetValue( titleText, PdaRuntimeTranslationHelper.TranslateRosterCompanyTitle( currentText, "GUIDuties.Init.txtTitleValue" ) );
      }
   }
}

[HarmonyPatch]
internal static class GUIJobItem_SetData_String_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.PDA.GUIJobItem" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var guiJobItemType = GameTypeResolver.Get( "Ostranauts.UI.PDA.GUIJobItem" );
      if( guiJobItemType == null ) return null;

      foreach( var method in guiJobItemType.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
      {
         if( !string.Equals( method.Name, "SetData", StringComparison.Ordinal ) ) continue;

         var parameters = method.GetParameters();
         if( parameters.Length != 3 ) continue;
         if( parameters[ 0 ].ParameterType != typeof( string ) ) continue;
         if( parameters[ 1 ].ParameterType != typeof( string ) ) continue;

         return method;
      }

      return null;
   }

   private static void Prefix( ref string title, string strImg )
   {
      title = PdaRuntimeTranslationHelper.TranslateMenuTitle( title, strImg, "GUIJobItem.SetData.title" );
   }
}

[HarmonyPatch]
internal static class GUIJobItem_SetData_Installable_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ostranauts.UI.PDA.GUIJobItem" ) != null
         && GameTypeResolver.Get( "JsonInstallable" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "Ostranauts.UI.PDA.GUIJobItem" ),
         "SetData",
         new[] { GameTypeResolver.Get( "JsonInstallable" ) } );
   }

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "_title", "GUIJobItem.SetData.installable" );
   }
}

internal static class ChargenCareerRuntimeTranslationHelper
{
   private static readonly Dictionary<string, string> ExactTextMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Summary" ] = "摘要",
      [ "Selected Skills" ] = "已选项目",
      [ "Costs" ] = "耗时",
      [ "Apply" ] = "应用",
      [ "Undo Last" ] = "撤销上一步",
      [ "Clear" ] = "清除",
      [ "Total cost cannot be negative" ] = "总耗时不能为负数",
      [ "Ambitious" ] = "有抱负",
      [ "Feels Ambitious" ] = "感到有抱负",
      [ "Anti-GMO" ] = "反对基改",
      [ "Apathetic" ] = "冷漠",
      [ "Feels Apathetic" ] = "感到冷漠",
      [ "Arrogant" ] = "傲慢",
      [ "Beautiful" ] = "迷人",
      [ "Brave" ] = "勇敢",
      [ "Feels Brave" ] = "感到勇敢",
      [ "Charismatic" ] = "有魅力",
      [ "Chaste" ] = "禁欲",
      [ "Feels Chaste" ] = "感到禁欲"
   };

   private static readonly Dictionary<string, string> ShipInfoLabelMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Make" ] = "制造商",
      [ "Model" ] = "型号",
      [ "Year" ] = "年份",
      [ "Designation" ] = "用途",
      [ "Dimensions" ] = "尺寸",
      [ "Mass" ] = "质量",
      [ "RCS Count" ] = "RCS 数量",
      [ "Torch Drive" ] = "火炬引擎",
      [ "Location" ] = "位置",
      [ "Docked" ] = "已停靠",
      [ "Mortgage" ] = "抵押贷款",
      [ "Payment per shift" ] = "每班还款"
   };

   private static readonly Dictionary<string, string> ShipInfoValueMap = new Dictionary<string, string>( StringComparer.Ordinal )
   {
      [ "Yes" ] = "是",
      [ "No" ] = "否",
      [ "N/A" ] = "无",
      [ "NO DATA" ] = "无数据"
   };

   private static readonly Regex SelectedSummaryEntryPattern = new Regex(
      "^(?<prefix><b>[+-]\\s*)(?<name>.+?)(?<suffix></b>\\s*)$",
      RegexOptions.Compiled | RegexOptions.Singleline );

   private static readonly Regex ShipInfoLinePattern = new Regex(
      "^(?<label>Make|Model|Year|Designation|Dimensions|Mass|RCS Count|Torch Drive|Location|Docked|Mortgage|Payment per shift):\\s*(?<value>.*)$",
      RegexOptions.Compiled | RegexOptions.Singleline );

   public static void TranslateMultiSelectSidebar( object target, string hookName )
   {
      var sidebarRoot = GetSidebarRoot( target );
      if( sidebarRoot == null ) return;

      RuntimeTextHookHelper.TranslateHierarchyIfChanged( sidebarRoot, hookName );

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( sidebarRoot, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components )
      {
         return;
      }

      foreach( var component in components )
      {
         TranslateTextComponent( component, hookName + "." + component?.GetType().Name );
      }
   }

    public static void TranslateMainPanel( object target, string hookName )
   {
      var mainRoot = GetMainRoot( target );
      if( mainRoot == null ) return;

      RuntimeTextHookHelper.TranslateHierarchyIfChanged( mainRoot, hookName );

      var getComponentsInChildren = typeof( UnityEngine.GameObject ).GetMethod( "GetComponentsInChildren", new[] { typeof( Type ), typeof( bool ) } );
      if( getComponentsInChildren?.Invoke( mainRoot, new object[] { typeof( UnityEngine.Component ), true } ) is not IEnumerable components )
      {
         return;
      }

      foreach( var component in components )
      {
         TranslateTextComponent( component, hookName + "." + component?.GetType().Name );
      }
   }

   private static UnityEngine.GameObject GetSidebarRoot( object target )
   {
      var sidebarField = target == null ? null : RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), "tfSidebar" );
      return RuntimeTextHookHelper.GetGameObject( sidebarField?.GetValue( target ) );
   }

   private static UnityEngine.GameObject GetMainRoot( object target )
   {
      var mainField = target == null ? null : RuntimeHookTranslationHelper.GetInstanceField( target.GetType(), "tfMain" );
      return RuntimeTextHookHelper.GetGameObject( mainField?.GetValue( target ) );
   }

   private static void TranslateTextComponent( object component, string hookName )
   {
      if( component == null ) return;

      var textProperty = RuntimeHookTranslationHelper.GetProperty( component.GetType(), "text" );
      if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      var currentText = textProperty.GetValue( component ) as string;
      var translatedText = TranslateChargenText( currentText, hookName );
      if( !string.Equals( translatedText, currentText, StringComparison.Ordinal ) )
      {
         textProperty.SetValue( component, translatedText );
      }
   }

   private static string TranslateChargenText( string text, string hookName )
   {
      var translated = TranslateSummaryText( text, hookName );
      if( !string.Equals( translated, text, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return TranslateShipInfoText( text, hookName );
   }

   private static string TranslateSummaryText( string text, string hookName )
   {
      if( string.IsNullOrEmpty( text ) ) return text;

      if( ExactTextMap.TryGetValue( text, out var exactTranslated ) )
      {
         return exactTranslated;
      }

      var translated = OstranautsTranslatorPlugin.Translate( text, hookName );
      if( !string.Equals( translated, text, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = NormalizeCareerSummaryNarrativeText(
         TooltipRuntimeTranslationHelper.TranslateEmbeddedPersonNames( text, hookName + ".Names" ) );
      if( !string.Equals( translated, text, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( text.Contains( "<b>Total Cost: </b>", StringComparison.Ordinal ) )
      {
         return ReplaceOrdinal( text, "<b>Total Cost: </b>", "<b>总耗时：</b>" );
      }

      var entryMatch = SelectedSummaryEntryPattern.Match( text );
      if( entryMatch.Success )
      {
         var conditionName = entryMatch.Groups[ "name" ].Value;
         var translatedName = TranslateConditionFriendlyName( conditionName, hookName + ".ConditionFriendlyName" );
         if( !string.Equals( translatedName, conditionName, StringComparison.Ordinal ) )
         {
            return entryMatch.Groups[ "prefix" ].Value + translatedName + entryMatch.Groups[ "suffix" ].Value;
         }
      }

      return text;
   }

   private static string NormalizeCareerSummaryNarrativeText( string text )
   {
      if( string.IsNullOrWhiteSpace( text ) ) return text;

      var normalized = text
         .Replace( "New father:", "新生父：" )
         .Replace( "New mother:", "新生母：" )
         .Replace( "New 生父:", "新生父：" )
         .Replace( "New 生母:", "新生母：" )
         .Replace( "New生父:", "新生父：" )
         .Replace( "New生母:", "新生母：" )
         .Replace( " from ", "，来自" );

      return normalized;
   }

   private static string TranslateShipInfoText( string text, string hookName )
   {
      if( string.IsNullOrEmpty( text ) ) return text;

      if( ShipInfoValueMap.TryGetValue( text, out var exactValueTranslated ) )
      {
         return exactValueTranslated;
      }

      var normalized = text.Replace( "\r\n", "\n" );
      var lines = normalized.Split( '\n' );
      var changed = false;

      for( var i = 0; i < lines.Length; i++ )
      {
         var line = lines[ i ];
         if( string.IsNullOrEmpty( line ) ) continue;

         if( ShipInfoValueMap.TryGetValue( line, out var mappedLine ) )
         {
            lines[ i ] = mappedLine;
            changed = true;
            continue;
         }

         var match = ShipInfoLinePattern.Match( line );
         if( !match.Success ) continue;

         var label = match.Groups[ "label" ].Value;
         var value = match.Groups[ "value" ].Value;
         if( !ShipInfoLabelMap.TryGetValue( label, out var translatedLabel ) ) continue;

         var translatedValue = TranslateShipInfoValue( value, hookName + ".ShipInfoValue." + label.Replace( ' ', '_' ) );
         var translatedLine = translatedLabel + "：" + translatedValue;
         if( !string.Equals( translatedLine, line, StringComparison.Ordinal ) )
         {
            lines[ i ] = translatedLine;
            changed = true;
         }
      }

      return changed ? string.Join( "\n", lines ) : text;
   }

   private static string TranslateShipInfoValue( string value, string hookName )
   {
      if( string.IsNullOrEmpty( value ) ) return value;

      if( ShipInfoValueMap.TryGetValue( value, out var mappedValue ) )
      {
         return mappedValue;
      }

      if( value.EndsWith( " (kg)", StringComparison.Ordinal ) )
      {
         return value.Substring( 0, value.Length - " (kg)".Length ) + "（千克）";
      }

      return OstranautsTranslatorPlugin.Translate( value, hookName );
   }

   private static string TranslateConditionFriendlyName( string conditionName, string hookName )
   {
      if( string.IsNullOrEmpty( conditionName ) ) return conditionName;

      if( ExactTextMap.TryGetValue( conditionName, out var mapped ) )
      {
         return mapped;
      }

      return OstranautsTranslatorPlugin.Translate( conditionName, hookName );
   }

   private static string ReplaceOrdinal( string source, string oldValue, string newValue )
   {
      var index = source.IndexOf( oldValue, StringComparison.Ordinal );
      if( index < 0 ) return source;

      return source.Substring( 0, index ) + newValue + source.Substring( index + oldValue.Length );
   }
}

[HarmonyPatch]
internal static class GUIChargenCareer_RebuildMultiSelectSidebar_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIChargenCareer" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIChargenCareer" ), "RebuildMultiSelectSidebar", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      ChargenCareerRuntimeTranslationHelper.TranslateMultiSelectSidebar( __instance, "GUIChargenCareer.RebuildMultiSelectSidebar" );
   }
}

[HarmonyPatch]
internal static class GUIChargenBody_Init_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIChargenBody" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUIChargenBody" ),
         "Init",
         new[] { GameTypeResolver.Get( "CondOwner" ), typeof( Dictionary<string, string> ), typeof( string ) } );
   }

   private static void Postfix( object __instance )
   {
      ChargenBodyRuntimeTranslationHelper.TranslateGeneratedName( __instance, "GUIChargenBody.Init" );
   }
}

[HarmonyPatch]
internal static class GUIChargenBody_RandName_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIChargenBody" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUIChargenBody" ), "RandName", Type.EmptyTypes );
   }

   private static void Postfix( object __instance )
   {
      ChargenBodyRuntimeTranslationHelper.TranslateGeneratedName( __instance, "GUIChargenBody.RandName" );
   }
}

[HarmonyPatch]
internal static class DataHandler_GetFullName_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "DataHandler" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "DataHandler" ), "GetFullName", new[] { typeof( string ), typeof( string ).MakeByRefType(), typeof( string ).MakeByRefType() } );
   }

   private static void Postfix( ref string strFirstName, ref string strLastName )
   {
      strFirstName = ChargenBodyRuntimeTranslationHelper.TranslateFirstName( strFirstName, "DataHandler.GetFullName.FirstName" );
      strLastName = ChargenBodyRuntimeTranslationHelper.TranslateLastName( strLastName, "DataHandler.GetFullName.LastName" );
   }
}

[HarmonyPatch]
internal static class GUIChargenCareer_ClickEvent_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIChargenCareer" ) != null
         && GameTypeResolver.Get( "JsonLifeEvent" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var guiChargenCareerType = GameTypeResolver.Get( "GUIChargenCareer" );
      if( guiChargenCareerType == null ) return null;

      foreach( var method in guiChargenCareerType.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
      {
         if( !string.Equals( method.Name, "ClickEvent", StringComparison.Ordinal ) ) continue;

         var parameters = method.GetParameters();
         if( parameters.Length != 2 ) continue;
         if( !string.Equals( parameters[ 0 ].ParameterType.Name, "GameObject", StringComparison.Ordinal ) ) continue;
         if( !string.Equals( parameters[ 1 ].ParameterType.Name, "JsonLifeEvent", StringComparison.Ordinal ) ) continue;

         return method;
      }

      return null;
   }

   private static void Postfix( object __instance )
   {
      ChargenCareerRuntimeTranslationHelper.TranslateMainPanel( __instance, "GUIChargenCareer.ClickEvent" );
   }
}

[HarmonyPatch]
internal static class GUIChargenCareer_PageEvent_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUIChargenCareer" ) != null
         && GameTypeResolver.Get( "JsonLifeEvent" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GUIChargenCareer" ),
         "PageEvent",
         new[] { GameTypeResolver.Get( "JsonLifeEvent" ) } );
   }

   private static void Postfix( object __instance )
   {
      ChargenCareerRuntimeTranslationHelper.TranslateMainPanel( __instance, "GUIChargenCareer.PageEvent" );
   }
}
