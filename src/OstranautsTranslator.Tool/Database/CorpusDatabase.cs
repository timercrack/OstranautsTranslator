using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Processing;
using OstranautsTranslator.Tool.Scanning;

namespace OstranautsTranslator.Tool.Database;

internal sealed class CorpusDatabase
{
   private const int SchemaVersion = 7;
   private const string IgnoredNativeModSourceKeyPrefix = "native-ignore::";
   private readonly string _databasePath;

   public CorpusDatabase( string databasePath )
   {
      _databasePath = databasePath;
   }

   public void Initialize()
   {
      using var connection = OpenConnection();
      EnsureSchemaMetaTable( connection );
      ThrowIfSchemaVersionIsUnsupported( connection );

      using( var command = connection.CreateCommand() )
      {
         command.CommandText = @"
CREATE TABLE IF NOT EXISTS native_mod_source (
   id INTEGER PRIMARY KEY AUTOINCREMENT,
   source_key TEXT NOT NULL UNIQUE,
   raw_text TEXT NOT NULL,
   occurrence_count INTEGER NOT NULL,
   patch_targets_json TEXT NOT NULL,
   is_translatable INTEGER NOT NULL DEFAULT 1,
   last_scan_token TEXT,
   state TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS runtime_source (
   id INTEGER PRIMARY KEY AUTOINCREMENT,
   source_key TEXT NOT NULL UNIQUE,
   raw_text TEXT NOT NULL,
   occurrence_count INTEGER NOT NULL,
   sample_payload_json TEXT,
   has_capture_source INTEGER NOT NULL,
   source_origin TEXT NOT NULL DEFAULT 'runtime-miss',
   last_scan_token TEXT,
   state TEXT NOT NULL
);

DROP TABLE IF EXISTS occurrences;
DROP TABLE IF EXISTS sources;
DROP TABLE IF EXISTS entries;
DROP TABLE IF EXISTS runtime_captures;
";
         command.ExecuteNonQuery();
      }

   EnsureNativeModSourceColumns( connection );
   EnsureRuntimeSourceColumns( connection );

      using( var indexCommand = connection.CreateCommand() )
      {
         indexCommand.CommandText = @"
CREATE INDEX IF NOT EXISTS ix_native_mod_source_state ON native_mod_source(state);
CREATE INDEX IF NOT EXISTS ix_runtime_source_state ON runtime_source(state);
CREATE INDEX IF NOT EXISTS ix_runtime_source_origin_state ON runtime_source(source_origin, state);
";
         indexCommand.ExecuteNonQuery();
      }

      SetSchemaMeta( connection, "schema_version", SchemaVersion.ToString( CultureInfo.InvariantCulture ) );
   }

   public void SetTextProcessingConfiguration( CorpusTextProcessingConfiguration configuration )
   {
      using var connection = OpenConnection();
      EnsureSchemaMetaTable( connection );
      ThrowIfSchemaVersionIsUnsupported( connection );

      SetSchemaMeta( connection, "from_language", configuration.FromLanguage );
      SetSchemaMeta( connection, "whitespace_between_words", configuration.WhitespaceBetweenWords.ToString( CultureInfo.InvariantCulture ) );
      SetSchemaMeta( connection, "template_all_numbers_away", configuration.TemplateAllNumbersAway.ToString( CultureInfo.InvariantCulture ) );
      SetSchemaMeta( connection, "handle_rich_text", configuration.HandleRichText.ToString( CultureInfo.InvariantCulture ) );
   }

   public CorpusTextProcessingConfiguration GetTextProcessingConfiguration()
   {
      using var connection = OpenConnection();
      EnsureSchemaMetaTable( connection );

      return new CorpusTextProcessingConfiguration(
         GetSchemaMeta( connection, "from_language" ) ?? CorpusTextProcessingConfiguration.Default.FromLanguage,
         ParseSchemaBoolean( GetSchemaMeta( connection, "whitespace_between_words" ), CorpusTextProcessingConfiguration.Default.WhitespaceBetweenWords ),
         ParseSchemaBoolean( GetSchemaMeta( connection, "template_all_numbers_away" ), CorpusTextProcessingConfiguration.Default.TemplateAllNumbersAway ),
         ParseSchemaBoolean( GetSchemaMeta( connection, "handle_rich_text" ), CorpusTextProcessingConfiguration.Default.HandleRichText ) );
   }

   public void ApplyScanResults( IReadOnlyList<ProcessedSourceScanResult> results )
   {
      using var connection = OpenConnection();
      using var transaction = connection.BeginTransaction();

      var scanToken = Guid.NewGuid().ToString( "N" );
      var nativeRows = new Dictionary<string, NativeModScanRow>( StringComparer.Ordinal );
      var runtimeRows = new Dictionary<string, RuntimeScanRow>( StringComparer.Ordinal );

      foreach( var result in results )
      {
         foreach( var occurrence in result.Occurrences )
         {
            if( string.Equals( result.SourceType, "ostranauts-data-json", StringComparison.Ordinal ) )
            {
               AccumulateNativeModRow( nativeRows, result, occurrence );
            }
            else if( string.Equals( result.SourceType, RuntimeSourceOrigins.DecompiledDll, StringComparison.Ordinal ) )
            {
               AccumulateRuntimeRow( runtimeRows, result, occurrence );
            }
         }
      }

      foreach( var row in nativeRows.Values )
      {
         UpsertNativeModRow( connection, transaction, row, scanToken );
      }

      foreach( var row in runtimeRows.Values )
      {
         UpsertRuntimeScanRow( connection, transaction, row, scanToken );
      }

      FinalizeScan( connection, transaction, scanToken );
      transaction.Commit();
   }

   public long GetEntryCount()
   {
      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      command.CommandText = @"
SELECT COUNT(*)
FROM (
   SELECT source_key FROM native_mod_source WHERE state = 'active'
   UNION
   SELECT source_key FROM runtime_source WHERE state = 'active'
);";
      return Convert.ToInt64( command.ExecuteScalar(), CultureInfo.InvariantCulture );
   }

   public long? FindSourceId( string sourceKind, string sourceKey )
   {
      if( string.IsNullOrWhiteSpace( sourceKey ) ) return null;

      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      command.CommandText = $"SELECT id FROM {GetSourceTableName( sourceKind )} WHERE source_key = $source_key;";
      command.Parameters.AddWithValue( "$source_key", sourceKey );

      var result = command.ExecuteScalar();
      return result == null || result == DBNull.Value
         ? null
         : Convert.ToInt64( result, CultureInfo.InvariantCulture );
   }

   public long UpsertRuntimeCaptureSource( EntrySnapshot entry )
   {
      using var connection = OpenConnection();
      using var transaction = connection.BeginTransaction();
      var sourceId = UpsertRuntimeCaptureSource( connection, transaction, entry );
      transaction.Commit();
      return sourceId;
   }

   private static void AccumulateNativeModRow( IDictionary<string, NativeModScanRow> rows, ProcessedSourceScanResult result, ProcessedScanOccurrence occurrence )
   {
      var sourceKey = CreateNativeModSourceKey( occurrence.Entry.EntryKey, occurrence.IsTranslatable );
      if( !rows.TryGetValue( sourceKey, out var row ) )
      {
         row = new NativeModScanRow(
            sourceKey,
            occurrence.Entry.RawText,
            occurrence.IsTranslatable,
            new List<PatchTarget>() );
         rows.Add( sourceKey, row );
      }

      row.OccurrenceCount++;

      if( !occurrence.IsTranslatable ) return;

      var sourcePath = GetRelativeSourcePath( occurrence.LocationPath );
      row.Targets.Add( new PatchTarget(
         sourcePath,
         result.ContentHash,
         occurrence.LocationKind,
         occurrence.LocationPath,
         occurrence.ContextBefore,
         occurrence.ContextAfter,
         occurrence.MetadataJson ) );
   }

   private static void AccumulateRuntimeRow( IDictionary<string, RuntimeScanRow> rows, ProcessedSourceScanResult result, ProcessedScanOccurrence occurrence )
   {
      if( !rows.TryGetValue( occurrence.Entry.EntryKey, out var row ) )
      {
         row = new RuntimeScanRow(
            occurrence.Entry.EntryKey,
            occurrence.Entry.RawText,
            0,
            CreateRuntimeSamplePayload( result, occurrence ) );
         rows.Add( occurrence.Entry.EntryKey, row );
      }

      row.OccurrenceCount++;
   }

   private static void UpsertNativeModRow( SqliteConnection connection, SqliteTransaction transaction, NativeModScanRow row, string scanToken )
   {
      var serializedTargets = JsonSerializer.Serialize( row.Targets );

      using var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = @"
INSERT INTO native_mod_source (
   source_key,
   raw_text,
   occurrence_count,
   patch_targets_json,
   is_translatable,
   last_scan_token,
   state )
VALUES (
   $source_key,
   $raw_text,
   $occurrence_count,
   $patch_targets_json,
   $is_translatable,
   $last_scan_token,
   $state )
ON CONFLICT(source_key) DO UPDATE SET
   raw_text = excluded.raw_text,
   occurrence_count = excluded.occurrence_count,
   patch_targets_json = excluded.patch_targets_json,
   is_translatable = excluded.is_translatable,
   last_scan_token = excluded.last_scan_token,
   state = excluded.state;";
      command.Parameters.AddWithValue( "$source_key", row.SourceKey );
      command.Parameters.AddWithValue( "$raw_text", row.RawText );
      command.Parameters.AddWithValue( "$occurrence_count", row.OccurrenceCount );
      command.Parameters.AddWithValue( "$patch_targets_json", serializedTargets );
      command.Parameters.AddWithValue( "$is_translatable", row.IsTranslatable ? 1 : 0 );
      command.Parameters.AddWithValue( "$last_scan_token", scanToken );
      command.Parameters.AddWithValue( "$state", "active" );
      command.ExecuteNonQuery();
   }

   private static long UpsertRuntimeCaptureSource( SqliteConnection connection, SqliteTransaction transaction, EntrySnapshot entry )
   {
      using( var command = connection.CreateCommand() )
      {
         command.Transaction = transaction;
         command.CommandText = @"
INSERT INTO runtime_source (
   source_key,
   raw_text,
   occurrence_count,
   sample_payload_json,
   has_capture_source,
   source_origin,
   last_scan_token,
   state )
VALUES (
   $source_key,
   $raw_text,
   1,
   NULL,
   1,
   $source_origin,
   NULL,
   $state )
ON CONFLICT(source_key) DO UPDATE SET
   raw_text = excluded.raw_text,
   has_capture_source = 1,
   source_origin = CASE
      WHEN runtime_source.source_origin = $decompiled_source_origin THEN runtime_source.source_origin
      ELSE excluded.source_origin
   END,
   state = excluded.state;";
         command.Parameters.AddWithValue( "$source_key", entry.EntryKey );
         command.Parameters.AddWithValue( "$raw_text", entry.RawText );
         command.Parameters.AddWithValue( "$source_origin", RuntimeSourceOrigins.RuntimeMiss );
         command.Parameters.AddWithValue( "$decompiled_source_origin", RuntimeSourceOrigins.DecompiledDll );
         command.Parameters.AddWithValue( "$state", "active" );
         command.ExecuteNonQuery();
      }

      using( var command = connection.CreateCommand() )
      {
         command.Transaction = transaction;
         command.CommandText = "SELECT id FROM runtime_source WHERE source_key = $source_key;";
         command.Parameters.AddWithValue( "$source_key", entry.EntryKey );
         return Convert.ToInt64( command.ExecuteScalar(), CultureInfo.InvariantCulture );
      }
   }

   private static void FinalizeScan( SqliteConnection connection, SqliteTransaction transaction, string scanToken )
   {
      using( var command = connection.CreateCommand() )
      {
         command.Transaction = transaction;
         command.CommandText = @"
UPDATE native_mod_source
SET state = CASE WHEN last_scan_token = $last_scan_token THEN 'active' ELSE 'removed' END;";
         command.Parameters.AddWithValue( "$last_scan_token", scanToken );
         command.ExecuteNonQuery();
      }

      using( var command = connection.CreateCommand() )
      {
         command.Transaction = transaction;
         command.CommandText = @"
UPDATE runtime_source
SET state = CASE
   WHEN source_origin = $runtime_miss_origin THEN 'active'
   WHEN last_scan_token = $last_scan_token THEN 'active'
   WHEN has_capture_source = 1 THEN 'active'
   ELSE 'removed'
END;";
         command.Parameters.AddWithValue( "$runtime_miss_origin", RuntimeSourceOrigins.RuntimeMiss );
         command.Parameters.AddWithValue( "$last_scan_token", scanToken );
         command.ExecuteNonQuery();
      }
   }

   private static void UpsertRuntimeScanRow( SqliteConnection connection, SqliteTransaction transaction, RuntimeScanRow row, string scanToken )
   {
      using var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = @"
INSERT INTO runtime_source (
   source_key,
   raw_text,
   occurrence_count,
   sample_payload_json,
   has_capture_source,
   source_origin,
   last_scan_token,
   state )
VALUES (
   $source_key,
   $raw_text,
   $occurrence_count,
   $sample_payload_json,
   0,
   $source_origin,
   $last_scan_token,
   $state )
ON CONFLICT(source_key) DO UPDATE SET
   raw_text = excluded.raw_text,
   occurrence_count = CASE
      WHEN runtime_source.occurrence_count > excluded.occurrence_count THEN runtime_source.occurrence_count
      ELSE excluded.occurrence_count
   END,
   sample_payload_json = excluded.sample_payload_json,
   source_origin = excluded.source_origin,
   last_scan_token = excluded.last_scan_token,
   state = excluded.state;";
      command.Parameters.AddWithValue( "$source_key", row.SourceKey );
      command.Parameters.AddWithValue( "$raw_text", row.RawText );
      command.Parameters.AddWithValue( "$occurrence_count", row.OccurrenceCount );
      command.Parameters.AddWithValue( "$sample_payload_json", (object?)row.SamplePayloadJson ?? DBNull.Value );
      command.Parameters.AddWithValue( "$source_origin", RuntimeSourceOrigins.DecompiledDll );
      command.Parameters.AddWithValue( "$last_scan_token", scanToken );
      command.Parameters.AddWithValue( "$state", "active" );
      command.ExecuteNonQuery();
   }

   private static string GetSourceTableName( string sourceKind )
   {
      return sourceKind switch
      {
         "native_mod" => "native_mod_source",
         "runtime" => "runtime_source",
         _ => throw new InvalidOperationException( $"Unsupported source kind '{sourceKind}'." ),
      };
   }

   private static string CreateNativeModSourceKey( string entryKey, bool isTranslatable )
   {
      return isTranslatable ? entryKey : CreateIgnoredNativeModSourceKey( entryKey );
   }

   private static string CreateIgnoredNativeModSourceKey( string entryKey )
   {
      return IgnoredNativeModSourceKeyPrefix + entryKey;
   }

   private static string GetRelativeSourcePath( string locationPath )
   {
      var separatorIndex = locationPath.IndexOf( "::", StringComparison.Ordinal );
      return separatorIndex >= 0 ? locationPath[..separatorIndex] : locationPath;
   }

   private static void EnsureSchemaMetaTable( SqliteConnection connection )
   {
      using var command = connection.CreateCommand();
      command.CommandText = @"
CREATE TABLE IF NOT EXISTS schema_meta (
   key TEXT PRIMARY KEY,
   value TEXT NOT NULL
);";
      command.ExecuteNonQuery();
   }

   private void ThrowIfSchemaVersionIsUnsupported( SqliteConnection connection )
   {
      var currentSchemaVersion = GetSchemaMeta( connection, "schema_version" );
      if( string.IsNullOrWhiteSpace( currentSchemaVersion ) ) return;

      if( int.TryParse( currentSchemaVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVersion )
         && parsedVersion is 5 or 6 or 7 )
      {
         return;
      }

      throw new InvalidOperationException(
         $"Unsupported corpus schema version '{currentSchemaVersion}'. Delete '{_databasePath}' and rerun scan/import with the current schema." );
   }

   private static string CreateRuntimeSamplePayload( ProcessedSourceScanResult result, ProcessedScanOccurrence occurrence )
   {
      return JsonSerializer.Serialize( new RuntimeSamplePayload(
         result.SourcePath,
         occurrence.LocationKind,
         occurrence.LocationPath,
         occurrence.ContextBefore,
         occurrence.ContextAfter,
         occurrence.MetadataJson ) );
   }

         private static void EnsureNativeModSourceColumns( SqliteConnection connection )
         {
         EnsureColumnExists( connection, "native_mod_source", "is_translatable", "INTEGER NOT NULL DEFAULT 1" );

         using var command = connection.CreateCommand();
         command.CommandText = @"
      UPDATE native_mod_source
      SET is_translatable = 1
      WHERE is_translatable IS NULL;";
         command.ExecuteNonQuery();
         }

   private static void EnsureRuntimeSourceColumns( SqliteConnection connection )
   {
      EnsureColumnExists( connection, "runtime_source", "source_origin", $"TEXT NOT NULL DEFAULT '{RuntimeSourceOrigins.RuntimeMiss}'" );

      using var command = connection.CreateCommand();
      command.CommandText = @"
UPDATE runtime_source
SET source_origin = $runtime_miss_origin
WHERE source_origin IS NULL OR TRIM(source_origin) = '';";
      command.Parameters.AddWithValue( "$runtime_miss_origin", RuntimeSourceOrigins.RuntimeMiss );
      command.ExecuteNonQuery();
   }

   private static void EnsureColumnExists( SqliteConnection connection, string tableName, string columnName, string columnDefinition )
   {
      using( var command = connection.CreateCommand() )
      {
         command.CommandText = $"PRAGMA table_info({tableName});";
         using var reader = command.ExecuteReader();
         while( reader.Read() )
         {
            if( string.Equals( reader.GetString( 1 ), columnName, StringComparison.OrdinalIgnoreCase ) )
            {
               return;
            }
         }
      }

      using( var command = connection.CreateCommand() )
      {
         command.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
         command.ExecuteNonQuery();
      }
   }

   private static void SetSchemaMeta( SqliteConnection connection, string key, string value )
   {
      using var command = connection.CreateCommand();
      command.CommandText = @"
INSERT INTO schema_meta ( key, value )
VALUES ( $key, $value )
ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
      command.Parameters.AddWithValue( "$key", key );
      command.Parameters.AddWithValue( "$value", value );
      command.ExecuteNonQuery();
   }

   private static string? GetSchemaMeta( SqliteConnection connection, string key )
   {
      using var command = connection.CreateCommand();
      command.CommandText = "SELECT value FROM schema_meta WHERE key = $key;";
      command.Parameters.AddWithValue( "$key", key );
      return command.ExecuteScalar() as string;
   }

   private static bool ParseSchemaBoolean( string? value, bool defaultValue )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return defaultValue;
      if( bool.TryParse( value, out var parsed ) ) return parsed;
      return defaultValue;
   }

   private SqliteConnection OpenConnection()
   {
      var builder = new SqliteConnectionStringBuilder
      {
         DataSource = _databasePath,
         Mode = SqliteOpenMode.ReadWriteCreate,
      };

      var connection = new SqliteConnection( builder.ToString() );
      connection.Open();
      return connection;
   }

   private sealed class NativeModScanRow
   {
      public NativeModScanRow( string sourceKey, string rawText, bool isTranslatable, List<PatchTarget> targets )
      {
         SourceKey = sourceKey;
         RawText = rawText;
         IsTranslatable = isTranslatable;
         Targets = targets;
      }

      public string SourceKey { get; }

      public string RawText { get; }

      public bool IsTranslatable { get; }

      public int OccurrenceCount { get; set; }

      public List<PatchTarget> Targets { get; }
   }

   private sealed class RuntimeScanRow
   {
      public RuntimeScanRow( string sourceKey, string rawText, int occurrenceCount, string? samplePayloadJson )
      {
         SourceKey = sourceKey;
         RawText = rawText;
         OccurrenceCount = occurrenceCount;
         SamplePayloadJson = samplePayloadJson;
      }

      public string SourceKey { get; }

      public string RawText { get; }

      public int OccurrenceCount { get; set; }

      public string? SamplePayloadJson { get; }
   }

   private sealed record RuntimeSamplePayload(
      string? SourcePath,
      string? LocationKind,
      string? LocationPath,
      string? ContextBefore,
      string? ContextAfter,
      string? MetadataJson );

   private sealed record PatchTarget(
      string SourcePath,
      string SourceContentHash,
      string LocationKind,
      string LocationPath,
      string? ContextBefore,
      string? ContextAfter,
      string? MetadataJson );
}
