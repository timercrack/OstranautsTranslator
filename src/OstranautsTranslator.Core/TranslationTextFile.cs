using System;
using System.Globalization;
using System.Text;

namespace OstranautsTranslator.Core;

public static class TranslationTextFile
{
   public static string Encode( string text )
   {
      if( string.IsNullOrEmpty( text ) ) return string.Empty;

      var builder = new StringBuilder( text.Length + 4 );
      for( var i = 0; i < text.Length; i++ )
      {
         var ch = text[ i ];
         switch( ch )
         {
            case '/':
               if( i + 1 < text.Length && text[ i + 1 ] == '/' )
               {
                  builder.Append( "\\/\\/" );
                  i++;
               }
               else
               {
                  builder.Append( ch );
               }
               break;
            case '\\':
               builder.Append( "\\\\" );
               break;
            case '=':
               builder.Append( "\\=" );
               break;
            case '\n':
               builder.Append( "\\n" );
               break;
            case '\r':
               builder.Append( "\\r" );
               break;
            default:
               builder.Append( ch );
               break;
         }
      }

      return builder.ToString();
   }

   public static string[] ReadTranslationLineAndDecode( string value )
   {
      if( string.IsNullOrEmpty( value ) ) return null;

      var parts = new string[ 2 ];
      var partIndex = 0;
      var escapeNext = false;
      var builder = new StringBuilder( value.Length );

      for( var i = 0; i < value.Length; i++ )
      {
         var ch = value[ i ];
         if( escapeNext )
         {
            switch( ch )
            {
               case '=':
               case '\\':
                  builder.Append( ch );
                  break;
               case 'n':
                  builder.Append( '\n' );
                  break;
               case 'r':
                  builder.Append( '\r' );
                  break;
               case 'u':
                  if( i + 4 >= value.Length ) throw new InvalidOperationException( "Invalid unicode escape in translation line." );
                  var code = int.Parse( value.Substring( i + 1, 4 ), NumberStyles.HexNumber, CultureInfo.InvariantCulture );
                  builder.Append( (char)code );
                  i += 4;
                  break;
               default:
                  builder.Append( '\\' ).Append( ch );
                  break;
            }

            escapeNext = false;
            continue;
         }

         if( ch == '\\' )
         {
            escapeNext = true;
            continue;
         }

         if( ch == '=' )
         {
            if( partIndex > 0 ) return null;
            parts[ partIndex++ ] = builder.ToString();
            builder.Length = 0;
            continue;
         }

         if( ch == '/' && i + 1 < value.Length && value[ i + 1 ] == '/' )
         {
            parts[ partIndex++ ] = builder.ToString();
            return partIndex == 2 ? parts : null;
         }

         builder.Append( ch );
      }

      if( partIndex != 1 ) return null;

      parts[ 1 ] = builder.ToString();
      return parts;
   }
}
