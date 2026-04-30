using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;
using Microsoft.Data.Sqlite;
using OstranautsTranslator.Core;
using OstranautsTranslator.Core.Processing;

namespace OstranautsTranslator.Plugin.BepInEx;

internal sealed class RuntimeMissCollector
{
   private readonly object _writeSync = new object();
   private readonly ConcurrentDictionary<string, byte> _capturedValues = new ConcurrentDictionary<string, byte>( StringComparer.Ordinal );
   private readonly ManualLogSource _logger;

   public RuntimeMissCollector( ManualLogSource logger )
   {
      _logger = logger;
   }

   public void Initialize( string databasePath )
   {
      _capturedValues.Clear();

      if( string.IsNullOrWhiteSpace( databasePath ) || !File.Exists( databasePath ) )
      {
         return;
      }

      lock( _writeSync )
      {
         try
         {
            using var connection = OpenConnection( databasePath );
            EnsureRuntimeSourceTable( connection );
         }
         catch( Exception e )
         {
            _logger.LogWarning( $"Failed to initialize runtime miss database capture. {e.Message}" );
         }
      }
   }

   public bool Capture( string databasePath, RuntimeTextProcessingConfiguration configuration, string value )
   {
      if( string.IsNullOrWhiteSpace( databasePath ) || string.IsNullOrWhiteSpace( value ) || configuration == null )
      {
         return false;
      }

      if( !_capturedValues.TryAdd( value, 0 ) )
      {
         return false;
      }

      try
      {
         lock( _writeSync )
         {
            using var connection = OpenConnection( databasePath );
            EnsureRuntimeSourceTable( connection );

            var projection = RuntimeTextProjector.CreateProjection( value, configuration );
            var sourceKey = ComputeTextHash( projection.TextKind + "\u001F" + projection.RenderKey );

            using var command = connection.CreateCommand();
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
   occurrence_count = CASE
      WHEN runtime_source.occurrence_count < 2147483647 THEN runtime_source.occurrence_count + 1
      ELSE runtime_source.occurrence_count
   END,
   has_capture_source = 1,
   source_origin = CASE
      WHEN runtime_source.source_origin = $decompiled_source_origin THEN runtime_source.source_origin
      ELSE excluded.source_origin
   END,
   state = excluded.state;";
            command.Parameters.AddWithValue( "$source_key", sourceKey );
            command.Parameters.AddWithValue( "$raw_text", value );
            command.Parameters.AddWithValue( "$source_origin", RuntimeSourceOrigins.RuntimeMiss );
            command.Parameters.AddWithValue( "$decompiled_source_origin", RuntimeSourceOrigins.DecompiledDll );
            command.Parameters.AddWithValue( "$state", "active" );
            command.ExecuteNonQuery();
         }

         return true;
      }
      catch( Exception e )
      {
         _capturedValues.TryRemove( value, out _ );
         _logger.LogWarning( $"Failed to capture runtime miss into corpus.sqlite. {e.Message}" );
         return false;
      }
   }

   private static SqliteConnection OpenConnection( string databasePath )
   {
      var connection = new SqliteConnection( $"Data Source={databasePath}" );
      connection.Open();
      return connection;
   }

   private static void EnsureRuntimeSourceTable( SqliteConnection connection )
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

      using( var command = connection.CreateCommand() )
      {
         command.CommandText = @"
UPDATE runtime_source
SET source_origin = $runtime_miss_origin
WHERE source_origin IS NULL OR TRIM(source_origin) = '';";
         command.Parameters.AddWithValue( "$runtime_miss_origin", RuntimeSourceOrigins.RuntimeMiss );
         command.ExecuteNonQuery();
      }

      using( var command = connection.CreateCommand() )
      {
         command.CommandText = "CREATE INDEX IF NOT EXISTS ix_runtime_source_state ON runtime_source(state);";
         command.ExecuteNonQuery();
      }

      using( var command = connection.CreateCommand() )
      {
         command.CommandText = "CREATE INDEX IF NOT EXISTS ix_runtime_source_origin_state ON runtime_source(source_origin, state);";
         command.ExecuteNonQuery();
      }
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

   private static string ComputeTextHash( string value )
   {
      using var sha256 = SHA256.Create();
      var hash = sha256.ComputeHash( Encoding.UTF8.GetBytes( value ?? string.Empty ) );
      var builder = new StringBuilder( hash.Length * 2 );
      for( var i = 0; i < hash.Length; i++ )
      {
         builder.Append( hash[ i ].ToString( "x2" ) );
      }

      return builder.ToString();
   }
}
