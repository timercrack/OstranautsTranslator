using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal static class TranslateGlossaryCommand
{
   public static async Task<int> ExecuteAsync(
      TranslateLlmCommandOptions options,
      CancellationToken cancellationToken,
      TranslateGlossaryExecutionSettings? glossarySettingsOverride = null )
   {
      var gameRootPath = options.GameRootPath;
      var resolvedTargetLanguage = RuntimeTranslationDeployment.ResolveTargetLanguage( options.ToLanguage );
      var fromLanguage = RuntimeTranslationDeployment.SourceLanguage;

      var workspace = new TranslationWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var metadataDatabase = new TranslationDatabase( workspace.CorpusDatabasePath );
      metadataDatabase.EnsureExists();

      var toLanguage = resolvedTargetLanguage;

      var genericGlossaryPath = workspace.GetGenericGlossaryPath();
      var translatedGlossaryPath = workspace.GetGlossaryPath( fromLanguage, toLanguage );

      var genericGlossary = GenericGlossary.Load( genericGlossaryPath );
      if( !genericGlossary.Exists )
      {
         throw new InvalidOperationException( $"Generic glossary was not found: {genericGlossaryPath}" );
      }

      var existingGlossary = TranslationGlossary.Load( translatedGlossaryPath );
      var configuration = ToolConfigurationResolver.ResolveLlmConfiguration( fromLanguage, toLanguage );
      var settings = configuration.GlossaryClientSettings;
      var glossarySettings = glossarySettingsOverride ?? configuration.GlossarySettings;

      Console.WriteLine( $"Game root: {gameRootPath}" );
      Console.WriteLine( $"Workspace: {workspace.RootPath}" );
      Console.WriteLine( $"Config: {configuration.ConfigPath}" );
      Console.WriteLine( $"Language: {fromLanguage} -> {toLanguage}" );
      Console.WriteLine( $"Endpoint: {settings.EndpointUrl}" );
      Console.WriteLine( $"Model: {settings.Model}" );
      Console.WriteLine( $"Prompt source: {settings.ConfigurationSource}" );
      Console.WriteLine( $"Batch: {settings.BatchSize}" );
      Console.WriteLine( $"Glossary: {genericGlossary.Path}" );
      Console.WriteLine( $"Entries: {genericGlossary.EntryCount}" );
      Console.WriteLine( $"Output: {translatedGlossaryPath}" );
      Console.WriteLine( $"Existing entries: {existingGlossary.EntryCount}" );
      Console.WriteLine( $"Overwrite existing: {glossarySettings.OverwriteExisting}" );

      using var client = new DeepSeekClient( settings );
      var translatedGlossary = await GenericGlossaryTranslationService.TranslateAsync(
         genericGlossary,
         existingGlossary,
         translatedGlossaryPath,
         client,
         settings.BatchSize,
         cancellationToken,
         glossarySettings.OverwriteExisting ).ConfigureAwait( false );

      Console.WriteLine( $"Glossary complete. Output={translatedGlossary.Path}, Entries={translatedGlossary.EntryCount}." );
      return 0;
   }
}