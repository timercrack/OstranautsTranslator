namespace OstranautsTranslator.Core.Processing;

public sealed class RuntimeTextProcessingConfiguration
{
   public RuntimeTextProcessingConfiguration(
      string fromLanguage,
      bool whitespaceBetweenWords,
      bool templateAllNumbersAway,
      bool handleRichText )
   {
      FromLanguage = fromLanguage;
      WhitespaceBetweenWords = whitespaceBetweenWords;
      TemplateAllNumbersAway = templateAllNumbersAway;
      HandleRichText = handleRichText;
   }

   public static RuntimeTextProcessingConfiguration Default { get; } = new RuntimeTextProcessingConfiguration(
      "en",
      whitespaceBetweenWords: true,
      templateAllNumbersAway: true,
      handleRichText: true );

   public string FromLanguage { get; }

   public bool WhitespaceBetweenWords { get; }

   public bool TemplateAllNumbersAway { get; }

   public bool HandleRichText { get; }
}
