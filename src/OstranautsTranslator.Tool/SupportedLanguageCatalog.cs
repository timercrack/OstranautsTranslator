namespace OstranautsTranslator.Tool;

internal sealed record SupportedLanguageInfo(
   string ArgumentName,
   string EnglishName,
   string NativeName );

internal static class SupportedLanguageCatalog
{
   // Keep in sync with SUPPORTED_TARGET_LANGUAGES in tmp/generate_symbol_charsets.py.
   public static IReadOnlyList<SupportedLanguageInfo> All { get; } =
   [
      new( "zh", "Simplified Chinese", "简体中文" ),
      new( "zh-TW", "Traditional Chinese", "繁體中文" ),
      new( "jp", "Japanese", "日本語" ),
      new( "ko", "Korean", "한국어" ),
      new( "ru", "Russian", "Русский" ),
      new( "uk", "Ukrainian", "Українська" ),
      new( "fr", "French", "Français" ),
      new( "de", "German", "Deutsch" ),
      new( "it", "Italian", "Italiano" ),
      new( "es", "Spanish (Spain)", "Español (España)" ),
      new( "es-419", "Spanish (Latin America)", "Español (Latinoamérica)" ),
      new( "pt", "Portuguese", "Português" ),
      new( "pt-BR", "Portuguese (Brazil)", "Português (Brasil)" ),
      new( "pl", "Polish", "Polski" ),
      new( "tr", "Turkish", "Türkçe" ),
      new( "id", "Indonesian", "Bahasa Indonesia" ),
      new( "vi", "Vietnamese", "Tiếng Việt" ),
      new( "th", "Thai", "ไทย" ),
      new( "ar", "Arabic", "العربية" ),
   ];
}