using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal sealed class DeployTranslationCommandOptions
{
   public required string GameRootPath { get; init; }

   public required string WorkspacePath { get; init; }

   public string ToLanguage { get; init; } = RuntimeTranslationDeployment.ResolveTargetLanguage();

   public string ModId => RuntimeTranslationDeployment.ModId;

   public string ModName => RuntimeTranslationDeployment.ModName;

   public string Author => RuntimeTranslationDeployment.DefaultAuthor;

   public bool IncludeDraft { get; init; }

   public bool VerifySourceHash { get; init; } = true;

   public static DeployTranslationCommandOptions Parse( IReadOnlyList<string> args )
   {
      var toLanguage = CliTargetLanguageOption.ParseOptionalValue( args );

      var resolvedGameRootPath = RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory );
      var resolvedWorkspacePath = RuntimeTranslationDeployment.GetDefaultWorkspacePath( resolvedGameRootPath );

      return new DeployTranslationCommandOptions
      {
         GameRootPath = resolvedGameRootPath,
         WorkspacePath = resolvedWorkspacePath,
         ToLanguage = RuntimeTranslationDeployment.ResolveTargetLanguage( toLanguage ),
         IncludeDraft = true,
         VerifySourceHash = true,
      };
   }
}
