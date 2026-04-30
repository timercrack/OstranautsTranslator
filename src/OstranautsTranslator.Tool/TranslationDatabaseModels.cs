namespace OstranautsTranslator.Tool;

internal sealed record TranslationEntry(
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

internal sealed record TranslationUpdate(
   string SourceKind,
   long SourceId,
   string TranslatedText,
   string TranslationState,
   string Translator );

internal sealed record TranslationBatchResult(
   int SelectedCount,
   int SubmittedCount,
   int AppliedCount,
   int SkippedCount,
   string TranslationDatabasePath );
