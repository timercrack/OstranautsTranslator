namespace OstranautsTranslator.Tool.Scanning;

internal sealed record ScanOccurrence(
   string RawText,
   string LocationKind,
   string LocationPath,
   string? ContextBefore,
   string? ContextAfter,
   bool IsTranslatable,
   string? MetadataJson );

internal sealed record SourceScanResult(
   string SourcePath,
   string SourceType,
   long SizeBytes,
   DateTimeOffset LastWriteUtc,
   string ContentHash,
   IReadOnlyList<ScanOccurrence> Occurrences );

internal sealed record EntrySnapshot(
   string EntryKey,
   string RawText,
   string RuntimeKey,
   string NormalizedText,
   string TemplatedText,
   string? RichTextTemplate,
   string RenderKey,
   string TextKind );

internal sealed record ProcessedScanOccurrence(
   EntrySnapshot Entry,
   string LocationKind,
   string LocationPath,
   string? ContextBefore,
   string? ContextAfter,
   bool IsTranslatable,
   string? MetadataJson );

internal sealed record ProcessedSourceScanResult(
   string SourcePath,
   string SourceType,
   long SizeBytes,
   DateTimeOffset LastWriteUtc,
   string ContentHash,
   IReadOnlyList<ProcessedScanOccurrence> Occurrences );

internal sealed record CorpusScanSummary(
   int SourcesScanned,
   int OccurrencesCaptured,
   long TotalEntries );
