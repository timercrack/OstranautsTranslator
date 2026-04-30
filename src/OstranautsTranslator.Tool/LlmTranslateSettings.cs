using System.Globalization;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal sealed record LlmTranslateSettings(
   string EndpointUrl,
   string ApiKey,
   string Model,
   string SystemPrompt,
   float Temperature,
   int MaxTokens,
   int BatchSize,
   string ConfigurationSource );

internal sealed record TranslateGlossaryExecutionSettings(
   bool OverwriteExisting );

internal sealed record TranslateLlmExecutionSettings(
   int Limit,
   string TranslationState,
   string Translator,
   bool TranslateGenericGlossaryFirst,
   bool RefreshGlossary,
   bool OverwriteExisting,
   bool IncludeDraft );

internal sealed record ResolvedLlmToolConfiguration(
   string ConfigPath,
   LlmTranslateSettings GlossaryClientSettings,
   LlmTranslateSettings TranslationClientSettings,
   TranslateGlossaryExecutionSettings GlossarySettings,
   TranslateLlmExecutionSettings TranslationSettings );

internal static class LlmTranslateDefaults
{
   public const string DefaultEndpointUrl = "https://api.deepseek.com/chat/completions";
   public const string DefaultModel = "deepseek-v4-flash";
   public const string DefaultGlossarySystemPromptTemplate = """
You are a dedicated translator for Ostranauts standalone in-game glossary terms. The final user message is always a JSON array of source terms, not sentences. Earlier messages may provide per-term categories and style rules.
Return exactly one valid JSON array with the same number of elements and the same order as the input. Output only the JSON array. Do not output markdown, code fences, comments, explanations, or any extra text.
Keep each translated term concise, reusable, and suitable for later reuse in body-text translation.
Ostranauts is grounded, working-class hard sci-fi: orbital stations, salvage crews, ship systems, labor exploitation, corporate bureaucracy, refugees, gangs, and dry black humor. Prefer contemporary, grounded, industrial-sounding {DestinationLanguageName}. Avoid fantasy, archaic, overly poetic, overly heroic, or internet-meme wording.
Preserve placeholders and markup such as <...> exactly. Preserve square-bracket tokens and machine control strings exactly when instructed.
Fully translate player-facing terms into {DestinationLanguageName}. For personal names, always use a stable transliteration into {DestinationLanguageName}, and preserve distinct cultural naming flavor instead of flattening names into one style.
For places, stations, factions, organizations, institutions, brands, doctrines, gangs, items, and ship names, prefer natural semantic translation into {DestinationLanguageName} whenever the meaning is interpretable. Choose wording that sounds plausible in a grubby, industrial, near-future space setting. If a name contains an explicit code, acronym, registration ID, callsign, model number, hotkey, or other obvious technical identifier, keep that code component unchanged.
Do not leave Latin spelling unchanged just because something is a proper noun. Keep recurring terminology stable across the batch.
""";
   public const string DefaultTranslationSystemPromptTemplate = """
You are a dedicated translator for Ostranauts game UI text. The final user message is always a JSON array of strings. Earlier messages may provide per-entry translation rules, glossary matches, and context.
Return exactly one valid JSON array with the same number of elements and the same order as the input. Output only the JSON array. Do not output markdown, code fences, comments, explanations, or any extra text.
Translate each array element into natural {DestinationLanguageName}. Preserve escape sequences such as \n, \r, and \t exactly as they appear. Preserve placeholders and markup such as <...> exactly.
Ostranauts is grounded hard sci-fi about station life, salvage, precarious labor, institutional decay, corporate bureaucracy, survival hazards, and dry gallows humor. The writing shifts between terse technical UI, sardonic tutorial prose, punchy news headlines, corporate notices, and blue-collar spacer slang.
Preserve the source register: technical text must stay terse and precise; warnings, legal copy, and system notices must stay clipped and bureaucratic; tutorial and help text should remain conversational, slightly snarky, and readable; headlines should sound like natural headlines rather than literal word-for-word renderings. Preserve black humor, class tension, and lived-in grime instead of smoothing everything into neutral product copy.
For square-bracket tokens, follow the per-entry instructions: keep runtime slot/control tokens exact when told, and rewrite English grammar helper tokens into natural {DestinationLanguageName} when told.
Fully translate player-facing English into {DestinationLanguageName}. For personal names, always use a stable transliteration into {DestinationLanguageName}, and preserve distinct cultural naming flavor instead of flattening names into one style.
For places, stations, factions, organizations, institutions, brands, doctrines, gangs, items, and ship names, prefer natural semantic translation into {DestinationLanguageName} whenever the meaning is interpretable. If a name contains an explicit code, acronym, registration ID, callsign, model number, hotkey, or other obvious technical identifier, keep that code component unchanged.
Avoid fantasy, archaic, overly literary, overly heroic, or internet-meme phrasing. Keep repeated names and terminology consistent across the batch.
""";
   public const float DefaultTemperature = 0.2f;
   public const int DefaultMaxTokens = 8192;
   public const int DefaultBatchSize = 100;
   public const int DefaultLimit = int.MaxValue;
   public const string DefaultTranslationState = "final";
   public const string DefaultTranslator = "deepseek";
   public const bool DefaultTranslateGenericGlossaryFirst = true;
   public const bool DefaultRefreshGlossary = false;
   public const bool DefaultOverwriteExisting = false;
   public const bool DefaultIncludeDraft = true;
   public const bool DefaultGlossaryOverwriteExisting = false;

   private static readonly Dictionary<string, string> LanguageDisplayNames = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
   {
      { "af", "Afrikaans" },
      { "auto", "the detected source language" },
      { "ar", "Arabic" },
      { "be", "Belarusian" },
      { "bg", "Bulgarian" },
      { "ca", "Catalan" },
      { "cs", "Czech" },
      { "da", "Danish" },
      { "de", "German" },
      { "el", "Greek" },
      { "en", "English" },
      { "es", "Spanish" },
      { "es-419", "Spanish (Latin America)" },
      { "et", "Estonian" },
      { "eu", "Basque" },
      { "fi", "Finnish" },
      { "fo", "Faroese" },
      { "fr", "French" },
      { "he", "Hebrew" },
      { "hu", "Hungarian" },
      { "id", "Indonesian" },
      { "is", "Icelandic" },
      { "it", "Italian" },
      { "ja", "Japanese" },
      { "jp", "Japanese" },
      { "ko", "Korean" },
      { "lt", "Lithuanian" },
      { "lv", "Latvian" },
      { "nl", "Dutch" },
      { "no", "Norwegian" },
      { "pl", "Polish" },
      { "pt", "Portuguese" },
      { "pt-BR", "Portuguese (Brazil)" },
      { "romaji", "Romaji" },
      { "ro", "Romanian" },
      { "ru", "Russian" },
      { "sh", "Serbo-Croatian" },
      { "sk", "Slovak" },
      { "sl", "Slovenian" },
      { "sv", "Swedish" },
      { "th", "Thai" },
      { "tr", "Turkish" },
      { "uk", "Ukrainian" },
      { "vi", "Vietnamese" },
      { "zh", "Simplified Chinese" },
      { "zh-CN", "Simplified Chinese" },
      { "zh-Hans", "Simplified Chinese" },
      { "zh-TW", "Traditional Chinese" },
      { "zh-Hant", "Traditional Chinese" },
   };

   public static string ResolveSystemPrompt( string template, string sourceLanguage, string destinationLanguage )
   {
      var normalizedSourceLanguage = NormalizeLanguageCode( sourceLanguage );
      var normalizedDestinationLanguage = NormalizeLanguageCode( destinationLanguage );
      var sourceLanguageName = GetLanguageDisplayName( normalizedSourceLanguage, "the detected source language" );
      var destinationLanguageName = GetLanguageDisplayName( normalizedDestinationLanguage, "the requested target language" );

      return ( template ?? string.Empty )
         .Replace( "{SourceLanguageCode}", normalizedSourceLanguage ?? string.Empty )
         .Replace( "{FromLanguageCode}", normalizedSourceLanguage ?? string.Empty )
         .Replace( "{DestinationLanguageCode}", normalizedDestinationLanguage ?? string.Empty )
         .Replace( "{ToLanguageCode}", normalizedDestinationLanguage ?? string.Empty )
         .Replace( "{SourceLanguageName}", sourceLanguageName )
         .Replace( "{FromLanguageName}", sourceLanguageName )
         .Replace( "{DestinationLanguageName}", destinationLanguageName )
         .Replace( "{ToLanguageName}", destinationLanguageName );
   }

   private static string GetLanguageDisplayName( string languageCode, string fallback )
   {
      if( string.IsNullOrEmpty( languageCode ) ) return fallback;

      if( LanguageDisplayNames.TryGetValue( languageCode, out var displayName ) )
      {
         return displayName;
      }

      try
      {
         var cultureCode = string.Equals( languageCode, "zh", StringComparison.OrdinalIgnoreCase )
            ? "zh-CN"
            : languageCode;
         var culture = CultureInfo.GetCultureInfo( cultureCode );
         if( culture != null && !string.IsNullOrEmpty( culture.EnglishName ) )
         {
            return culture.EnglishName;
         }
      }
      catch( Exception )
      {
      }

      var separatorIndex = languageCode.IndexOf( '-' );
      if( separatorIndex > 0 )
      {
         return GetLanguageDisplayName( languageCode.Substring( 0, separatorIndex ), fallback );
      }

      return languageCode;
   }

   private static string NormalizeLanguageCode( string languageCode )
   {
      return RuntimeTranslationDeployment.NormalizeLanguageCode( languageCode );
   }
}
