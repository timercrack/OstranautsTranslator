using OstranautsTranslator.Core;
using OstranautsTranslator.Tool.Scanning;
using OstranautsTranslator.Tool.Workspace;

namespace OstranautsTranslator.Tool.Exporting;

internal sealed class TranslationDeployer
{
   private readonly CorpusWorkspace _workspace;
   private readonly DeployTranslationCommandOptions _options;

   public TranslationDeployer( CorpusWorkspace workspace, DeployTranslationCommandOptions options )
   {
      _workspace = workspace;
      _options = options;
   }

   public TranslationDeploySummary Deploy( bool refreshCorpus = true )
   {
      if( refreshCorpus )
      {
         RefreshCorpus();
      }

      var nativeModSummary = DeployNativeMod();
      var translationDatabasePath = _workspace.GetTranslationDatabasePath( _options.ToLanguage );
      RemoveObsoleteRuntimeDatabase();
      _workspace.DeleteTransientExports();

      return new TranslationDeploySummary(
         nativeModSummary.TranslatedEntries,
         nativeModSummary.PatchedOccurrences,
         nativeModSummary.FilesWritten,
         nativeModSummary.WarningCount,
         nativeModSummary.OutputRootPath,
         nativeModSummary.LoadingOrderPath,
         nativeModSummary.ModDirectoryPath,
         translationDatabasePath );
   }

   private void RefreshCorpus()
   {
      var scanOptions = new ScanCommandOptions
      {
         GameRootPath = _options.GameRootPath,
         WorkspacePath = _options.WorkspacePath,
         FromLanguage = "en",
         WhitespaceBetweenWords = true,
         TemplateAllNumbersAway = true,
         HandleRichText = true,
      };

      var scanner = new CorpusScanner( _workspace, scanOptions );
      scanner.Scan();
   }

   private NativeModExportSummary DeployNativeMod()
   {
      var nativeModOptions = new ExportNativeModCommandOptions
      {
         GameRootPath = _options.GameRootPath,
         WorkspacePath = _options.WorkspacePath,
         ToLanguage = _options.ToLanguage,
         ModId = _options.ModId,
         ModName = _options.ModName,
         GameVersion = RecordedGameVersionResolver.ResolveRecordedGameVersionOrEmpty( _options.GameRootPath ),
         OutputPath = GetModsRootPath(),
         IncludeDraft = _options.IncludeDraft,
         VerifySourceHash = _options.VerifySourceHash,
      };

      var nativeModExporter = new NativeModExporter( _workspace, nativeModOptions );
      return nativeModExporter.Export();
   }

   private void RemoveObsoleteRuntimeDatabase()
   {
      var obsoleteRuntimeDatabasePath = RuntimeTranslationDeployment.GetRuntimeDatabasePath( _options.GameRootPath, _options.ToLanguage );
      if( File.Exists( obsoleteRuntimeDatabasePath ) )
      {
         File.Delete( obsoleteRuntimeDatabasePath );
      }

      var obsoleteRuntimeTranslationsDirectoryPath = RuntimeTranslationDeployment.GetRuntimeTranslationsDirectoryPath( _options.GameRootPath );
      if( Directory.Exists( obsoleteRuntimeTranslationsDirectoryPath )
         && !Directory.EnumerateFileSystemEntries( obsoleteRuntimeTranslationsDirectoryPath ).Any() )
      {
         Directory.Delete( obsoleteRuntimeTranslationsDirectoryPath );
      }
   }

   private string GetModsRootPath()
   {
      return RuntimeTranslationDeployment.GetModsRootPath( _options.GameRootPath );
   }
}
