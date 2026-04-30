namespace OstranautsTranslator.Tool;

internal sealed class ImportTranslationsCommandOptions
{
   public required string WorkspacePath { get; init; }

   public required string ToLanguage { get; init; }

   public string FromLanguage { get; init; } = "en";

   public string? InputPath { get; init; }

   public string? Translator { get; init; }

   public string TranslationState { get; init; } = "final";

   public bool OverwriteExisting { get; init; } = true;

   public static ImportTranslationsCommandOptions Parse( IReadOnlyList<string> args )
   {
      var toLanguage = CliTargetLanguageOption.ParseOptionalValue( args );

      var resolvedWorkspacePath = OstranautsTranslator.Core.RuntimeTranslationDeployment.GetDefaultWorkspacePath(
         OstranautsTranslator.Core.RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory ) );

      return new ImportTranslationsCommandOptions
      {
         WorkspacePath = resolvedWorkspacePath,
         ToLanguage = OstranautsTranslator.Core.RuntimeTranslationDeployment.ResolveTargetLanguage( toLanguage ),
         FromLanguage = OstranautsTranslator.Core.RuntimeTranslationDeployment.SourceLanguage,
         InputPath = null,
         Translator = null,
         TranslationState = "final",
         OverwriteExisting = true,
      };
   }
}
