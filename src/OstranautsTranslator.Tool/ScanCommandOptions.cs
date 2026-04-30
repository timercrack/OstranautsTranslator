using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Processing;

namespace OstranautsTranslator.Tool;

internal sealed class ScanCommandOptions
{
   public required string GameRootPath { get; init; }

   public required string WorkspacePath { get; init; }

   public string FromLanguage { get; init; } = "en";

   public bool WhitespaceBetweenWords { get; init; } = true;

   public bool TemplateAllNumbersAway { get; init; } = true;

   public bool HandleRichText { get; init; } = true;

   public CorpusTextProcessingConfiguration ToTextProcessingConfiguration()
   {
      return new CorpusTextProcessingConfiguration(
         FromLanguage,
         WhitespaceBetweenWords,
         TemplateAllNumbersAway,
         HandleRichText );
   }

   public static ScanCommandOptions Parse( IReadOnlyList<string> args )
   {
      if( args.Count > 0 )
      {
         throw new ArgumentException( $"Unknown option '{args[ 0 ]}'." );
      }

      var resolvedGameRootPath = RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory );
      var resolvedWorkspacePath = RuntimeTranslationDeployment.GetDefaultWorkspacePath( resolvedGameRootPath );

      return new ScanCommandOptions
      {
         GameRootPath = resolvedGameRootPath,
         WorkspacePath = resolvedWorkspacePath,
         FromLanguage = RuntimeTranslationDeployment.SourceLanguage,
         WhitespaceBetweenWords = true,
         TemplateAllNumbersAway = true,
         HandleRichText = true,
      };
   }
}
