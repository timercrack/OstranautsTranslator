using System.Text;
using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Database;
using OstranautsTranslator.Tool.Processing;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Exporting;

internal sealed class RuntimeExporter
{
   private readonly CorpusWorkspace _workspace;
   private readonly ExportRuntimeCommandOptions _options;

   public RuntimeExporter( CorpusWorkspace workspace, ExportRuntimeCommandOptions options )
   {
      _workspace = workspace;
      _options = options;
   }

   public RuntimeExportSummary Export()
   {
      var translationDatabasePath = _workspace.GetTranslationDatabasePath( _options.ToLanguage );
      if( !File.Exists( translationDatabasePath ) )
      {
         throw new FileNotFoundException( "Translation database was not found. Run source first.", translationDatabasePath );
      }

      var textProcessingConfiguration = File.Exists( _workspace.CorpusDatabasePath )
         ? new CorpusDatabase( _workspace.CorpusDatabasePath ).GetTextProcessingConfiguration()
         : Processing.CorpusTextProcessingConfiguration.Default;

      var translationDatabase = new TranslationDatabase( translationDatabasePath, textProcessingConfiguration.FromLanguage, _options.ToLanguage );
      translationDatabase.Initialize();
      var exportEntries = translationDatabase.GetRuntimeExportEntries( _options.IncludeDraft );

      var outputPath = _options.OutputPath ?? _workspace.GetRuntimeExportPath( _options.ToLanguage );
      var directoryPath = Path.GetDirectoryName( outputPath );
      if( !string.IsNullOrWhiteSpace( directoryPath ) )
      {
         Directory.CreateDirectory( directoryPath );
      }

      var exportedEntries = 0;
      var seenRuntimeKeys = new HashSet<string>( StringComparer.Ordinal );
      using var stream = new FileStream( outputPath, FileMode.Create, FileAccess.Write, FileShare.None );
      using var writer = new StreamWriter( stream, new UTF8Encoding( false ) );
      foreach( var entry in exportEntries )
      {
         var runtimeKey = !string.IsNullOrWhiteSpace( entry.RuntimeKey )
            ? entry.RuntimeKey!
            : EntryProjector.CreateEntrySnapshot( entry.RawText, textProcessingConfiguration ).RuntimeKey;

         if( !seenRuntimeKeys.Add( runtimeKey ) )
         {
            continue;
         }

         writer.WriteLine( TranslationTextFile.Encode( runtimeKey ) + '=' + TranslationTextFile.Encode( entry.TranslatedText ) );
         exportedEntries++;
      }

      writer.Flush();
      return new RuntimeExportSummary( exportedEntries, outputPath );
   }
}
