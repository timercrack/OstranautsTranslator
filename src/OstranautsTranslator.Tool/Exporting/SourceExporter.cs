using System.Text;
using System.Text.Json;
using OstranautsTranslator.Tool.Database;
using OstranautsTranslator.Tool.Importing;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Exporting;

internal sealed class SourceExporter
{
   private static readonly JsonSerializerOptions JsonOptions = new()
   {
      WriteIndented = false,
   };

   private readonly CorpusWorkspace _workspace;
   private readonly ExportSourceCommandOptions _options;

   public SourceExporter( CorpusWorkspace workspace, ExportSourceCommandOptions options )
   {
      _workspace = workspace;
      _options = options;
   }

   public SourceExportSummary Export()
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
      translationDatabase.SetTextProcessingConfiguration( textProcessingConfiguration );

      var sourceEntryCount = translationDatabase.GetSourceEntryCount();
      var exportEntries = translationDatabase.GetExportEntries( _options.IncludeTranslated );
      var exportPath = _options.OutputPath ?? _workspace.GetSourceExportPath( _options.FromLanguage, _options.ToLanguage );
      WriteJsonLines( exportPath, exportEntries );

      return new SourceExportSummary( sourceEntryCount, exportEntries.Count, translationDatabasePath, exportPath );
   }

   private void WriteJsonLines( string exportPath, IReadOnlyList<TranslationExportRecord> entries )
   {
      var directoryPath = Path.GetDirectoryName( exportPath );
      if( !string.IsNullOrWhiteSpace( directoryPath ) )
      {
         Directory.CreateDirectory( directoryPath );
      }

      using var stream = new FileStream( exportPath, FileMode.Create, FileAccess.Write, FileShare.None );
      using var writer = new StreamWriter( stream, new UTF8Encoding( false ) );

      foreach( var entry in entries )
      {
         var row = new
         {
            from_language = _options.FromLanguage,
            to_language = _options.ToLanguage,
            source_kind = entry.SourceKind,
            source_origin = entry.SourceOrigin,
            source_id = entry.SourceId,
            source_key = entry.SourceKey,
            raw_text = entry.RawText,
            runtime_key = entry.RuntimeKey,
            render_key = entry.RenderKey,
            text_kind = entry.TextKind,
            occurrence_count = entry.OccurrenceCount,
            sample_source_path = entry.SampleSourcePath,
            sample_location_kind = entry.SampleLocationKind,
            sample_location_path = entry.SampleLocationPath,
            sample_context_before = entry.SampleContextBefore,
            sample_context_after = entry.SampleContextAfter,
            token_policy = entry.TokenPolicy,
            token_examples = entry.TokenExamples,
            needs_manual_review = entry.NeedsManualReview,
            token_corrections = entry.TokenCorrections.Count > 0 ? entry.TokenCorrections : null,
            translated_text = entry.TranslatedText,
            translation_state = entry.TranslationState,
         };

         writer.WriteLine( JsonSerializer.Serialize( row, JsonOptions ) );
      }
   }
}
