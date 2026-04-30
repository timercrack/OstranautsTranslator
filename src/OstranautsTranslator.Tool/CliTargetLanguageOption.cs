namespace OstranautsTranslator.Tool;

internal static class CliTargetLanguageOption
{
   public const string ShortName = "-t";

   public static string? ParseOptionalValue( IReadOnlyList<string> args )
   {
      string? toLanguage = null;

      for( int i = 0; i < args.Count; i++ )
      {
         var argument = args[ i ];
         switch( argument )
         {
            case ShortName:
               toLanguage = ReadRequiredValue( args, ref i, argument );
               break;
            default:
               if( TryReadInlineValue( argument, out var inlineValue ) )
               {
                  toLanguage = inlineValue;
                  break;
               }

               throw new ArgumentException( $"Unknown option '{argument}'." );
         }
      }

      return string.IsNullOrWhiteSpace( toLanguage ) ? null : toLanguage;
   }

   private static string ReadRequiredValue( IReadOnlyList<string> args, ref int index, string optionName )
   {
      var valueIndex = index + 1;
      if( valueIndex >= args.Count )
      {
         throw new ArgumentException( $"Option '{optionName}' requires a value." );
      }

      index = valueIndex;
      return args[ valueIndex ];
   }

   private static bool TryReadInlineValue( string argument, out string value )
   {
      value = string.Empty;

      if( argument.StartsWith( ShortName + "=", StringComparison.Ordinal ) )
      {
         value = argument.Substring( ShortName.Length + 1 );
         if( string.IsNullOrWhiteSpace( value ) )
         {
            throw new ArgumentException( $"Option '{ShortName}' requires a value." );
         }

         return true;
      }

      if( argument.StartsWith( ShortName, StringComparison.Ordinal ) && argument.Length > ShortName.Length )
      {
         value = argument.Substring( ShortName.Length );
         if( string.IsNullOrWhiteSpace( value ) )
         {
            throw new ArgumentException( $"Option '{ShortName}' requires a value." );
         }

         return true;
      }

      return false;
   }
}