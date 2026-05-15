using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Database;
using OstranautsTranslator.Tool.Scanning;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Exporting;

internal sealed class NativeModExporter
{
   private const char HotkeyTokenDelimiter = '\u241E';
   private const string WorkspaceCustomImagesDirectoryName = "mod-images";

   private static readonly JavaScriptEncoder JsonEncoder = JavaScriptEncoder.Create( UnicodeRanges.All );

   private static readonly JsonSerializerOptions JsonOptions = new()
   {
      WriteIndented = true,
      Encoder = JsonEncoder,
   };

   private static readonly JsonWriterOptions WriterOptions = new()
   {
      Indented = true,
      Encoder = JsonEncoder,
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

      PreserveCustomImagesFromExistingMod( modDirectoryPath );
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

         try
         {
            if( TryWritePatchedJsonFile( sourceFilePath, outputFilePath, relativeSourcePath, exportPlan, out var patchedCount ) )
            {
               patchedOccurrences += patchedCount;
            }
            else
            {
               File.Copy( sourceFilePath, outputFilePath, overwrite: true );
            }

            filesWritten++;
         }
         catch( Exception exception )
         {
            Warn( $"Skipping '{relativeSourcePath}' because it could not be parsed: {exception.Message}" );
            continue;
         }
      }

      filesWritten += CopyWorkspaceCustomImages( modDirectoryPath );
      filesWritten += CopyGuiButtonImages( modDirectoryPath );

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

   private int CopyGuiButtonImages( string modDirectoryPath )
   {
      var sourceImagesDirectoryPath = Path.Combine( _options.GameRootPath, "Ostranauts_Data", "StreamingAssets", "images" );
      if( !Directory.Exists( sourceImagesDirectoryPath ) )
      {
         Warn( $"Skipping GUI button image mirroring because '{sourceImagesDirectoryPath}' was not found." );
         return 0;
      }

      var outputImagesDirectoryPath = Path.Combine( modDirectoryPath, "images" );
      Directory.CreateDirectory( outputImagesDirectoryPath );

      var copiedCount = 0;
      foreach( var sourceFilePath in Directory.EnumerateFiles( sourceImagesDirectoryPath, "GUIBtn*.png", SearchOption.TopDirectoryOnly ) )
      {
         var fileName = Path.GetFileName( sourceFilePath );
         if( string.IsNullOrWhiteSpace( fileName ) ) continue;

         var outputFilePath = Path.Combine( outputImagesDirectoryPath, fileName );
         if( File.Exists( outputFilePath ) )
         {
            continue;
         }

         File.Copy( sourceFilePath, outputFilePath, overwrite: false );
         copiedCount++;
      }

      return copiedCount;
   }

   private void PreserveCustomImagesFromExistingMod( string modDirectoryPath )
   {
      var existingImagesDirectoryPath = Path.Combine( modDirectoryPath, "images" );
      if( !Directory.Exists( existingImagesDirectoryPath ) )
      {
         return;
      }

      var workspaceCustomImagesDirectoryPath = GetWorkspaceCustomImagesDirectoryPath();
      foreach( var existingImagePath in Directory.EnumerateFiles( existingImagesDirectoryPath, "*", SearchOption.AllDirectories ) )
      {
         var relativeImagePath = Path.GetRelativePath( existingImagesDirectoryPath, existingImagePath );
         var sourceImagePath = Path.Combine( _options.GameRootPath, "Ostranauts_Data", "StreamingAssets", "images", relativeImagePath );
         if( File.Exists( sourceImagePath )
            && string.Equals( FileHashHelper.ComputeFileHash( existingImagePath ), FileHashHelper.ComputeFileHash( sourceImagePath ), StringComparison.OrdinalIgnoreCase ) )
         {
            continue;
         }

         var preservedImagePath = Path.Combine( workspaceCustomImagesDirectoryPath, relativeImagePath );
         var preservedImageDirectoryPath = Path.GetDirectoryName( preservedImagePath );
         if( !string.IsNullOrWhiteSpace( preservedImageDirectoryPath ) )
         {
            Directory.CreateDirectory( preservedImageDirectoryPath );
         }

         if( File.Exists( preservedImagePath ) )
         {
            continue;
         }

         File.Copy( existingImagePath, preservedImagePath, overwrite: false );
      }
   }

   private int CopyWorkspaceCustomImages( string modDirectoryPath )
   {
      var workspaceCustomImagesDirectoryPath = GetWorkspaceCustomImagesDirectoryPath();
      if( !Directory.Exists( workspaceCustomImagesDirectoryPath ) )
      {
         return 0;
      }

      var outputImagesDirectoryPath = Path.Combine( modDirectoryPath, "images" );
      Directory.CreateDirectory( outputImagesDirectoryPath );

      var copiedCount = 0;
      foreach( var customImagePath in Directory.EnumerateFiles( workspaceCustomImagesDirectoryPath, "*", SearchOption.AllDirectories ) )
      {
         var relativeImagePath = Path.GetRelativePath( workspaceCustomImagesDirectoryPath, customImagePath );
         var outputImagePath = Path.Combine( outputImagesDirectoryPath, relativeImagePath );
         var outputImageDirectoryPath = Path.GetDirectoryName( outputImagePath );
         if( !string.IsNullOrWhiteSpace( outputImageDirectoryPath ) )
         {
            Directory.CreateDirectory( outputImageDirectoryPath );
         }

         File.Copy( customImagePath, outputImagePath, overwrite: true );
         copiedCount++;
      }

      return copiedCount;
   }

   private string GetWorkspaceCustomImagesDirectoryPath()
   {
      return Path.Combine( _workspace.RootPath, WorkspaceCustomImagesDirectoryName );
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

   private bool TryWritePatchedJsonFile( string sourceFilePath, string outputFilePath, string relativeSourcePath, NativeModFileExportPlan exportPlan, out int patchedCount )
   {
      using var rootDocument = LooseJsonHelper.ParseDocumentFromFile( sourceFilePath );
      var patchOperations = exportPlan.PatchRequests
         .OrderBy( x => x.LocationPath, StringComparer.Ordinal )
         .Select( x => new JsonPatchOperation(
            x.LocationPath,
            GetJsonPath( x.LocationPath ),
            x.LocationKind,
            x.ContextAfter,
            x.TranslatedText ) )
         .ToList();

      using var outputStream = new MemoryStream();
      using( var writer = new Utf8JsonWriter( outputStream, WriterOptions ) )
      {
         WritePatchedElement( writer, rootDocument.RootElement, "$", patchOperations );
         writer.Flush();
      }

      outputStream.Write( Encoding.UTF8.GetBytes( Environment.NewLine ) );
      File.WriteAllBytes( outputFilePath, outputStream.ToArray() );

      patchedCount = 0;
      foreach( var patchOperation in patchOperations )
      {
         if( patchOperation.Applied )
         {
            patchedCount++;
            continue;
         }

         Warn( $"Failed to patch '{patchOperation.LocationPath}' in '{relativeSourcePath}'." );
      }

      return patchedCount > 0;
   }

   private static void WritePatchedElement( Utf8JsonWriter writer, JsonElement element, string currentPath, IReadOnlyList<JsonPatchOperation> patchOperations )
   {
      switch( element.ValueKind )
      {
         case JsonValueKind.Object:
         {
            writer.WriteStartObject();
            foreach( var property in element.EnumerateObject() )
            {
               writer.WritePropertyName( property.Name );
               WritePatchedElement( writer, property.Value, currentPath + "." + property.Name, patchOperations );
            }

            writer.WriteEndObject();
            return;
         }
         case JsonValueKind.Array:
         {
            writer.WriteStartArray();
            var index = 0;
            foreach( var item in element.EnumerateArray() )
            {
               WritePatchedElement( writer, item, $"{currentPath}[{index}]", patchOperations );
               index++;
            }

            writer.WriteEndArray();
            return;
         }
         case JsonValueKind.String:
         {
            if( TryGetPatchedStringValue( currentPath, element.GetString(), patchOperations, out var patchedValue ) )
            {
               writer.WriteStringValue( patchedValue );
               return;
            }

            break;
         }
      }

      element.WriteTo( writer );
   }

   private static bool TryGetPatchedStringValue( string currentPath, string? currentValue, IReadOnlyList<JsonPatchOperation> patchOperations, out string patchedValue )
   {
      foreach( var patchOperation in patchOperations )
      {
         if( patchOperation.Applied || !string.Equals( patchOperation.JsonPath, currentPath, StringComparison.Ordinal ) )
         {
            continue;
         }

         if( string.Equals( patchOperation.LocationKind, "json-assignment-array-field", StringComparison.Ordinal ) )
         {
            if( !TryParseAssignmentValue( currentValue, out var fieldName, out _, out _ )
               || ( !string.IsNullOrWhiteSpace( patchOperation.ContextAfter )
                  && !string.Equals( fieldName, patchOperation.ContextAfter, StringComparison.Ordinal ) ) )
            {
               continue;
            }

            patchOperation.Applied = true;
            TryParseAssignmentValue( currentValue, out _, out var fieldValue, out var delimiter );
            patchedValue = fieldName + delimiter + PreserveDelimitedTokens( fieldValue, patchOperation.TranslatedText );
            return true;
         }

         patchOperation.Applied = true;
         patchedValue = PreserveDelimitedTokens( currentValue, patchOperation.TranslatedText );
         return true;
      }

      patchedValue = string.Empty;
      return false;
   }

   private static bool TryParseAssignmentValue( string? rawValue, out string? fieldName, out string? fieldValue, out char delimiter )
   {
      fieldName = null;
      fieldValue = null;
      delimiter = '\0';

      if( string.IsNullOrWhiteSpace( rawValue ) ) return false;

      var delimiterIndex = rawValue.IndexOfAny( ['|', '='] );
      if( delimiterIndex <= 0 || delimiterIndex >= rawValue.Length - 1 ) return false;

      delimiter = rawValue[ delimiterIndex ];
      fieldName = rawValue[..delimiterIndex];
      fieldValue = rawValue[( delimiterIndex + 1 )..];

      return !string.IsNullOrWhiteSpace( fieldName ) && !string.IsNullOrWhiteSpace( fieldValue );
   }

   private static string PreserveDelimitedTokens( string? sourceText, string translatedText )
   {
      if( string.IsNullOrEmpty( sourceText ) || string.IsNullOrEmpty( translatedText ) )
      {
         return translatedText;
      }

      var sourceTokens = ExtractDelimitedTokenContents( sourceText );
      if( sourceTokens.Count == 0 )
      {
         return translatedText;
      }

      var translatedRanges = ExtractDelimitedTokenRanges( translatedText );
      if( translatedRanges.Count != sourceTokens.Count )
      {
         return translatedText;
      }

      var builder = new StringBuilder( translatedText.Length );
      var cursor = 0;
      for( var index = 0; index < translatedRanges.Count; index++ )
      {
         var range = translatedRanges[ index ];
         builder.Append( translatedText, cursor, range.Start - cursor );
         builder.Append( HotkeyTokenDelimiter );
         builder.Append( sourceTokens[ index ] );
         builder.Append( HotkeyTokenDelimiter );
         cursor = range.End + 1;
      }

      builder.Append( translatedText, cursor, translatedText.Length - cursor );
      return builder.ToString();
   }

   private static List<string> ExtractDelimitedTokenContents( string value )
   {
      var results = new List<string>();
      foreach( var range in ExtractDelimitedTokenRanges( value ) )
      {
         results.Add( value.Substring( range.Start + 1, range.End - range.Start - 1 ) );
      }

      return results;
   }

   private static List<(int Start, int End)> ExtractDelimitedTokenRanges( string value )
   {
      var results = new List<(int Start, int End)>();
      var tokenStart = -1;

      for( var index = 0; index < value.Length; index++ )
      {
         if( value[ index ] != HotkeyTokenDelimiter )
         {
            continue;
         }

         if( tokenStart < 0 )
         {
            tokenStart = index;
            continue;
         }

         results.Add( (tokenStart, index) );
         tokenStart = -1;
      }

      if( tokenStart >= 0 )
      {
         return new List<(int Start, int End)>();
      }

      return results;
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

   private sealed class JsonPatchOperation
   {
      public JsonPatchOperation( string locationPath, string jsonPath, string locationKind, string? contextAfter, string translatedText )
      {
         LocationPath = locationPath;
         JsonPath = jsonPath;
         LocationKind = locationKind;
         ContextAfter = contextAfter;
         TranslatedText = translatedText;
      }

      public string LocationPath { get; }

      public string JsonPath { get; }

      public string LocationKind { get; }

      public string? ContextAfter { get; }

      public string TranslatedText { get; }

      public bool Applied { get; set; }
   }
}
