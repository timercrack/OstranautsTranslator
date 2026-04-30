using System;
using System.Reflection;
using OstranautsTranslator.Core;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx;

internal static class RuntimeGameVersionResolver
{
   private const string BuildPrefix = "Early Access Build: ";

   public static bool TryResolveCurrentGameBuild( out GameBuildInfo gameBuildInfo, out string errorMessage )
   {
      gameBuildInfo = null;

      try
      {
         var versionAsset = Resources.Load( "version" );
         if( versionAsset == null )
         {
            errorMessage = "Unity resource 'version' was not found.";
            return false;
         }

         var textProperty = versionAsset.GetType().GetProperty( "text", BindingFlags.Public | BindingFlags.Instance );
         if( textProperty == null || textProperty.PropertyType != typeof( string ) )
         {
            errorMessage = "Unity resource 'version' does not expose a string text property.";
            return false;
         }

         var displayVersion = NormalizeDisplayVersion( textProperty.GetValue( versionAsset, null ) as string );
         if( string.IsNullOrWhiteSpace( displayVersion ) )
         {
            errorMessage = "Unity resource 'version' is empty.";
            return false;
         }

         gameBuildInfo = new GameBuildInfo( "Resources/version", displayVersion, displayVersion );
         errorMessage = string.Empty;
         return true;
      }
      catch( Exception exception )
      {
         errorMessage = exception.Message;
         return false;
      }
   }

   private static string NormalizeDisplayVersion( string versionText )
   {
      var normalizedVersion = string.IsNullOrWhiteSpace( versionText )
         ? string.Empty
         : versionText.Replace( "\r", string.Empty ).Replace( "\n", string.Empty ).Trim();

      if( normalizedVersion.Length == 0 )
      {
         return string.Empty;
      }

      return normalizedVersion.StartsWith( BuildPrefix, StringComparison.OrdinalIgnoreCase )
         ? normalizedVersion
         : BuildPrefix + normalizedVersion;
   }
}