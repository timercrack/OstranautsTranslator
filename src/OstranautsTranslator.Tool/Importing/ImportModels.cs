namespace OstranautsTranslator.Tool.Importing;

internal sealed record ImportedTranslationRecord(
   long? SourceCaptureId,
   string? SourceKind,
   long? SourceId,
   string? SourceKey,
   string TranslatedText,
   string TranslationState,
   string? Translator );

internal sealed record TranslationImportSummary(
   int ProcessedCount,
   int AppliedCount,
   int UnknownCount,
   int SkippedCount,
   string TranslationDatabasePath,
   IReadOnlyList<long> ResolvedSourceIds,
   IReadOnlyList<string> ResolvedSourceKeys );

internal sealed record RuntimeMissImportSummary(
   int ProcessedCount,
   int AppliedCount,
   int StoredCaptureCount,
   int SkippedCount,
   string TranslationDatabasePath );
