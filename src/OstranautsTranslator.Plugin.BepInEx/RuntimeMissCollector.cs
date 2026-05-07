using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using BepInEx.Logging;
using Microsoft.Data.Sqlite;
using OstranautsTranslator.Core;
using OstranautsTranslator.Core.Processing;

namespace OstranautsTranslator.Plugin.BepInEx;

internal sealed class RuntimeMissCollector
{
   private const string IgnoredMalformedRichTextState = "ignored-malformed-richtext";
   private const string RichTextTagPattern = @"align|alpha|b|cspace|color|font|i|indent|line-height|line-indent|link|lowercase|margin(?:-left|-right)?|mark|mspace|nobr|noparse|pos|rotate|s|size|smallcaps|space|sprite|sub|sup|u|uppercase|voffset|width";
   private static readonly Regex RichTextTagRegex = new Regex(
      @"<\s*/?\s*(?:" + RichTextTagPattern + @")(?:\s*=[^<>]*)?\s*>",
      RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex RichTextTagTokenRegex = new Regex(
      @"<\s*(/?)\s*(" + RichTextTagPattern + @")(?:\s*=[^<>]*)?\s*>",
      RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex BrokenBoundaryRichTextTagRegex = new Regex(
      @"(?:^|[\r\n])\s*(?:/\s*)?(?:" + RichTextTagPattern + @")(?:\s*=[^<>\r\n]*)?>",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );
   private static readonly Regex BrokenTrailingRichTextTagRegex = new Regex(
      @"</\s*(?:" + RichTextTagPattern + @")\s*$",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );
   private static readonly Regex IncompleteOpeningRichTextTagRegex = new Regex(
      @"<\s*/?\s*(?:" + RichTextTagPattern + @")[^>\r\n]*$",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );
   private static readonly Regex BrokenInlineClosingRichTextTagRegex = new Regex(
      @"(?:^|[^<])/(?:" + RichTextTagPattern + @")\s*$",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );
   private static readonly HashSet<string> SelfContainedRichTextTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
   {
      "sprite",
      "space",
      "cspace",
      "voffset",
      "pos",
      "width",
      "margin",
      "margin-left",
      "margin-right",
      "indent",
      "line-indent",
      "line-height",
      "rotate",
      "mark",
      "noparse",
      "nobr",
      "mspace",
   };
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
            var cleanedRows = CleanupMalformedRuntimeSourceEntries( connection );
            if( cleanedRows > 0 )
            {
               _logger.LogInfo( $"Marked {cleanedRows} malformed rich-text runtime_source rows as '{IgnoredMalformedRichTextState}'." );
            }
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

      if( RuntimeVolatileTextDetector.LooksVolatile( value, configuration ) )
      {
         return false;
      }

      if( MalformedRichTextDetector.LooksMalformed( value ) )
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

   private static bool LooksLikeBrokenRichTextFragment( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) )
      {
         return true;
      }

      var trimmed = value.Trim();
      if( trimmed.Length == 0 )
      {
         return true;
      }

      if( BrokenBoundaryRichTextTagRegex.IsMatch( trimmed )
         || BrokenTrailingRichTextTagRegex.IsMatch( trimmed )
         || IncompleteOpeningRichTextTagRegex.IsMatch( trimmed )
         || BrokenInlineClosingRichTextTagRegex.IsMatch( trimmed ) )
      {
         return true;
      }

      if( HasUnbalancedRichTextTags( trimmed ) )
      {
         return true;
      }

      if( trimmed.IndexOf( '<' ) < 0 )
      {
         return false;
      }

      var stripped = RichTextTagRegex.Replace( trimmed, string.Empty ).Trim();
      return stripped.Length == 0 || stripped.StartsWith( "<", StringComparison.Ordinal ) || stripped.EndsWith( ">", StringComparison.Ordinal );
   }

   private static bool HasUnbalancedRichTextTags( string value )
   {
      var stack = new Stack<string>();
      var recognizedTagCount = 0;

      foreach( Match match in RichTextTagTokenRegex.Matches( value ) )
      {
         if( !match.Success ) continue;

         recognizedTagCount++;
         var isClosing = string.Equals( match.Groups[ 1 ].Value, "/", StringComparison.Ordinal );
         var tagName = match.Groups[ 2 ].Value;
         if( tagName.Length == 0 || SelfContainedRichTextTags.Contains( tagName ) )
         {
            continue;
         }

         if( isClosing )
         {
            if( stack.Count == 0 )
            {
               return true;
            }

            var openTagName = stack.Pop();
            if( !string.Equals( openTagName, tagName, StringComparison.OrdinalIgnoreCase ) )
            {
               return true;
            }

            continue;
         }

         stack.Push( tagName );
      }

      return recognizedTagCount > 0 && stack.Count > 0;
   }

   private static int CleanupMalformedRuntimeSourceEntries( SqliteConnection connection )
   {
      var rowIds = new List<long>();

      using( var command = connection.CreateCommand() )
      {
         command.CommandText = @"
SELECT id, raw_text
FROM runtime_source
WHERE state = 'active'
  AND (raw_text LIKE '%<%' OR raw_text LIKE '%>%' OR raw_text LIKE '%/%');";

         using var reader = command.ExecuteReader();
         while( reader.Read() )
         {
            var id = reader.GetInt64( 0 );
            var rawText = reader.IsDBNull( 1 ) ? string.Empty : reader.GetString( 1 );
            if( MalformedRichTextDetector.LooksMalformed( rawText ) )
            {
               rowIds.Add( id );
            }
         }
      }

      if( rowIds.Count == 0 )
      {
         return 0;
      }

      foreach( var rowId in rowIds )
      {
         using var command = connection.CreateCommand();
         command.CommandText = "UPDATE runtime_source SET state = $state WHERE id = $id;";
         command.Parameters.AddWithValue( "$state", IgnoredMalformedRichTextState );
         command.Parameters.AddWithValue( "$id", rowId );
         command.ExecuteNonQuery();
      }

      return rowIds.Count;
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
