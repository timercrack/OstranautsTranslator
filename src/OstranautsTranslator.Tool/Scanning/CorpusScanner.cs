using OstranautsTranslator.Tool.Database;
using OstranautsTranslator.Tool.Processing;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Scanning;

internal sealed class CorpusScanner
{
   private readonly AssemblyCSharpScanner _assemblyCSharpScanner = new AssemblyCSharpScanner();
   private readonly OstranautsDataScanner _ostranautsDataScanner = new OstranautsDataScanner();
   private readonly ScanCommandOptions _options;
   private readonly CorpusWorkspace _workspace;

   public CorpusScanner( CorpusWorkspace workspace, ScanCommandOptions options )
   {
      _workspace = workspace;
      _options = options;
   }

   public CorpusScanSummary Scan()
   {
      var textProcessingConfiguration = _options.ToTextProcessingConfiguration();

      BracketTokenPolicyAnalyzer.ConfigureForGameRoot( _options.GameRootPath );

      var rawResults = _ostranautsDataScanner.Scan( _options.GameRootPath )
         .Concat( _assemblyCSharpScanner.Scan( _options.GameRootPath ) )
         .ToList();

      var processedResults = rawResults.Select( x => ProjectSource( x, textProcessingConfiguration ) ).ToList();

      var database = new CorpusDatabase( _workspace.CorpusDatabasePath );
      database.Initialize();
      database.SetTextProcessingConfiguration( textProcessingConfiguration );
      database.ApplyScanResults( processedResults );

      return new CorpusScanSummary(
         processedResults.Count,
         processedResults.Sum( x => x.Occurrences.Count ),
         database.GetEntryCount() );
   }

   private static ProcessedSourceScanResult ProjectSource( SourceScanResult source, CorpusTextProcessingConfiguration textProcessingConfiguration )
   {
      var processedOccurrences = source.Occurrences
         .Select( occurrence => new ProcessedScanOccurrence(
            EntryProjector.CreateEntrySnapshot( occurrence.RawText, textProcessingConfiguration ),
            occurrence.LocationKind,
            occurrence.LocationPath,
            occurrence.ContextBefore,
            occurrence.ContextAfter,
            occurrence.IsTranslatable,
            occurrence.MetadataJson ) )
         .ToList();

      return new ProcessedSourceScanResult(
         source.SourcePath,
         source.SourceType,
         source.SizeBytes,
         source.LastWriteUtc,
         source.ContentHash,
         processedOccurrences );
   }
}
