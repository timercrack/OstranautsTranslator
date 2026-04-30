using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Database;
using OstranautsTranslator.Tool.Scanning;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Exporting;

internal sealed class NativeModExporter
{
   private static readonly JsonSerializerOptions JsonOptions = new()
   {
      WriteIndented = true,
   };

   private readonly CorpusWorkspace _workspace;
   private readonly ExportNativeModCommandOptions _options;
   private int _warningCount;

   public NativeModExporter( CorpusWorkspace workspace, ExportNativeModCommandOptions options )
   {
      _workspace = workspace;
      _options = options;
   }

   public NativeModExportSummary Export()
   {
      if( !File.Exists( _workspace.CorpusDatabasePath ) )
      {
         throw new FileNotFoundException( "corpus.sqlite was not found. Run scan first.", _workspace.CorpusDatabasePath );
      }

      var corpusDatabase = new CorpusDatabase( _workspace.CorpusDatabasePath );
      var translationDatabasePath = _workspace.GetTranslationDatabasePath( _options.ToLanguage );
      if( !File.Exists( translationDatabasePath ) )
      {
         throw new FileNotFoundException( $"Translation database for '{_options.ToLanguage}' was not found. Run source/import first.", translationDatabasePath );
      }

      var textProcessingConfiguration = corpusDatabase.GetTextProcessingConfiguration();
      var translationDatabase = new TranslationDatabase( translationDatabasePath, textProcessingConfiguration.FromLanguage, _options.ToLanguage );
      translationDatabase.Initialize();

      var sourceEntries = translationDatabase.GetNativeModSourceExportEntries( _options.IncludeDraft );
      var exportPlansBySourcePath = BuildExportPlans( sourceEntries );

      var outputRootPath = _options.OutputPath ?? RuntimeTranslationDeployment.GetModsRootPath( _options.GameRootPath );
      var modDirectoryPath = Path.Combine( outputRootPath, _options.ModId );
      var modDataDirectoryPath = Path.Combine( modDirectoryPath, "data" );
      var gameDataRootPath = GetGameDataRootPath();

      PrepareOutputDirectories( outputRootPath, modDirectoryPath, modDataDirectoryPath );

      var patchedOccurrences = 0;
      var filesWritten = 0;
      var translatedEntries = sourceEntries.Count( x => !string.IsNullOrWhiteSpace( x.TranslatedText ) );
      var filesToMirror = exportPlansBySourcePath.Keys
         .Where( path => path.EndsWith( ".json", StringComparison.OrdinalIgnoreCase ) )
         .OrderBy( path => path, StringComparer.OrdinalIgnoreCase )
         .ToList();

      foreach( var relativeSourcePath in filesToMirror )
      {
         var sourceFilePath = Path.Combine( gameDataRootPath, ToPlatformRelativePath( relativeSourcePath ) );
         if( !File.Exists( sourceFilePath ) )
         {
            Warn( $"Skipping '{relativeSourcePath}' because the source file no longer exists at '{sourceFilePath}'." );
            continue;
         }

         exportPlansBySourcePath.TryGetValue( relativeSourcePath, out var exportPlan );

         if( _options.VerifySourceHash && exportPlan != null )
         {
            var expectedHash = exportPlan.SourceContentHash;
            if( !string.IsNullOrWhiteSpace( expectedHash ) )
            {
               var currentHash = FileHashHelper.ComputeFileHash( sourceFilePath );
               if( !string.Equals( expectedHash, currentHash, StringComparison.OrdinalIgnoreCase ) )
               {
                  Warn( $"Skipping '{relativeSourcePath}' because the source hash changed since the last scan. Re-run scan to refresh the recorded source hashes before exporting again." );
                  continue;
               }
            }
         }

         var outputFilePath = Path.Combine( modDataDirectoryPath, ToPlatformRelativePath( relativeSourcePath ) );
         var outputDirectoryPath = Path.GetDirectoryName( outputFilePath );
         if( !string.IsNullOrWhiteSpace( outputDirectoryPath ) )
         {
            Directory.CreateDirectory( outputDirectoryPath );
         }

         if( exportPlan == null || exportPlan.PatchRequests.Count == 0 || !relativeSourcePath.EndsWith( ".json", StringComparison.OrdinalIgnoreCase ) )
         {
            File.Copy( sourceFilePath, outputFilePath, overwrite: true );
            filesWritten++;
            continue;
         }

         JsonNode rootNode;
         try
         {
            rootNode = LooseJsonHelper.ParseNodeFromFile( sourceFilePath );
         }
         catch( Exception exception )
         {
            Warn( $"Skipping '{relativeSourcePath}' because it could not be parsed: {exception.Message}" );
            continue;
         }

         var fileModified = false;
         foreach( var occurrence in exportPlan.PatchRequests.OrderBy( x => x.LocationPath, StringComparer.Ordinal ) )
         {
            if( TrySetStringAtLocation( rootNode, GetJsonPath( occurrence.LocationPath ), occurrence.TranslatedText, occurrence.LocationKind, occurrence.ContextAfter ) )
            {
               patchedOccurrences++;
               fileModified = true;
            }
            else
            {
               Warn( $"Failed to patch '{occurrence.LocationPath}' in '{relativeSourcePath}'." );
            }
         }

         if( fileModified )
         {
            File.WriteAllText( outputFilePath, rootNode.ToJsonString( JsonOptions ) + Environment.NewLine, new UTF8Encoding( false ) );
         }
         else
         {
            File.Copy( sourceFilePath, outputFilePath, overwrite: true );
         }

         filesWritten++;
      }

      var loadingOrderPath = WriteLoadingOrder( outputRootPath );
      WriteModInfo( modDirectoryPath );

      return new NativeModExportSummary(
         translatedEntries,
         patchedOccurrences,
         filesWritten,
         _warningCount,
         outputRootPath,
         loadingOrderPath,
         modDirectoryPath );
   }

   private string WriteLoadingOrder( string outputRootPath )
   {
      var loadingOrderPath = Path.Combine( outputRootPath, "loading_order.json" );
      Directory.CreateDirectory( outputRootPath );

      var entries = LoadExistingLoadingOrder();
      if( entries.Count == 0 )
      {
         entries.Add( new LoadingOrderDocument
         {
            strName = "Mod Loading Order",
            aLoadOrder = ["core"],
            aIgnorePatterns = [],
         } );
      }

      var loadingOrderEntry = entries.FirstOrDefault( x => string.Equals( x.strName, "Mod Loading Order", StringComparison.OrdinalIgnoreCase ) );
      if( loadingOrderEntry == null )
      {
         loadingOrderEntry = new LoadingOrderDocument
         {
            strName = "Mod Loading Order",
            aLoadOrder = [],
            aIgnorePatterns = [],
         };
         entries.Insert( 0, loadingOrderEntry );
      }

      var mergedLoadOrder = new List<string>();
      foreach( var item in loadingOrderEntry.aLoadOrder ?? [] )
      {
         if( string.IsNullOrWhiteSpace( item ) ) continue;
         if( mergedLoadOrder.Any( existing => string.Equals( existing, item, StringComparison.OrdinalIgnoreCase ) ) ) continue;
         mergedLoadOrder.Add( item );
      }

      if( !mergedLoadOrder.Any( x => string.Equals( x, "core", StringComparison.OrdinalIgnoreCase ) ) )
      {
         mergedLoadOrder.Insert( 0, "core" );
      }

      foreach( var obsoleteModId in RuntimeTranslationDeployment.ObsoleteModIdsToDelete )
      {
         mergedLoadOrder.RemoveAll( x => string.Equals( x, obsoleteModId, StringComparison.OrdinalIgnoreCase ) );
      }

      mergedLoadOrder.RemoveAll( x => string.Equals( x, _options.ModId, StringComparison.OrdinalIgnoreCase ) );
      mergedLoadOrder.Add( _options.ModId );

      loadingOrderEntry.strName = string.IsNullOrWhiteSpace( loadingOrderEntry.strName ) ? "Mod Loading Order" : loadingOrderEntry.strName;
      loadingOrderEntry.aLoadOrder = mergedLoadOrder.ToArray();
      loadingOrderEntry.aIgnorePatterns ??= [];

      File.WriteAllText( loadingOrderPath, JsonSerializer.Serialize( entries, JsonOptions ) + Environment.NewLine, new UTF8Encoding( false ) );
      return loadingOrderPath;
   }

   private void WriteModInfo( string modDirectoryPath )
   {
      Directory.CreateDirectory( modDirectoryPath );
      var modInfoPath = Path.Combine( modDirectoryPath, "mod_info.json" );
      if( string.IsNullOrWhiteSpace( _options.GameVersion ) )
      {
         Warn( $"Game version is empty because the runtime plugin has not recorded it yet. Launch Ostranauts once so the plugin can read Resources/version, then rerun {RuntimeTranslationDeployment.ToolExecutableName}.exe to refresh mod_info.json." );
      }

      var modInfo = new[]
      {
         new ModInfoDocument
         {
            strName = _options.ModName,
            strAuthor = _options.Author,
            strModURL = _options.ModUrl,
            strGameVersion = _options.GameVersion,
            strModVersion = _options.ModVersion,
            strNotes = _options.Notes,
         },
      };

      File.WriteAllText( modInfoPath, JsonSerializer.Serialize( modInfo, JsonOptions ) + Environment.NewLine, new UTF8Encoding( false ) );
   }

   private List<LoadingOrderDocument> LoadExistingLoadingOrder()
   {
      var loadingOrderPath = Path.Combine( _options.GameRootPath, "Ostranauts_Data", "Mods", "loading_order.json" );
      if( !File.Exists( loadingOrderPath ) )
      {
         return [];
      }

      try
      {
         var parsed = JsonSerializer.Deserialize<List<LoadingOrderDocument>>( LooseJsonHelper.NormalizeLooseJson( File.ReadAllText( loadingOrderPath ) ) );
         return parsed ?? [];
      }
      catch( Exception exception )
      {
         Warn( $"Failed to read existing loading_order.json from '{loadingOrderPath}': {exception.Message}" );
         return [];
      }
   }

   private string GetGameDataRootPath()
   {
      var dataRootPath = Path.Combine( _options.GameRootPath, "Ostranauts_Data", "StreamingAssets", "data" );
      if( !Directory.Exists( dataRootPath ) )
      {
         throw new DirectoryNotFoundException( $"Could not find game data directory '{dataRootPath}'." );
      }

      return dataRootPath;
   }

   private static void PrepareOutputDirectories( string outputRootPath, string modDirectoryPath, string modDataDirectoryPath )
   {
      Directory.CreateDirectory( outputRootPath );

      foreach( var obsoleteModId in RuntimeTranslationDeployment.ObsoleteModIdsToDelete )
      {
         var obsoleteModDirectoryPath = Path.Combine( outputRootPath, obsoleteModId );
         if( Directory.Exists( obsoleteModDirectoryPath ) )
         {
            Directory.Delete( obsoleteModDirectoryPath, recursive: true );
         }
      }

      if( Directory.Exists( modDirectoryPath ) )
      {
         Directory.Delete( modDirectoryPath, recursive: true );
      }

      Directory.CreateDirectory( modDataDirectoryPath );
   }

   private Dictionary<string, NativeModFileExportPlan> BuildExportPlans( IReadOnlyList<NativeModSourceExportRecord> sourceEntries )
   {
      var results = new Dictionary<string, NativeModFileExportPlan>( StringComparer.OrdinalIgnoreCase );
      foreach( var entry in sourceEntries )
      {
         List<PatchTarget>? patchTargets;
         try
         {
            patchTargets = JsonSerializer.Deserialize<List<PatchTarget>>( entry.PatchTargetsJson );
         }
         catch( Exception exception )
         {
            Warn( $"Skipping native-mod source '{entry.SourceKey}' because patch_targets_json could not be parsed: {exception.Message}" );
            continue;
         }

         if( patchTargets == null || patchTargets.Count == 0 )
         {
            continue;
         }

         foreach( var patchTarget in patchTargets )
         {
            if( string.IsNullOrWhiteSpace( patchTarget.SourcePath ) || string.IsNullOrWhiteSpace( patchTarget.LocationPath ) )
            {
               continue;
            }

            if( !results.TryGetValue( patchTarget.SourcePath, out var exportPlan ) )
            {
               exportPlan = new NativeModFileExportPlan(
                  patchTarget.SourceContentHash,
                  new List<NativeModPatchRequest>() );
               results.Add( patchTarget.SourcePath, exportPlan );
            }
            else if( string.IsNullOrWhiteSpace( exportPlan.SourceContentHash ) && !string.IsNullOrWhiteSpace( patchTarget.SourceContentHash ) )
            {
               exportPlan.SourceContentHash = patchTarget.SourceContentHash;
            }

            if( !string.IsNullOrWhiteSpace( entry.TranslatedText ) )
            {
               exportPlan.PatchRequests.Add( new NativeModPatchRequest(
                  entry.SourceId,
                  entry.SourceKey,
                  patchTarget.LocationKind,
                  patchTarget.LocationPath,
                  patchTarget.ContextAfter,
                  entry.TranslatedText ) );
            }
         }
      }

      return results;
   }

   private static string GetJsonPath( string locationPath )
   {
      var separatorIndex = locationPath.IndexOf( "::", StringComparison.Ordinal );
      if( separatorIndex < 0 || separatorIndex + 2 >= locationPath.Length )
      {
         throw new InvalidOperationException( $"Location path '{locationPath}' does not contain a JSON path segment." );
      }

      return locationPath[( separatorIndex + 2 )..];
   }

   private static string ToPlatformRelativePath( string relativePath )
   {
      return relativePath
         .Replace( '/', Path.DirectorySeparatorChar )
         .Replace( '\\', Path.DirectorySeparatorChar );
   }

   private static bool TrySetStringAtLocation( JsonNode rootNode, string jsonPath, string translatedText, string locationKind, string? contextAfter )
   {
      var segments = ParseJsonPath( jsonPath );
      JsonNode? currentNode = rootNode;
      for( var i = 0; i < segments.Count - 1; i++ )
      {
         currentNode = segments[ i ].TryGetChildNode( currentNode );
         if( currentNode == null )
         {
            return false;
         }
      }

      if( currentNode == null )
      {
         return false;
      }

      return string.Equals( locationKind, "json-assignment-array-field", StringComparison.Ordinal )
         ? segments[^1].TrySetAssignmentValue( currentNode, contextAfter, translatedText )
         : segments[^1].TrySetNodeValue( currentNode, translatedText );
   }

   private static bool TryParseAssignmentValue( string? rawValue, out string? fieldName, out string? fieldValue )
   {
      fieldName = null;
      fieldValue = null;

      if( string.IsNullOrWhiteSpace( rawValue ) ) return false;

      var values = rawValue.Split( '|' );
      if( values.Length != 2 ) return false;

      fieldName = values[ 0 ];
      fieldValue = values[ 1 ];

      return !string.IsNullOrWhiteSpace( fieldName ) && !string.IsNullOrWhiteSpace( fieldValue );
   }

   private static List<JsonPathSegment> ParseJsonPath( string jsonPath )
   {
      if( string.IsNullOrWhiteSpace( jsonPath ) || jsonPath[ 0 ] != '$' )
      {
         throw new InvalidOperationException( $"Unsupported JSON path '{jsonPath}'." );
      }

      var segments = new List<JsonPathSegment>();
      for( var i = 1; i < jsonPath.Length; )
      {
         if( jsonPath[ i ] == '.' )
         {
            var propertyStart = ++i;
            while( i < jsonPath.Length && jsonPath[ i ] != '.' && jsonPath[ i ] != '[' )
            {
               i++;
            }

            segments.Add( JsonPathSegment.ForProperty( jsonPath[propertyStart..i] ) );
            continue;
         }

         if( jsonPath[ i ] == '[' )
         {
            var indexStart = ++i;
            while( i < jsonPath.Length && jsonPath[ i ] != ']' )
            {
               i++;
            }

            if( i >= jsonPath.Length )
            {
               throw new InvalidOperationException( $"Unsupported JSON path '{jsonPath}'." );
            }

            var indexText = jsonPath[indexStart..i];
            i++;
            if( !int.TryParse( indexText, out var index ) )
            {
               throw new InvalidOperationException( $"Unsupported JSON path index '{indexText}' in '{jsonPath}'." );
            }

            segments.Add( JsonPathSegment.ForIndex( index ) );
            continue;
         }

         throw new InvalidOperationException( $"Unsupported JSON path '{jsonPath}'." );
      }

      return segments;
   }

   private void Warn( string message )
   {
      _warningCount++;
      Console.Error.WriteLine( "[native-mod] " + message );
   }

   private sealed class ModInfoDocument
   {
      public string? strName { get; set; }

      public string? strAuthor { get; set; }

      public string? strModURL { get; set; }

      public string? strGameVersion { get; set; }

      public string? strModVersion { get; set; }

      public string? strNotes { get; set; }
   }

   private sealed class LoadingOrderDocument
   {
      public string? strName { get; set; }

      public string[]? aLoadOrder { get; set; }

      public string[]? aIgnorePatterns { get; set; }
   }

   private sealed record PatchTarget(
      string SourcePath,
      string SourceContentHash,
      string LocationKind,
      string LocationPath,
      string? ContextBefore,
      string? ContextAfter );

   private sealed record NativeModPatchRequest(
      long SourceId,
      string SourceKey,
      string LocationKind,
      string LocationPath,
      string? ContextAfter,
      string TranslatedText );

   private sealed class NativeModFileExportPlan
   {
      public NativeModFileExportPlan( string? sourceContentHash, List<NativeModPatchRequest> patchRequests )
      {
         SourceContentHash = sourceContentHash;
         PatchRequests = patchRequests;
      }

      public string? SourceContentHash { get; set; }

      public List<NativeModPatchRequest> PatchRequests { get; }
   }

   private readonly record struct JsonPathSegment( string? PropertyName, int? Index )
   {
      public static JsonPathSegment ForProperty( string propertyName ) => new( propertyName, null );

      public static JsonPathSegment ForIndex( int index ) => new( null, index );

      public JsonNode? TryGetChildNode( JsonNode? currentNode )
      {
         if( currentNode == null ) return null;

         if( PropertyName != null )
         {
            if( currentNode is not JsonObject jsonObject ) return null;
            return jsonObject[ PropertyName ];
         }

         if( Index.HasValue )
         {
            if( currentNode is not JsonArray jsonArray ) return null;
            if( Index.Value < 0 || Index.Value >= jsonArray.Count ) return null;
            return jsonArray[ Index.Value ];
         }

         return null;
      }

      public bool TrySetNodeValue( JsonNode currentNode, string value )
      {
         if( PropertyName != null )
         {
            if( currentNode is not JsonObject jsonObject ) return false;
            if( !jsonObject.ContainsKey( PropertyName ) ) return false;
            jsonObject[ PropertyName ] = value;
            return true;
         }

         if( Index.HasValue )
         {
            if( currentNode is not JsonArray jsonArray ) return false;
            if( Index.Value < 0 || Index.Value >= jsonArray.Count ) return false;
            jsonArray[ Index.Value ] = value;
            return true;
         }

         return false;
      }

      public bool TrySetAssignmentValue( JsonNode currentNode, string? expectedFieldName, string translatedText )
      {
         if( PropertyName != null )
         {
            if( currentNode is not JsonObject jsonObject ) return false;
            if( !jsonObject.ContainsKey( PropertyName ) ) return false;

            var childNode = jsonObject[ PropertyName ];
            if( !TryParseAssignmentValue( childNode, expectedFieldName, out var fieldName ) ) return false;

            jsonObject[ PropertyName ] = fieldName + "|" + translatedText;
            return true;
         }

         if( Index.HasValue )
         {
            if( currentNode is not JsonArray jsonArray ) return false;
            if( Index.Value < 0 || Index.Value >= jsonArray.Count ) return false;

            var childNode = jsonArray[ Index.Value ];
            if( !TryParseAssignmentValue( childNode, expectedFieldName, out var fieldName ) ) return false;

            jsonArray[ Index.Value ] = fieldName + "|" + translatedText;
            return true;
         }

         return false;
      }

      private static bool TryParseAssignmentValue( JsonNode? node, string? expectedFieldName, out string? fieldName )
      {
         fieldName = null;
         if( node == null ) return false;

         string? rawValue;
         try
         {
            rawValue = node.GetValue<string?>();
         }
         catch( InvalidOperationException )
         {
            return false;
         }

         if( !NativeModExporter.TryParseAssignmentValue( rawValue, out fieldName, out _ ) ) return false;
         if( string.IsNullOrWhiteSpace( expectedFieldName ) ) return true;

         return string.Equals( fieldName, expectedFieldName, StringComparison.Ordinal );
      }
   }
}
