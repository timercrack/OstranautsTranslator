using System.ComponentModel;
using System.Diagnostics;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal sealed class GenericGlossaryGenerator
{
   private const string ScriptFileName = "generate_generic_glossary.py";

   private readonly string _gameRootPath;
   private readonly TranslationWorkspace _workspace;

   public GenericGlossaryGenerator( string gameRootPath, string workspacePath )
   {
      _gameRootPath = Path.GetFullPath( gameRootPath );
      _workspace = new TranslationWorkspace( workspacePath );
   }

   public GenericGlossary Generate()
   {
      _workspace.EnsureCreated();

      var dataRootPath = Path.Combine( _gameRootPath, "Ostranauts_Data", "StreamingAssets", "data" );
      if( !Directory.Exists( dataRootPath ) )
      {
         throw new DirectoryNotFoundException( $"Could not find game data directory '{dataRootPath}'." );
      }

      var scriptPath = Path.Combine( AppContext.BaseDirectory, ScriptFileName );
      if( !File.Exists( scriptPath ) )
      {
         throw new FileNotFoundException(
            $"The bundled glossary generator script was not found next to {RuntimeTranslationDeployment.ToolExecutableName}.exe.",
            scriptPath );
      }

      var outputPath = _workspace.GetGenericGlossaryPath();
      RunScript( scriptPath, dataRootPath, outputPath );
      return GenericGlossary.Load( outputPath );
   }

   private static void RunScript( string scriptPath, string dataRootPath, string outputPath )
   {
      var launchers = new ( string FileName, string[] Arguments )[]
      {
         ( "py", [ "-3", scriptPath, dataRootPath, outputPath ] ),
         ( "python", [ scriptPath, dataRootPath, outputPath ] ),
      };

      var launcherErrors = new List<string>();
      foreach( var launcher in launchers )
      {
         try
         {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
               FileName = launcher.FileName,
               WorkingDirectory = AppContext.BaseDirectory,
               RedirectStandardOutput = true,
               RedirectStandardError = true,
               UseShellExecute = false,
               CreateNoWindow = true,
            };

            foreach( var argument in launcher.Arguments )
            {
               process.StartInfo.ArgumentList.Add( argument );
            }

            process.Start();
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if( !string.IsNullOrWhiteSpace( standardOutput ) )
            {
               Console.WriteLine( standardOutput.TrimEnd() );
            }

            if( process.ExitCode != 0 )
            {
               throw new InvalidOperationException(
                  $"Glossary generator script exited with code {process.ExitCode}.{Environment.NewLine}{standardError.Trim()}".Trim() );
            }

            if( !string.IsNullOrWhiteSpace( standardError ) )
            {
               Console.Error.WriteLine( standardError.TrimEnd() );
            }

            return;
         }
         catch( Win32Exception exception )
         {
            launcherErrors.Add( $"{launcher.FileName}: {exception.Message}" );
         }
      }

      throw new InvalidOperationException(
         "Failed to launch the bundled glossary generator script. Make sure Python is installed and available as 'py' or 'python'."
         + Environment.NewLine
         + string.Join( Environment.NewLine, launcherErrors ) );
   }
}