using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal static class RuntimeFixedSourceSeeder
{
   public const string SourceKind = "runtime_fixed";
   public const string SourceTableName = "runtime_fixed_source";

   private const string ManifestFileName = "runtime-fixed-source.json";
   private const string SeedTranslator = "runtime-fixed-seed";
   private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
   {
      PropertyNameCaseInsensitive = true,
   };

   public static void EnsureSourceTable( SqliteConnection connection )
   {
      using var command = connection.CreateCommand();
      command.CommandText = @"
CREATE TABLE IF NOT EXISTS runtime_fixed_source (
   id INTEGER PRIMARY KEY AUTOINCREMENT,
   source_key TEXT NOT NULL UNIQUE,
   raw_text TEXT NOT NULL,
   occurrence_count INTEGER NOT NULL,
   last_scan_token TEXT,
   state TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_runtime_fixed_source_state ON runtime_fixed_source(state);";
      command.ExecuteNonQuery();
   }

   public static void SyncSources( SqliteConnection connection )
   {
      var entries = LoadEntries();
      if( entries.Count == 0 ) return;

      var scanToken = Guid.NewGuid().ToString( "N" );
      foreach( var entry in entries )
      {
         using var command = connection.CreateCommand();
         command.CommandText = @"
INSERT INTO runtime_fixed_source (
   source_key,
   raw_text,
   occurrence_count,
   last_scan_token,
   state )
VALUES (
   $source_key,
   $raw_text,
   1,
   $last_scan_token,
   'active' )
ON CONFLICT(source_key) DO UPDATE SET
   raw_text = excluded.raw_text,
   occurrence_count = excluded.occurrence_count,
   last_scan_token = excluded.last_scan_token,
   state = excluded.state;";
         command.Parameters.AddWithValue( "$source_key", CreateSourceKey( entry.RawText ) );
         command.Parameters.AddWithValue( "$raw_text", entry.RawText );
         command.Parameters.AddWithValue( "$last_scan_token", scanToken );
         command.ExecuteNonQuery();
      }

      using var finalizeCommand = connection.CreateCommand();
      finalizeCommand.CommandText = @"
UPDATE runtime_fixed_source
SET state = CASE WHEN last_scan_token = $last_scan_token THEN 'active' ELSE 'removed' END;";
      finalizeCommand.Parameters.AddWithValue( "$last_scan_token", scanToken );
      finalizeCommand.ExecuteNonQuery();
   }

   public static void SeedTranslations( SqliteConnection connection, string translationTable, string language )
   {
      if( string.IsNullOrWhiteSpace( language ) ) return;

      var entries = LoadEntries();
      if( entries.Count == 0 ) return;

      foreach( var entry in entries )
      {
         if( entry.SeedTranslations == null
            || !entry.SeedTranslations.TryGetValue( language, out var translatedText )
            || string.IsNullOrWhiteSpace( translatedText ) )
         {
            continue;
         }

         long? sourceId;
         using( var selectCommand = connection.CreateCommand() )
         {
            selectCommand.CommandText = @"
SELECT id
FROM runtime_fixed_source
WHERE source_key = $source_key
  AND state = 'active';";
            selectCommand.Parameters.AddWithValue( "$source_key", CreateSourceKey( entry.RawText ) );
            var scalar = selectCommand.ExecuteScalar();
            sourceId = scalar == null || scalar == DBNull.Value
               ? null
               : Convert.ToInt64( scalar );
         }

         if( !sourceId.HasValue ) continue;

         using var upsertCommand = connection.CreateCommand();
         upsertCommand.CommandText = $@"
INSERT INTO {translationTable} (
   source_kind,
   source_id,
   translated_text,
   translation_state,
   translator,
   created_utc,
   updated_utc )
VALUES (
   $source_kind,
   $source_id,
   $translated_text,
   'final',
   $translator,
   $created_utc,
   $updated_utc )
ON CONFLICT(source_kind, source_id) DO UPDATE SET
   translated_text = excluded.translated_text,
   translation_state = excluded.translation_state,
   translator = excluded.translator,
   updated_utc = excluded.updated_utc
WHERE {translationTable}.translator = $translator
   OR {translationTable}.translated_text IS NULL
   OR {translationTable}.translated_text = '';";
         var now = DateTimeOffset.UtcNow.ToString( "O" );
         upsertCommand.Parameters.AddWithValue( "$source_kind", SourceKind );
         upsertCommand.Parameters.AddWithValue( "$source_id", sourceId.Value );
         upsertCommand.Parameters.AddWithValue( "$translated_text", translatedText );
         upsertCommand.Parameters.AddWithValue( "$translator", SeedTranslator );
         upsertCommand.Parameters.AddWithValue( "$created_utc", now );
         upsertCommand.Parameters.AddWithValue( "$updated_utc", now );
         upsertCommand.ExecuteNonQuery();
      }
   }

   private static IReadOnlyList<RuntimeFixedSourceSeedEntry> LoadEntries()
   {
      var manifestPath = Path.Combine( AppContext.BaseDirectory, ManifestFileName );
      if( !File.Exists( manifestPath ) ) return Array.Empty<RuntimeFixedSourceSeedEntry>();

      using var stream = File.OpenRead( manifestPath );
      var document = JsonSerializer.Deserialize<RuntimeFixedSourceDocument>( stream, JsonOptions );
      if( document?.Entries == null || document.Entries.Count == 0 ) return Array.Empty<RuntimeFixedSourceSeedEntry>();

      return document.Entries
         .Where( entry => !string.IsNullOrWhiteSpace( entry.RawText ) )
         .ToArray();
   }

   private static string CreateSourceKey( string rawText )
   {
      var hash = SHA256.HashData( Encoding.UTF8.GetBytes( rawText ) );
      return "runtime-fixed::" + Convert.ToHexString( hash );
   }

   private sealed record RuntimeFixedSourceDocument( [property: JsonPropertyName( "entries" )] IReadOnlyList<RuntimeFixedSourceSeedEntry> Entries );

   private sealed record RuntimeFixedSourceSeedEntry(
      [property: JsonPropertyName( "raw_text" )] string RawText,
      [property: JsonPropertyName( "seed_translations" )] IReadOnlyDictionary<string, string>? SeedTranslations );
}