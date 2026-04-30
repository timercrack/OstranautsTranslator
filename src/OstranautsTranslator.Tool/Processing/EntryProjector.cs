using OstranautsTranslator.Core.Processing;
using OstranautsTranslator.Tool.Scanning;

namespace OstranautsTranslator.Tool.Processing;

internal static class EntryProjector
{
   public static EntrySnapshot CreateEntrySnapshot( string rawText, CorpusTextProcessingConfiguration configuration )
   {
      var projection = RuntimeTextProjector.CreateProjection(
         rawText,
         new RuntimeTextProcessingConfiguration(
            configuration.FromLanguage,
            configuration.WhitespaceBetweenWords,
            configuration.TemplateAllNumbersAway,
            configuration.HandleRichText ) );

      var entryKey = FileHashHelper.ComputeTextHash( projection.TextKind + "\u001F" + projection.RenderKey );
      return new EntrySnapshot(
         entryKey,
         projection.RawText,
         projection.RuntimeKey,
         projection.NormalizedText,
         projection.TemplatedText,
         projection.RichTextTemplate?.TemplateText,
         projection.RenderKey,
         projection.TextKind );
   }
}
