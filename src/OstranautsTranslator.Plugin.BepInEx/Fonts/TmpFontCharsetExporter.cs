using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx.Fonts;

internal static class TmpFontCharsetExporter
{
   private const string LegacyOutputFilePrefix = "loaded-tmp-font.";
   private const string OutputMergedFileName = "loaded-tmp-fonts.merged.txt";
   private const string OutputReportFileName = "loaded-tmp-fonts.report.txt";
   private static readonly HashSet<string> IgnoredFontKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
   private static readonly Dictionary<string, ObservedFontRecord> Records = new Dictionary<string, ObservedFontRecord>( StringComparer.OrdinalIgnoreCase );

   private static ManualLogSource _logger;
   private static string _outputDirectory;
   private static bool _mergeIntoAllFile;
   private static bool _enabled;
   private static bool _pendingFlush;
   private static DateTime _nextFlushUtc = DateTime.MinValue;

   private sealed class FontDescription
   {
      public FontDescription( string key, string displayName, string sourceFontFileName, string atlasTextureNames )
      {
         Key = key;
         DisplayName = displayName;
         SourceFontFileName = sourceFontFileName ?? string.Empty;
         AtlasTextureNames = atlasTextureNames ?? string.Empty;
      }

      public string Key { get; }
      public string DisplayName { get; }
      public string SourceFontFileName { get; }
      public string AtlasTextureNames { get; }
   }

   private sealed class ObservedFontRecord
   {
      public ObservedFontRecord( FontDescription description, string characters )
      {
         Key = description.Key;
         DisplayName = description.DisplayName;
         SourceFontFileName = description.SourceFontFileName;
         AtlasTextureNames = description.AtlasTextureNames;
         Characters = characters ?? string.Empty;
      }

      public string Key { get; }
      public string DisplayName { get; }
      public string SourceFontFileName { get; }
      public string AtlasTextureNames { get; }
      public string Characters { get; private set; }
      public HashSet<string> ObservedFrom { get; } = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

      public bool UpdateCharacters( string characters )
      {
         var normalized = characters ?? string.Empty;
         if( string.Equals( Characters, normalized, StringComparison.Ordinal ) ) return false;

         Characters = normalized;
         return true;
      }
   }

   public static void Initialize( ManualLogSource logger, string outputDirectory, bool mergeIntoAllFile )
   {
      _logger = logger;
      _mergeIntoAllFile = mergeIntoAllFile;
      _outputDirectory = ResolveOutputDirectory( outputDirectory );
      _enabled = !string.IsNullOrWhiteSpace( _outputDirectory );
      _pendingFlush = false;
      _nextFlushUtc = DateTime.MinValue;
      IgnoredFontKeys.Clear();
      Records.Clear();

      if( !_enabled )
      {
         _logger.LogInfo( "TMP font charset exporter disabled because no output directory is configured." );
         return;
      }

      Directory.CreateDirectory( _outputDirectory );
      _logger.LogInfo( $"TMP font charset exporter initialized: '{_outputDirectory}'." );
   }

   public static void ObserveFont( object fontAsset, string observedFrom )
   {
      if( !_enabled || fontAsset == null ) return;

      try
      {
         var description = DescribeFont( fontAsset );
         if( description == null || IgnoredFontKeys.Contains( description.Key ) ) return;

         var characters = ExtractCharacters( fontAsset );
         if( characters.Length == 0 ) return;

         if( !Records.TryGetValue( description.Key, out var record ) )
         {
            record = new ObservedFontRecord( description, characters );
            Records.Add( record.Key, record );
            ScheduleFlush();
         }
         else if( record.UpdateCharacters( characters ) )
         {
            ScheduleFlush();
         }

         if( !string.IsNullOrWhiteSpace( observedFrom ) && record.ObservedFrom.Add( observedFrom.Trim() ) )
         {
            ScheduleFlush();
         }
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to observe TMP font charset. {e.Message}" );
      }
   }

   public static void RegisterIgnoredFont( object fontAsset, string reason )
   {
      if( !_enabled || fontAsset == null ) return;

      try
      {
         var description = DescribeFont( fontAsset );
         if( description == null || !IgnoredFontKeys.Add( description.Key ) ) return;

         _logger.LogInfo( $"Ignoring TMP font charset export for '{description.DisplayName}' ({reason ?? "no reason"})." );
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to register ignored TMP font. {e.Message}" );
      }
   }

   public static void ObserveLoadedFontAssetsSnapshot( object fontToExclude )
   {
      if( !_enabled ) return;

      var tmpFontAssetType = Type.GetType( "TMPro.TMP_FontAsset, Unity.TextMeshPro", false );
      if( tmpFontAssetType == null ) return;

      try
      {
         var fontAssets = Resources.FindObjectsOfTypeAll( tmpFontAssetType );
         if( fontAssets == null ) return;

         foreach( var fontAsset in fontAssets )
         {
            if( fontAsset == null ) continue;
            if( fontToExclude != null && ReferenceEquals( fontAsset, fontToExclude ) ) continue;

            ObserveFont( fontAsset, "Resources.FindObjectsOfTypeAll(TMP_FontAsset)" );
         }
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to enumerate loaded TMP font assets. {e.Message}" );
      }
   }

   public static void Tick()
   {
      if( !_enabled || !_pendingFlush || DateTime.UtcNow < _nextFlushUtc ) return;

      Flush();
   }

   private static void Flush()
   {
      _pendingFlush = false;
      if( string.IsNullOrWhiteSpace( _outputDirectory ) || Records.Count == 0 ) return;

      try
      {
         Directory.CreateDirectory( _outputDirectory );

         var combinedCharactersBuilder = new StringBuilder();
         foreach( var record in Records.Values.OrderBy( x => x.DisplayName, StringComparer.OrdinalIgnoreCase ) )
         {
            combinedCharactersBuilder.Append( record.Characters );
         }

         var mergedCharacters = OrderedUnique( SanitizeExportText( combinedCharactersBuilder.ToString() ) );
         File.WriteAllText( Path.Combine( _outputDirectory, OutputMergedFileName ), mergedCharacters, new UTF8Encoding( false ) );

         WriteReportFile( mergedCharacters );

         if( _mergeIntoAllFile )
         {
            MergeIntoAllFile( mergedCharacters );
         }

         DeleteLegacyPerFontFiles();

         _logger.LogInfo( $"Exported {Records.Count} observed TMP font charset(s) into '{OutputMergedFileName}' under '{_outputDirectory}'." );
      }
      catch( Exception e )
      {
         _logger.LogWarning( $"Failed to flush TMP font charset exports. {e}" );
      }
   }

   private static void WriteReportFile( string mergedCharacters )
   {
      var lines = new List<string>
      {
         $"GeneratedAtUtc: {DateTime.UtcNow:O}",
         $"FontCount: {Records.Count}",
         $"MergedFile: {OutputMergedFileName}",
         $"MergedCharCount: {CountUnicodeScalars( mergedCharacters )}",
         string.Empty,
      };

      foreach( var record in Records.Values.OrderBy( x => x.DisplayName, StringComparer.OrdinalIgnoreCase ) )
      {
         lines.Add( record.DisplayName );
         lines.Add( $"  charCount: {CountUnicodeScalars( record.Characters )}" );
         if( !string.IsNullOrWhiteSpace( record.SourceFontFileName ) )
         {
            lines.Add( $"  sourceFontFile: {record.SourceFontFileName}" );
         }

         if( !string.IsNullOrWhiteSpace( record.AtlasTextureNames ) )
         {
            lines.Add( $"  atlasTextures: {record.AtlasTextureNames}" );
         }

         if( record.ObservedFrom.Count > 0 )
         {
            lines.Add( $"  observedFrom: {string.Join( ", ", record.ObservedFrom.OrderBy( x => x, StringComparer.OrdinalIgnoreCase ) )}" );
         }

         lines.Add( string.Empty );
      }

      File.WriteAllLines( Path.Combine( _outputDirectory, OutputReportFileName ), lines, new UTF8Encoding( false ) );
   }

   private static void MergeIntoAllFile( string observedCharacters )
   {
      var allPath = Path.Combine( _outputDirectory, "all.txt" );
      var existing = File.Exists( allPath )
         ? File.ReadAllText( allPath, Encoding.UTF8 )
         : string.Empty;
      var merged = OrderedUnique( SanitizeExportText( existing + observedCharacters ) );
      if( string.Equals( existing, merged, StringComparison.Ordinal ) ) return;

      File.WriteAllText( allPath, merged, new UTF8Encoding( false ) );
   }

   private static void DeleteLegacyPerFontFiles()
   {
      foreach( var file in Directory.GetFiles( _outputDirectory, LegacyOutputFilePrefix + "*.txt" ) )
      {
         var fileName = Path.GetFileName( file );
         if( string.Equals( fileName, OutputMergedFileName, StringComparison.OrdinalIgnoreCase ) ) continue;
         if( string.Equals( fileName, OutputReportFileName, StringComparison.OrdinalIgnoreCase ) ) continue;

         File.Delete( file );
      }
   }

   private static void ScheduleFlush()
   {
      _pendingFlush = true;
      _nextFlushUtc = DateTime.UtcNow.AddSeconds( 3 );
   }

   private static string ResolveOutputDirectory( string configuredPath )
   {
      var normalized = ( configuredPath ?? string.Empty )
         .Trim()
         .Trim( '"' )
         .Replace( '/', '\\' );

      if( string.IsNullOrWhiteSpace( normalized ) ) return null;

      return Path.IsPathRooted( normalized )
         ? Path.GetFullPath( normalized )
         : Path.GetFullPath( Path.Combine( Paths.BepInExRootPath, normalized ) );
   }

   private static FontDescription DescribeFont( object fontAsset )
   {
      var displayName = TryGetObjectName( fontAsset );
      if( string.IsNullOrWhiteSpace( displayName ) )
      {
         displayName = fontAsset.GetType().Name;
      }
      else
      {
         displayName = displayName.Trim();
      }

      var sourceFontFileName = TryGetSourceFontFileName( fontAsset );
      var atlasTextureNames = TryGetAtlasTextureNames( fontAsset );
      var key = string.Join( "|", new[] { displayName, sourceFontFileName ?? string.Empty, atlasTextureNames ?? string.Empty } );
      return new FontDescription(
         key,
         displayName,
         sourceFontFileName,
         atlasTextureNames );
   }

   private static string TryGetObjectName( object value )
   {
      return value == null
         ? string.Empty
         : GetMemberValue( value.GetType(), value, "name" )?.ToString() ?? string.Empty;
   }

   private static string TryGetSourceFontFileName( object fontAsset )
   {
      var creationSettings = GetMemberValue( fontAsset.GetType(), fontAsset, "creationSettings" );
      if( creationSettings == null ) return string.Empty;

      return GetMemberValue( creationSettings.GetType(), creationSettings, "sourceFontFileName" )?.ToString() ?? string.Empty;
   }

   private static string TryGetAtlasTextureNames( object fontAsset )
   {
      var atlasTextures = GetMemberValue( fontAsset.GetType(), fontAsset, "atlasTextures" );
      if( atlasTextures == null ) return string.Empty;

      if( atlasTextures is IEnumerable enumerable )
      {
         var names = new List<string>();
         foreach( var texture in enumerable )
         {
            var textureName = TryGetObjectName( texture );
            if( !string.IsNullOrWhiteSpace( textureName ) )
            {
               names.Add( textureName );
            }
         }

         return string.Join( ", ", names.Distinct( StringComparer.OrdinalIgnoreCase ) );
      }

      return string.Empty;
   }

   private static string ExtractCharacters( object fontAsset )
   {
      var seen = new HashSet<uint>();
      var buffer = new StringBuilder();

      var characterTable = GetMemberValue( fontAsset.GetType(), fontAsset, "characterTable" );
      var appended = TryAppendCharacters( characterTable, seen, buffer );
      if( appended ) return buffer.ToString();

      var characterLookupTable = GetMemberValue( fontAsset.GetType(), fontAsset, "characterLookupTable" );
      TryAppendCharacters( characterLookupTable, seen, buffer );
      return SanitizeExportText( buffer.ToString() );
   }

   private static bool TryAppendCharacters( object collectionObject, HashSet<uint> seen, StringBuilder buffer )
   {
      if( collectionObject == null ) return false;

      var appendedAny = false;
      if( collectionObject is IDictionary dictionary )
      {
         foreach( DictionaryEntry entry in dictionary )
         {
            appendedAny |= TryAppendCharacterEntry( entry.Value, seen, buffer );
         }

         return appendedAny;
      }

      if( collectionObject is IEnumerable enumerable )
      {
         foreach( var entry in enumerable )
         {
            appendedAny |= TryAppendCharacterEntry( entry, seen, buffer );
         }
      }

      return appendedAny;
   }

   private static bool TryAppendCharacterEntry( object entry, HashSet<uint> seen, StringBuilder buffer )
   {
      if( !TryGetUnicode( entry, out var unicode ) ) return false;
      if( unicode == 0 || unicode > 0x10FFFF || !seen.Add( unicode ) ) return false;

      var textElement = char.ConvertFromUtf32( (int)unicode );
      if( !ShouldKeepTextElement( textElement ) ) return false;

      buffer.Append( textElement );
      return true;
   }

   private static bool ShouldKeepTextElement( string textElement )
   {
      if( string.IsNullOrEmpty( textElement ) ) return false;

      var codePoint = char.ConvertToUtf32( textElement, 0 );
      var category = char.GetUnicodeCategory( textElement, 0 );
      switch( category )
      {
         case UnicodeCategory.Control:
         case UnicodeCategory.Format:
         case UnicodeCategory.Surrogate:
         case UnicodeCategory.PrivateUse:
         case UnicodeCategory.OtherNotAssigned:
         case UnicodeCategory.LineSeparator:
         case UnicodeCategory.ParagraphSeparator:
            return false;
         case UnicodeCategory.SpaceSeparator:
            return codePoint == 0x0020 || codePoint == 0x3000;
         default:
            return true;
      }
   }

   private static string SanitizeExportText( string text )
   {
      var source = text ?? string.Empty;
      var elementStarts = StringInfo.ParseCombiningCharacters( source );
      var buffer = new StringBuilder();

      for( var i = 0; i < elementStarts.Length; i++ )
      {
         var start = elementStarts[ i ];
         var nextStart = i + 1 < elementStarts.Length
            ? elementStarts[ i + 1 ]
            : source.Length;
         var element = source.Substring( start, nextStart - start );
         if( ShouldKeepTextElement( element ) )
         {
            buffer.Append( element );
         }
      }

      return buffer.ToString();
   }

   private static bool TryGetUnicode( object entry, out uint unicode )
   {
      unicode = 0;
      if( entry == null ) return false;

      var value = GetMemberValue( entry.GetType(), entry, "unicode" );
      switch( value )
      {
         case uint uintValue when uintValue <= 0x10FFFF:
            unicode = uintValue;
            return true;
         case int intValue when intValue >= 0 && intValue <= 0x10FFFF:
            unicode = (uint)intValue;
            return true;
         case long longValue when longValue >= 0 && longValue <= 0x10FFFF:
            unicode = (uint)longValue;
            return true;
         case ushort ushortValue:
            unicode = ushortValue;
            return true;
         case short shortValue when shortValue >= 0:
            unicode = (uint)shortValue;
            return true;
         case byte byteValue:
            unicode = byteValue;
            return true;
         case string textValue when uint.TryParse( textValue, out var parsed ) && parsed <= 0x10FFFF:
            unicode = parsed;
            return true;
         default:
            return false;
      }
   }

   private static object GetMemberValue( Type type, object instance, string memberName )
   {
      const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

      var property = type.GetProperty( memberName, Flags );
      if( property != null && property.CanRead )
      {
         var getter = property.GetMethod;
         var target = getter != null && getter.IsStatic
            ? null
            : instance;
         return property.GetValue( target, null );
      }

      var field = type.GetField( memberName, Flags );
      if( field != null )
      {
         var target = field.IsStatic
            ? null
            : instance;
         return field.GetValue( target );
      }

      return null;
   }

   private static int CountUnicodeScalars( string text )
   {
      return StringInfo.ParseCombiningCharacters( text ?? string.Empty ).Length;
   }

   private static string OrderedUnique( string text )
   {
      var source = text ?? string.Empty;
      var elementStarts = StringInfo.ParseCombiningCharacters( source );
      var seen = new HashSet<string>( StringComparer.Ordinal );
      var buffer = new StringBuilder();

      for( var i = 0; i < elementStarts.Length; i++ )
      {
         var start = elementStarts[ i ];
         var nextStart = i + 1 < elementStarts.Length
            ? elementStarts[ i + 1 ]
            : source.Length;
         var element = source.Substring( start, nextStart - start );
         if( seen.Add( element ) )
         {
            buffer.Append( element );
         }
      }

      return buffer.ToString();
   }
}