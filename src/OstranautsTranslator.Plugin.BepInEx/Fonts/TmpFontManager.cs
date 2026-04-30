using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using OstranautsTranslator.Plugin.BepInEx.Configuration;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx.Fonts;

internal static class TmpFontManager
{
   private static readonly List<UnityEngine.Object> OverrideFonts = new List<UnityEngine.Object>();

   private static ManualLogSource _logger;
   private static string _overrideFontName;
   private static bool _overrideLoaded;
   private static bool _overrideFontLogged;
   private static bool _globalFallbacksConfigured;
   private static bool _suspendOverrideForCharsetExport;
   private static bool _suspendOverrideLogged;

   public static void Initialize( ManualLogSource logger, string overrideFontName )
   {
      _logger = logger;
      _overrideFontName = overrideFontName ?? string.Empty;
      _overrideLoaded = false;
      _overrideFontLogged = false;
      _globalFallbacksConfigured = false;
      _suspendOverrideForCharsetExport = PluginSettings.ShouldSuspendOverrideTmpFontForCharsetExport();
      _suspendOverrideLogged = false;
      OverrideFonts.Clear();

      TmpFontCharsetExporter.Initialize( logger, PluginSettings.ObservedFontCharsetOutputDirectory.Value, PluginSettings.ObservedFontCharsetMergeIntoAll.Value );

      if( _suspendOverrideForCharsetExport )
      {
         LogOverrideSuspendedIfNeeded();
      }
   }

   public static void Tick()
   {
      TmpFontCharsetExporter.Tick();
   }

   public static void ApplyDefaultFontAsset()
   {
      try
      {
         var settingsType = Type.GetType( "TMPro.TMP_Settings, Unity.TextMeshPro", false );
         if( settingsType == null ) return;

         var defaultFontProperty = settingsType.GetProperty( "defaultFontAsset", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance );
         if( defaultFontProperty == null || !defaultFontProperty.CanWrite ) return;

         object target = null;
         var accessor = defaultFontProperty.GetMethod ?? defaultFontProperty.SetMethod;
         if( accessor != null && !accessor.IsStatic )
         {
            var instanceProperty = settingsType.GetProperty( "instance", BindingFlags.Public | BindingFlags.Static )
               ?? settingsType.GetProperty( "Instance", BindingFlags.Public | BindingFlags.Static );
            target = instanceProperty?.GetValue( null, null );
            if( target == null ) return;
         }

         var existingFont = defaultFontProperty.GetValue( target, null );
         if( _suspendOverrideForCharsetExport )
         {
            TmpFontCharsetExporter.ObserveFont( existingFont, "TMP_Settings.defaultFontAsset(export-only)" );
            return;
         }

         if( existingFont != null )
         {
            TmpFontCharsetExporter.ObserveFont( existingFont, "TMP_Settings.defaultFontAsset(with-fallbacks)" );
         }

         EnsureFallbackFontsConfigured( existingFont );
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to configure TMPro default font fallbacks. {e.Message}" );
      }
   }

   public static int ApplyOverrideFontToLoadedTextComponents()
   {
      var tmpTextType = Type.GetType( "TMPro.TMP_Text, Unity.TextMeshPro", false );
      if( tmpTextType == null ) return 0;

      try
      {
         var objects = Resources.FindObjectsOfTypeAll( tmpTextType );
         var configuredCount = 0;
         if( objects != null )
         {
            foreach( var textComponent in objects )
            {
               if( ApplyOverrideFont( textComponent ) )
               {
                  configuredCount++;
               }
            }
         }

         TmpFontCharsetExporter.ObserveLoadedFontAssetsSnapshot( null );

         if( _suspendOverrideForCharsetExport )
         {
            LogOverrideSuspendedIfNeeded();
         }
         else if( configuredCount > 0 )
         {
            _logger.LogInfo( $"Configured TMP fallback fonts from '{_overrideFontName}' for {configuredCount} loaded text components." );
         }

         return configuredCount;
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed while sweeping loaded TMP text components for fallback font configuration. {e.Message}" );
         return 0;
      }
   }

   public static bool ApplyOverrideFont( object textComponent )
   {
      if( textComponent == null ) return false;

      try
      {
         var type = textComponent.GetType();
         var fontProperty = type.GetProperty( "font", BindingFlags.Public | BindingFlags.Instance );
         if( fontProperty == null || !fontProperty.CanWrite ) return false;

         var previousFont = fontProperty.GetValue( textComponent, null );
         if( previousFont == null ) return false;

         if( _suspendOverrideForCharsetExport )
         {
            TmpFontCharsetExporter.ObserveFont( previousFont, type.FullName + ".font(export-only)" );
            LogOverrideSuspendedIfNeeded();
            return false;
         }

         TmpFontCharsetExporter.ObserveFont( previousFont, type.FullName + ".font(with-fallbacks)" );
         return EnsureFallbackFontsConfigured( previousFont ) > 0;
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to configure TMP fallback fonts. {e.Message}" );
         return false;
      }
   }

   private static IReadOnlyList<UnityEngine.Object> GetOverrideFonts()
   {
      if( _suspendOverrideForCharsetExport )
      {
         LogOverrideSuspendedIfNeeded();
         return Array.Empty<UnityEngine.Object>();
      }

      if( !_overrideLoaded )
      {
         _overrideLoaded = true;
         var loadedFonts = LoadTextMeshProFonts( _overrideFontName );
         OverrideFonts.Clear();
         OverrideFonts.AddRange( loadedFonts );

         foreach( var font in OverrideFonts )
         {
            TmpFontCharsetExporter.RegisterIgnoredFont( font, "configured fallback font" );
         }

         if( OverrideFonts.Count > 0 && !_overrideFontLogged )
         {
            _overrideFontLogged = true;
            if( OverrideFonts.Count > 1 )
            {
               _logger.LogInfo( $"Loaded TMP fallback font bundle '{_overrideFontName}' with {OverrideFonts.Count} TMP font assets. Primary fallback candidate: {GetObjectDisplayName( OverrideFonts[ 0 ] )}" );
            }
            else
            {
               _logger.LogInfo( $"Loaded TMP fallback font: {_overrideFontName}" );
            }
         }
      }

      return OverrideFonts;
   }

   private static void LogOverrideSuspendedIfNeeded()
   {
      if( _suspendOverrideLogged ) return;

      _suspendOverrideLogged = true;
      _logger.LogInfo( "ObservedFontCharsetOutputDirectory is configured. Suspended TMPro override font replacement so the exporter can capture the original game font charsets." );
   }

   private static int EnsureFallbackFontsConfigured( object currentFont )
   {
      var fallbackFonts = GetOverrideFonts();
      if( fallbackFonts.Count == 0 ) return 0;

      var totalAdded = 0;

      if( !_globalFallbacksConfigured )
      {
         totalAdded += MergeTmpSettingsFallbackFonts( fallbackFonts );
         totalAdded += ConfigureBundleInternalFallbacks( fallbackFonts );
         _globalFallbacksConfigured = true;

         var fallbackNames = string.Join( ", ", fallbackFonts.Select( GetObjectDisplayName ) );
         _logger.LogInfo( $"Registered TMP fallback font assets from '{_overrideFontName}': [{fallbackNames}]" );
      }

      if( currentFont != null )
      {
         totalAdded += MergeFontFallbacks( currentFont, fallbackFonts );
      }

      return totalAdded;
   }

   private static int ConfigureBundleInternalFallbacks( IEnumerable<UnityEngine.Object> fallbackFonts )
   {
      var fonts = fallbackFonts.Where( font => font != null ).ToArray();
      var totalAdded = 0;

      foreach( var font in fonts )
      {
         totalAdded += MergeFontFallbacks( font, fonts, font );
      }

      return totalAdded;
   }

   private static int MergeFontFallbacks( object primaryFont, IEnumerable<UnityEngine.Object> fallbackFonts, params object[] excludedFonts )
   {
      if( primaryFont == null ) return 0;

      var fallbackProperty = primaryFont.GetType().GetProperty( "fallbackFontAssetTable", BindingFlags.Public | BindingFlags.Instance );
      if( fallbackProperty == null )
      {
         _logger.LogWarning( $"TMP font '{GetObjectDisplayName( primaryFont )}' does not expose fallbackFontAssetTable; skipping per-font fallback configuration." );
         return 0;
      }

      var list = fallbackProperty.GetValue( primaryFont, null ) as IList;
      if( list == null )
      {
         _logger.LogWarning( $"TMP font '{GetObjectDisplayName( primaryFont )}' fallbackFontAssetTable is unavailable; skipping per-font fallback configuration." );
         return 0;
      }

      return AddUniqueFontAssets( list, fallbackFonts, excludedFonts );
   }

   private static int MergeTmpSettingsFallbackFonts( IEnumerable<UnityEngine.Object> fallbackFonts )
   {
      var settingsType = Type.GetType( "TMPro.TMP_Settings, Unity.TextMeshPro", false );
      if( settingsType == null ) return 0;

      var fallbackProperty = settingsType.GetProperty( "fallbackFontAssets", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance );
      if( fallbackProperty == null ) return 0;

      object target = null;
      var accessor = fallbackProperty.GetMethod ?? fallbackProperty.SetMethod;
      if( accessor != null && !accessor.IsStatic )
      {
         var instanceProperty = settingsType.GetProperty( "instance", BindingFlags.Public | BindingFlags.Static )
            ?? settingsType.GetProperty( "Instance", BindingFlags.Public | BindingFlags.Static );
         target = instanceProperty?.GetValue( null, null );
         if( target == null ) return 0;
      }

      var list = fallbackProperty.GetValue( target, null ) as IList;
      if( list == null ) return 0;

      return AddUniqueFontAssets( list, fallbackFonts );
   }

   private static int AddUniqueFontAssets( IList targetList, IEnumerable<UnityEngine.Object> fonts, params object[] excludedFonts )
   {
      if( targetList == null ) return 0;

      var excludedIds = new HashSet<int>( excludedFonts.Where( font => font is UnityEngine.Object ).Cast<UnityEngine.Object>().Where( font => font != null ).Select( font => font.GetInstanceID() ) );
      var existingIds = new HashSet<int>();
      foreach( var entry in targetList )
      {
         if( entry is UnityEngine.Object existingFont && existingFont != null )
         {
            existingIds.Add( existingFont.GetInstanceID() );
         }
      }

      var added = 0;
      foreach( var font in fonts )
      {
         if( font == null ) continue;

         var instanceId = font.GetInstanceID();
         if( excludedIds.Contains( instanceId ) || !existingIds.Add( instanceId ) ) continue;

         targetList.Add( font );
         added++;
      }

      return added;
   }

   private static string GetObjectDisplayName( object value )
   {
      if( value == null ) return "(null)";

      try
      {
         var nameProperty = value.GetType().GetProperty( "name", BindingFlags.Public | BindingFlags.Instance );
         var name = nameProperty?.GetValue( value, null ) as string;
         if( !string.IsNullOrWhiteSpace( name ) ) return name;
      }
      catch
      {
      }

      return value.GetType().Name;
   }

   private static IReadOnlyList<UnityEngine.Object> LoadTextMeshProFonts( string assetName )
   {
      if( string.IsNullOrWhiteSpace( assetName ) ) return Array.Empty<UnityEngine.Object>();

      var trimmedAssetName = assetName.Trim();
      var tmpFontAssetType = Type.GetType( "TMPro.TMP_FontAsset, Unity.TextMeshPro", false );
      if( tmpFontAssetType == null )
      {
         _logger.LogWarning( "TMPro.TMP_FontAsset type is unavailable. Skipping font load." );
         return Array.Empty<UnityEngine.Object>();
      }

      var resolvedPath = FindAssetBundlePath( trimmedAssetName );
      var fonts = new List<UnityEngine.Object>();

      if( resolvedPath != null )
      {
         AssetBundle bundle = null;
         try
         {
            bundle = AssetBundle.LoadFromFile( resolvedPath );
            if( bundle != null )
            {
               var typedFonts = bundle.LoadAllAssets( tmpFontAssetType );
               if( typedFonts != null )
               {
                  fonts.AddRange( typedFonts.Where( asset => asset != null ) );
               }

               if( fonts.Count == 0 )
               {
                  fonts.AddRange( bundle.LoadAllAssets().Where( asset => asset != null && tmpFontAssetType.IsAssignableFrom( asset.GetType() ) ) );
               }
            }
         }
         catch( Exception e )
         {
            _logger.LogWarning( $"Failed to load TMP font asset bundle '{resolvedPath}'. {e.Message}" );
         }
         finally
         {
            if( bundle != null )
            {
               bundle.Unload( false );
            }
         }
      }

      if( fonts.Count == 0 )
      {
         try
         {
            var font = Resources.Load( trimmedAssetName );
            if( font != null )
            {
               fonts.Add( font );
            }
         }
         catch( Exception e )
         {
            _logger.LogWarning( $"Failed to load TMP font '{trimmedAssetName}' from Resources. {e.Message}" );
         }
      }

      if( fonts.Count > 0 )
      {
         var uniqueFonts = new List<UnityEngine.Object>();
         var seenFontIds = new HashSet<int>();
         foreach( var font in fonts )
         {
            if( font == null || !seenFontIds.Add( font.GetInstanceID() ) ) continue;

            GameObject.DontDestroyOnLoad( font );
            uniqueFonts.Add( font );
         }

         return uniqueFonts;
      }

      _logger.LogWarning( $"Could not locate TMP font asset '{trimmedAssetName}'." );
      return Array.Empty<UnityEngine.Object>();
   }

   private static string FindAssetBundlePath( string assetName )
   {
      foreach( var directory in EnumerateBundleSearchDirectories() )
      {
         if( string.IsNullOrWhiteSpace( directory ) || !Directory.Exists( directory ) ) continue;

         string fallback = null;
         foreach( var file in Directory.GetFiles( directory ) )
         {
            var fileName = Path.GetFileName( file );
            if( string.Equals( fileName, assetName, StringComparison.OrdinalIgnoreCase ) )
            {
               return file;
            }

            if( fallback == null && string.Equals( Path.GetFileNameWithoutExtension( file ), assetName, StringComparison.OrdinalIgnoreCase ) )
            {
               fallback = file;
            }
         }

         if( fallback != null ) return fallback;
      }

      return null;
   }

   private static IEnumerable<string> EnumerateBundleSearchDirectories()
   {
      yield return Paths.GameRootPath;

      var pluginDirectory = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );
      if( !string.IsNullOrWhiteSpace( pluginDirectory ) )
      {
         yield return pluginDirectory;
      }

      var bepinexRoot = Paths.BepInExRootPath;
      if( Directory.Exists( bepinexRoot ) )
      {
         yield return bepinexRoot;

         var pluginsDirectory = Path.Combine( bepinexRoot, "plugins" );
         if( Directory.Exists( pluginsDirectory ) )
         {
            yield return pluginsDirectory;
         }
      }
   }
}
