using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OstranautsTranslator.Core.Processing;

public static class RuntimeVolatileTextDetector
{
   private static readonly Regex VolatileTimestampRegex = new Regex( @"^(?:UTC\s+)?(?:\d{4}-\d{2}-\d{2}\s+)?\d{2}:\d{2}:\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex PlaceholderTimestampRegex = new Regex( @"^(?:UTC\s+)?[-0-9: ]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex MultiplierRegex = new Regex( @"^x\d+(?:[.,]\d+)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex NumericValueRegex = new Regex( @"^[\$€¥£]?-?\d+(?:[.,]\d+)*%?$", RegexOptions.Compiled );
   private static readonly Regex NumericUnitRegex = new Regex( @"^[\$€¥£]?-?\d+(?:[.,]\d+)?\s*(?:kPa|Pa|°C|C|km|m|s|km/s|m/s|kg|g|kWh|Wh|kW|W|MW|au|h|m|ms)$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex DimensionRegex = new Regex( @"^\d+\s*[xX]\s*\d+$", RegexOptions.Compiled );
   private static readonly Regex SaveNameRegex = new Regex( @"^(?:(?:auto|quick)save(?:_\d+)?_.+|[a-z]+(?: [a-z]+){1,4}_\d{9,})$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex RuntimeCodeRegex = new Regex( @"^(?:[A-Za-z]{1,4}\s+)?[A-Za-z]{1,4}-\d{1,4}[A-Za-z]?$", RegexOptions.Compiled );
   private static readonly Regex TagPlaceholderRegex = new Regex( @"^\s*<(?:hash|equals|sel|size|color|alpha|align|sprite|b|s)[^>]*>\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex FreeSpacePathRegex = new Regex( @"^[A-Za-z]:[\\/].+\|\s*\d+(?:[.,]\d+)?\s*GB\s+free$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex MeasurementSignalRegex = new Regex( @"(?:\d+[.,]\d+|\d{2,}|[%°]|\b(?:kPa|Pa|km/s|m/s|km|kg|g|kWh|Wh|kW|W|MW|au|fps|eta|rng|brg|vcrs|vrel|opt|signal|temp)\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex WordTokenRegex = new Regex( @"[A-Za-z]+", RegexOptions.Compiled );
   private static readonly HashSet<string> TelemetryTokens = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
   {
      "a",
      "au",
      "brg",
      "c",
      "co2",
      "dist",
      "eta",
      "fps",
      "g",
      "kg",
      "km",
      "kpa",
      "kwh",
      "kw",
      "m",
      "mass",
      "ms",
      "mw",
      "n2",
      "o2",
      "opt",
      "pa",
      "quality",
      "range",
      "rng",
      "s",
      "signal",
      "temp",
      "temperature",
      "up",
      "v",
      "vcrs",
      "vrel",
      "w",
      "wh"
   };
   private static readonly HashSet<string> KnownRichTextTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
   {
      "align",
      "alpha",
      "b",
      "br",
      "color",
      "cspace",
      "font",
      "gradient",
      "i",
      "indent",
      "line-height",
      "line-indent",
      "link",
      "lowercase",
      "margin",
      "mark",
      "material",
      "mspace",
      "nobr",
      "noparse",
      "page",
      "pos",
      "rotate",
      "s",
      "size",
      "smallcaps",
      "space",
      "sprite",
      "strikethrough",
      "style",
      "sub",
      "sup",
      "u",
      "uppercase",
      "voffset",
      "width"
   };

   public static bool LooksVolatile( string value, RuntimeTextProcessingConfiguration configuration = null )
   {
      return LooksVolatile( value, configuration?.HandleRichText ?? RuntimeTextProcessingConfiguration.Default.HandleRichText );
   }

   public static bool LooksVolatile( string value, bool handleRichText )
   {
      if( string.IsNullOrWhiteSpace( value ) )
      {
         return true;
      }

      var trimmed = value.Trim();
      if( IsRuntimeArtifact( trimmed ) )
      {
         return true;
      }

      if( IsSimpleVolatileValue( trimmed ) )
      {
         return true;
      }

      var sanitized = NormalizeCandidate( trimmed, handleRichText );
      if( sanitized.Length == 0 )
      {
         return true;
      }

      if( IsSimpleVolatileValue( sanitized ) )
      {
         return true;
      }

      if( IsRuntimeArtifact( sanitized ) )
      {
         return true;
      }

      var hasDigits = sanitized.Any( char.IsDigit );
      if( !sanitized.Any( char.IsLetter ) )
      {
         return true;
      }

      if( !hasDigits )
      {
         return false;
      }

      var lines = sanitized
         .Split( '\n' )
         .Select( NormalizeLine )
         .Where( line => line.Length > 0 )
         .ToList();
      if( lines.Count == 0 )
      {
         return true;
      }

      if( lines.All( IsNumericTelemetryLine ) )
      {
         return true;
      }

      var wordTokens = ExtractWordTokens( sanitized );
      if( wordTokens.Count == 0 )
      {
         return true;
      }

      if( wordTokens.All( IsTelemetryToken ) )
      {
         return true;
      }

      var descriptiveTelemetryLabelCount = lines.Count( IsDescriptiveTelemetryLabelLine );
      if( descriptiveTelemetryLabelCount == 1
         && lines.Count > 1
         && lines.All( line => IsDescriptiveTelemetryLabelLine( line ) || IsNumericTelemetryLine( line ) ) )
      {
         return true;
      }

      return false;
   }

   private static bool IsRuntimeArtifact( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) )
      {
         return true;
      }

      var trimmed = value.Trim();
      if( trimmed.Length == 0 )
      {
         return true;
      }

      return LooksLikeFilesystemPath( trimmed )
         || SaveNameRegex.IsMatch( trimmed )
         || RuntimeCodeRegex.IsMatch( trimmed )
         || TagPlaceholderRegex.IsMatch( trimmed );
   }

   private static bool LooksLikeFilesystemPath( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) )
      {
         return false;
      }

      if( FreeSpacePathRegex.IsMatch( value ) )
      {
         return true;
      }

      if( value.IndexOf( "AppData", StringComparison.OrdinalIgnoreCase ) >= 0
         || value.IndexOf( "Program Files", StringComparison.OrdinalIgnoreCase ) >= 0
         || value.IndexOf( "Blue Bottle Games", StringComparison.OrdinalIgnoreCase ) >= 0
         || value.IndexOf( "loading_order.json", StringComparison.OrdinalIgnoreCase ) >= 0 )
      {
         return value.IndexOf( '\\' ) >= 0 || value.IndexOf( '/' ) >= 0;
      }

      return false;
   }

   private static bool IsSimpleVolatileValue( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return true;

      var trimmed = TrimBoundaryDecorators( value.Trim() );
      if( trimmed.Length == 0 ) return true;

      return VolatileTimestampRegex.IsMatch( trimmed )
         || ( PlaceholderTimestampRegex.IsMatch( trimmed ) && trimmed.IndexOf( ':' ) >= 0 )
         || MultiplierRegex.IsMatch( trimmed )
         || NumericValueRegex.IsMatch( trimmed )
         || NumericUnitRegex.IsMatch( trimmed )
         || DimensionRegex.IsMatch( trimmed );
   }

   private static string NormalizeCandidate( string value, bool handleRichText )
   {
      var normalized = value.Replace( "\r\n", "\n" ).Replace( '\r', '\n' );
      if( handleRichText && normalized.IndexOf( '<' ) >= 0 && normalized.IndexOf( '>' ) >= 0 )
      {
         normalized = StripRecognizedRichTextTags( normalized );
      }

      var builder = new StringBuilder( normalized.Length );
      var previousWasSpace = false;
      foreach( var ch in normalized )
      {
         if( ch == '\n' )
         {
            builder.Append( '\n' );
            previousWasSpace = false;
            continue;
         }

         if( char.IsWhiteSpace( ch ) )
         {
            if( previousWasSpace )
            {
               continue;
            }

            builder.Append( ' ' );
            previousWasSpace = true;
            continue;
         }

         builder.Append( ch );
         previousWasSpace = false;
      }

      return builder.ToString().Trim();
   }

   private static string StripRecognizedRichTextTags( string value )
   {
      var builder = new StringBuilder( value.Length );

      for( var index = 0; index < value.Length; index++ )
      {
         var ch = value[ index ];
         if( ch != '<' )
         {
            builder.Append( ch );
            continue;
         }

         var closingIndex = value.IndexOf( '>', index + 1 );
         if( closingIndex < 0 )
         {
            builder.Append( ch );
            continue;
         }

         var candidate = value.Substring( index + 1, closingIndex - index - 1 );
         if( !IsRecognizedRichTextTag( candidate ) )
         {
            builder.Append( ch );
            continue;
         }

         index = closingIndex;
      }

      return builder.ToString();
   }

   private static bool IsRecognizedRichTextTag( string candidate )
   {
      if( string.IsNullOrWhiteSpace( candidate ) )
      {
         return false;
      }

      var trimmed = candidate.Trim();
      if( trimmed.Length == 0 )
      {
         return false;
      }

      if( trimmed[ 0 ] == '/' )
      {
         trimmed = trimmed.Substring( 1 ).TrimStart();
         if( trimmed.Length == 0 )
         {
            return false;
         }
      }

      if( trimmed[ 0 ] == '#' )
      {
         return true;
      }

      var endIndex = 0;
      while( endIndex < trimmed.Length )
      {
         var current = trimmed[ endIndex ];
         if( char.IsWhiteSpace( current ) || current == '=' || current == '/' )
         {
            break;
         }

         endIndex++;
      }

      if( endIndex == 0 )
      {
         return false;
      }

      var tagName = trimmed.Substring( 0, endIndex );
      return KnownRichTextTags.Contains( tagName );
   }

   private static string NormalizeLine( string value )
   {
      return TrimBoundaryDecorators( value ).Trim();
   }

   private static bool IsNumericTelemetryLine( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) )
      {
         return true;
      }

      var trimmed = NormalizeLine( value );
      if( trimmed.Length == 0 )
      {
         return true;
      }

      if( IsSimpleVolatileValue( trimmed ) )
      {
         return true;
      }

      var colonIndex = trimmed.IndexOf( ':' );
      if( colonIndex > 0 && colonIndex < trimmed.Length - 1 )
      {
         var label = NormalizeLine( trimmed.Substring( 0, colonIndex ) );
         var remainder = NormalizeLine( trimmed.Substring( colonIndex + 1 ) );
         if( remainder.Length > 0 && IsNumericTelemetryLine( remainder ) )
         {
            var labelTokens = ExtractWordTokens( label );
            return labelTokens.Count > 0 && labelTokens.All( IsTelemetryToken );
         }
      }

      var wordTokens = ExtractWordTokens( trimmed );
      return HasMeasurementSignal( trimmed )
         && wordTokens.Count > 0
         && wordTokens.All( IsTelemetryToken );
   }

   private static bool IsDescriptiveTelemetryLabelLine( string value )
   {
      var trimmed = NormalizeLine( value );
      if( trimmed.Length == 0 || trimmed.Any( char.IsDigit ) || !trimmed.EndsWith( ":", StringComparison.Ordinal ) )
      {
         return false;
      }

      var labelTokens = ExtractWordTokens( trimmed );
      return labelTokens.Count > 0 && labelTokens.All( IsTelemetryToken );
   }

   private static List<string> ExtractWordTokens( string value )
   {
      return WordTokenRegex.Matches( value )
         .Cast<Match>()
         .Select( match => match.Value )
         .Where( token => !string.IsNullOrWhiteSpace( token ) )
         .ToList();
   }

   private static bool HasMeasurementSignal( string value )
   {
      return !string.IsNullOrWhiteSpace( value ) && MeasurementSignalRegex.IsMatch( value );
   }

   private static bool IsTelemetryToken( string token )
   {
      if( string.IsNullOrWhiteSpace( token ) )
      {
         return false;
      }

      return token.Length <= 2 || TelemetryTokens.Contains( token );
   }

   private static string TrimBoundaryDecorators( string value )
   {
      var start = 0;
      var end = value.Length - 1;

      while( start <= end && IsBoundaryDecorator( value[ start ] ) )
      {
         start++;
      }

      while( end >= start && IsBoundaryDecorator( value[ end ] ) )
      {
         end--;
      }

      return start > end ? string.Empty : value.Substring( start, end - start + 1 );
   }

   private static bool IsBoundaryDecorator( char ch )
   {
      switch( ch )
      {
         case '<':
         case '>':
         case '[':
         case ']':
         case '(':
         case ')':
         case '{':
         case '}':
         case '|':
         case '=':
         case '+':
         case '-':
         case '~':
         case '*':
         case '#':
         case '•':
         case '·':
         case '→':
         case '←':
         case '↑':
         case '↓':
         case '«':
         case '»':
            return true;
         default:
            return char.IsWhiteSpace( ch );
      }
   }
}