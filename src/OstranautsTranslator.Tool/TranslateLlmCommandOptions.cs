using System.Globalization;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal sealed class TranslateLlmCommandOptions
{
   public required string GameRootPath { get; init; }

   public required string WorkspacePath { get; init; }

   public string? ToLanguage { get; init; }

   public static TranslateLlmCommandOptions Parse( IReadOnlyList<string> args )
   {
      var toLanguage = CliTargetLanguageOption.ParseOptionalValue( args );

      var resolvedGameRootPath = RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory );
      var resolvedWorkspacePath = RuntimeTranslationDeployment.GetDefaultWorkspacePath( resolvedGameRootPath );

      return new TranslateLlmCommandOptions
      {
         GameRootPath = resolvedGameRootPath,
         WorkspacePath = resolvedWorkspacePath,
         ToLanguage = string.IsNullOrWhiteSpace( toLanguage ) ? null : toLanguage,
      };
   }
}
