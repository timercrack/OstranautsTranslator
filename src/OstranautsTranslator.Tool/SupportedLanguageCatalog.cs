using System.Globalization;

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
      Create( "zh", "Simplified Chinese", "zh-CN" ),
      Create( "zh-TW", "Traditional Chinese", "zh-TW" ),
      Create( "jp", "Japanese", "ja-JP" ),
      Create( "ko", "Korean", "ko-KR" ),
      Create( "ru", "Russian", "ru-RU" ),
      Create( "uk", "Ukrainian", "uk-UA" ),
      Create( "fr", "French", "fr-FR" ),
      Create( "de", "German", "de-DE" ),
      Create( "it", "Italian", "it-IT" ),
      Create( "es", "Spanish (Spain)", "es-ES" ),
      Create( "es-419", "Spanish (Latin America)", "es-419" ),
      Create( "pt", "Portuguese", "pt-PT" ),
      Create( "pt-BR", "Portuguese (Brazil)", "pt-BR" ),
      Create( "pl", "Polish", "pl-PL" ),
      Create( "tr", "Turkish", "tr-TR" ),
      Create( "id", "Indonesian", "id-ID" ),
      Create( "vi", "Vietnamese", "vi-VN" ),
      Create( "th", "Thai", "th-TH" ),
      Create( "ar", "Arabic", "ar-SA" ),
   ];

   private static SupportedLanguageInfo Create( string argumentName, string englishName, string cultureName )
   {
      return new SupportedLanguageInfo( argumentName, englishName, ResolveNativeName( cultureName, englishName ) );
   }

   private static string ResolveNativeName( string cultureName, string fallback )
   {
      try
      {
         return CultureInfo.GetCultureInfo( cultureName ).NativeName;
      }
      catch( CultureNotFoundException )
      {
         return fallback;
      }
   }
}