using System.IO;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal sealed class TranslationWorkspace
{
   public TranslationWorkspace( string rootPath )
   {
      RootPath = Path.GetFullPath( rootPath );
      CorpusDatabasePath = Path.Combine( RootPath, "corpus.sqlite" );
      ReferenceDirectoryPath = Path.Combine( RootPath, "reference" );
      TranslationImportsDirectoryPath = Path.Combine( RootPath, "imports", "translations" );
      RuntimeMissImportsDirectoryPath = Path.Combine( RootPath, "imports", "runtime-miss" );
      SourceExportsDirectoryPath = Path.Combine( RootPath, "exports", "source" );
      RuntimeExportsDirectoryPath = Path.Combine( RootPath, "exports", "runtime" );
   }

   public string RootPath { get; }

   public string CorpusDatabasePath { get; }

      public string ReferenceDirectoryPath { get; }

   public string TranslationImportsDirectoryPath { get; }

   public string RuntimeMissImportsDirectoryPath { get; }

   public string SourceExportsDirectoryPath { get; }

   public string RuntimeExportsDirectoryPath { get; }

   public string LlmDiagnosticsDirectoryPath => Path.Combine( RootPath, "logs" );

   public string GetGlossaryPath( string fromLanguage, string toLanguage )
   {
      return Path.Combine( ReferenceDirectoryPath, $"glossary-{fromLanguage}-to-{toLanguage}.json" );
   }

   public string GetGenericGlossaryPath()
   {
      return Path.Combine( ReferenceDirectoryPath, "glossary.json" );
   }

   public string GetTranslationDatabasePath( string language )
   {
      return CorpusDatabasePath;
   }

   public void EnsureCreated()
   {
      Directory.CreateDirectory( RootPath );
      DeleteObsoleteWorkspaceContent();
      DeleteEmptyWorkspaceScaffolding();
   }

   private void DeleteObsoleteWorkspaceContent()
   {
      DeleteDirectoryIfExists( Path.Combine( RootPath, "translations" ) );
      DeleteDirectoryIfExists( Path.Combine( RootPath, "exports", "native-mod" ) );
   }

   private void DeleteEmptyWorkspaceScaffolding()
   {
      DeleteEmptyDirectoryTree( Path.Combine( RootPath, "imports" ) );
      DeleteEmptyDirectoryTree( Path.Combine( RootPath, "exports" ) );
   }

   private static void DeleteDirectoryIfExists( string directoryPath )
   {
      if( Directory.Exists( directoryPath ) )
      {
         Directory.Delete( directoryPath, recursive: true );
      }
   }

   private static void DeleteEmptyDirectoryTree( string directoryPath )
   {
      if( !Directory.Exists( directoryPath ) ) return;

      foreach( var childDirectoryPath in Directory.EnumerateDirectories( directoryPath ) )
      {
         DeleteEmptyDirectoryTree( childDirectoryPath );
      }

      if( !Directory.EnumerateFileSystemEntries( directoryPath ).Any() )
      {
         Directory.Delete( directoryPath );
      }
   }

   public static TranslationWorkspace CreateDefaultFromExecutableLocation()
   {
      var executableDirectory = AppContext.BaseDirectory;
      var gameRootPath = RuntimeTranslationDeployment.GetDefaultGameRootPath( executableDirectory );
      var workspacePath = RuntimeTranslationDeployment.GetDefaultWorkspacePath( gameRootPath );
      return new TranslationWorkspace( workspacePath );
   }
}
