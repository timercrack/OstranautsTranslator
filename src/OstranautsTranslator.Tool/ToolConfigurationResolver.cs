using System.Globalization;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal static class ToolConfigurationResolver
{
   private const string ConfigFileName = "config.ini";

   public static ResolvedLlmToolConfiguration ResolveLlmConfiguration( string sourceLanguage, string destinationLanguage )
   {
      var configPath = ResolveConfigPath();
      var iniValues = LoadToolConfig( configPath );

      var endpointUrl = FirstNonEmpty( GetDictionaryValue( iniValues, "LLMTranslate", "Url" ) )
         ?? LlmTranslateDefaults.DefaultEndpointUrl;

      var apiKey = FirstNonEmpty( GetDictionaryValue( iniValues, "LLMTranslate", "ApiKey" ) )
         ?? string.Empty;

      var model = FirstNonEmpty( GetDictionaryValue( iniValues, "LLMTranslate", "Model" ) )
         ?? LlmTranslateDefaults.DefaultModel;

      var glossaryPromptTemplate = FirstNonEmpty( GetDictionaryValue( iniValues, "TranslateGlossary", "SystemPrompt" ) )
         ?? throw new InvalidOperationException(
            $"config.ini is missing [TranslateGlossary] SystemPrompt. Keep the glossary system prompt in config.ini next to {RuntimeTranslationDeployment.ToolExecutableName}.exe and run again." );
      var translationPromptTemplate = FirstNonEmpty( GetDictionaryValue( iniValues, "TranslateLlm", "SystemPrompt" ) )
         ?? throw new InvalidOperationException(
            $"config.ini is missing [TranslateLlm] SystemPrompt. Keep the body-text system prompt in config.ini next to {RuntimeTranslationDeployment.ToolExecutableName}.exe and run again." );

      var temperature = ParseFloat(
         GetDictionaryValue( iniValues, "LLMTranslate", "Temperature" ),
         LlmTranslateDefaults.DefaultTemperature,
         "[LLMTranslate] Temperature" );

      var maxTokens = ParseInt(
         GetDictionaryValue( iniValues, "LLMTranslate", "MaxTokens" ),
         LlmTranslateDefaults.DefaultMaxTokens,
         "[LLMTranslate] MaxTokens" );

      var batchSize = ParsePositiveInt(
         GetDictionaryValue( iniValues, "LLMTranslate", "BatchSize" ),
         LlmTranslateDefaults.DefaultBatchSize,
         "[LLMTranslate] BatchSize" );

      var glossaryOverwriteExisting = ParseBoolean(
         GetDictionaryValue( iniValues, "TranslateGlossary", "OverwriteExisting" ),
         LlmTranslateDefaults.DefaultGlossaryOverwriteExisting,
         "[TranslateGlossary] OverwriteExisting" );

      var translationState = FirstNonEmpty( GetDictionaryValue( iniValues, "TranslateLlm", "TranslationState" ) )
         ?? LlmTranslateDefaults.DefaultTranslationState;
      var translator = FirstNonEmpty( GetDictionaryValue( iniValues, "TranslateLlm", "Translator" ) )
         ?? LlmTranslateDefaults.DefaultTranslator;
      var translateGenericGlossaryFirst = ParseBoolean(
         GetDictionaryValue( iniValues, "TranslateLlm", "TranslateGenericGlossaryFirst" ),
         LlmTranslateDefaults.DefaultTranslateGenericGlossaryFirst,
         "[TranslateLlm] TranslateGenericGlossaryFirst" );
      var refreshGlossary = ParseBoolean(
         GetDictionaryValue( iniValues, "TranslateLlm", "RefreshGlossary" ),
         LlmTranslateDefaults.DefaultRefreshGlossary,
         "[TranslateLlm] RefreshGlossary" );
      var overwriteExisting = ParseBoolean(
         GetDictionaryValue( iniValues, "TranslateLlm", "OverwriteExisting" ),
         LlmTranslateDefaults.DefaultOverwriteExisting,
         "[TranslateLlm] OverwriteExisting" );
      var includeDraft = ParseBoolean(
         GetDictionaryValue( iniValues, "TranslateLlm", "IncludeDraft" ),
         LlmTranslateDefaults.DefaultIncludeDraft,
         "[TranslateLlm] IncludeDraft" );

      if( string.IsNullOrWhiteSpace( apiKey ) )
      {
         throw new InvalidOperationException(
            $"config.ini is missing [LLMTranslate] ApiKey. Copy config-example.ini to config.ini next to {RuntimeTranslationDeployment.ToolExecutableName}.exe, set ApiKey, and run again." );
      }

      return new ResolvedLlmToolConfiguration(
         configPath,
         CreateClientSettings(
            endpointUrl,
            apiKey,
            model,
            glossaryPromptTemplate,
            DescribePromptSource( "[TranslateGlossary] SystemPrompt" ),
            temperature,
            maxTokens,
            batchSize,
            sourceLanguage,
            destinationLanguage ),
         CreateClientSettings(
            endpointUrl,
            apiKey,
            model,
            translationPromptTemplate,
            DescribePromptSource( "[TranslateLlm] SystemPrompt" ),
            temperature,
            maxTokens,
            batchSize,
            sourceLanguage,
            destinationLanguage ),
         new TranslateGlossaryExecutionSettings(
            glossaryOverwriteExisting ),
         new TranslateLlmExecutionSettings(
            translationState,
            translator,
            translateGenericGlossaryFirst,
            refreshGlossary,
            overwriteExisting,
            includeDraft ) );
   }

   private static Dictionary<string, Dictionary<string, string>> LoadToolConfig( string configPath )
   {
      if( !File.Exists( configPath ) )
      {
         throw new FileNotFoundException(
            $"No {ConfigFileName} was found next to {RuntimeTranslationDeployment.ToolExecutableName}.exe. Copy config-example.ini to config.ini, set [LLMTranslate] ApiKey, and run again.",
            configPath );
      }

      var sections = new Dictionary<string, Dictionary<string, string>>( StringComparer.OrdinalIgnoreCase );

      Dictionary<string, string>? currentSection = null;
      foreach( var rawLine in File.ReadLines( configPath ) )
      {
         var line = rawLine.Trim();
         if( line.Length == 0 || line.StartsWith( ";", StringComparison.Ordinal ) || line.StartsWith( "#", StringComparison.Ordinal ) )
         {
            continue;
         }

         if( line.StartsWith( "[", StringComparison.Ordinal ) && line.EndsWith( "]", StringComparison.Ordinal ) )
         {
            var sectionName = line.Substring( 1, line.Length - 2 ).Trim();
            if( sectionName.Length == 0 )
            {
               currentSection = null;
               continue;
            }

            if( !sections.TryGetValue( sectionName, out currentSection ) )
            {
               currentSection = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
               sections.Add( sectionName, currentSection );
            }

            continue;
         }

         if( currentSection == null ) continue;

         var separatorIndex = line.IndexOf( '=' );
         if( separatorIndex <= 0 ) continue;

         var key = line.Substring( 0, separatorIndex ).Trim();
         var value = line.Substring( separatorIndex + 1 ).Trim();
         if( key.Length == 0 ) continue;

         currentSection[ key ] = value;
      }

      return sections;
   }

   private static string ResolveConfigPath()
   {
      return Path.Combine( AppContext.BaseDirectory, ConfigFileName );
   }

   private static string? FirstNonEmpty( params string?[] values )
   {
      foreach( var value in values )
      {
         if( !string.IsNullOrWhiteSpace( value ) ) return value.Trim();
      }

      return null;
   }

   private static string? GetDictionaryValue( IReadOnlyDictionary<string, string> values, string key )
   {
      return values.TryGetValue( key, out var value ) && !string.IsNullOrWhiteSpace( value )
         ? value.Trim()
         : null;
   }

   private static string? GetDictionaryValue( IReadOnlyDictionary<string, Dictionary<string, string>> sections, string sectionName, string key )
   {
      if( !sections.TryGetValue( sectionName, out var section ) ) return null;
      return section.TryGetValue( key, out var value ) && !string.IsNullOrWhiteSpace( value )
         ? value.Trim()
         : null;
   }

   private static int ParsePositiveInt( string? rawValue, int defaultValue, string settingName )
   {
      if( string.IsNullOrWhiteSpace( rawValue ) )
      {
         return defaultValue;
      }

      if( int.TryParse( rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) && parsed > 0 )
      {
         return parsed;
      }

      throw new InvalidOperationException( $"{settingName} must be a positive integer, but got '{rawValue}'." );
   }

   private static int ParseInt( string? rawValue, int defaultValue, string settingName )
   {
      if( string.IsNullOrWhiteSpace( rawValue ) )
      {
         return defaultValue;
      }

      if( int.TryParse( rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) && parsed > 0 )
      {
         return parsed;
      }

      throw new InvalidOperationException( $"{settingName} must be a positive integer, but got '{rawValue}'." );
   }

   private static float ParseFloat( string? rawValue, float defaultValue, string settingName )
   {
      if( string.IsNullOrWhiteSpace( rawValue ) )
      {
         return defaultValue;
      }

      if( float.TryParse( rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed ) )
      {
         return parsed;
      }

      throw new InvalidOperationException( $"{settingName} must be a number, but got '{rawValue}'." );
   }

   private static bool ParseBoolean( string? rawValue, bool defaultValue, string settingName )
   {
      if( string.IsNullOrWhiteSpace( rawValue ) )
      {
         return defaultValue;
      }

      if( bool.TryParse( rawValue, out var parsed ) )
      {
         return parsed;
      }

      throw new InvalidOperationException( $"{settingName} must be 'true' or 'false', but got '{rawValue}'." );
   }

   private static LlmTranslateSettings CreateClientSettings(
      string endpointUrl,
      string apiKey,
      string model,
      string promptTemplate,
      string promptSource,
      float temperature,
      int maxTokens,
      int batchSize,
      string sourceLanguage,
      string destinationLanguage )
   {
      return new LlmTranslateSettings(
         endpointUrl,
         apiKey,
         model,
         LlmTranslateDefaults.ResolveSystemPrompt( promptTemplate, sourceLanguage, destinationLanguage ),
         temperature,
         maxTokens,
         batchSize,
         promptSource );
   }

   private static string DescribePromptSource( string sectionSettingName )
   {
      return $"{ConfigFileName} {sectionSettingName}";
   }
}
