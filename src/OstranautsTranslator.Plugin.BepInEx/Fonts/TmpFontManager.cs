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
   private static readonly Dictionary<string, Material> OverrideMaterialCopies = new Dictionary<string, Material>( StringComparer.Ordinal );

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
      OverrideMaterialCopies.Clear();

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
            TmpFontCharsetExporter.ObserveFont( existingFont, "TMP_Settings.defaultFontAsset(before-override)" );
         }

         GetOverrideFonts();
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to initialize TMPro override font state. {e.Message}" );
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
            _logger.LogInfo( $"Replaced TMP fonts with '{_overrideFontName}' on {configuredCount} loaded text components." );
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

         TmpFontCharsetExporter.ObserveFont( previousFont, type.FullName + ".font(before-override)" );

         var replacementFont = GetPrimaryOverrideFont();
         if( replacementFont == null ) return false;

         if( previousFont is UnityEngine.Object previousFontObject
            && replacementFont is UnityEngine.Object replacementFontObject
            && previousFontObject.GetInstanceID() == replacementFontObject.GetInstanceID() )
         {
            return false;
         }

         var fontMaterialProperty = type.GetProperty( "fontSharedMaterial", BindingFlags.Public | BindingFlags.Instance );
         var previousMaterial = fontMaterialProperty?.GetValue( textComponent, null ) as Material;

         if( !IsSameUnityObject( previousFont, replacementFont ) )
         {
            fontProperty.SetValue( textComponent, replacementFont, null );
         }

         if( fontMaterialProperty?.CanWrite == true )
         {
            var replacementMaterial = GetOrCreateOverrideMaterialCopy( previousMaterial, fontMaterialProperty.GetValue( textComponent, null ) as Material );
            if( replacementMaterial != null )
            {
               ApplyReplacementMaterial( type, textComponent, replacementMaterial );
            }
         }

         return !IsSameUnityObject( previousFont, replacementFont );
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to replace TMP font. {e.Message}" );
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
            TmpFontCharsetExporter.RegisterIgnoredFont( font, "configured override font" );
         }

         if( !_globalFallbacksConfigured )
         {
            ConfigureBundleInternalFallbacks( OverrideFonts );
            _globalFallbacksConfigured = true;
         }

         if( OverrideFonts.Count > 0 && !_overrideFontLogged )
         {
            _overrideFontLogged = true;
            if( OverrideFonts.Count > 1 )
            {
               _logger.LogInfo( $"Loaded TMP override font bundle '{_overrideFontName}' with {OverrideFonts.Count} TMP font assets. Primary replacement font: {GetObjectDisplayName( OverrideFonts[ 0 ] )}" );
            }
            else
            {
               _logger.LogInfo( $"Loaded TMP override font: {_overrideFontName}" );
            }
         }
      }

      return OverrideFonts;
   }

   private static UnityEngine.Object GetPrimaryOverrideFont()
   {
      var fonts = GetOverrideFonts();
      return fonts.Count == 0 ? null : fonts[ 0 ];
   }

   private static void LogOverrideSuspendedIfNeeded()
   {
      if( _suspendOverrideLogged ) return;

      _suspendOverrideLogged = true;
      _logger.LogInfo( "ObservedFontCharsetOutputDirectory is configured. Suspended TMPro override font replacement so the exporter can capture the original game font charsets." );
   }

   private static Material GetOrCreateOverrideMaterialCopy( Material styleSourceMaterial, Material atlasSourceMaterial )
   {
      if( styleSourceMaterial == null || atlasSourceMaterial == null ) return null;

      var cacheKey = styleSourceMaterial.GetInstanceID().ToString() + ":" + atlasSourceMaterial.GetInstanceID();
      if( OverrideMaterialCopies.TryGetValue( cacheKey, out var cachedMaterial ) && cachedMaterial != null ) return cachedMaterial;

      var copyMaterial = UnityEngine.Object.Instantiate( atlasSourceMaterial );
      InvokeMaterialMethodIfPresent( copyMaterial, "CopyPropertiesFromMaterial", styleSourceMaterial );
      CopyTexturePropertyIfPresent( copyMaterial, atlasSourceMaterial, "_MainTex" );
      CopyTexturePropertyIfPresent( copyMaterial, atlasSourceMaterial, "_FaceTex" );
      CopyTexturePropertyIfPresent( copyMaterial, atlasSourceMaterial, "_OutlineTex" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_GradientScale" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_TextureWidth" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_TextureHeight" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_OutlineWidth" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_OutlineSoftness" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_UnderlaySoftness" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_UnderlayOffsetX" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_UnderlayOffsetY" );
      CopyFloatPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_UnderlayDilate" );
      CopyColorPropertyIfPresent( copyMaterial, atlasSourceMaterial, "_UnderlayColor" );
      GameObject.DontDestroyOnLoad( copyMaterial );
      OverrideMaterialCopies[ cacheKey ] = copyMaterial;
      return copyMaterial;
   }

   private static void ApplyReplacementMaterial( Type textComponentType, object textComponent, Material replacementMaterial )
   {
      if( textComponentType == null || textComponent == null || replacementMaterial == null ) return;

      SetMaterialPropertyIfWritable( textComponentType, textComponent, "fontSharedMaterial", replacementMaterial );
      SetMaterialPropertyIfWritable( textComponentType, textComponent, "fontMaterial", replacementMaterial );
      SetMaterialPropertyIfWritable( textComponentType, textComponent, "material", replacementMaterial );
      ReplaceFirstMaterialInArrayProperty( textComponentType, textComponent, "fontSharedMaterials", replacementMaterial );
      ReplaceFirstMaterialInArrayProperty( textComponentType, textComponent, "fontMaterials", replacementMaterial );

      InvokeIfPresent( textComponentType, textComponent, "SetSharedMaterial", replacementMaterial );
      InvokeIfPresent( textComponentType, textComponent, "UpdateMeshPadding" );
      InvokeIfPresent( textComponentType, textComponent, "SetMaterialDirty" );
      InvokeIfPresent( textComponentType, textComponent, "SetVerticesDirty" );
   }

   private static void SetMaterialPropertyIfWritable( Type targetType, object target, string propertyName, Material value )
   {
      var property = targetType.GetProperty( propertyName, BindingFlags.Public | BindingFlags.Instance );
      if( property?.CanWrite != true ) return;

      property.SetValue( target, value, null );
   }

   private static void ReplaceFirstMaterialInArrayProperty( Type targetType, object target, string propertyName, Material value )
   {
      var property = targetType.GetProperty( propertyName, BindingFlags.Public | BindingFlags.Instance );
      if( property?.CanRead != true || property.CanWrite != true ) return;

      if( property.GetValue( target, null ) is not Material[] materials || materials.Length == 0 ) return;

      var updatedMaterials = (Material[])materials.Clone();
      updatedMaterials[ 0 ] = value;
      property.SetValue( target, updatedMaterials, null );
   }

   private static void InvokeIfPresent( Type targetType, object target, string methodName, params object[] args )
   {
      var argumentTypes = args.Select( arg => arg?.GetType() ?? typeof( object ) ).ToArray();
      var method = targetType.GetMethod( methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, argumentTypes, null );
      if( method == null && args.Length == 0 )
      {
         method = targetType.GetMethod( methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null );
      }

      method?.Invoke( target, args );
   }

   private static bool IsSameUnityObject( object left, object right )
   {
      if( ReferenceEquals( left, right ) ) return true;
      if( left is not UnityEngine.Object leftObject || right is not UnityEngine.Object rightObject ) return false;

      return leftObject.GetInstanceID() == rightObject.GetInstanceID();
   }

   private static void CopyTexturePropertyIfPresent( Material targetMaterial, Material sourceMaterial, string propertyName )
   {
      if( targetMaterial == null || sourceMaterial == null ) return;
      if( !targetMaterial.HasProperty( propertyName ) || !sourceMaterial.HasProperty( propertyName ) ) return;

      targetMaterial.SetTexture( propertyName, sourceMaterial.GetTexture( propertyName ) );
   }

   private static void CopyFloatPropertyIfPresent( Material targetMaterial, Material sourceMaterial, string propertyName )
   {
      if( targetMaterial == null || sourceMaterial == null ) return;
      if( !targetMaterial.HasProperty( propertyName ) || !sourceMaterial.HasProperty( propertyName ) ) return;

      targetMaterial.SetFloat( propertyName, sourceMaterial.GetFloat( propertyName ) );
   }

   private static void CopyColorPropertyIfPresent( Material targetMaterial, Material sourceMaterial, string propertyName )
   {
      if( targetMaterial == null || sourceMaterial == null ) return;
      if( !targetMaterial.HasProperty( propertyName ) || !sourceMaterial.HasProperty( propertyName ) ) return;

      var colorValue = InvokeMaterialMethodIfPresent( sourceMaterial, "GetColor", propertyName );
      if( colorValue != null )
      {
         InvokeMaterialMethodIfPresent( targetMaterial, "SetColor", propertyName, colorValue );
      }
   }

   private static object InvokeMaterialMethodIfPresent( Material material, string methodName, params object[] args )
   {
      if( material == null || string.IsNullOrWhiteSpace( methodName ) ) return null;

      var argumentTypes = args.Select( arg => arg?.GetType() ?? typeof( object ) ).ToArray();
      var method = material.GetType().GetMethod( methodName, BindingFlags.Public | BindingFlags.Instance, null, argumentTypes, null );
      if( method == null ) return null;

      return method.Invoke( material, args );
   }

   private static int EnsureFallbackFontsConfigured( object currentFont )
   {
      var fallbackFonts = GetOverrideFonts();
      if( fallbackFonts.Count == 0 ) return 0;

      var totalAdded = 0;

      if( !_globalFallbacksConfigured )
      {
         totalAdded += ConfigureBundleInternalFallbacks( fallbackFonts );
         _globalFallbacksConfigured = true;

         var fallbackNames = string.Join( ", ", fallbackFonts.Select( GetObjectDisplayName ) );
         _logger.LogInfo( $"Registered per-font TMP fallback font assets from '{_overrideFontName}': [{fallbackNames}]" );
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
      var bundleAssetSummary = string.Empty;
      var bundleWasFound = !string.IsNullOrWhiteSpace( resolvedPath );
      var bundleWasLoaded = false;

      if( resolvedPath != null )
      {
         AssetBundle bundle = null;
         try
         {
            bundle = AssetBundle.LoadFromFile( resolvedPath );
            if( bundle != null )
            {
               bundleWasLoaded = true;
               var typedFonts = bundle.LoadAllAssets( tmpFontAssetType );
               if( typedFonts != null )
               {
                  fonts.AddRange( typedFonts.Where( asset => asset != null ) );
               }

               if( fonts.Count == 0 )
               {
                  var allAssets = bundle.LoadAllAssets().Where( asset => asset != null ).ToArray();
                  fonts.AddRange( allAssets.Where( asset => tmpFontAssetType.IsAssignableFrom( asset.GetType() ) ) );
                  bundleAssetSummary = string.Join( ", ", allAssets.Take( 8 ).Select( asset => asset.GetType().Name + ":" + GetObjectDisplayName( asset ) ) );
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

      if( bundleWasFound )
      {
         if( bundleWasLoaded )
         {
            _logger.LogWarning(
               $"TMP font bundle '{resolvedPath}' was found but no runtime-loadable TMP_FontAsset could be deserialized from it."
               + ( string.IsNullOrWhiteSpace( bundleAssetSummary ) ? string.Empty : $" Loaded assets: {bundleAssetSummary}" ) );
         }
         else
         {
            _logger.LogWarning( $"TMP font bundle path was resolved but the bundle could not be loaded: '{resolvedPath}'." );
         }

         return Array.Empty<UnityEngine.Object>();
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
