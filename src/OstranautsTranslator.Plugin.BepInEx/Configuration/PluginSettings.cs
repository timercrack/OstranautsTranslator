using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Plugin.BepInEx.Configuration;

internal static class PluginSettings
{
   public static ConfigEntry<bool> Enabled { get; private set; }
   public static ConfigEntry<string> Language { get; private set; }
   public static ConfigEntry<string> TranslationDatabasePath { get; private set; }
   public static ConfigEntry<bool> IncludeDraftTranslations { get; private set; }
   public static ConfigEntry<bool> LogSuccessfulTranslations { get; private set; }
   public static ConfigEntry<bool> LogMissingTranslations { get; private set; }
   public static ConfigEntry<bool> CaptureMissingTranslations { get; private set; }
   public static ConfigEntry<string> OverrideFontTextMeshPro { get; private set; }
   public static ConfigEntry<string> ObservedFontCharsetOutputDirectory { get; private set; }
   public static ConfigEntry<bool> ObservedFontCharsetMergeIntoAll { get; private set; }
   public static ConfigEntry<string> RecordedGameVersion { get; private set; }
   public static ConfigEntry<string> RecordedGameBuildSignature { get; private set; }

   public static void Bind( ConfigFile config )
   {
      Enabled = config.Bind( "General", "Enabled", true, "Enable runtime translation lookup for strings not already covered by the native mod." );
      Language = config.Bind( "General", "Language", string.Empty, "Language code used to resolve the runtime translation database path. Leave empty to use the system language." );
      TranslationDatabasePath = config.Bind(
         "General",
         "TranslationDatabasePath",
         RuntimeTranslationDeployment.GetPluginRelativeWorkspaceDatabasePathTemplate(),
         "Path to the unified SQLite translation database. Relative paths are resolved from the BepInEx root." );
      IncludeDraftTranslations = config.Bind( "General", "IncludeDraftTranslations", false, "Include draft translations when loading the runtime translation catalog." );
      OverrideFontTextMeshPro = config.Bind(
         "Fonts",
         "OverrideFontTextMeshPro",
         "notosans",
         "TMPro font asset bundle or resource name used only as fallback fonts. The game's original TMP font stays active; when it lacks glyphs, TMPro can fall back to the configured bundle/resource fonts. If the bundle contains multiple TMP_FontAsset objects, all of them are loaded and registered as fallback fonts. Supports language-specific mappings like zh=foo;default=bar." );
      ObservedFontCharsetOutputDirectory = config.Bind(
         "Diagnostics",
         "ObservedFontCharsetOutputDirectory",
         string.Empty,
         "Optional directory used to dump characters from actually observed TMP font assets. If empty, font charset dumping is disabled. Relative paths are resolved from the BepInEx root. When configured, TMPro override font replacement is suspended so the exporter can capture the original game font charsets." );
      ObservedFontCharsetMergeIntoAll = config.Bind(
         "Diagnostics",
         "ObservedFontCharsetMergeIntoAll",
         true,
         "When ObservedFontCharsetOutputDirectory is configured, also merge all observed TMP font characters into <output dir>\\all.txt." );
      RecordedGameVersion = config.Bind(
         "State",
         "RecordedGameVersion",
         string.Empty,
         "Automatically maintained. Last recorded game version seen by the plugin." );
      RecordedGameBuildSignature = config.Bind(
         "State",
         "RecordedGameBuildSignature",
         string.Empty,
         "Automatically maintained. Last recorded game build signature seen by the plugin." );
      LogSuccessfulTranslations = config.Bind( "Diagnostics", "LogSuccessfulTranslations", false, "Log every translated runtime string." );
      LogMissingTranslations = config.Bind( "Diagnostics", "LogMissingTranslations", false, "Log runtime strings that do not match any translation." );
      CaptureMissingTranslations = config.Bind( "Diagnostics", "CaptureMissingTranslations", true, "Write untranslated runtime strings into the runtime_source table of corpus.sqlite." );

      MigrateLegacyTranslationDatabasePath();
   }

   public static string ResolveOverrideTmpFont()
   {
      return ResolveLanguageSpecificSetting( OverrideFontTextMeshPro.Value, ResolveEffectiveLanguage() );
   }

   public static bool ShouldSuspendOverrideTmpFontForCharsetExport()
   {
      return !string.IsNullOrWhiteSpace( ObservedFontCharsetOutputDirectory?.Value );
   }

   private static void MigrateLegacyTranslationDatabasePath()
   {
      var desiredPathTemplate = RuntimeTranslationDeployment.GetPluginRelativeWorkspaceDatabasePathTemplate();
      var desiredPath = NormalizePathTemplate( desiredPathTemplate );
      var legacyPath = NormalizePathTemplate( RuntimeTranslationDeployment.GetPluginRelativeRuntimeDatabasePathTemplate() );
      var currentPath = NormalizePathTemplate( TranslationDatabasePath?.Value );

      if( string.Equals( currentPath, desiredPath, StringComparison.OrdinalIgnoreCase ) )
      {
         return;
      }

      if( string.IsNullOrWhiteSpace( currentPath ) || string.Equals( currentPath, legacyPath, StringComparison.OrdinalIgnoreCase ) )
      {
         TranslationDatabasePath.Value = desiredPathTemplate;
      }
   }

   private static string NormalizePathTemplate( string path )
   {
      return ( path ?? string.Empty ).Trim().Replace( '/', '\\' );
   }

   private static string ResolveEffectiveLanguage()
   {
      return RuntimeTranslationDeployment.ResolveTargetLanguage( Language?.Value );
   }

   private static string ResolveLanguageSpecificSetting( string rawValue, string language )
   {
      if( string.IsNullOrWhiteSpace( rawValue ) ) return string.Empty;

      if( !TryParseLanguageSpecificMappings( rawValue.Trim(), out var mappings ) )
      {
         return rawValue.Trim();
      }

      foreach( var candidate in EnumerateLanguageCandidates( language ) )
      {
         if( mappings.TryGetValue( candidate, out var resolved ) ) return resolved;
      }

      if( mappings.TryGetValue( "default", out var fallback ) ) return fallback;
      if( mappings.TryGetValue( "*", out fallback ) ) return fallback;
      return string.Empty;
   }

   private static bool TryParseLanguageSpecificMappings( string rawValue, out Dictionary<string, string> mappings )
   {
      mappings = null;
      var entries = rawValue.Split( new[] { ';' }, StringSplitOptions.RemoveEmptyEntries );
      if( entries.Length == 0 ) return false;

      var parsed = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
      foreach( var rawEntry in entries )
      {
         var entry = rawEntry.Trim();
         if( entry.Length == 0 ) continue;

         var separatorIndex = entry.IndexOf( '=' );
         if( separatorIndex <= 0 ) return false;

         var key = entry.Substring( 0, separatorIndex ).Trim();
         var value = entry.Substring( separatorIndex + 1 ).Trim();
         if( key.Length == 0 ) return false;

         parsed[ key ] = value;
      }

      mappings = parsed;
      return parsed.Count > 0;
   }

   private static IEnumerable<string> EnumerateLanguageCandidates( string language )
   {
      if( string.IsNullOrWhiteSpace( language ) ) yield break;

      var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
      var normalized = language.Trim().Replace( '_', '-' );
      if( normalized.Length == 0 ) yield break;

      if( seen.Add( normalized ) ) yield return normalized;

      var underscore = normalized.Replace( '-', '_' );
      if( seen.Add( underscore ) ) yield return underscore;

      var separatorIndex = normalized.IndexOf( '-' );
      if( separatorIndex > 0 )
      {
         var neutral = normalized.Substring( 0, separatorIndex );
         if( seen.Add( neutral ) ) yield return neutral;
      }
   }
}
