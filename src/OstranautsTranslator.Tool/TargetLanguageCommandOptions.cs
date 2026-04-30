using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal sealed class TargetLanguageCommandOptions
{
   public required string GameRootPath { get; init; }

   public required string WorkspacePath { get; init; }

   public string? ToLanguage { get; init; }

   public static TargetLanguageCommandOptions Parse( IReadOnlyList<string> args )
   {
      var toLanguage = CliTargetLanguageOption.ParseOptionalValue( args );

      var resolvedGameRootPath = RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory );
      var resolvedWorkspacePath = RuntimeTranslationDeployment.GetDefaultWorkspacePath( resolvedGameRootPath );

      return new TargetLanguageCommandOptions
      {
         GameRootPath = resolvedGameRootPath,
         WorkspacePath = resolvedWorkspacePath,
         ToLanguage = string.IsNullOrWhiteSpace( toLanguage ) ? null : toLanguage,
      };
   }
}