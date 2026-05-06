using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
   public static void TranslateStringField( object target, string fieldName, string hookName )
   {
      TranslateStringField( target, fieldName, value => OstranautsTranslatorPlugin.Translate( value, hookName + "." + fieldName ) );
   }

   public static void TranslateStringField( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
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

      var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
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

      var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( field?.GetValue( target ) is not IEnumerable values ) return;

      foreach( var item in values )
      {
         TranslateStringField( item, "Label", translator );
         TranslateStringField( item, "MainText", translator );
      }
   }

   public static void TranslateTextComponentField( object target, string fieldName, string hookName )
   {
      if( target == null ) return;

      var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var component = field?.GetValue( target );
      if( component == null ) return;

      var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      var value = textProperty.GetValue( component ) as string;
      if( string.IsNullOrEmpty( value ) ) return;

      textProperty.SetValue( component, RuntimeTextHookHelper.TranslateTextValue( value, hookName + "." + fieldName ) );
   }

   public static void SetTextComponentField( object target, string fieldName, string value )
   {
      if( target == null ) return;

      var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var component = field?.GetValue( target );
      if( component == null ) return;

      var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( textProperty == null || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      textProperty.SetValue( component, value );
   }

   public static void SetTextComponentProperty( object component, string value )
   {
      if( component == null ) return;

      var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( textProperty == null || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      textProperty.SetValue( component, value );
   }

   public static void TranslateDropdownOptionsField( object target, string fieldName, Func<string, string> translator )
   {
      if( target == null ) return;

      var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      TranslateDropdownOptions( field?.GetValue( target ), translator );
   }

   public static void TranslateDropdownOptions( object dropdown, Func<string, string> translator )
   {
      if( dropdown == null ) return;

      var optionsProperty = dropdown.GetType().GetProperty( "options", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( optionsProperty?.GetValue( dropdown ) is not IList options ) return;

      foreach( var option in options )
      {
         if( option == null ) continue;

         var textProperty = option.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
         if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) continue;

         var text = textProperty.GetValue( option ) as string;
         if( string.IsNullOrWhiteSpace( text ) ) continue;

         textProperty.SetValue( option, translator( text ) );
      }

      dropdown.GetType().GetMethod( "RefreshShownValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.Invoke( dropdown, null );
   }
}

internal static class PdaRuntimeTranslationHelper
{
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

   public static string TranslateMenuTitle( string title, string imageName, string hookName )
   {
      if( string.IsNullOrWhiteSpace( title ) ) return title;

      if( !string.IsNullOrWhiteSpace( imageName ) && TitleByImageName.TryGetValue( imageName, out var canonicalTitle ) )
      {
         return RuntimeTextHookHelper.TranslateTextValue( canonicalTitle, hookName + "." + imageName );
      }

      return RuntimeTextHookHelper.TranslateTextValue( title, hookName );
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
         return subject + " 获得了 " + payload + "。";
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

      if( value.StartsWith( "OPT ", StringComparison.Ordinal ) )
      {
         return "光学 " + value.Substring( "OPT ".Length );
      }

      if( value.StartsWith( "Signal:", StringComparison.Ordinal ) )
      {
         return "信号：" + value.Substring( "Signal:".Length );
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

      var dictField = guiData.GetType().GetField( "dictPropMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
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

      var field = panel.GetType().GetField( "txtArray", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( field?.GetValue( panel ) is not Array textComponents ) return;

      for( var i = 0; i < textComponents.Length; i++ )
      {
         var component = textComponents.GetValue( i );
         if( component == null ) continue;

         var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
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

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, hookName );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      translated = ReplacePrefix( value, "Point of Ref:", "参考点：" );
      translated = ReplacePrefix( translated, "VREL: ", "相对速度：" );
      translated = ReplacePrefix( translated, "VREL ", "相对速度 " );
      translated = ReplacePrefix( translated, "VCRS ", "横向速度 " );
      translated = ReplacePrefix( translated, "BRG ", "方位 " );
      translated = ReplacePrefix( translated, "ETA ", "预计到达 " );
      translated = ReplacePrefix( translated, "Claimed by: ", "归属：" );
      translated = ReplaceExact( translated, "F: Unclaimed", "F: 未认领" );
      translated = ReplaceToken( translated, "RNG ", "距离 " );

      return translated;
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
      RuntimeHookTranslationHelper.TranslateTextComponentField( messageDisplay, "txtStatus", "GUIMessageDisplay.txtStatus" );
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
}

internal static class ReservesRuntimeTranslationHelper
{
   public static string TranslateFuelText( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return value;

      var translated = RuntimeTextHookHelper.TranslateTextValue( value, "NavModReserves.UpdateUI.txtFuel" );
      if( !string.Equals( translated, value, StringComparison.Ordinal ) )
      {
         return translated;
      }

      if( value.StartsWith( "FUEL: ", StringComparison.Ordinal ) )
      {
         return "燃料：" + value.Substring( "FUEL: ".Length );
      }

      if( value.StartsWith( "DELTA-V: ", StringComparison.Ordinal ) )
      {
         return "速度增量：" + value.Substring( "DELTA-V: ".Length );
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
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GrammarUtils" ) != null
         && GameTypeResolver.Get( "Condition" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GrammarUtils" ),
         "GetInflectedString",
         new[] { typeof( string ), GameTypeResolver.Get( "Condition" ), GameTypeResolver.Get( "CondOwner" ) } );
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
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GrammarUtils" ) != null && GameTypeResolver.Get( "Interaction" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GrammarUtils" ),
         "GetInflectedString",
         new[] { typeof( string ), GameTypeResolver.Get( "Interaction" ) } );
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
      strTitle = OstranautsTranslatorPlugin.Translate( strTitle, "GUITooltip2.SetToolTip.title" );
      strBody = OstranautsTranslatorPlugin.Translate( strBody, "GUITooltip2.SetToolTip.body" );
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
      strTitle = OstranautsTranslatorPlugin.Translate( strTitle, "GUITooltip2.SetToolTip_1.title" );
      strBody = OstranautsTranslatorPlugin.Translate( strBody, "GUITooltip2.SetToolTip_1.body" );
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
      RuntimeTextRescanner.ForceScanAll();
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
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "txtRange", "GUIOrbitDraw.UpdateUIs" );
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
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "LeftPanelData", "GUIMFDDisplay.ShowMenu" );
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "RightPanelData", "GUIMFDDisplay.ShowMenu" );
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "TopPanelData", "GUIMFDDisplay.ShowMenu" );
      RuntimeHookTranslationHelper.TranslateSidePanelList( mfdDto, "BottomPanelData", "GUIMFDDisplay.ShowMenu" );
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
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "txtTargetStatus", "NavModMooringControl.UpdateText" );
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
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "txtRNGETA", "GUIDockSys.SetUI" );
      RuntimeHookTranslationHelper.TranslateTextComponentField( __instance, "txtBRGVCRS", "GUIDockSys.SetUI" );
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
      var field = __instance?.GetType().GetField( "txtFuel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      var component = field?.GetValue( __instance );
      if( component == null ) return;

      var textProperty = component.GetType().GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
      if( textProperty == null || !textProperty.CanRead || !textProperty.CanWrite || textProperty.PropertyType != typeof( string ) ) return;

      var value = textProperty.GetValue( component ) as string;
      textProperty.SetValue( component, ReservesRuntimeTranslationHelper.TranslateFuelText( value ) );
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
      RuntimeTextHookHelper.TranslateHierarchy( RuntimeTextHookHelper.GetGameObject( __instance ), "MainMenu.Start" );
      RuntimeTextRescanner.ForceScanAll();
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
