using System.Text.Json;
using OstranautsTranslator.Tool.Database;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Importing;

internal sealed class TranslationsImporter
{
   private readonly CorpusWorkspace _workspace;
   private readonly ImportTranslationsCommandOptions _options;

   public TranslationsImporter( CorpusWorkspace workspace, ImportTranslationsCommandOptions options )
   {
      _workspace = workspace;
      _options = options;
   }

   public TranslationImportSummary Import()
   {
      var inputPath = ResolveInputPath();
      var inputFiles = ResolveInputFiles( inputPath );
      if( inputFiles.Count == 0 )
      {
         throw new FileNotFoundException( "No translation import files were found.", inputPath );
      }

      var records = new List<ImportedTranslationRecord>();
      var parseSkippedCount = 0;
      foreach( var filePath in inputFiles )
      {
         ReadRecordsFromFile( filePath, records, ref parseSkippedCount );
      }

      var translationDatabasePath = _workspace.GetTranslationDatabasePath( _options.ToLanguage );
      var translationDatabase = new TranslationDatabase( translationDatabasePath, _options.FromLanguage, _options.ToLanguage );
      translationDatabase.Initialize();

      var summary = translationDatabase.ImportTranslations( records, _options.OverwriteExisting );
      return new TranslationImportSummary(
         summary.ProcessedCount + parseSkippedCount,
         summary.AppliedCount,
         summary.UnknownCount,
         summary.SkippedCount + parseSkippedCount,
         summary.TranslationDatabasePath,
         summary.ResolvedSourceIds,
         summary.ResolvedSourceKeys );
   }

   private string ResolveInputPath()
   {
      if( !string.IsNullOrWhiteSpace( _options.InputPath ) )
      {
         return _options.InputPath;
      }

      var preferredImportPath = _workspace.GetTranslationImportPath( _options.FromLanguage, _options.ToLanguage );
      if( File.Exists( preferredImportPath ) || Directory.Exists( preferredImportPath ) )
      {
         return preferredImportPath;
      }

      return _workspace.GetSourceExportPath( _options.FromLanguage, _options.ToLanguage );
   }

   private static IReadOnlyList<string> ResolveInputFiles( string inputPath )
   {
      if( File.Exists( inputPath ) )
      {
         return new[] { inputPath };
      }

      if( Directory.Exists( inputPath ) )
      {
         return Directory.EnumerateFiles( inputPath, "*.jsonl", SearchOption.TopDirectoryOnly )
            .OrderBy( x => x, StringComparer.OrdinalIgnoreCase )
            .ToList();
      }

      throw new FileNotFoundException( "Translation import input path does not exist.", inputPath );
   }

   private void ReadRecordsFromFile( string filePath, ICollection<ImportedTranslationRecord> records, ref int parseSkippedCount )
   {
      foreach( var line in File.ReadLines( filePath ) )
      {
         if( string.IsNullOrWhiteSpace( line ) ) continue;

         using var document = JsonDocument.Parse( line );
         var root = document.RootElement;

         var sourceKind = TryGetString( root, "source_kind" );
         var sourceKey = TryGetString( root, "source_key" );
         var sourceId = TryGetInt64( root, "source_id" );
         var translatedText = TryGetString( root, "translated_text" );
         if( ( !sourceId.HasValue && string.IsNullOrWhiteSpace( sourceKey ) ) || string.IsNullOrWhiteSpace( translatedText ) )
         {
            parseSkippedCount++;
            continue;
         }

         var translationState = TryGetString( root, "translation_state" ) ?? _options.TranslationState;
         var translator = TryGetString( root, "translator" ) ?? _options.Translator;

         records.Add( new ImportedTranslationRecord( null, sourceKind, sourceId, sourceKey, translatedText, translationState, translator ) );
      }
   }

   private static string? TryGetString( JsonElement element, string propertyName )
   {
      if( element.TryGetProperty( propertyName, out var property ) )
      {
         return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
      }

      return null;
   }

   private static long? TryGetInt64( JsonElement element, string propertyName )
   {
      if( !element.TryGetProperty( propertyName, out var property ) )
      {
         return null;
      }

      return property.ValueKind switch
      {
         JsonValueKind.Number when property.TryGetInt64( out var value ) => value,
         JsonValueKind.String when long.TryParse( property.GetString(), out var value ) => value,
         _ => null,
      };
   }
}
