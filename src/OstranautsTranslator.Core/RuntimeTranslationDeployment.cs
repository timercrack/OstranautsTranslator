using System.Globalization;
using System.IO;
using System.Text;

namespace OstranautsTranslator.Core;

public static class RuntimeTranslationDeployment
{
   public const string GameExecutableName = "Ostranauts.exe";
   public const string SourceLanguage = "en";
   public const string DefaultLanguage = "zh";
   public const string ToolDirectoryName = "OstranautsTranslator";
   public const string ToolExecutableName = "OstranautsTranslator";
   public const string ModId = "OstranautsTranslate";
   public const string ModName = "OstranautsTranslate";
   public const string DefaultAuthor = "timercrack";
   public const string WorkspaceDirectoryName = "workspace";
   public const string WorkspaceDatabaseFileName = "corpus.sqlite";
   public const string RuntimeTranslationsDirectoryName = "translations";
   public const string RuntimeDatabaseFileName = "translations.sqlite";
   public static readonly string[] ObsoleteModIdsToDelete = [ "xunity-translation-zh" ];

   public static string ResolveTargetLanguage( string configuredLanguage = null )
   {
      var normalizedConfiguredLanguage = NormalizeLanguageCode( configuredLanguage );
      if( !string.IsNullOrWhiteSpace( normalizedConfiguredLanguage ) )
      {
         return normalizedConfiguredLanguage;
      }

      var systemLanguage = ResolveSystemLanguageCode();
      if( !string.IsNullOrWhiteSpace( systemLanguage ) )
      {
         return systemLanguage;
      }

      return DefaultLanguage;
   }

   public static string ResolveSystemLanguageCode()
   {
      return GetLanguageCodeFromCulture( CultureInfo.CurrentUICulture )
         ?? GetLanguageCodeFromCulture( CultureInfo.InstalledUICulture )
         ?? GetLanguageCodeFromCulture( CultureInfo.CurrentCulture )
         ?? DefaultLanguage;
   }

   public static string NormalizeLanguageCode( string languageCode )
   {
      if( string.IsNullOrWhiteSpace( languageCode ) ) return string.Empty;

      var normalized = languageCode.Trim().Replace( '_', '-' );
      if( normalized.Length == 0 ) return string.Empty;

      if( normalized.Equals( "romaji", System.StringComparison.OrdinalIgnoreCase ) )
      {
         return "romaji";
      }

      if( normalized.Equals( "ja", System.StringComparison.OrdinalIgnoreCase )
         || normalized.StartsWith( "ja-", System.StringComparison.OrdinalIgnoreCase )
         || normalized.Equals( "jp", System.StringComparison.OrdinalIgnoreCase )
         || normalized.StartsWith( "jp-", System.StringComparison.OrdinalIgnoreCase ) )
      {
         return "jp";
      }

      if( normalized.StartsWith( "zh", System.StringComparison.OrdinalIgnoreCase ) )
      {
         if( normalized.Equals( "zh-TW", System.StringComparison.OrdinalIgnoreCase )
            || normalized.Equals( "zh-HK", System.StringComparison.OrdinalIgnoreCase )
            || normalized.Equals( "zh-MO", System.StringComparison.OrdinalIgnoreCase )
            || normalized.StartsWith( "zh-Hant", System.StringComparison.OrdinalIgnoreCase ) )
         {
            return "zh-TW";
         }

         return "zh";
      }

      if( normalized.Equals( "pt-BR", System.StringComparison.OrdinalIgnoreCase )
         || normalized.StartsWith( "pt-BR-", System.StringComparison.OrdinalIgnoreCase ) )
      {
         return "pt-BR";
      }

      if( normalized.StartsWith( "pt-", System.StringComparison.OrdinalIgnoreCase ) )
      {
         return "pt";
      }

      if( normalized.Equals( "es-419", System.StringComparison.OrdinalIgnoreCase ) )
      {
         return "es-419";
      }

      if( normalized.StartsWith( "es-", System.StringComparison.OrdinalIgnoreCase ) )
      {
         return normalized.Equals( "es-ES", System.StringComparison.OrdinalIgnoreCase )
            ? "es"
            : "es-419";
      }

      var separatorIndex = normalized.IndexOf( '-' );
      if( separatorIndex > 0 )
      {
         var neutralLanguage = normalized.Substring( 0, separatorIndex );
         if( neutralLanguage.Length == 2 )
         {
            return neutralLanguage.ToLowerInvariant();
         }
      }

      if( normalized.Length == 2 )
      {
         return normalized.ToLowerInvariant();
      }

      return normalized;
   }

   public static string GetDefaultGameRootPath( string executableDirectoryPath )
   {
      var normalizedPath = Path.GetFullPath( executableDirectoryPath );
      var trimmedPath = normalizedPath.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar );
      var directoryName = Path.GetFileName( trimmedPath );

      if( string.Equals( directoryName, ToolDirectoryName, System.StringComparison.OrdinalIgnoreCase ) )
      {
         return Path.GetFullPath( Path.Combine( trimmedPath, ".." ) );
      }

      return normalizedPath;
   }

   public static string GetToolDirectoryPath( string gameRootPath )
   {
      return Path.Combine( gameRootPath, ToolDirectoryName );
   }

   public static string GetGameExecutablePath( string gameRootPath )
   {
      return Path.Combine( gameRootPath, GameExecutableName );
   }

   public static string GetPluginConfigurationPath( string gameRootPath )
   {
      return Path.Combine( gameRootPath, "BepInEx", "config", ToolExecutableName + ".cfg" );
   }

   public static string GetDefaultWorkspacePath( string gameRootPath )
   {
      return Path.Combine( GetToolDirectoryPath( gameRootPath ), WorkspaceDirectoryName );
   }

   public static string GetWorkspaceDatabasePath( string gameRootPath )
   {
      return Path.Combine( GetDefaultWorkspacePath( gameRootPath ), WorkspaceDatabaseFileName );
   }

   public static string GetModsRootPath( string gameRootPath )
   {
      return Path.Combine( gameRootPath, "Ostranauts_Data", "Mods" );
   }

   public static string GetModDirectoryPath( string gameRootPath )
   {
      return Path.Combine( GetModsRootPath( gameRootPath ), ModId );
   }

   public static string GetRuntimeTranslationsDirectoryPath( string gameRootPath )
   {
      return Path.Combine( GetModDirectoryPath( gameRootPath ), RuntimeTranslationsDirectoryName );
   }

   public static string GetRuntimeDatabasePath( string gameRootPath, string language )
   {
      return Path.Combine( GetRuntimeTranslationsDirectoryPath( gameRootPath ), RuntimeDatabaseFileName );
   }

   public static string GetPluginRelativeRuntimeDatabasePathTemplate()
   {
      return Path.Combine( "..", "Ostranauts_Data", "Mods", ModId, RuntimeTranslationsDirectoryName, RuntimeDatabaseFileName );
   }

   public static string GetPluginRelativeWorkspaceDatabasePathTemplate()
   {
      return Path.Combine( "..", ToolDirectoryName, WorkspaceDirectoryName, WorkspaceDatabaseFileName );
   }

   public static string GetTranslationTableName( string language )
   {
      var normalizedLanguage = ResolveTargetLanguage( language );
      var builder = new StringBuilder( normalizedLanguage.Length );
      foreach( var ch in normalizedLanguage )
      {
         builder.Append( char.IsLetterOrDigit( ch ) ? char.ToLowerInvariant( ch ) : '_' );
      }

      return "translate_" + builder;
   }

   private static string GetLanguageCodeFromCulture( CultureInfo culture )
   {
      if( culture == null ) return null;

      var normalizedCultureName = NormalizeLanguageCode( culture.Name );
      if( !string.IsNullOrWhiteSpace( normalizedCultureName ) )
      {
         return normalizedCultureName;
      }

      var twoLetterIsoLanguageName = culture.TwoLetterISOLanguageName;
      if( string.IsNullOrWhiteSpace( twoLetterIsoLanguageName ) || string.Equals( twoLetterIsoLanguageName, "iv", System.StringComparison.OrdinalIgnoreCase ) )
      {
         return null;
      }

      return NormalizeLanguageCode( twoLetterIsoLanguageName );
   }
}
