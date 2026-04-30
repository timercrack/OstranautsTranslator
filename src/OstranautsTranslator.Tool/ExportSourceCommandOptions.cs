namespace OstranautsTranslator.Tool;

internal sealed class ExportSourceCommandOptions
{
   public required string WorkspacePath { get; init; }

   public required string ToLanguage { get; init; }

   public string FromLanguage { get; init; } = "en";

   public string? OutputPath { get; init; }

   public bool IncludeTranslated { get; init; }

   public static ExportSourceCommandOptions Parse( IReadOnlyList<string> args )
   {
      var toLanguage = CliTargetLanguageOption.ParseOptionalValue( args );

      var resolvedWorkspacePath = OstranautsTranslator.Core.RuntimeTranslationDeployment.GetDefaultWorkspacePath(
         OstranautsTranslator.Core.RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory ) );

      return new ExportSourceCommandOptions
      {
         WorkspacePath = resolvedWorkspacePath,
         ToLanguage = OstranautsTranslator.Core.RuntimeTranslationDeployment.ResolveTargetLanguage( toLanguage ),
         FromLanguage = OstranautsTranslator.Core.RuntimeTranslationDeployment.SourceLanguage,
         OutputPath = null,
         IncludeTranslated = true,
      };
   }
}
