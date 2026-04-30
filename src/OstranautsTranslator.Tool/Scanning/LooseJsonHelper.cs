using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OstranautsTranslator.Tool.Scanning;

internal static class LooseJsonHelper
{
   private static readonly JsonDocumentOptions JsonOptions = new()
   {
      AllowTrailingCommas = true,
      CommentHandling = JsonCommentHandling.Skip,
   };

   public static JsonDocument ParseDocumentFromFile( string filePath )
   {
      return ParseDocument( File.ReadAllText( filePath ) );
   }

   public static JsonDocument ParseDocument( string content )
   {
      try
      {
         return JsonDocument.Parse( content, JsonOptions );
      }
      catch( JsonException )
      {
         return JsonDocument.Parse( NormalizeLooseJson( content ), JsonOptions );
      }
   }

   public static JsonNode ParseNodeFromFile( string filePath )
   {
      return ParseNode( File.ReadAllText( filePath ) )
         ?? throw new JsonException( $"Parsed JSON root node was null for '{filePath}'." );
   }

   public static JsonNode? ParseNode( string content )
   {
      try
      {
         return JsonNode.Parse( content, nodeOptions: null, documentOptions: JsonOptions );
      }
      catch( JsonException )
      {
         return JsonNode.Parse( NormalizeLooseJson( content ), nodeOptions: null, documentOptions: JsonOptions );
      }
   }

   public static string NormalizeLooseJson( string content )
   {
      var builder = new StringBuilder( content.Length + 128 );
      var inString = false;
      var escaped = false;

      for( var i = 0; i < content.Length; i++ )
      {
         var ch = content[ i ];
         if( !inString )
         {
            builder.Append( ch );
            if( ch == '"' )
            {
               inString = true;
            }

            continue;
         }

         if( escaped )
         {
            builder.Append( ch );
            escaped = false;
            continue;
         }

         if( ch == '\\' )
         {
            if( i + 1 < content.Length && content[ i + 1 ] == '\'' )
            {
               builder.Append( '\'' );
               i++;
               continue;
            }

            builder.Append( ch );
            escaped = true;
            continue;
         }

         if( ch == '"' )
         {
            builder.Append( ch );
            inString = false;
            continue;
         }

         if( ch == '\r' )
         {
            builder.Append( "\\n" );
            if( i + 1 < content.Length && content[ i + 1 ] == '\n' )
            {
               i++;
            }

            continue;
         }

         if( ch == '\n' )
         {
            builder.Append( "\\n" );
            continue;
         }

         if( ch == '\t' )
         {
            builder.Append( "\\t" );
            continue;
         }

         builder.Append( ch );
      }

      return builder.ToString();
   }
}
