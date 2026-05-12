using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( value.Contains( " - ", StringComparison.Ordinal ) )
      {
         var parts = value.Split( new[] { " - " }, StringSplitOptions.None );
         var changed = false;
         for( var i = 0; i < parts.Length; i++ )
         {
            var part = parts[ i ];
            var translatedPart = RuntimeTextHookHelper.TranslateTextValue( part, hookName + "[" + i + "]" );
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

      return value;
   }
}

internal static class CrewBarRuntimeTranslationHelper
{
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
}

internal static class LogMessageRuntimeTranslationHelper
{
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

      return value;
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
      [ "5 mins" ] = "5分钟",
      [ "10 mins" ] = "10分钟",
      [ "20 mins" ] = "20分钟",
      [ "30 mins" ] = "30分钟",
      [ "60 mins" ] = "60分钟",
      [ "Soft" ] = "柔和",
      [ "Kelvin" ] = "开尔文",
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
   }

   public static string TranslateOptionText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, "GUIOptions.Option" );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      return ExactTextMap.TryGetValue( value, out var exactText ) ? exactText : value;
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

   private static string ReplaceToken( string value, string token, string replacement )
   {
      var index = value.IndexOf( token, StringComparison.Ordinal );
      if( index < 0 ) return value;

      return value.Substring( 0, index ) + replacement + value.Substring( index + token.Length );
   }
}

internal static class TooltipRuntimeTranslationHelper
{
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
      var friendlyName = GetStringMember( crewMember, "FriendlyName" );
      if( string.IsNullOrWhiteSpace( friendlyName ) )
      {
         friendlyName = GetStringMember( crewMember, "strName" );
      }

      if( string.IsNullOrWhiteSpace( friendlyName ) ) return value;

      var translatedName = RuntimeTextHookHelper.TranslateTextValue( friendlyName, hookName + ".FriendlyName" );
      return string.Equals( translatedName, friendlyName, StringComparison.Ordinal )
         ? value
         : value.Replace( friendlyName, translatedName );
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
}

internal static class MegaToolTipRuntimeTranslationHelper
{
   private static readonly Regex ItemSentencePattern = new Regex( "^The (?<subject>.+?) is (?<article>an? )?(?<descriptor>.+?) item\\.$", RegexOptions.CultureInvariant );
   private static readonly Regex ItemTokenSentencePattern = new Regex( "^\\[us\\] \\[is\\] (?<article>an? )?(?<descriptor>.+?) item\\.$", RegexOptions.CultureInvariant );
   private static readonly Regex StateSentencePattern = new Regex( "^The (?<subject>.+?) is (?<article>an? )?(?<descriptor>.+?)\\.$", RegexOptions.CultureInvariant );

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
            lines[ i ] = "派系：" + line.Substring( "Factions: ".Length );
         }
         else if( string.Equals( line, "n/a", StringComparison.Ordinal ) )
         {
            lines[ i ] = "无";
         }
         else
         {
            lines[ i ] = RuntimeTextHookHelper.TranslateTextValue( line, hookName + "[" + i + "]" );
         }
      }

      return string.Join( "\n", lines );
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
      strMsg = OstranautsTranslatorPlugin.Translate( strMsg, "CondOwner.LogMessage" );
      strMsg = LogMessageRuntimeTranslationHelper.TranslateMessage( strMsg );
      if( !string.IsNullOrEmpty( strShort ) )
      {
         strShort = OstranautsTranslatorPlugin.Translate( strShort, "CondOwner.LogMessage.short" );
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

   private static void Postfix( object __instance )
   {
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "m_txtName", value => RuntimeTextHookHelper.TranslateTextValue( value, "GUIItemToolTip.SetCondOwner.m_txtName" ) );
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
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "_txtFullName", value => RuntimeTextHookHelper.TranslateTextValue( value, "MegaToolTip.ItemModule.SetData._txtFullName" ) );
      RuntimeHookTranslationHelper.TranslateTextComponentFieldIfChanged( __instance, "_txtDescription", value => MegaToolTipRuntimeTranslationHelper.TranslateItemDescription( value, __0, "MegaToolTip.ItemModule.SetData._txtDescription" ) );
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( RuntimeTextHookHelper.GetGameObject( __instance ), "MegaToolTip.ItemModule.SetData" );
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
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "_txtTitle", "ObjectivePanel.CompleteObjective" );
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "_txtDescription", "ObjectivePanel.CompleteObjective" );
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
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "_txtTitle", "ObjectivePanel.SetData" );
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "_txtDescription", "ObjectivePanel.SetData" );
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
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "_txtTitle", "ObjectivePanel.RefreshText" );
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "_txtDescription", "ObjectivePanel.RefreshText" );
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
      RuntimeHookTranslationHelper.SetTextComponentField( __instance, "_txtName", CrewBarRuntimeTranslationHelper.TranslateCrewDisplayName( co ) );
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
