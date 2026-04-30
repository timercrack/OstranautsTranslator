using System.Text.Json;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal static class DeployedModGameVersionResolver
{
   public static string ResolveDeployedModGameVersionOrEmpty( string gameRootPath )
   {
      return TryResolveDeployedModGameVersion( gameRootPath, out var gameVersion, out _ )
         ? gameVersion
         : string.Empty;
   }

   public static bool TryResolveDeployedModGameVersion( string gameRootPath, out string gameVersion, out string errorMessage )
   {
      gameVersion = string.Empty;

      if( string.IsNullOrWhiteSpace( gameRootPath ) )
      {
         errorMessage = "Game root path is empty.";
         return false;
      }

      var modInfoPath = Path.Combine( RuntimeTranslationDeployment.GetModDirectoryPath( gameRootPath ), "mod_info.json" );
      if( !File.Exists( modInfoPath ) )
      {
         errorMessage = $"Deployed mod metadata was not found: '{modInfoPath}'.";
         return false;
      }

      try
      {
         using var stream = File.OpenRead( modInfoPath );
         using var document = JsonDocument.Parse( stream );
         if( document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0 )
         {
            errorMessage = $"mod_info.json does not contain any mod entries: '{modInfoPath}'.";
            return false;
         }

         var firstEntry = document.RootElement[ 0 ];
         if( firstEntry.ValueKind != JsonValueKind.Object
            || !firstEntry.TryGetProperty( "strGameVersion", out var versionProperty )
            || versionProperty.ValueKind != JsonValueKind.String )
         {
            errorMessage = $"strGameVersion was not found in '{modInfoPath}'.";
            return false;
         }

         gameVersion = versionProperty.GetString()?.Trim() ?? string.Empty;
         if( gameVersion.Length == 0 )
         {
            errorMessage = $"strGameVersion is empty in '{modInfoPath}'.";
            return false;
         }

         errorMessage = string.Empty;
         return true;
      }
      catch( Exception exception ) when( exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException )
      {
         errorMessage = exception.Message;
         return false;
      }
   }
}