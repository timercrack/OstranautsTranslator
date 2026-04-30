using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OstranautsTranslator.Core.Processing;

public static class RuntimeTextProjector
{
   public static RuntimeTextProjection CreateProjection( string rawText, RuntimeTextProcessingConfiguration configuration )
   {
      var safeRawText = rawText ?? string.Empty;
      var runtimeTemplate = TextTemplate.CreateNumberTemplate( safeRawText, configuration.WhitespaceBetweenWords, configuration.TemplateAllNumbersAway );
      var richTextTemplate = configuration.HandleRichText ? TextTemplate.CreateRichTextTemplate( safeRawText ) : null;

      var renderKey = richTextTemplate != null
         ? richTextTemplate.TemplateText
         : string.IsNullOrWhiteSpace( runtimeTemplate.TrimmedTemplateText )
            ? runtimeTemplate.NormalizedText
            : runtimeTemplate.TrimmedTemplateText;

      var textKind = richTextTemplate != null
         ? "rich-text"
         : runtimeTemplate.HasTokens
            ? "templated"
            : "plain";

      return new RuntimeTextProjection(
         safeRawText,
         runtimeTemplate,
         richTextTemplate,
         runtimeTemplate.TemplateText,
         runtimeTemplate.NormalizedText,
         runtimeTemplate.TrimmedTemplateText,
         renderKey,
         textKind );
   }
}

public sealed class RuntimeTextProjection
{
   public RuntimeTextProjection(
      string rawText,
      TextTemplate runtimeTemplate,
      TextTemplate richTextTemplate,
      string runtimeKey,
      string normalizedText,
      string templatedText,
      string renderKey,
      string textKind )
   {
      RawText = rawText;
      RuntimeTemplate = runtimeTemplate;
      RichTextTemplate = richTextTemplate;
      RuntimeKey = runtimeKey;
      NormalizedText = normalizedText;
      TemplatedText = templatedText;
      RenderKey = renderKey;
      TextKind = textKind;
   }

   public string RawText { get; }

   public TextTemplate RuntimeTemplate { get; }

   public TextTemplate RichTextTemplate { get; }

   public string RuntimeKey { get; }

   public string NormalizedText { get; }

   public string TemplatedText { get; }

   public string RenderKey { get; }

   public string TextKind { get; }
}

public sealed class TextTemplate
{
   private static readonly Regex NumberLikeRegex = new Regex( @"(?<!\{)\b[0-9０-９][0-9０-９.,:%+\-xX/]*\b", RegexOptions.Compiled );
   private static readonly Regex RichTextRegex = new Regex( "(<.*?>)", RegexOptions.Compiled );

   private TextTemplate( string templateText, string trimmedTemplateText, string normalizedText, IReadOnlyDictionary<string, string> tokens )
   {
      TemplateText = templateText;
      TrimmedTemplateText = trimmedTemplateText;
      NormalizedText = normalizedText;
      Tokens = tokens;
   }

   public string TemplateText { get; }

   public string TrimmedTemplateText { get; }

   public string NormalizedText { get; }

   public IReadOnlyDictionary<string, string> Tokens { get; }

   public bool HasTokens => Tokens.Count > 0;

   public static TextTemplate CreateNumberTemplate( string rawText, bool whitespaceBetweenWords, bool templateAllNumbersAway )
   {
      var text = rawText ?? string.Empty;
      var tokens = new Dictionary<string, string>( StringComparer.Ordinal );
      var templateText = text;

      if( templateAllNumbersAway )
      {
         var counter = 'A';
         templateText = NumberLikeRegex.Replace( text, match =>
         {
            var token = "{{" + counter + "}}";
            counter++;
            if( !tokens.ContainsKey( token ) )
            {
               tokens[ token ] = match.Value;
            }
            return token;
         } );
      }

      var normalizedText = NormalizeInternalWhitespace( text.Trim(), whitespaceBetweenWords );
      var trimmedTemplateText = NormalizeInternalWhitespace( templateText.Trim(), whitespaceBetweenWords );
      return new TextTemplate( templateText, trimmedTemplateText, normalizedText, tokens );
   }

   public static TextTemplate CreateRichTextTemplate( string rawText )
   {
      var text = rawText ?? string.Empty;
      if( text.IndexOf( '<' ) < 0 || text.IndexOf( '>' ) < 0 ) return null;

      var parts = RichTextRegex.Split( text );
      var tokens = new Dictionary<string, string>( StringComparer.Ordinal );
      var builder = new StringBuilder( text.Length );
      var counter = 'A';
      var hasTag = false;

      foreach( var part in parts )
      {
         if( string.IsNullOrEmpty( part ) ) continue;

         var isTag = part.Length >= 3 && part[ 0 ] == '<' && part[ part.Length - 1 ] == '>';
         if( isTag )
         {
            hasTag = true;
            builder.Append( part );
            continue;
         }

         var token = "[[" + counter + "]]";
         counter++;
         tokens[ token ] = part;
         builder.Append( token );
      }

      if( !hasTag || tokens.Count == 0 ) return null;

      var templateText = builder.ToString();
      return new TextTemplate( templateText, templateText, text.Trim(), tokens );
   }

   public string Apply( string translatedText )
   {
      var result = translatedText ?? string.Empty;
      foreach( var pair in Tokens )
      {
         var token = pair.Key;
         var value = pair.Value;
         result = result.Replace( token, value );

         var translatorFriendlyToken = CreateTranslatorFriendlyKey( token );
         result = ReplaceApproximateMatches( result, translatorFriendlyToken, value );
      }

      return result;
   }

   private static string NormalizeInternalWhitespace( string value, bool whitespaceBetweenWords )
   {
      if( string.IsNullOrEmpty( value ) ) return string.Empty;

      var builder = new StringBuilder( value.Length );
      var inWhitespace = false;

      for( var i = 0; i < value.Length; i++ )
      {
         var ch = value[ i ];
         if( char.IsWhiteSpace( ch ) )
         {
            if( !inWhitespace && whitespaceBetweenWords )
            {
               builder.Append( ' ' );
            }
            else if( !whitespaceBetweenWords )
            {
               builder.Append( ch );
            }

            inWhitespace = true;
            continue;
         }

         builder.Append( ch );
         inWhitespace = false;
      }

      return whitespaceBetweenWords ? builder.ToString().Trim() : builder.ToString();
   }

   private static string CreateTranslatorFriendlyKey( string token )
   {
      if( string.IsNullOrEmpty( token ) || token.Length < 3 ) return token;
      var marker = token[ 2 ];
      return "ZM" + (char)( marker + 2 ) + "Z";
   }

   private static string ReplaceApproximateMatches( string text, string source, string replacement )
   {
      if( string.IsNullOrEmpty( text ) || string.IsNullOrEmpty( source ) ) return text;

      var maxIndex = source.Length - 1;
      var currentIndex = maxIndex;
      var endIndex = maxIndex;

      for( var i = text.Length - 1; i >= 0; i-- )
      {
         var ch = text[ i ];
         if( ch == ' ' || ch == '　' ) continue;

         var normalized = char.ToUpperInvariant( ch );
         if( normalized == char.ToUpperInvariant( source[ currentIndex ] )
            || normalized == char.ToUpperInvariant( source[ currentIndex = maxIndex ] ) )
         {
            if( currentIndex == maxIndex ) endIndex = i;
            currentIndex--;
         }

         if( currentIndex >= 0 ) continue;

         var length = ( endIndex + 1 ) - i;
         text = text.Remove( i, length ).Insert( i, replacement );
         currentIndex = maxIndex;
      }

      return text;
   }
}
