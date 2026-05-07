using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OstranautsTranslator.Core.Processing;

public static class MalformedRichTextDetector
{
   private const string RichTextTagPattern = @"align|alpha|b|cspace|color|font|i|indent|line-height|line-indent|link|lowercase|margin(?:-left|-right)?|mark|mspace|nobr|noparse|pos|rotate|s|size|smallcaps|space|sprite|sub|sup|u|uppercase|voffset|width";

   private static readonly Regex RichTextTagRegex = new Regex(
      @"<\s*/?\s*(?:" + RichTextTagPattern + @")(?:\s*=[^<>]*)?\s*>",
      RegexOptions.Compiled | RegexOptions.IgnoreCase );

   private static readonly Regex RichTextTagTokenRegex = new Regex(
      @"<\s*(/?)\s*(" + RichTextTagPattern + @")(?:\s*=[^<>]*)?\s*>",
      RegexOptions.Compiled | RegexOptions.IgnoreCase );

   private static readonly Regex BrokenBoundaryRichTextTagRegex = new Regex(
      @"(?:^|[\r\n])\s*<\s*(?:/\s*)?(?:" + RichTextTagPattern + @")(?:\s*=[^<>\r\n]*)?>",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );

   private static readonly Regex BrokenMissingOpenAngleTagRegex = new Regex(
      @"(?:^|[\r\n])\s*(?:/\s*)?(?:" + RichTextTagPattern + @")(?:\s*=[^<>\r\n]*)?>",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );

   private static readonly Regex BrokenTrailingRichTextTagRegex = new Regex(
      @"</\s*(?:" + RichTextTagPattern + @")\s*$",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );

   private static readonly Regex IncompleteOpeningRichTextTagRegex = new Regex(
      @"<\s*/?\s*(?:" + RichTextTagPattern + @")[^>\r\n]*$",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );

   private static readonly Regex BrokenInlineClosingRichTextTagRegex = new Regex(
      @"(?:^|[^<])/(?:" + RichTextTagPattern + @")\s*$",
      RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );

   private static readonly HashSet<string> SelfContainedRichTextTags = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
   {
      "sprite",
      "space",
      "cspace",
      "voffset",
      "pos",
      "width",
      "margin",
      "margin-left",
      "margin-right",
      "indent",
      "line-indent",
      "line-height",
      "rotate",
      "mark",
      "noparse",
      "nobr",
      "mspace",
   };

   public static bool LooksMalformed( string value )
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

      if( BrokenBoundaryRichTextTagRegex.IsMatch( trimmed )
         || BrokenMissingOpenAngleTagRegex.IsMatch( trimmed )
         || BrokenTrailingRichTextTagRegex.IsMatch( trimmed )
         || IncompleteOpeningRichTextTagRegex.IsMatch( trimmed )
         || BrokenInlineClosingRichTextTagRegex.IsMatch( trimmed ) )
      {
         return true;
      }

      if( HasUnbalancedRichTextTags( trimmed ) )
      {
         return true;
      }

      if( trimmed.IndexOf( '<' ) < 0 )
      {
         return false;
      }

      var stripped = RichTextTagRegex.Replace( trimmed, string.Empty ).Trim();
      return stripped.Length == 0 || stripped.StartsWith( "<", StringComparison.Ordinal ) || stripped.EndsWith( ">", StringComparison.Ordinal );
   }

   private static bool HasUnbalancedRichTextTags( string value )
   {
      var stack = new Stack<string>();
      var recognizedTagCount = 0;

      foreach( Match match in RichTextTagTokenRegex.Matches( value ) )
      {
         if( !match.Success ) continue;

         recognizedTagCount++;
         var isClosing = string.Equals( match.Groups[ 1 ].Value, "/", StringComparison.Ordinal );
         var tagName = match.Groups[ 2 ].Value;
         if( tagName.Length == 0 || SelfContainedRichTextTags.Contains( tagName ) )
         {
            continue;
         }

         if( isClosing )
         {
            if( stack.Count == 0 )
            {
               return true;
            }

            var openTagName = stack.Pop();
            if( !string.Equals( openTagName, tagName, StringComparison.OrdinalIgnoreCase ) )
            {
               return true;
            }

            continue;
         }

         stack.Push( tagName );
      }

      return recognizedTagCount > 0 && stack.Count > 0;
   }
}