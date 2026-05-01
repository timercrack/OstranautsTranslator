using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Exporting;
using OstranautsTranslator.Tool.Scanning;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool;

internal sealed class AutoRefreshWorkflow
{
   private readonly string _gameRootPath;
   private readonly string _workspacePath;
   private readonly string _toLanguage;
   private readonly bool _forceFullRebuild;

   public AutoRefreshWorkflow( string? toLanguage = null, bool forceFullRebuild = false )
   {
      _gameRootPath = RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory );
      _workspacePath = RuntimeTranslationDeployment.GetDefaultWorkspacePath( _gameRootPath );
      _toLanguage = RuntimeTranslationDeployment.ResolveTargetLanguage( toLanguage );
      _forceFullRebuild = forceFullRebuild;
   }

   public async Task<int> ExecuteAsync( CancellationToken cancellationToken )
   {
      var decision = _forceFullRebuild ? EvaluateForcedDecision() : EvaluateDecision();

      Console.WriteLine( $"Game root: {_gameRootPath}" );
      Console.WriteLine( $"Workspace: {_workspacePath}" );
      Console.WriteLine( $"Target language: {_toLanguage}" );
      Console.WriteLine( $"Plugin version: {( string.IsNullOrWhiteSpace( decision.PluginVersion ) ? "(missing)" : decision.PluginVersion )}" );
      Console.WriteLine( $"Mod version: {( string.IsNullOrWhiteSpace( decision.ModVersion ) ? "(missing)" : decision.ModVersion )}" );

      if( decision.Mode == AutoRefreshMode.NoAction )
      {
         Console.WriteLine( $"Game version already matches the deployed mod ({decision.PluginVersion}). No action taken." );
         return 0;
      }

      Console.WriteLine( decision.Mode == AutoRefreshMode.FullRebuild
         ? "Mode: full rebuild."
         : "Mode: incremental update." );

      foreach( var reason in decision.Reasons )
      {
         Console.WriteLine( $"- {reason}" );
      }

      var resolvedConfiguration = ToolConfigurationResolver.ResolveLlmConfiguration( RuntimeTranslationDeployment.SourceLanguage, _toLanguage );
      var glossarySettings = resolvedConfiguration.GlossarySettings with
      {
         OverwriteExisting = decision.Mode == AutoRefreshMode.FullRebuild,
      };
      var translationSettings = resolvedConfiguration.TranslationSettings with
      {
         TranslateGenericGlossaryFirst = false,
         RefreshGlossary = false,
         OverwriteExisting = decision.Mode == AutoRefreshMode.FullRebuild,
         IncludeDraft = true,
      };

      var steps = new WorkflowStepPrinter( 5 );

      steps.BeginStep( "Scan" );
      RunScan();

      steps.BeginStep( "Generate" );
      RunGenerateGenericGlossary();

      steps.BeginStep( "Glossary" );
      await TranslateGlossaryCommand.ExecuteAsync(
         new TranslateLlmCommandOptions
         {
            GameRootPath = _gameRootPath,
            WorkspacePath = _workspacePath,
            ToLanguage = _toLanguage,
         },
         cancellationToken,
         glossarySettings ).ConfigureAwait( false );

      steps.BeginStep( "Translate" );
      await TranslateLlmCommand.ExecuteAsync(
         new TranslateLlmCommandOptions
         {
            GameRootPath = _gameRootPath,
            WorkspacePath = _workspacePath,
            ToLanguage = _toLanguage,
         },
         cancellationToken,
         translationSettings ).ConfigureAwait( false );

      steps.BeginStep( "Deploy" );
      RunDeployTranslation();

      Console.WriteLine( _forceFullRebuild
         ? "Forced rebuild complete."
         : decision.Mode == AutoRefreshMode.FullRebuild
            ? "Full rebuild complete."
            : "Incremental refresh complete." );
      return 0;
   }

   private void RunScan()
   {
      var workspace = new CorpusWorkspace( _workspacePath );
      workspace.EnsureCreated();

      var options = new ScanCommandOptions
      {
         GameRootPath = _gameRootPath,
         WorkspacePath = _workspacePath,
         FromLanguage = RuntimeTranslationDeployment.SourceLanguage,
         WhitespaceBetweenWords = true,
         TemplateAllNumbersAway = true,
         HandleRichText = true,
      };

      var scanner = new CorpusScanner( workspace, options );
      var summary = scanner.Scan();

      Console.WriteLine( $"Scan: sources={summary.SourcesScanned}, occurrences={summary.OccurrencesCaptured}, entries={summary.TotalEntries}." );
      Console.WriteLine( $"Database: {workspace.CorpusDatabasePath}" );
   }

   private void RunGenerateGenericGlossary()
   {
      var generator = new GenericGlossaryGenerator( _gameRootPath, _workspacePath );
      var glossary = generator.Generate();
      Console.WriteLine( $"Glossary: {glossary.Path}" );
      Console.WriteLine( $"Entries: {glossary.EntryCount}" );
   }

   private void RunDeployTranslation()
   {
      var workspace = new CorpusWorkspace( _workspacePath );
      workspace.EnsureCreated();

      var options = new DeployTranslationCommandOptions
      {
         GameRootPath = _gameRootPath,
         WorkspacePath = _workspacePath,
         ToLanguage = _toLanguage,
         IncludeDraft = true,
         VerifySourceHash = true,
      };

      var deployer = new TranslationDeployer( workspace, options );
      var summary = deployer.Deploy( refreshCorpus: false );

      Console.WriteLine( $"Deploy: translated={summary.TranslatedEntries}, patched={summary.PatchedOccurrences}, files={summary.FilesWritten}, warnings={summary.WarningCount}." );
      Console.WriteLine( $"Mods root: {summary.ModsRootPath}" );
      Console.WriteLine( $"Load order: {summary.LoadingOrderPath}" );
      Console.WriteLine( $"Directory: {summary.ModDirectoryPath}" );
      Console.WriteLine( $"Database: {summary.TranslationDatabasePath}" );
   }

   private AutoRefreshDecision EvaluateDecision()
   {
      var translationWorkspace = new TranslationWorkspace( _workspacePath );
      var reasons = new List<string>();

      var pluginVersion = RecordedGameVersionResolver.ResolveRecordedGameVersionOrEmpty( _gameRootPath );
      if( !RecordedGameVersionResolver.TryResolveRecordedGameVersion( _gameRootPath, out _, out var pluginVersionError ) )
      {
         reasons.Add( pluginVersionError );
      }

      var modVersion = DeployedModGameVersionResolver.ResolveDeployedModGameVersionOrEmpty( _gameRootPath );
      if( !DeployedModGameVersionResolver.TryResolveDeployedModGameVersion( _gameRootPath, out _, out var modVersionError ) )
      {
         reasons.Add( modVersionError );
      }

      if( !File.Exists( translationWorkspace.CorpusDatabasePath ) )
      {
         reasons.Add( $"Workspace database was not found: '{translationWorkspace.CorpusDatabasePath}'." );
      }

      if( reasons.Count > 0 )
      {
         return new AutoRefreshDecision( AutoRefreshMode.FullRebuild, pluginVersion, modVersion, reasons );
      }

      if( string.Equals( pluginVersion, modVersion, StringComparison.Ordinal ) )
      {
         return new AutoRefreshDecision( AutoRefreshMode.NoAction, pluginVersion, modVersion, Array.Empty<string>() );
      }

      return new AutoRefreshDecision(
         AutoRefreshMode.IncrementalUpdate,
         pluginVersion,
         modVersion,
         [ $"Detected version change: {modVersion} -> {pluginVersion}." ] );
   }

   private AutoRefreshDecision EvaluateForcedDecision()
   {
      var decision = EvaluateDecision();
      if( decision.Mode == AutoRefreshMode.FullRebuild )
      {
         return decision with
         {
            Reasons = decision.Reasons.Concat( [ "Forced by rebuild." ] ).ToArray(),
         };
      }

      return new AutoRefreshDecision(
         AutoRefreshMode.FullRebuild,
         decision.PluginVersion,
         decision.ModVersion,
         [ "Forced by rebuild." ] );
   }

   private sealed record AutoRefreshDecision(
      AutoRefreshMode Mode,
      string PluginVersion,
      string ModVersion,
      IReadOnlyList<string> Reasons );

   private enum AutoRefreshMode
   {
      NoAction,
      FullRebuild,
      IncrementalUpdate,
   }

   private sealed class WorkflowStepPrinter
   {
      private readonly int _totalSteps;
      private int _currentStep;

      public WorkflowStepPrinter( int totalSteps )
      {
         _totalSteps = Math.Max( 1, totalSteps );
      }

      public void BeginStep( string title )
      {
         _currentStep++;
         Console.WriteLine();
         Console.WriteLine( $"[{_currentStep}/{_totalSteps}] {title}" );
      }
   }
}