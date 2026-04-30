using System;
using System.IO;

namespace OstranautsTranslator.Core;

public static class RecordedGameVersionResolver
{
   public static string ResolveRecordedGameVersionOrEmpty( string gameRootPath )
   {
      return TryResolveRecordedGameVersion( gameRootPath, out var gameVersion, out _ )
         ? gameVersion
         : string.Empty;
   }

   public static bool TryResolveRecordedGameVersion( string gameRootPath, out string gameVersion, out string errorMessage )
   {
      gameVersion = string.Empty;

      if( string.IsNullOrWhiteSpace( gameRootPath ) )
      {
         errorMessage = "Game root path is empty.";
         return false;
      }

      var configPath = RuntimeTranslationDeployment.GetPluginConfigurationPath( gameRootPath );
      if( !File.Exists( configPath ) )
      {
         errorMessage = $"Plugin configuration was not found: '{configPath}'.";
         return false;
      }

      try
      {
         string currentSection = string.Empty;
         foreach( var rawLine in File.ReadLines( configPath ) )
         {
            var line = rawLine?.Trim() ?? string.Empty;
            if( line.Length == 0 || line.StartsWith( "#", StringComparison.Ordinal ) || line.StartsWith( ";", StringComparison.Ordinal ) )
            {
               continue;
            }

            if( line.StartsWith( "[", StringComparison.Ordinal ) && line.EndsWith( "]", StringComparison.Ordinal ) )
            {
               currentSection = line.Substring( 1, line.Length - 2 ).Trim();
               continue;
            }

            if( !string.Equals( currentSection, "State", StringComparison.OrdinalIgnoreCase ) )
            {
               continue;
            }

            var separatorIndex = line.IndexOf( '=' );
            if( separatorIndex <= 0 )
            {
               continue;
            }

            var key = line.Substring( 0, separatorIndex ).Trim();
            if( !string.Equals( key, "RecordedGameVersion", StringComparison.OrdinalIgnoreCase ) )
            {
               continue;
            }

            gameVersion = line.Substring( separatorIndex + 1 ).Trim();
            if( gameVersion.Length == 0 )
            {
               errorMessage = $"RecordedGameVersion is empty in '{configPath}'.";
               return false;
            }

            errorMessage = string.Empty;
            return true;
         }

         errorMessage = $"RecordedGameVersion was not found in '{configPath}'.";
         return false;
      }
      catch( Exception exception ) when( exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException )
      {
         errorMessage = exception.Message;
         return false;
      }
   }
}