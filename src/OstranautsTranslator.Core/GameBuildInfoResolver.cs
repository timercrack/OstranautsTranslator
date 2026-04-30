using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace OstranautsTranslator.Core;

public sealed class GameBuildInfo
{
   public GameBuildInfo( string executablePath, string displayVersion, string signature )
   {
      ExecutablePath = executablePath ?? string.Empty;
      DisplayVersion = displayVersion ?? string.Empty;
      Signature = signature ?? string.Empty;
   }

   public string ExecutablePath { get; }

   public string DisplayVersion { get; }

   public string Signature { get; }
}

public static class GameBuildInfoResolver
{
   public static string ResolveCurrentGameVersionOrEmpty( string gameRootPath )
   {
      return TryResolveCurrentGameBuild( gameRootPath, out var gameBuildInfo, out _ )
         ? gameBuildInfo.DisplayVersion
         : string.Empty;
   }

   public static bool TryResolveCurrentGameBuild( string gameRootPath, out GameBuildInfo gameBuildInfo, out string errorMessage )
   {
      gameBuildInfo = null;

      if( string.IsNullOrWhiteSpace( gameRootPath ) )
      {
         errorMessage = "Game root path is empty.";
         return false;
      }

      var executablePath = RuntimeTranslationDeployment.GetGameExecutablePath( gameRootPath );
      if( !File.Exists( executablePath ) )
      {
         errorMessage = $"Game executable was not found: '{executablePath}'.";
         return false;
      }

      try
      {
         var fileInfo = new FileInfo( executablePath );
         var versionInfo = FileVersionInfo.GetVersionInfo( executablePath );
         var displayVersion = BuildDisplayVersion( versionInfo, fileInfo );
         var signature = BuildSignature( versionInfo, fileInfo );

         gameBuildInfo = new GameBuildInfo( executablePath, displayVersion, signature );
         errorMessage = string.Empty;
         return true;
      }
      catch( Exception exception ) when( exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException )
      {
         errorMessage = exception.Message;
         return false;
      }
   }

   private static string BuildDisplayVersion( FileVersionInfo versionInfo, FileInfo fileInfo )
   {
      var components = new List<string>();
      AppendUnique( components, versionInfo.ProductVersion );
      AppendUnique( components, versionInfo.FileVersion );

      if( components.Count == 0 )
      {
         components.Add( fileInfo.LastWriteTimeUtc.ToString( "yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture ) );
      }

      return string.Join( " / ", components );
   }

   private static string BuildSignature( FileVersionInfo versionInfo, FileInfo fileInfo )
   {
      return string.Join(
         "|",
         NormalizeValue( versionInfo.ProductVersion ),
         NormalizeValue( versionInfo.FileVersion ),
         fileInfo.Length.ToString( CultureInfo.InvariantCulture ),
         fileInfo.LastWriteTimeUtc.ToString( "O", CultureInfo.InvariantCulture ) );
   }

   private static void AppendUnique( List<string> components, string value )
   {
      var normalizedValue = NormalizeValue( value );
      if( normalizedValue.Length == 0 )
      {
         return;
      }

      foreach( var existing in components )
      {
         if( string.Equals( existing, normalizedValue, StringComparison.OrdinalIgnoreCase ) )
         {
            return;
         }
      }

      components.Add( normalizedValue );
   }

   private static string NormalizeValue( string value )
   {
      return string.IsNullOrWhiteSpace( value )
         ? string.Empty
         : value.Trim();
   }
}