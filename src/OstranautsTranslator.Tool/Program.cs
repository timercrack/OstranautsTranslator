using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Exporting;
using OstranautsTranslator.Tool.Importing;
using OstranautsTranslator.Tool.Scanning;
using OstranautsTranslator.Tool.Workspace;
using System.Text;

namespace OstranautsTranslator.Tool;

internal static class Program
{
   public static async Task<int> Main( string[] args )
   {
      try
      {
         InitializeConsoleEncoding();

         if( args.Length > 0 && IsListLanguagesCommand( args[ 0 ] ) )
         {
            if( args.Length > 1 )
            {
               throw new ArgumentException( $"Option '{args[ 0 ]}' does not accept additional arguments." );
            }

            PrintSupportedLanguages();
            return 0;
         }

         if( args.Length > 0 && IsHelpCommand( args[ 0 ] ) )
         {
            PrintUsage();
            return 0;
         }

         using var cancellationTokenSource = new CancellationTokenSource();
         Console.CancelKeyPress += ( _, eventArgs ) =>
         {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
         };

         InitializeSqliteProvider();

         if( args.Length == 0 )
         {
            return await RunAutoRefreshAsync( cancellationTokenSource.Token ).ConfigureAwait( false );
         }

         var command = args[ 0 ];
         switch( command.ToLowerInvariant() )
         {
            case "scan":
               return RunScan( args.Skip( 1 ).ToArray() );
            case "generate":
               return RunGenerateGlossary( args.Skip( 1 ).ToArray() );
            case "rebuild":
               return await RunRebuildTranslationAsync( args.Skip( 1 ).ToArray(), cancellationTokenSource.Token ).ConfigureAwait( false );
            case "source":
               return RunExportSource( args.Skip( 1 ).ToArray() );
            case "import":
               return RunImportTranslations( args.Skip( 1 ).ToArray() );
            case "runtime":
               return RunExportRuntime( args.Skip( 1 ).ToArray() );
            case "runtime-miss":
               return RunImportRuntimeMiss( args.Skip( 1 ).ToArray() );
            case "native-mod":
               return RunExportNativeMod( args.Skip( 1 ).ToArray() );
            case "deploy":
               return RunDeployTranslation( args.Skip( 1 ).ToArray() );
            case "glossary":
               return await TranslateGlossaryCommand.ExecuteAsync( TranslateLlmCommandOptions.Parse( args.Skip( 1 ).ToArray() ), cancellationTokenSource.Token ).ConfigureAwait( false );
            case "translate":
               return await TranslateLlmCommand.ExecuteAsync( TranslateLlmCommandOptions.Parse( args.Skip( 1 ).ToArray() ), cancellationTokenSource.Token ).ConfigureAwait( false );
            default:
               Console.Error.WriteLine( $"Unknown command '{command}'." );
               PrintUsage();
               return 1;
         }
      }
      catch( OperationCanceledException )
      {
         Console.Error.WriteLine( "Operation cancelled." );
         return 1;
      }
      catch( Exception e )
      {
         Console.Error.WriteLine( e );
         return 1;
      }
   }

   private static void InitializeSqliteProvider()
   {
      SQLitePCL.raw.SetProvider( new SQLitePCL.SQLite3Provider_winsqlite3() );
      SQLitePCL.raw.FreezeProvider();
   }

   private static void InitializeConsoleEncoding()
   {
      Console.OutputEncoding = new UTF8Encoding( encoderShouldEmitUTF8Identifier: false );
   }

   private static Task<int> RunAutoRefreshAsync( CancellationToken cancellationToken )
   {
      return new AutoRefreshWorkflow().ExecuteAsync( cancellationToken );
   }

   private static Task<int> RunRebuildTranslationAsync( IReadOnlyList<string> args, CancellationToken cancellationToken )
   {
      var options = TargetLanguageCommandOptions.Parse( args );
      return new AutoRefreshWorkflow( options.ToLanguage, forceFullRebuild: true ).ExecuteAsync( cancellationToken );
   }

   private static int RunScan( IReadOnlyList<string> args )
   {
      var options = ScanCommandOptions.Parse( args );
      var workspace = new CorpusWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var scanner = new CorpusScanner( workspace, options );
      var summary = scanner.Scan();

      Console.WriteLine( $"Scan: sources={summary.SourcesScanned}, occurrences={summary.OccurrencesCaptured}, entries={summary.TotalEntries}." );
      Console.WriteLine( $"Database: {workspace.CorpusDatabasePath}" );
      return 0;
   }

   private static int RunGenerateGlossary( IReadOnlyList<string> args )
   {
      if( args.Count > 0 )
      {
         throw new ArgumentException( $"Unknown option '{args[ 0 ]}'." );
      }

      var gameRootPath = RuntimeTranslationDeployment.GetDefaultGameRootPath( AppContext.BaseDirectory );
      var workspacePath = RuntimeTranslationDeployment.GetDefaultWorkspacePath( gameRootPath );
      var glossary = new GenericGlossaryGenerator( gameRootPath, workspacePath ).Generate();

      Console.WriteLine( $"Workspace: {workspacePath}" );
      Console.WriteLine( $"Glossary: {glossary.Path}" );
      Console.WriteLine( $"Entries: {glossary.EntryCount}" );
      return 0;
   }

   private static int RunExportSource( IReadOnlyList<string> args )
   {
      var options = ExportSourceCommandOptions.Parse( args );
      var workspace = new CorpusWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var exporter = new SourceExporter( workspace, options );
      var summary = exporter.Export();

      Console.WriteLine( $"Source: synced={summary.SynchronizedEntries}, exported={summary.ExportedEntries}." );
      Console.WriteLine( $"Database: {summary.TranslationDatabasePath}" );
      Console.WriteLine( $"Output: {summary.ExportPath}" );
      return 0;
   }

   private static int RunImportTranslations( IReadOnlyList<string> args )
   {
      var options = ImportTranslationsCommandOptions.Parse( args );
      var workspace = new CorpusWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var importer = new TranslationsImporter( workspace, options );
      var summary = importer.Import();

      Console.WriteLine( $"Import: processed={summary.ProcessedCount}, applied={summary.AppliedCount}, unknown={summary.UnknownCount}, skipped={summary.SkippedCount}." );
      Console.WriteLine( $"Database: {summary.TranslationDatabasePath}" );
      return 0;
   }

   private static int RunExportRuntime( IReadOnlyList<string> args )
   {
      var options = ExportRuntimeCommandOptions.Parse( args );
      var workspace = new CorpusWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var exporter = new RuntimeExporter( workspace, options );
      var summary = exporter.Export();

      Console.WriteLine( $"Runtime: exported={summary.ExportedEntries}." );
      Console.WriteLine( $"File: {summary.ExportPath}" );
      return 0;
   }

   private static int RunImportRuntimeMiss( IReadOnlyList<string> args )
   {
      var options = ImportRuntimeMissCommandOptions.Parse( args );
      var workspace = new CorpusWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var importer = new RuntimeMissImporter( workspace, options );
      var summary = importer.Import();

      Console.WriteLine( $"Runtime miss: processed={summary.ProcessedCount}, applied={summary.AppliedCount}, stored={summary.StoredCaptureCount}, skipped={summary.SkippedCount}." );
      Console.WriteLine( $"Database: {summary.TranslationDatabasePath}" );
      return 0;
   }

   private static int RunExportNativeMod( IReadOnlyList<string> args )
   {
      var options = ExportNativeModCommandOptions.Parse( args );
      var workspace = new CorpusWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var exporter = new NativeModExporter( workspace, options );
      var summary = exporter.Export();

      Console.WriteLine( $"Native mod: translated={summary.TranslatedEntries}, patched={summary.PatchedOccurrences}, files={summary.FilesWritten}, warnings={summary.WarningCount}." );
      Console.WriteLine( $"Output: {summary.OutputRootPath}" );
      Console.WriteLine( $"Load order: {summary.LoadingOrderPath}" );
      Console.WriteLine( $"Directory: {summary.ModDirectoryPath}" );
      return 0;
   }

   private static int RunDeployTranslation( IReadOnlyList<string> args )
   {
      var options = DeployTranslationCommandOptions.Parse( args );
      var workspace = new CorpusWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var deployer = new TranslationDeployer( workspace, options );
      var summary = deployer.Deploy();

      Console.WriteLine( $"Game root: {options.GameRootPath}" );
      Console.WriteLine( $"Workspace: {options.WorkspacePath}" );
      Console.WriteLine( $"Deploy: translated={summary.TranslatedEntries}, patched={summary.PatchedOccurrences}, files={summary.FilesWritten}, warnings={summary.WarningCount}." );
      Console.WriteLine( $"Mods root: {summary.ModsRootPath}" );
      Console.WriteLine( $"Load order: {summary.LoadingOrderPath}" );
      Console.WriteLine( $"Directory: {summary.ModDirectoryPath}" );
      Console.WriteLine( $"Database: {summary.TranslationDatabasePath}" );
      return 0;
   }

   private static bool IsListLanguagesCommand( string command )
   {
      return command.Equals( "-l", StringComparison.OrdinalIgnoreCase );
   }

   private static bool IsHelpCommand( string command )
   {
      return command.Equals( "help", StringComparison.OrdinalIgnoreCase )
         || command.Equals( "--help", StringComparison.OrdinalIgnoreCase )
         || command.Equals( "-h", StringComparison.OrdinalIgnoreCase );
   }

   private static void PrintUsage()
   {
      Console.WriteLine( RuntimeTranslationDeployment.ToolExecutableName );
      Console.WriteLine();
      Console.WriteLine( $"Exe   : <game root>\\{RuntimeTranslationDeployment.ToolDirectoryName}\\{RuntimeTranslationDeployment.ToolExecutableName}.exe" );
      Console.WriteLine( $"Work  : <game root>\\{RuntimeTranslationDeployment.ToolDirectoryName}\\{RuntimeTranslationDeployment.WorkspaceDirectoryName}" );
      Console.WriteLine( "Db    : <workspace>\\corpus.sqlite" );
      Console.WriteLine( $"Lang  : {RuntimeTranslationDeployment.SourceLanguage} -> system default" );
      Console.WriteLine( $"Mod   : {RuntimeTranslationDeployment.ModId} / {RuntimeTranslationDeployment.ModName} / {RuntimeTranslationDeployment.DefaultAuthor}" );
      Console.WriteLine( "Cfg   : <exe dir>\\config.ini" );
      Console.WriteLine();
      Console.WriteLine( "Run:" );
      Console.WriteLine( $"  {RuntimeTranslationDeployment.ToolExecutableName}          auto" );
      Console.WriteLine( $"  {RuntimeTranslationDeployment.ToolExecutableName} -l       list langs" );
      Console.WriteLine( $"  {RuntimeTranslationDeployment.ToolExecutableName} rebuild [-t<lang>]" );
      Console.WriteLine();
      Console.WriteLine( "Flow:" );
      Console.WriteLine( $"  scan" );
      Console.WriteLine( $"  generate" );
      Console.WriteLine( $"  rebuild [-t<lang>]" );
      Console.WriteLine( $"  glossary [-t<lang>]" );
      Console.WriteLine( $"  translate [-t<lang>]" );
      Console.WriteLine( $"  deploy [-t<lang>]" );
      Console.WriteLine();
      Console.WriteLine( "More:" );
      Console.WriteLine( $"  source [-t<lang>]" );
      Console.WriteLine( $"  import [-t<lang>]" );
      Console.WriteLine( $"  runtime [-t<lang>]" );
      Console.WriteLine( $"  runtime-miss [-t<lang>]" );
      Console.WriteLine( $"  native-mod [-t<lang>]" );
      Console.WriteLine();
      Console.WriteLine( "Notes:" );
      Console.WriteLine( "  -l      : prints arg / English / native name" );
      Console.WriteLine( "  -t      : supports -tzh, -t=zh, or -t zh" );
      Console.WriteLine( "  auto    : version check -> no-op / full rebuild / incremental update" );
      Console.WriteLine( "  rebuild : always runs scan -> generate -> glossary -> translate -> deploy" );
      Console.WriteLine( "  generate: runs bundled generate_generic_glossary.py" );
      Console.WriteLine( "  jsonl   : source_kind + source_id/source_key" );
      Console.WriteLine( "  glossary: <workspace>\\reference\\glossary.json + glossary-<from>-to-<to>.json" );
      Console.WriteLine( $"  deploy  : author is fixed to '{RuntimeTranslationDeployment.DefaultAuthor}'" );
      Console.WriteLine( "  version : launch the game once after updates so the plugin can record the UI version" );
      Console.WriteLine( "  config  : copy config-example.ini to config.ini, set [LLMTranslate] ApiKey" );
   }

   private static void PrintSupportedLanguages()
   {
      var argWidth = Math.Max( 3, SupportedLanguageCatalog.All.Max( x => x.ArgumentName.Length ) );
      var englishWidth = Math.Max( 7, SupportedLanguageCatalog.All.Max( x => x.EnglishName.Length ) );

      Console.WriteLine( "Lang:" );
      Console.WriteLine( $"  {"arg".PadRight( argWidth )}  {"English".PadRight( englishWidth )}  Native" );
      foreach( var language in SupportedLanguageCatalog.All )
      {
         Console.WriteLine( $"  {language.ArgumentName.PadRight( argWidth )}  {language.EnglishName.PadRight( englishWidth )}  {language.NativeName}" );
      }

      Console.WriteLine();
      Console.WriteLine( "Use : -t<arg> / -t=<arg> / -t <arg>" );
   }
}
