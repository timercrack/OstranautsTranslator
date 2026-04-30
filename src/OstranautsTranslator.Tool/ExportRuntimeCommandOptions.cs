namespace OstranautsTranslator.Tool;

internal sealed class ExportRuntimeCommandOptions
{
   public required string WorkspacePath { get; init; }

   public required string ToLanguage { get; init; }

   public string? OutputPath { get; init; }

   public bool IncludeDraft { get; init; }

   public static ExportRuntimeCommandOptions Parse( IReadOnlyList<string> args )
   {
      var toLanguage = CliTargetLanguageOption.ParseOptionalValue( args );

      var resolvedWorkspacePath = OstranautsTranslator.Core.RuntimeTranslationDeployment.GetDefaultWorkspacePath(
         OstranautsTranslator.Core.RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory ) );

      return new ExportRuntimeCommandOptions
      {
         WorkspacePath = resolvedWorkspacePath,
         ToLanguage = OstranautsTranslator.Core.RuntimeTranslationDeployment.ResolveTargetLanguage( toLanguage ),
         OutputPath = null,
         IncludeDraft = true,
      };
   }
}
