namespace OstranautsTranslator.Tool.Exporting;

internal sealed record SourceEntryExportRecord(
   string SourceKind,
   long SourceId,
   string SourceKey,
   string RawText,
   string RuntimeKey,
   string RenderKey,
   string TextKind,
   int OccurrenceCount,
   string? SampleSourcePath,
   string? SampleLocationKind,
   string? SampleLocationPath,
   string? SampleContextBefore,
   string? SampleContextAfter );

internal sealed record TranslationExportRecord(
   string SourceKind,
   long SourceId,
   string SourceKey,
   string RawText,
   string RuntimeKey,
   string RenderKey,
   string TextKind,
   int OccurrenceCount,
   string? SampleSourcePath,
   string? SampleLocationKind,
   string? SampleLocationPath,
   string? SampleContextBefore,
   string? SampleContextAfter,
   string? TokenPolicy,
   IReadOnlyList<string> TokenExamples,
   bool NeedsManualReview,
   IReadOnlyDictionary<string, string> TokenCorrections,
   string? TranslatedText,
   string TranslationState,
   string? SourceOrigin );

internal sealed record SourceExportSummary(
   int SynchronizedEntries,
   int ExportedEntries,
   string TranslationDatabasePath,
   string ExportPath );

internal sealed record RuntimeExportRecord(
   long SourceId,
   string SourceKey,
   string RawText,
   string? RuntimeKey,
   string TranslatedText,
   string TranslationState,
   int OccurrenceCount );

internal sealed record RuntimeExportSummary(
   int ExportedEntries,
   string ExportPath );

internal sealed record NativeModTranslationRecord(
   long SourceId,
   string SourceKey,
   string RawText,
   string TranslatedText,
   string TranslationState,
   int OccurrenceCount,
   string PatchTargetsJson );

internal sealed record NativeModSourceExportRecord(
   long SourceId,
   string SourceKey,
   string RawText,
   string? TranslatedText,
   string TranslationState,
   int OccurrenceCount,
   string PatchTargetsJson );

internal sealed record NativeModExportSummary(
   int TranslatedEntries,
   int PatchedOccurrences,
   int FilesWritten,
   int WarningCount,
   string OutputRootPath,
   string LoadingOrderPath,
   string ModDirectoryPath );

internal sealed record TranslationDeploySummary(
   int TranslatedEntries,
   int PatchedOccurrences,
   int FilesWritten,
   int WarningCount,
   string ModsRootPath,
   string LoadingOrderPath,
   string ModDirectoryPath,
   string TranslationDatabasePath );
