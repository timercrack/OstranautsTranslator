namespace OstranautsTranslator.Tool.Processing;

internal sealed record CorpusTextProcessingConfiguration(
   string FromLanguage,
   bool WhitespaceBetweenWords,
   bool TemplateAllNumbersAway,
   bool HandleRichText )
{
   public static CorpusTextProcessingConfiguration Default { get; } = new CorpusTextProcessingConfiguration( "en", true, true, true );
}
