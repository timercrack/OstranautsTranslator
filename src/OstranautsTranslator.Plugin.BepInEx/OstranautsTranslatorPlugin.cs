using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using OstranautsTranslator.Core;
using OstranautsTranslator.Core.Storage;
using OstranautsTranslator.Plugin.BepInEx.Configuration;
using OstranautsTranslator.Plugin.BepInEx.Fonts;
using OstranautsTranslator.Plugin.BepInEx.Input;
using OstranautsTranslator.Plugin.BepInEx.UI;
using SQLitePCL;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx;

[BepInPlugin( PluginData.Identifier, PluginData.Name, PluginData.Version )]
public sealed class OstranautsTranslatorPlugin : BaseUnityPlugin
{
   private const KeyCode StatusWindowToggleKey = KeyCode.F6;
   private static bool _runtimeProxyCreated;
   private static bool _sqliteRuntimeInitialized;
   private Harmony _harmony;
   private RuntimeTranslationCatalog _catalog = RuntimeTranslationCatalog.Empty;
   private RuntimeMissCollector _runtimeMissCollector;
   private readonly TranslationRuntimeStatistics _translationStatistics = new TranslationRuntimeStatistics();
   private TranslationStatusWindow _statusWindow;
   private bool _inputSupported = true;
   private bool _inputLoopLogged;
   private string _currentGameVersion = string.Empty;
   private string _recordedGameVersion = string.Empty;
   private string _gameVersionStatus = "未检查";
   private string _gameVersionReminder = string.Empty;

   internal static OstranautsTranslatorPlugin Instance { get; private set; }

   private void Awake()
   {
      try
      {
         Instance = this;
         _runtimeMissCollector = new RuntimeMissCollector( Logger );

         PluginSettings.Bind( Config );
         _statusWindow = new TranslationStatusWindow( CreateStatusSnapshot );
         EvaluateGameVersionState();
         Logger.LogInfo( $"Status window hotkey: {StatusWindowToggleKey}" );
         Logger.LogInfo( $"Status window input backend: {UnityInput.BackendName}" );
         if( !UnityInput.IsAvailable && !string.IsNullOrWhiteSpace( UnityInput.InitializationError ) )
         {
            Logger.LogWarning( $"Failed to initialize any supported status window input backend. {UnityInput.InitializationError}" );
         }

         Logger.LogInfo( "Initializing status window runtime host..." );
         EnsureRuntimeProxy();

         Logger.LogInfo( "Initializing SQLite runtime..." );
         EnsureSqliteRuntime();

         Logger.LogInfo( "Loading runtime translation catalog..." );
         ReloadCatalog();

         Logger.LogInfo( "Initializing TMPro font manager..." );
         TmpFontManager.Initialize( Logger, PluginSettings.ResolveOverrideTmpFont() );
         TmpFontManager.ApplyDefaultFontAsset();

         Logger.LogInfo( "Installing Harmony patches..." );
         _harmony = new Harmony( PluginData.Identifier );
         _harmony.PatchAll( typeof( OstranautsTranslatorPlugin ).Assembly );

         Logger.LogInfo( "Applying configured TMP fallback fonts to currently loaded text components..." );
         TmpFontManager.ApplyOverrideFontToLoadedTextComponents();

         Logger.LogInfo( "OstranautsTranslator initialization complete." );
      }
      catch( Exception e )
      {
         Logger.LogError( $"An unexpected error occurred during OstranautsTranslator initialization.{Environment.NewLine}{e}" );
      }
   }

   private void OnDestroy()
   {
      _harmony?.UnpatchSelf();
      if( ReferenceEquals( Instance, this ) )
      {
         Instance = null;
      }
   }

   internal void TickRuntime()
   {
      TmpFontManager.Tick();

      if( !_inputLoopLogged )
      {
         _inputLoopLogged = true;
         Logger.LogInfo( "Status window input loop active." );
      }

      HandleStatusWindowHotkeySafe();
   }

   internal void RenderRuntimeGui()
   {
      if( _statusWindow == null || !_statusWindow.IsShown )
      {
         return;
      }

      try
      {
         _statusWindow.OnGUI();
      }
      catch( Exception e )
      {
         Logger.LogError( $"Failed to render OstranautsTranslator status window.{Environment.NewLine}{e}" );
         _statusWindow.IsShown = false;
      }
   }

   private void EnsureRuntimeProxy()
   {
      if( _runtimeProxyCreated )
      {
         return;
      }

      try
      {
         var hostObject = new GameObject( "___OstranautsTranslator" );
         hostObject.hideFlags = HideFlags.HideAndDontSave;
         var proxy = (OstranautsTranslatorRuntimeProxyBehaviour)hostObject.AddComponent( typeof( OstranautsTranslatorRuntimeProxyBehaviour ) );
         proxy.Initialize( this );
         GameObject.DontDestroyOnLoad( hostObject );

         _runtimeProxyCreated = true;
         Logger.LogInfo( "Status window runtime host: DedicatedMonoBehaviour" );
      }
      catch( Exception e )
      {
         Logger.LogError( $"Failed to create status window runtime host.{Environment.NewLine}{e}" );
      }
   }

   internal static string Translate( string value, string hookName )
   {
      var instance = Instance;
      if( instance == null || !PluginSettings.Enabled.Value || string.IsNullOrEmpty( value ) )
      {
         return value;
      }

      return instance.TranslateCore( value, hookName );
   }

   private void ReloadCatalog()
   {
      if( !PluginSettings.Enabled.Value )
      {
         _catalog = RuntimeTranslationCatalog.Empty;
         Logger.LogInfo( "OstranautsTranslator is disabled through configuration." );
         return;
      }

      var databasePath = ResolveDatabasePath();
      _runtimeMissCollector.Initialize( databasePath );

      if( !File.Exists( databasePath ) )
      {
         _catalog = RuntimeTranslationCatalog.Empty;
         Logger.LogWarning( $"Runtime translation database was not found: '{databasePath}'." );
         return;
      }

      try
      {
         var language = RuntimeTranslationDeployment.ResolveTargetLanguage( PluginSettings.Language.Value );
         _catalog = SqliteTranslationCatalogLoader.Load( databasePath, language, PluginSettings.IncludeDraftTranslations.Value );
         Logger.LogInfo( $"Loaded {_catalog.LoadedTranslationCount} runtime translations from '{databasePath}'." );
      }
      catch( Exception e )
      {
         _catalog = RuntimeTranslationCatalog.Empty;
         Logger.LogError( $"Failed to load runtime translation database '{databasePath}'.{Environment.NewLine}{e}" );
      }
   }

   private void EvaluateGameVersionState()
   {
      _recordedGameVersion = NormalizeConfigValue( PluginSettings.RecordedGameVersion.Value );
      var recordedGameBuildSignature = NormalizeConfigValue( PluginSettings.RecordedGameBuildSignature.Value );

      if( !RuntimeGameVersionResolver.TryResolveCurrentGameBuild( out var gameBuildInfo, out var errorMessage ) )
      {
         _currentGameVersion = string.Empty;
         _gameVersionStatus = "无法读取";
         _gameVersionReminder = "无法读取当前游戏版本，版本变更提醒不可用。";
         Logger.LogWarning( $"Failed to resolve the current Ostranauts game version from Unity resources. {errorMessage}" );
         return;
      }

      _currentGameVersion = gameBuildInfo.DisplayVersion;

      if( string.IsNullOrWhiteSpace( recordedGameBuildSignature ) )
      {
         _recordedGameVersion = gameBuildInfo.DisplayVersion;
         _gameVersionStatus = "已初始化";
         _gameVersionReminder = string.Empty;
         PersistRecordedGameVersion( gameBuildInfo );
         Logger.LogInfo( $"Recorded current game version: {_currentGameVersion}" );
         return;
      }

      if( !string.Equals( recordedGameBuildSignature, gameBuildInfo.Signature, StringComparison.Ordinal ) )
      {
         var previousRecordedGameVersion = string.IsNullOrWhiteSpace( _recordedGameVersion )
            ? "(unknown)"
            : _recordedGameVersion;

         _gameVersionStatus = "检测到变更";
         _gameVersionReminder = $"检测到游戏版本变化，请运行 {RuntimeTranslationDeployment.ToolExecutableName}.exe 更新翻译。";
         Logger.LogWarning( $"Detected game version change: {previousRecordedGameVersion} -> {_currentGameVersion}. Run {RuntimeTranslationDeployment.ToolExecutableName}.exe to refresh translations." );
         if( _statusWindow != null )
         {
            _statusWindow.IsShown = true;
         }

         PersistRecordedGameVersion( gameBuildInfo );
         return;
      }

      if( string.IsNullOrWhiteSpace( _recordedGameVersion ) )
      {
         _recordedGameVersion = gameBuildInfo.DisplayVersion;
      }

      _gameVersionStatus = "未变化";
      _gameVersionReminder = string.Empty;
      Logger.LogInfo( $"Current game version: {_currentGameVersion}" );
   }

   private void PersistRecordedGameVersion( GameBuildInfo gameBuildInfo )
   {
      PluginSettings.RecordedGameVersion.Value = gameBuildInfo.DisplayVersion;
      PluginSettings.RecordedGameBuildSignature.Value = gameBuildInfo.Signature;

      try
      {
         Config.Save();
      }
      catch( Exception e )
      {
         Logger.LogWarning( $"Failed to persist recorded game version state.{Environment.NewLine}{e.Message}" );
      }
   }

   private static string NormalizeConfigValue( string value )
   {
      return string.IsNullOrWhiteSpace( value )
         ? string.Empty
         : value.Trim();
   }

   private string TranslateCore( string value, string hookName )
   {
      try
      {
         if( string.IsNullOrWhiteSpace( _catalog.DatabasePath ) )
         {
            return value;
         }

         _translationStatistics.RecordLookup();

         var lookupResult = _catalog.Lookup( value, out var translated );
         if( lookupResult == RuntimeTranslationLookupResult.IgnoredNativeModSource )
         {
            _translationStatistics.RecordIgnoredNativeModSource( value );
            return value;
         }

         if( lookupResult == RuntimeTranslationLookupResult.Translated )
         {
            _translationStatistics.RecordDatabaseHit( value, translated );

            if( PluginSettings.LogSuccessfulTranslations.Value )
            {
               Logger.LogDebug( $"[{hookName}] {value} -> {translated}" );
            }

            return translated;
         }

         if( PluginSettings.CaptureMissingTranslations.Value )
         {
            if( _runtimeMissCollector.Capture( _catalog.DatabasePath, _catalog.Configuration, value ) )
            {
               _translationStatistics.RecordCapturedMiss();
            }
         }

         _translationStatistics.RecordDatabaseMiss( value );

         if( PluginSettings.LogMissingTranslations.Value )
         {
            Logger.LogDebug( $"[{hookName}] missing translation for: {value}" );
         }
      }
      catch( Exception e )
      {
         _translationStatistics.RecordError();
         Logger.LogError( $"Failed to translate runtime string from '{hookName}'.{Environment.NewLine}{e}" );
      }

      return value;
   }

   private string ResolveDatabasePath()
   {
      return ResolveConfiguredPath( PluginSettings.TranslationDatabasePath.Value, RuntimeTranslationDeployment.GetPluginRelativeWorkspaceDatabasePathTemplate() );
   }

   private string ResolveConfiguredPath( string configuredPathValue, string defaultRelativePath )
   {
      var language = RuntimeTranslationDeployment.ResolveTargetLanguage( PluginSettings.Language.Value );
      var configuredPath = NormalizeConfiguredPath( configuredPathValue, defaultRelativePath, language );
      if( TryResolveConfiguredPath( configuredPath, out var resolvedPath, out var errorMessage ) )
      {
         return resolvedPath;
      }

      throw new InvalidOperationException( $"Failed to resolve configured path '{EscapePathValue( configuredPathValue )}' (normalized: '{configuredPath}'). {errorMessage}" );
   }

   private static string NormalizeConfiguredPath( string configuredPathValue, string defaultRelativePath, string language )
   {
      var configuredPath = RemoveControlCharacters( configuredPathValue ?? string.Empty )
         .Trim()
         .Trim( '"' )
         .Replace( '/', '\\' );

      if( string.IsNullOrWhiteSpace( configuredPath ) )
      {
         configuredPath = defaultRelativePath ?? string.Empty;
      }

      return configuredPath
         .Replace( "{Lang}", language )
         .Replace( "{lang}", language );
   }

   private static string RemoveControlCharacters( string value )
   {
      if( string.IsNullOrEmpty( value ) ) return string.Empty;

      var buffer = new char[ value.Length ];
      var length = 0;
      for( var i = 0; i < value.Length; i++ )
      {
         var character = value[ i ];
         if( !char.IsControl( character ) )
         {
            buffer[ length++ ] = character;
         }
      }

      return length == value.Length
         ? value
         : new string( buffer, 0, length );
   }

   private static void EnsureSqliteRuntime()
   {
      if( _sqliteRuntimeInitialized )
      {
         return;
      }

      raw.SetProvider( new SQLite3Provider_winsqlite3() );
      raw.FreezeProvider();
      _sqliteRuntimeInitialized = true;
   }

   private static bool TryResolveConfiguredPath( string configuredPath, out string resolvedPath, out string errorMessage )
   {
      try
      {
         resolvedPath = Path.IsPathRooted( configuredPath )
            ? Path.GetFullPath( configuredPath )
            : Path.GetFullPath( Path.Combine( Paths.BepInExRootPath, configuredPath ) );
         errorMessage = null;
         return true;
      }
      catch( Exception e ) when( e is ArgumentException || e is NotSupportedException || e is PathTooLongException )
      {
         resolvedPath = null;
         errorMessage = e.Message;
         return false;
      }
   }

   private static string EscapePathValue( string pathValue )
   {
      if( pathValue == null ) return "<null>";

      return pathValue
         .Replace( "\r", "\\r" )
         .Replace( "\n", "\\n" )
         .Replace( "\t", "\\t" )
         .Replace( "\0", "\\0" );
   }

   private void HandleStatusWindowHotkeySafe()
   {
      if( !_inputSupported )
      {
         return;
      }

      try
      {
         HandleStatusWindowHotkey();
      }
      catch( Exception e )
      {
         _inputSupported = false;
         Logger.LogWarning( $"Unity input API is unavailable. Disabling OstranautsTranslator status-window hotkey.{Environment.NewLine}{e.Message}" );
      }
   }

   private void HandleStatusWindowHotkey()
   {
      if( UnityInput.Current.GetKeyDown( StatusWindowToggleKey ) )
      {
         if( _statusWindow != null )
         {
            _statusWindow.IsShown = !_statusWindow.IsShown;
            Logger.LogInfo( _statusWindow.IsShown
               ? "Status window shown."
               : "Status window hidden." );
         }
      }
   }

   private TranslationStatusSnapshot CreateStatusSnapshot()
   {
      var databasePath = ResolveDatabasePath();
      return new TranslationStatusSnapshot(
         PluginSettings.Enabled.Value ? "Running" : "Disabled",
         _currentGameVersion,
         _recordedGameVersion,
         _gameVersionStatus,
         _gameVersionReminder,
         RuntimeTranslationDeployment.ResolveTargetLanguage( PluginSettings.Language.Value ),
         ResolveDatabaseStatus( databasePath ),
         _catalog.LoadedTranslationCount,
         _translationStatistics.LookupCount,
         _translationStatistics.DatabaseHitCount,
         _translationStatistics.NativeModIgnoredCount,
         _translationStatistics.ReplacementCount,
         _translationStatistics.UniqueReplacementCount,
         _translationStatistics.UniqueNativeModIgnoredCount,
         _translationStatistics.MissCount,
         _translationStatistics.UniqueMissCount,
         _translationStatistics.CapturedMissCount,
         _translationStatistics.ErrorCount );
   }

   private string ResolveDatabaseStatus( string databasePath )
   {
      if( !PluginSettings.Enabled.Value )
      {
         return "Disabled";
      }

      if( string.IsNullOrWhiteSpace( databasePath ) )
      {
         return "Not configured";
      }

      if( !File.Exists( databasePath ) )
      {
         return "Missing file";
      }

      return _catalog.HasTranslations
         ? "Loaded"
         : "Loaded (0 active translations)";
   }
}
