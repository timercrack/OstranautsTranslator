using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using OstranautsTranslator.Core.Processing;

namespace OstranautsTranslator.Core.Storage;

public static class SqliteTranslationCatalogLoader
{
   public static RuntimeTranslationCatalog Load( string databasePath, string language, bool includeDraft )
   {
      if( string.IsNullOrWhiteSpace( databasePath ) )
      {
         throw new ArgumentException( "A translation database path is required.", nameof( databasePath ) );
      }

      if( !File.Exists( databasePath ) )
      {
         throw new FileNotFoundException( "The runtime translation database was not found.", databasePath );
      }

      using var connection = OpenConnection( databasePath );
      var configuration = LoadConfiguration( connection );
      var translationTable = RuntimeTranslationDeployment.GetTranslationTableName( language );
      var ignoredNativeModSourceLookupKeys = LoadIgnoredNativeModSourceLookupKeys( connection, configuration, translationTable, includeDraft );
      var translationsByRawText = new Dictionary<string, string>( StringComparer.Ordinal );
      var translationsByRuntimeKey = new Dictionary<string, string>( StringComparer.Ordinal );
      var translationsByRenderKey = new Dictionary<string, string>( StringComparer.Ordinal );
      var runtimeKeyCollisionCount = 0;
      var renderKeyCollisionCount = 0;
      var loadedTranslationCount = 0;

      if( !TableExists( connection, translationTable ) )
      {
         return new RuntimeTranslationCatalog(
            configuration,
            databasePath,
            ignoredNativeModSourceLookupKeys,
            translationsByRawText,
            translationsByRuntimeKey,
            translationsByRenderKey,
            loadedTranslationCount,
            runtimeKeyCollisionCount,
            renderKeyCollisionCount );
      }

      using var command = connection.CreateCommand();
      command.CommandText = includeDraft
         ? $@"SELECT s.raw_text, t.translated_text
FROM runtime_source s
INNER JOIN {translationTable} t ON t.source_kind = 'runtime' AND t.source_id = s.id
WHERE s.state = 'active'
   AND t.translated_text IS NOT NULL
   AND t.translated_text <> ''
ORDER BY s.occurrence_count DESC, s.id;"
         : $@"SELECT s.raw_text, t.translated_text
FROM runtime_source s
INNER JOIN {translationTable} t ON t.source_kind = 'runtime' AND t.source_id = s.id
WHERE s.state = 'active'
   AND t.translation_state = 'final'
   AND t.translated_text IS NOT NULL
   AND t.translated_text <> ''
ORDER BY s.occurrence_count DESC, s.id;";

      using var reader = command.ExecuteReader();
      while( reader.Read() )
      {
         loadedTranslationCount++;

         var rawText = reader.GetString( 0 );
         var projection = RuntimeTextProjector.CreateProjection( rawText, configuration );
         var runtimeKey = projection.RuntimeKey;
         var renderKey = projection.RenderKey;
         var textKind = projection.TextKind;
         var translatedText = reader.GetString( 1 );

         if( !string.IsNullOrWhiteSpace( rawText ) && !translationsByRawText.ContainsKey( rawText ) )
         {
            translationsByRawText.Add( rawText, translatedText );
         }

         if( !string.IsNullOrWhiteSpace( runtimeKey ) )
         {
            if( translationsByRuntimeKey.ContainsKey( runtimeKey ) )
            {
               runtimeKeyCollisionCount++;
            }
            else
            {
               translationsByRuntimeKey.Add( runtimeKey, translatedText );
            }
         }

         if( !string.IsNullOrWhiteSpace( renderKey ) )
         {
            var renderLookupKey = RuntimeTranslationCatalog.CreateRenderLookupKey( textKind, renderKey );
            if( translationsByRenderKey.ContainsKey( renderLookupKey ) )
            {
               renderKeyCollisionCount++;
            }
            else
            {
               translationsByRenderKey.Add( renderLookupKey, translatedText );
            }
         }
      }

      return new RuntimeTranslationCatalog(
         configuration,
         databasePath,
         ignoredNativeModSourceLookupKeys,
         translationsByRawText,
         translationsByRuntimeKey,
         translationsByRenderKey,
         loadedTranslationCount,
         runtimeKeyCollisionCount,
         renderKeyCollisionCount );
   }

   private static HashSet<string> LoadIgnoredNativeModSourceLookupKeys( DbConnection connection, RuntimeTextProcessingConfiguration configuration, string translationTable, bool includeDraft )
   {
      var lookupKeys = new HashSet<string>( StringComparer.Ordinal );
      if( !TableExists( connection, "native_mod_source" ) )
      {
         return lookupKeys;
      }

      using var command = connection.CreateCommand();
      var hasIsTranslatableColumn = ColumnExists( connection, "native_mod_source", "is_translatable" );
      if( TableExists( connection, translationTable ) )
      {
         command.CommandText = hasIsTranslatableColumn
            ? ( includeDraft
               ? $@"SELECT s.raw_text
FROM native_mod_source s
LEFT JOIN {translationTable} t
   ON t.source_kind = 'native_mod'
  AND t.source_id = s.id
  AND COALESCE(s.is_translatable, 1) = 1
  AND t.translated_text IS NOT NULL
  AND t.translated_text <> ''
WHERE s.state = 'active'
  AND (COALESCE(s.is_translatable, 1) = 0 OR t.id IS NULL)
ORDER BY s.occurrence_count DESC, s.id;"
               : $@"SELECT s.raw_text
FROM native_mod_source s
LEFT JOIN {translationTable} t
   ON t.source_kind = 'native_mod'
  AND t.source_id = s.id
  AND COALESCE(s.is_translatable, 1) = 1
  AND t.translation_state = 'final'
  AND t.translated_text IS NOT NULL
  AND t.translated_text <> ''
WHERE s.state = 'active'
  AND (COALESCE(s.is_translatable, 1) = 0 OR t.id IS NULL)
ORDER BY s.occurrence_count DESC, s.id;" )
            : ( includeDraft
               ? $@"SELECT s.raw_text
FROM native_mod_source s
LEFT JOIN {translationTable} t
   ON t.source_kind = 'native_mod'
  AND t.source_id = s.id
  AND t.translated_text IS NOT NULL
  AND t.translated_text <> ''
WHERE s.state = 'active'
  AND t.id IS NULL
ORDER BY s.occurrence_count DESC, s.id;"
               : $@"SELECT s.raw_text
FROM native_mod_source s
LEFT JOIN {translationTable} t
   ON t.source_kind = 'native_mod'
  AND t.source_id = s.id
  AND t.translation_state = 'final'
  AND t.translated_text IS NOT NULL
  AND t.translated_text <> ''
WHERE s.state = 'active'
  AND t.id IS NULL
ORDER BY s.occurrence_count DESC, s.id;" );
      }
      else
      {
         command.CommandText = @"SELECT raw_text
FROM native_mod_source
WHERE state = 'active'
ORDER BY occurrence_count DESC, id;";
      }

      using var reader = command.ExecuteReader();
      while( reader.Read() )
      {
         var rawText = reader.GetString( 0 );
         var projection = RuntimeTextProjector.CreateProjection( rawText, configuration );
         lookupKeys.Add( RuntimeTranslationCatalog.CreateRenderLookupKey( projection.TextKind, projection.RenderKey ) );
      }

      return lookupKeys;
   }

   private static bool TableExists( DbConnection connection, string tableName )
   {
      using var command = connection.CreateCommand();
      command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
      var parameter = command.CreateParameter();
      parameter.ParameterName = "@name";
      parameter.Value = tableName;
      command.Parameters.Add( parameter );
      return Convert.ToInt64( command.ExecuteScalar(), CultureInfo.InvariantCulture ) > 0;
   }

   private static bool ColumnExists( DbConnection connection, string tableName, string columnName )
   {
      using var command = connection.CreateCommand();
      command.CommandText = $"PRAGMA table_info({tableName});";

      using var reader = command.ExecuteReader();
      while( reader.Read() )
      {
         if( string.Equals( reader.GetString( 1 ), columnName, StringComparison.OrdinalIgnoreCase ) )
         {
            return true;
         }
      }

      return false;
   }

   private static RuntimeTextProcessingConfiguration LoadConfiguration( DbConnection connection )
   {
      var schemaMeta = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

      using var command = connection.CreateCommand();
      command.CommandText = "SELECT key, value FROM schema_meta;";

      using var reader = command.ExecuteReader();
      while( reader.Read() )
      {
         schemaMeta[ reader.GetString( 0 ) ] = reader.GetString( 1 );
      }

      return new RuntimeTextProcessingConfiguration(
         GetSchemaValue( schemaMeta, "from_language", RuntimeTextProcessingConfiguration.Default.FromLanguage ),
         ParseBoolean( GetSchemaValue( schemaMeta, "whitespace_between_words", null ), RuntimeTextProcessingConfiguration.Default.WhitespaceBetweenWords ),
         ParseBoolean( GetSchemaValue( schemaMeta, "template_all_numbers_away", null ), RuntimeTextProcessingConfiguration.Default.TemplateAllNumbersAway ),
         ParseBoolean( GetSchemaValue( schemaMeta, "handle_rich_text", null ), RuntimeTextProcessingConfiguration.Default.HandleRichText ) );
   }

   private static string GetSchemaValue( IReadOnlyDictionary<string, string> schemaMeta, string key, string defaultValue )
   {
      return schemaMeta.TryGetValue( key, out var value ) && !string.IsNullOrWhiteSpace( value )
         ? value
         : defaultValue;
   }

   private static bool ParseBoolean( string value, bool defaultValue )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return defaultValue;
      if( bool.TryParse( value, out var parsed ) ) return parsed;
      return defaultValue;
   }

   private static DbConnection OpenConnection( string databasePath )
   {
      var connection = new SqliteConnection( string.Format( CultureInfo.InvariantCulture, "Data Source={0}", databasePath ) );
      connection.Open();
      return connection;
   }
}
