using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Database;
using OstranautsTranslator.Tool.Processing;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Importing;

internal sealed class RuntimeMissImporter
{
   private readonly CorpusWorkspace _workspace;
   private readonly ImportRuntimeMissCommandOptions _options;

   public RuntimeMissImporter( CorpusWorkspace workspace, ImportRuntimeMissCommandOptions options )
   {
      _workspace = workspace;
      _options = options;
   }

   public RuntimeMissImportSummary Import()
   {
      if( !File.Exists( _workspace.CorpusDatabasePath ) )
      {
         throw new FileNotFoundException( "corpus.sqlite was not found. Run scan first.", _workspace.CorpusDatabasePath );
      }

      var corpusDatabase = new CorpusDatabase( _workspace.CorpusDatabasePath );
      corpusDatabase.Initialize();
      var textProcessingConfiguration = corpusDatabase.GetTextProcessingConfiguration();

      var translationDatabasePath = _workspace.GetTranslationDatabasePath( _options.ToLanguage );
      var translationDatabase = new TranslationDatabase( translationDatabasePath, _options.FromLanguage, _options.ToLanguage );
      translationDatabase.Initialize();

      var inputPath = ResolveInputPath();
      var inputFiles = ResolveInputFiles( inputPath );
      if( inputFiles.Count == 0 )
      {
         throw new FileNotFoundException( "No runtime miss import files were found.", inputPath );
      }

      var importedTranslations = new List<ImportedTranslationRecord>();
      var skippedCount = 0;
      var processedCount = 0;
      foreach( var filePath in inputFiles )
      {
         ReadRuntimeFile( filePath, corpusDatabase, textProcessingConfiguration, importedTranslations, ref processedCount, ref skippedCount );
      }

      var importSummary = translationDatabase.ImportTranslations( importedTranslations, _options.OverwriteExisting );

      return new RuntimeMissImportSummary(
         processedCount,
         importSummary.AppliedCount,
         importedTranslations.Count,
         skippedCount + importSummary.SkippedCount,
         importSummary.TranslationDatabasePath );
   }

   private string ResolveInputPath()
   {
      if( !string.IsNullOrWhiteSpace( _options.InputPath ) )
      {
         return _options.InputPath;
      }

      var languageSpecificDirectory = _workspace.GetRuntimeMissImportDirectoryPath( _options.ToLanguage );
      if( Directory.Exists( languageSpecificDirectory ) )
      {
         return languageSpecificDirectory;
      }

      return _workspace.RuntimeMissImportsDirectoryPath;
   }

   private static IReadOnlyList<string> ResolveInputFiles( string inputPath )
   {
      if( File.Exists( inputPath ) )
      {
         return new[] { inputPath };
      }

      if( Directory.Exists( inputPath ) )
      {
         return Directory.EnumerateFiles( inputPath, "*.txt", SearchOption.TopDirectoryOnly )
            .OrderBy( x => x, StringComparer.OrdinalIgnoreCase )
            .ToList();
      }

      throw new FileNotFoundException( "Runtime miss import input path does not exist.", inputPath );
   }

   private void ReadRuntimeFile(
      string filePath,
      CorpusDatabase corpusDatabase,
      Processing.CorpusTextProcessingConfiguration textProcessingConfiguration,
      ICollection<ImportedTranslationRecord> importedTranslations,
      ref int processedCount,
      ref int skippedCount )
   {
      foreach( var line in File.ReadLines( filePath ) )
      {
         if( string.IsNullOrWhiteSpace( line ) ) continue;

         var trimmed = line.TrimStart();
         if( trimmed.StartsWith( "#", StringComparison.Ordinal ) || trimmed.StartsWith( "//", StringComparison.Ordinal ) )
         {
            continue;
         }

         var kvp = TranslationTextFile.ReadTranslationLineAndDecode( line );
         if( kvp == null )
         {
            skippedCount++;
            continue;
         }

         var key = kvp[ 0 ];
         var value = kvp[ 1 ];
         if( string.IsNullOrWhiteSpace( key ) || string.IsNullOrWhiteSpace( value ) || key.StartsWith( "r:", StringComparison.Ordinal ) || key.StartsWith( "sr:", StringComparison.Ordinal ) )
         {
            skippedCount++;
            continue;
         }

         var snapshot = EntryProjector.CreateEntrySnapshot( key, textProcessingConfiguration );
         var sourceId = corpusDatabase.UpsertRuntimeCaptureSource( snapshot );
         importedTranslations.Add( new ImportedTranslationRecord( null, "runtime", sourceId, snapshot.EntryKey, value, _options.TranslationState, _options.Translator ) );
         processedCount++;
      }
   }
}
