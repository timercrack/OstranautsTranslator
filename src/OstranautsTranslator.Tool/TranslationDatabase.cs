using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Exporting;
using OstranautsTranslator.Tool.Importing;
using OstranautsTranslator.Tool.Processing;

namespace OstranautsTranslator.Tool;

internal sealed class TranslationDatabase
{
   private const int SchemaVersion = 7;
   private readonly string _databasePath;
   private readonly string? _fromLanguage;
   private readonly string? _toLanguage;

   public TranslationDatabase( string databasePath )
   {
      _databasePath = Path.GetFullPath( databasePath );
   }

   public TranslationDatabase( string databasePath, string fromLanguage, string toLanguage )
      : this( databasePath )
   {
      _fromLanguage = fromLanguage;
      _toLanguage = RuntimeTranslationDeployment.ResolveTargetLanguage( toLanguage );
   }

   public string DatabasePath => _databasePath;

   public void Initialize()
   {
      using var connection = OpenConnection();
      EnsureSchemaMetaTable( connection );
      ThrowIfSchemaVersionIsUnsupported( connection );
      EnsureNativeModSourceColumns( connection );
      EnsureRuntimeSourceColumns( connection );

      SetSchemaMeta( connection, "schema_version", SchemaVersion.ToString( CultureInfo.InvariantCulture ) );
      if( !string.IsNullOrWhiteSpace( _fromLanguage ) )
      {
         SetSchemaMeta( connection, "from_language", _fromLanguage );
      }

      if( !string.IsNullOrWhiteSpace( _toLanguage ) )
      {
         SetSchemaMeta( connection, "last_translation_language", _toLanguage );
         EnsureTranslationTable( connection, GetTranslationTableName() );
      }
   }

   public string? GetSchemaMetaValue( string key )
   {
      using var connection = OpenConnection();
      EnsureSchemaMetaTable( connection );
      return GetSchemaMeta( connection, key );
   }

   public void EnsureExists()
   {
      if( !File.Exists( _databasePath ) )
      {
         throw new FileNotFoundException( "Translation database was not found.", _databasePath );
      }
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
         GetSchemaMeta( connection, "from_language" ) ?? _fromLanguage ?? CorpusTextProcessingConfiguration.Default.FromLanguage,
         ParseSchemaBoolean( GetSchemaMeta( connection, "whitespace_between_words" ), CorpusTextProcessingConfiguration.Default.WhitespaceBetweenWords ),
         ParseSchemaBoolean( GetSchemaMeta( connection, "template_all_numbers_away" ), CorpusTextProcessingConfiguration.Default.TemplateAllNumbersAway ),
         ParseSchemaBoolean( GetSchemaMeta( connection, "handle_rich_text" ), CorpusTextProcessingConfiguration.Default.HandleRichText ) );
   }

   public int GetSourceEntryCount()
   {
      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      command.CommandText = @"
SELECT COUNT(*)
FROM (
   SELECT id FROM native_mod_source WHERE state = 'active' AND COALESCE(is_translatable, 1) = 1
   UNION ALL
   SELECT id FROM runtime_source WHERE state = 'active'
);";
      return Convert.ToInt32( command.ExecuteScalar(), CultureInfo.InvariantCulture );
   }

   public IReadOnlyList<TranslationExportRecord> GetExportEntries( bool includeTranslated )
   {
      var textProcessingConfiguration = GetTextProcessingConfiguration();

      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      command.CommandText = BuildCombinedSourceQuery(
         GetTranslationTableName(),
         includeTranslated
            ? string.Empty
            : "AND (t.id IS NULL OR t.translated_text IS NULL OR t.translated_text = '' OR COALESCE(t.translation_state, 'untranslated') <> 'final')" );

      using var reader = command.ExecuteReader();
      var results = new List<TranslationExportRecord>();
      while( reader.Read() )
      {
         var rawText = reader.GetString( 3 );
         var projected = ProjectEntryFields( rawText, textProcessingConfiguration );
         var sampleContext = ResolveSampleContext( GetNullableString( reader, 5 ), GetNullableString( reader, 6 ) );
         var tokenMetadata = BracketTokenPolicyAnalyzer.Resolve( rawText, sampleContext.MetadataJson );

         results.Add( new TranslationExportRecord(
            reader.GetString( 0 ),
            reader.GetInt64( 1 ),
            reader.GetString( 2 ),
            rawText,
            projected.RuntimeKey,
            projected.RenderKey,
            projected.TextKind,
            reader.GetInt32( 4 ),
            sampleContext.SampleSourcePath,
            sampleContext.SampleLocationKind,
            sampleContext.SampleLocationPath,
            sampleContext.SampleContextBefore,
            sampleContext.SampleContextAfter,
            tokenMetadata.TokenPolicy,
            tokenMetadata.TokenExamples,
            tokenMetadata.NeedsManualReview,
            tokenMetadata.TokenCorrections,
            GetNullableString( reader, 7 ),
            GetNullableString( reader, 8 ) ?? "untranslated",
            GetNullableString( reader, 9 ) ) );
      }

      return results;
   }

   public TranslationImportSummary ImportTranslations( IReadOnlyList<ImportedTranslationRecord> records, bool overwriteExisting )
   {
      using var connection = OpenConnection();
      using var transaction = connection.BeginTransaction();

      var appliedCount = 0;
      var unknownCount = 0;
      var skippedCount = 0;
      var resolvedSourceIds = new List<long>();
      var resolvedSourceKeys = new HashSet<string>( StringComparer.Ordinal );
      var translationTable = GetTranslationTableName();

      foreach( var record in records )
      {
         var targets = ResolveImportTargets( connection, transaction, record );
         if( targets.Count == 0 )
         {
            unknownCount++;
            continue;
         }

         var appliedForRecord = 0;
         var skippedForRecord = 0;
         foreach( var target in targets )
         {
            var status = UpsertTranslation(
               connection,
               transaction,
               translationTable,
               target.SourceKind,
               target.SourceId,
               record.TranslatedText,
               record.TranslationState,
               record.Translator,
               overwriteExisting );

            if( status == TranslationImportStatus.Applied )
            {
               appliedForRecord++;
               resolvedSourceIds.Add( target.SourceId );
               if( !string.IsNullOrWhiteSpace( target.SourceKey ) )
               {
                  resolvedSourceKeys.Add( target.SourceKey );
               }
            }
            else if( status == TranslationImportStatus.Skipped )
            {
               skippedForRecord++;
               resolvedSourceIds.Add( target.SourceId );
               if( !string.IsNullOrWhiteSpace( target.SourceKey ) )
               {
                  resolvedSourceKeys.Add( target.SourceKey );
               }
            }
         }

         if( appliedForRecord > 0 )
         {
            appliedCount++;
         }
         else if( skippedForRecord > 0 )
         {
            skippedCount++;
         }
         else
         {
            unknownCount++;
         }
      }

      transaction.Commit();
      return new TranslationImportSummary( records.Count, appliedCount, unknownCount, skippedCount, _databasePath, resolvedSourceIds, resolvedSourceKeys.ToList() );
   }

   public IReadOnlyList<RuntimeExportRecord> GetRuntimeExportEntries( bool includeDraft )
   {
      var textProcessingConfiguration = GetTextProcessingConfiguration();

      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      var translationTable = GetTranslationTableName();
      command.CommandText = $@"
SELECT
   s.id,
   s.source_key,
   s.raw_text,
   t.translated_text,
   t.translation_state,
   s.occurrence_count
FROM runtime_source s
INNER JOIN {translationTable} t ON t.source_kind = 'runtime' AND t.source_id = s.id
WHERE s.state = 'active'
  AND t.translated_text IS NOT NULL
  AND t.translated_text <> ''{( includeDraft ? string.Empty : "\n  AND t.translation_state = 'final'" )}
ORDER BY s.occurrence_count DESC, s.id;";

      using var reader = command.ExecuteReader();
      var results = new List<RuntimeExportRecord>();
      while( reader.Read() )
      {
         var rawText = reader.GetString( 2 );
         var projected = ProjectEntryFields( rawText, textProcessingConfiguration );

         results.Add( new RuntimeExportRecord(
            reader.GetInt64( 0 ),
            reader.GetString( 1 ),
            rawText,
            projected.RuntimeKey,
            reader.GetString( 3 ),
            reader.GetString( 4 ),
            reader.GetInt32( 5 ) ) );
      }

      return results;
   }

   public IReadOnlyList<NativeModTranslationRecord> GetNativeModExportEntries( bool includeDraft )
   {
      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      var translationTable = GetTranslationTableName();
      command.CommandText = $@"
SELECT
   s.id,
   s.source_key,
   s.raw_text,
   t.translated_text,
   t.translation_state,
   s.occurrence_count,
   s.patch_targets_json
FROM native_mod_source s
INNER JOIN {translationTable} t ON t.source_kind = 'native_mod' AND t.source_id = s.id
WHERE s.state = 'active'
   AND COALESCE(s.is_translatable, 1) = 1
  AND t.translated_text IS NOT NULL
  AND t.translated_text <> ''{( includeDraft ? string.Empty : "\n  AND t.translation_state = 'final'" )}
ORDER BY s.occurrence_count DESC, s.id;";

      using var reader = command.ExecuteReader();
      var results = new List<NativeModTranslationRecord>();
      while( reader.Read() )
      {
         results.Add( new NativeModTranslationRecord(
            reader.GetInt64( 0 ),
            reader.GetString( 1 ),
            reader.GetString( 2 ),
            reader.GetString( 3 ),
            reader.GetString( 4 ),
            reader.GetInt32( 5 ),
            reader.GetString( 6 ) ) );
      }

      return results;
   }

   public IReadOnlyList<NativeModSourceExportRecord> GetNativeModSourceExportEntries( bool includeDraft )
   {
      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      var translationTable = GetTranslationTableName();
      command.CommandText = $@"
SELECT
   s.id,
   s.source_key,
   s.raw_text,
   CASE
      WHEN t.translated_text IS NULL OR t.translated_text = '' THEN NULL
      WHEN {( includeDraft ? "1 = 1" : "t.translation_state = 'final'" )} THEN t.translated_text
      ELSE NULL
   END AS translated_text,
   COALESCE(t.translation_state, 'untranslated') AS translation_state,
   s.occurrence_count,
   s.patch_targets_json
FROM native_mod_source s
LEFT JOIN {translationTable} t ON t.source_kind = 'native_mod' AND t.source_id = s.id
WHERE s.state = 'active'
   AND COALESCE(s.is_translatable, 1) = 1
ORDER BY s.occurrence_count DESC, s.id;";

      using var reader = command.ExecuteReader();
      var results = new List<NativeModSourceExportRecord>();
      while( reader.Read() )
      {
         results.Add( new NativeModSourceExportRecord(
            reader.GetInt64( 0 ),
            reader.GetString( 1 ),
            reader.GetString( 2 ),
            GetNullableString( reader, 3 ),
            reader.GetString( 4 ),
            reader.GetInt32( 5 ),
            reader.GetString( 6 ) ) );
      }

      return results;
   }

   public IReadOnlyList<TranslationEntry> GetEntriesForTranslation( bool includeDraft, bool overwriteExisting )
   {
      var textProcessingConfiguration = GetTextProcessingConfiguration();

      using var connection = OpenConnection();
      using var command = connection.CreateCommand();
      command.CommandText = BuildCombinedSourceQuery(
         GetTranslationTableName(),
         ( overwriteExisting
            ? string.Empty
            : "AND (t.id IS NULL OR t.translated_text IS NULL OR t.translated_text = '' OR COALESCE(t.translation_state, 'untranslated') <> 'final')" )
         + ( includeDraft ? string.Empty : "\nAND (t.id IS NULL OR COALESCE(t.translation_state, 'untranslated') <> 'draft')" ) );

      using var reader = command.ExecuteReader();
      var results = new List<TranslationEntry>();
      while( reader.Read() )
      {
         var rawText = reader.GetString( 3 );
         var projected = ProjectEntryFields( rawText, textProcessingConfiguration );
         var sampleContext = ResolveSampleContext( GetNullableString( reader, 5 ), GetNullableString( reader, 6 ) );
         var tokenMetadata = BracketTokenPolicyAnalyzer.Resolve( rawText, sampleContext.MetadataJson );

         results.Add( new TranslationEntry(
            reader.GetString( 0 ),
            reader.GetInt64( 1 ),
            reader.GetString( 2 ),
            rawText,
            projected.RuntimeKey,
            projected.RenderKey,
            projected.TextKind,
            reader.GetInt32( 4 ),
            sampleContext.SampleSourcePath,
            sampleContext.SampleLocationKind,
            sampleContext.SampleLocationPath,
            sampleContext.SampleContextBefore,
            sampleContext.SampleContextAfter,
            tokenMetadata.TokenPolicy,
            tokenMetadata.TokenExamples,
            tokenMetadata.NeedsManualReview,
            tokenMetadata.TokenCorrections,
            GetNullableString( reader, 7 ),
            GetNullableString( reader, 8 ) ?? "untranslated",
            GetNullableString( reader, 9 ) ) );
      }

      return results;
   }

   public int ApplyTranslations( IReadOnlyList<TranslationUpdate> updates, bool overwriteExisting )
   {
      if( updates.Count == 0 ) return 0;

      using var connection = OpenConnection();
      using var transaction = connection.BeginTransaction();
      var appliedCount = 0;
      var tableName = GetTranslationTableName();

      foreach( var update in updates )
      {
         if( UpsertTranslation(
            connection,
            transaction,
            tableName,
            update.SourceKind,
            update.SourceId,
            update.TranslatedText,
            update.TranslationState,
            update.Translator,
            overwriteExisting ) == TranslationImportStatus.Applied )
         {
            appliedCount++;
         }
      }

      transaction.Commit();
      return appliedCount;
   }

   private string GetTranslationTableName()
   {
      if( string.IsNullOrWhiteSpace( _toLanguage ) )
      {
         throw new InvalidOperationException( "A target language is required for translation table operations." );
      }

      return RuntimeTranslationDeployment.GetTranslationTableName( _toLanguage );
   }

   private static void EnsureTranslationTable( SqliteConnection connection, string tableName )
   {
      using var command = connection.CreateCommand();
      command.CommandText = $@"
CREATE TABLE IF NOT EXISTS {tableName} (
   id INTEGER PRIMARY KEY AUTOINCREMENT,
   source_kind TEXT NOT NULL,
   source_id INTEGER NOT NULL,
   translated_text TEXT NOT NULL,
   translation_state TEXT NOT NULL,
   translator TEXT,
   created_utc TEXT NOT NULL,
   updated_utc TEXT NOT NULL,
   UNIQUE(source_kind, source_id)
);
CREATE INDEX IF NOT EXISTS ix_{tableName}_source_ref ON {tableName}(source_kind, source_id);
CREATE INDEX IF NOT EXISTS ix_{tableName}_translation_state ON {tableName}(translation_state);";
      command.ExecuteNonQuery();
   }

   private static IReadOnlyList<SourceReference> ResolveImportTargets( SqliteConnection connection, SqliteTransaction transaction, ImportedTranslationRecord record )
   {
      if( !string.IsNullOrWhiteSpace( record.SourceKey ) )
      {
         var targetsBySourceKey = FindTargetsBySourceKey( connection, transaction, record.SourceKey!, record.SourceKind );
         if( targetsBySourceKey.Count > 0 )
         {
            return targetsBySourceKey;
         }
      }

      if( !string.IsNullOrWhiteSpace( record.SourceKind ) && record.SourceId.HasValue )
      {
         return SourceExists( connection, transaction, record.SourceKind!, record.SourceId.Value )
            ? [ new SourceReference( record.SourceKind!, record.SourceId.Value, record.SourceKey ?? string.Empty ) ]
            : [];
      }

      return [];
   }

   private static IReadOnlyList<SourceReference> FindTargetsBySourceKey( SqliteConnection connection, SqliteTransaction transaction, string sourceKey, string? sourceKind )
   {
      var results = new List<SourceReference>();
      if( string.IsNullOrWhiteSpace( sourceKey ) )
      {
         return results;
      }

      if( string.IsNullOrWhiteSpace( sourceKind ) || string.Equals( sourceKind, "native_mod", StringComparison.Ordinal ) )
      {
         results.AddRange( QueryTargetsBySourceKey( connection, transaction, "native_mod", "native_mod_source", sourceKey ) );
      }

      if( string.IsNullOrWhiteSpace( sourceKind ) || string.Equals( sourceKind, "runtime", StringComparison.Ordinal ) )
      {
         results.AddRange( QueryTargetsBySourceKey( connection, transaction, "runtime", "runtime_source", sourceKey ) );
      }

      return results;
   }

   private static List<SourceReference> QueryTargetsBySourceKey( SqliteConnection connection, SqliteTransaction transaction, string sourceKind, string tableName, string sourceKey )
   {
      var results = new List<SourceReference>();
      using var command = connection.CreateCommand();
      command.Transaction = transaction;
      var translatableFilter = string.Equals( sourceKind, "native_mod", StringComparison.Ordinal )
         ? "\n  AND COALESCE(is_translatable, 1) = 1"
         : string.Empty;

      command.CommandText = $@"
   SELECT id, source_key
   FROM {tableName}
   WHERE source_key = $source_key
     AND state = 'active'{translatableFilter};";
      command.Parameters.AddWithValue( "$source_key", sourceKey );

      using var reader = command.ExecuteReader();
      while( reader.Read() )
      {
         results.Add( new SourceReference( sourceKind, reader.GetInt64( 0 ), reader.GetString( 1 ) ) );
      }

      return results;
   }

   private static bool SourceExists( SqliteConnection connection, SqliteTransaction transaction, string sourceKind, long sourceId )
   {
      var tableName = sourceKind switch
      {
         "native_mod" => "native_mod_source",
         "runtime" => "runtime_source",
         _ => throw new InvalidOperationException( $"Unsupported source kind '{sourceKind}'." ),
      };

      using var command = connection.CreateCommand();
      command.Transaction = transaction;
      var translatableFilter = string.Equals( sourceKind, "native_mod", StringComparison.Ordinal )
         ? " AND COALESCE(is_translatable, 1) = 1"
         : string.Empty;

      command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE id = $id AND state = 'active'{translatableFilter};";
      command.Parameters.AddWithValue( "$id", sourceId );
      return Convert.ToInt64( command.ExecuteScalar(), CultureInfo.InvariantCulture ) > 0;
   }

   private static TranslationImportStatus UpsertTranslation(
      SqliteConnection connection,
      SqliteTransaction transaction,
      string tableName,
      string sourceKind,
      long sourceId,
      string translatedText,
      string translationState,
      string? translator,
      bool overwriteExisting )
   {
      using( var selectCommand = connection.CreateCommand() )
      {
         selectCommand.Transaction = transaction;
         selectCommand.CommandText = $@"
SELECT translated_text, translation_state
FROM {tableName}
WHERE source_kind = $source_kind
  AND source_id = $source_id;";
         selectCommand.Parameters.AddWithValue( "$source_kind", sourceKind );
         selectCommand.Parameters.AddWithValue( "$source_id", sourceId );

         using var reader = selectCommand.ExecuteReader();
         if( reader.Read() )
         {
            var existingTranslatedText = GetNullableString( reader, 0 );
            var existingState = GetNullableString( reader, 1 ) ?? "untranslated";
            if( !overwriteExisting
               && !string.IsNullOrWhiteSpace( existingTranslatedText )
               && string.Equals( existingState, "final", StringComparison.OrdinalIgnoreCase ) )
            {
               return TranslationImportStatus.Skipped;
            }
         }
      }

      using var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = $@"
INSERT INTO {tableName} (
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
   $translation_state,
   $translator,
   $created_utc,
   $updated_utc )
ON CONFLICT(source_kind, source_id) DO UPDATE SET
   translated_text = excluded.translated_text,
   translation_state = excluded.translation_state,
   translator = excluded.translator,
   updated_utc = excluded.updated_utc;";
      command.Parameters.AddWithValue( "$source_kind", sourceKind );
      command.Parameters.AddWithValue( "$source_id", sourceId );
      command.Parameters.AddWithValue( "$translated_text", translatedText );
      command.Parameters.AddWithValue( "$translation_state", translationState );
      command.Parameters.AddWithValue( "$translator", (object?)translator ?? DBNull.Value );
      command.Parameters.AddWithValue( "$created_utc", ToIsoString( DateTimeOffset.UtcNow ) );
      command.Parameters.AddWithValue( "$updated_utc", ToIsoString( DateTimeOffset.UtcNow ) );
      command.ExecuteNonQuery();
      return TranslationImportStatus.Applied;
   }

   private static string BuildCombinedSourceQuery( string translationTable, string extraWhereClause )
   {
      return $@"
SELECT
   s.source_kind,
   s.source_id,
   s.source_key,
   s.raw_text,
   s.occurrence_count,
   s.sample_payload_json,
   s.patch_targets_json,
   t.translated_text,
   COALESCE(t.translation_state, 'untranslated') AS translation_state,
   s.source_origin
FROM (
   SELECT
      'native_mod' AS source_kind,
      id AS source_id,
      source_key,
      raw_text,
      occurrence_count,
      NULL AS sample_payload_json,
      patch_targets_json,
      NULL AS source_origin
   FROM native_mod_source
    WHERE state = 'active'
       AND COALESCE(is_translatable, 1) = 1

   UNION ALL

   SELECT
      'runtime' AS source_kind,
      id AS source_id,
      source_key,
      raw_text,
      occurrence_count,
      sample_payload_json,
      NULL AS patch_targets_json,
      source_origin
   FROM runtime_source
   WHERE state = 'active'
) s
LEFT JOIN {translationTable} t ON t.source_kind = s.source_kind AND t.source_id = s.source_id
WHERE 1 = 1
{extraWhereClause}
ORDER BY s.occurrence_count DESC, s.source_kind, s.source_id";
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

   private static void EnsureRuntimeSourceColumns( SqliteConnection connection )
   {
      using( var command = connection.CreateCommand() )
      {
         command.CommandText = @"
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
);";
         command.ExecuteNonQuery();
      }

      EnsureColumnExists( connection, "runtime_source", "source_origin", $"TEXT NOT NULL DEFAULT '{RuntimeSourceOrigins.RuntimeMiss}'" );

      using var command2 = connection.CreateCommand();
      command2.CommandText = @"
UPDATE runtime_source
SET source_origin = $runtime_miss_origin
WHERE source_origin IS NULL OR TRIM(source_origin) = '';";
      command2.Parameters.AddWithValue( "$runtime_miss_origin", RuntimeSourceOrigins.RuntimeMiss );
      command2.ExecuteNonQuery();
   }

   private static void EnsureNativeModSourceColumns( SqliteConnection connection )
   {
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
);";
         command.ExecuteNonQuery();
      }

      EnsureColumnExists( connection, "native_mod_source", "is_translatable", "INTEGER NOT NULL DEFAULT 1" );

      using var command2 = connection.CreateCommand();
      command2.CommandText = @"
UPDATE native_mod_source
SET is_translatable = 1
WHERE is_translatable IS NULL;";
      command2.ExecuteNonQuery();
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

   private static string ToIsoString( DateTimeOffset value )
   {
      return value.UtcDateTime.ToString( "O", CultureInfo.InvariantCulture );
   }

   private static string? GetNullableString( SqliteDataReader reader, int index )
   {
      return reader.IsDBNull( index ) ? null : reader.GetString( index );
   }

   private static ProjectedEntryFields ProjectEntryFields( string rawText, CorpusTextProcessingConfiguration configuration )
   {
      var snapshot = EntryProjector.CreateEntrySnapshot( rawText, configuration );
      return new ProjectedEntryFields( snapshot.RuntimeKey, snapshot.RenderKey, snapshot.TextKind );
   }

   private static SampleContext ResolveSampleContext( string? samplePayloadJson, string? patchTargetsJson )
   {
      if( !string.IsNullOrWhiteSpace( samplePayloadJson ) )
      {
         try
         {
            var runtimeSample = JsonSerializer.Deserialize<RuntimeSamplePayload>( samplePayloadJson );
            if( runtimeSample != null )
            {
               return new SampleContext(
                  runtimeSample.SourcePath,
                  runtimeSample.LocationKind,
                  runtimeSample.LocationPath,
                  runtimeSample.ContextBefore,
                  runtimeSample.ContextAfter,
                  runtimeSample.MetadataJson );
            }
         }
         catch( JsonException )
         {
         }
      }

      if( !string.IsNullOrWhiteSpace( patchTargetsJson ) )
      {
         try
         {
            var patchTargets = JsonSerializer.Deserialize<List<NativeModPatchTarget>>( patchTargetsJson );
            var sampleTarget = patchTargets?.FirstOrDefault();
            if( sampleTarget != null )
            {
               return new SampleContext(
                  sampleTarget.SourcePath,
                  sampleTarget.LocationKind,
                  sampleTarget.LocationPath,
                  sampleTarget.ContextBefore,
                  sampleTarget.ContextAfter,
                  sampleTarget.MetadataJson );
            }
         }
         catch( JsonException )
         {
         }
      }

      return SampleContext.Empty;
   }

   private enum TranslationImportStatus
   {
      Applied,
      Skipped,
      Unknown,
   }

   private sealed record SourceReference( string SourceKind, long SourceId, string SourceKey );

   private sealed record ProjectedEntryFields( string RuntimeKey, string RenderKey, string TextKind );

   private sealed record SampleContext(
      string? SampleSourcePath,
      string? SampleLocationKind,
      string? SampleLocationPath,
      string? SampleContextBefore,
      string? SampleContextAfter,
      string? MetadataJson )
   {
      public static readonly SampleContext Empty = new( null, null, null, null, null, null );
   }

   private sealed record RuntimeSamplePayload(
      string? SourcePath,
      string? LocationKind,
      string? LocationPath,
      string? ContextBefore,
      string? ContextAfter,
      string? MetadataJson );

   private sealed record NativeModPatchTarget(
      string SourcePath,
      string SourceContentHash,
      string LocationKind,
      string LocationPath,
      string? ContextBefore,
      string? ContextAfter,
      string? MetadataJson );
}
