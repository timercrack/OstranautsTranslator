using System.Text.Json;
using System.Text.RegularExpressions;
using OstranautsTranslator.Tool.Scanning;

namespace OstranautsTranslator.Tool;

internal static class BracketTokenPolicies
{
   public const string PreserveSlot = "preserve-slot";
   public const string RewriteGrammar = "rewrite-grammar";
   public const string SkipControl = "skip-control";
   public const string ReviewUnknown = "review-unknown";
}

internal sealed record BracketTokenPolicyMetadata(
   string? TokenPolicy,
   IReadOnlyList<string> TokenExamples,
   bool NeedsManualReview,
   IReadOnlyDictionary<string, string> TokenCorrections )
{
   public static readonly BracketTokenPolicyMetadata None = new( null, Array.Empty<string>(), false, new Dictionary<string, string>( StringComparer.Ordinal ) );

   public bool HasBracketTokens => TokenExamples.Count > 0;

   public bool ShouldSkipAutomaticTranslation =>
      string.Equals( TokenPolicy, BracketTokenPolicies.SkipControl, StringComparison.Ordinal )
      || string.Equals( TokenPolicy, BracketTokenPolicies.ReviewUnknown, StringComparison.Ordinal )
      || NeedsManualReview;

   public string? CreateLlmInstruction()
   {
      var baseInstruction = TokenPolicy switch
      {
         BracketTokenPolicies.PreserveSlot => "Keep every square-bracket token exactly unchanged, including case and punctuation. You may reorder the surrounding sentence naturally, but do not translate, rename, or reformat those bracket tokens.",
         BracketTokenPolicies.RewriteGrammar => "Keep runtime slot/object/name square-bracket tokens unchanged. Rewrite English grammar or verb helper bracket tokens into natural target-language wording instead of outputting translated bracket tokens.",
         BracketTokenPolicies.SkipControl => "This entry is a machine control string rather than player-facing text. Do not translate it.",
         BracketTokenPolicies.ReviewUnknown => "This entry contains unknown square-bracket tokens. Skip automatic translation and review manually.",
         _ => null,
      };

      if( TokenCorrections.Count == 0 )
      {
         return baseInstruction;
      }

      var correctionInstruction = "Known malformed square-bracket tokens in this entry: "
         + string.Join( "; ", TokenCorrections.Select( pair => $"{pair.Key} should be interpreted as {pair.Value}" ) )
         + ". If you keep any bracket token in the output, use the canonical spelling rather than the malformed source spelling.";

      return string.IsNullOrWhiteSpace( baseInstruction )
         ? correctionInstruction
         : $"{baseInstruction} {correctionInstruction}";
   }
}

internal sealed record KnownTokenCorrection(
   string CanonicalToken,
   string Policy,
   string Reason );

internal sealed class BracketTokenPolicyAnalyzer
{
   private static readonly Regex BracketTokenRegex = new( @"\[(?<token>[A-Za-z0-9_-]+)\]", RegexOptions.Compiled );
   private static readonly Regex ControlStringRegex = new( @"^[A-Za-z0-9_]+(?:,(?:\[[A-Za-z0-9_-]+\]|null|[A-Za-z0-9_]+))+$", RegexOptions.Compiled );
   private static readonly Regex PronounGrammarTokenRegex = new( @"^(?:us|them|3rd)-(?:subj|obj|pos|reflexive|contractIs|contractHas|contractWill)$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex PrefixedVerbTokenRegex = new( @"^(?:us|them|3rd)-(?<verb>[A-Za-z0-9_]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex PreserveSlotTokenRegex = new( @"^(?:us|them|3rd)-(?:friendly|fullName|regID|shipfriendly|captain)$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex PlotSlotTokenRegex = new( @"^(?:protag|contact|target)$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex PrereqTokenRegex = new( @"^prereq\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase );
   private static readonly Regex UppercasePlaceholderTokenRegex = new( @"^[A-Z0-9_]+$", RegexOptions.Compiled );
   private static readonly Regex SpecialCaseTokenRegex = new( @"^(?:object|itm|txt\d+|x|purple|racing_icon|data)$", RegexOptions.Compiled | RegexOptions.IgnoreCase );

   private static readonly IReadOnlyDictionary<string, KnownTokenCorrection> KnownTokenCorrections
      = new Dictionary<string, KnownTokenCorrection>( StringComparer.OrdinalIgnoreCase )
      {
         [ "they-obj" ] = new( "them-obj", BracketTokenPolicies.RewriteGrammar, "Known data typo in parts-of-speech token; game data uses them-obj." ),
         [ "us-contractHave" ] = new( "us-contractHas", BracketTokenPolicies.RewriteGrammar, "Known data typo in contracted grammar token; canonical token is us-contractHas." ),
         [ "us-contractWould" ] = new( "us-contractWill", BracketTokenPolicies.RewriteGrammar, "Known data typo in contracted grammar token; canonical token is us-contractWill." ),
         [ "them-contractWould" ] = new( "them-contractWill", BracketTokenPolicies.RewriteGrammar, "Known data typo in contracted grammar token; canonical token is them-contractWill." ),
         [ "us-walks" ] = new( "walks", BracketTokenPolicies.RewriteGrammar, "Known data anomaly: prefixed verb token references a missing walks verb. Treat it as an English verb helper that should be rewritten into natural target-language wording." ),
      };

   private static readonly BracketTokenPolicyAnalyzer Empty = new( Array.Empty<string>(), Array.Empty<string>() );

   public static BracketTokenPolicyAnalyzer Current { get; private set; } = Empty;

   private readonly HashSet<string> _customTokens;
   private readonly HashSet<string> _verbTokens;

   private BracketTokenPolicyAnalyzer( IEnumerable<string> verbTokens, IEnumerable<string> customTokens )
   {
      _verbTokens = new HashSet<string>( verbTokens.Where( value => !string.IsNullOrWhiteSpace( value ) ), StringComparer.OrdinalIgnoreCase );
      _customTokens = new HashSet<string>( customTokens.Where( value => !string.IsNullOrWhiteSpace( value ) ), StringComparer.OrdinalIgnoreCase );
   }

   public static void ConfigureForGameRoot( string gameRootPath )
   {
      Current = TryCreateForGameRoot( gameRootPath );
   }

   public static BracketTokenPolicyMetadata Resolve( string? rawText, string? metadataJson )
   {
      return Current.ResolveInternal( rawText, metadataJson );
   }

   public static string? EnrichMetadata( string? rawText, string? metadataJson )
   {
      return Current.EnrichMetadataInternal( rawText, metadataJson );
   }

   private static BracketTokenPolicyAnalyzer TryCreateForGameRoot( string gameRootPath )
   {
      try
      {
         var dataRoot = Path.Combine( gameRootPath, "Ostranauts_Data", "StreamingAssets", "data" );
         if( !Directory.Exists( dataRoot ) ) return Empty;

         return new BracketTokenPolicyAnalyzer(
            LoadVerbTokens( dataRoot ),
            LoadCustomTokens( dataRoot ) );
      }
      catch
      {
         return Empty;
      }
   }

   private string? EnrichMetadataInternal( string? rawText, string? metadataJson )
   {
      var metadata = ParseMetadata( metadataJson );
      var resolved = ResolveInternal( rawText, metadataJson );
      if( resolved.TokenPolicy == null )
      {
         return metadata.Count == 0 ? null : JsonSerializer.Serialize( metadata );
      }

      metadata[ "token_policy" ] = resolved.TokenPolicy;
      metadata[ "token_examples" ] = string.Join( ",", resolved.TokenExamples );
      metadata[ "needs_manual_review" ] = resolved.NeedsManualReview ? "true" : "false";
      if( resolved.TokenCorrections.Count > 0 )
      {
         metadata[ "token_corrections_json" ] = JsonSerializer.Serialize( resolved.TokenCorrections );
      }
      return JsonSerializer.Serialize( metadata );
   }

   private BracketTokenPolicyMetadata ResolveInternal( string? rawText, string? metadataJson )
   {
      var metadata = ParseMetadata( metadataJson );
      if( metadata.TryGetValue( "token_policy", out var tokenPolicy ) && !string.IsNullOrWhiteSpace( tokenPolicy ) )
      {
         return new BracketTokenPolicyMetadata(
            tokenPolicy,
            ParseTokenExamples( metadata ),
         ParseBoolean( metadata, "needs_manual_review" ),
         ParseTokenCorrections( metadata ) );
      }

      return Analyze( rawText );
   }

   private BracketTokenPolicyMetadata Analyze( string? rawText )
   {
      if( string.IsNullOrWhiteSpace( rawText ) ) return BracketTokenPolicyMetadata.None;

      var tokenExamples = ExtractTokenExamples( rawText );
      if( tokenExamples.Count == 0 ) return BracketTokenPolicyMetadata.None;
      if( LooksLikeControlString( rawText ) )
      {
         return new BracketTokenPolicyMetadata( BracketTokenPolicies.SkipControl, tokenExamples, false, new Dictionary<string, string>( StringComparer.Ordinal ) );
      }

      var tokenNames = tokenExamples
         .Select( token => token.Trim().TrimStart( '[' ).TrimEnd( ']' ) )
         .Where( token => !string.IsNullOrWhiteSpace( token ) )
         .Distinct( StringComparer.OrdinalIgnoreCase )
         .ToList();

      var hasRewriteTokens = false;
      var hasUnknownTokens = false;
      var tokenCorrections = new Dictionary<string, string>( StringComparer.Ordinal );

      foreach( var tokenName in tokenNames )
      {
         if( TryGetKnownTokenCorrection( tokenName, out var correction ) )
         {
            tokenCorrections[ FormatBracketToken( tokenName ) ] = FormatBracketToken( correction.CanonicalToken );
            if( string.Equals( correction.Policy, BracketTokenPolicies.RewriteGrammar, StringComparison.Ordinal ) )
            {
               hasRewriteTokens = true;
               continue;
            }

            if( string.Equals( correction.Policy, BracketTokenPolicies.PreserveSlot, StringComparison.Ordinal ) )
            {
               continue;
            }
         }

         var effectiveTokenName = tokenName;

         if( IsRewriteToken( effectiveTokenName ) )
         {
            hasRewriteTokens = true;
            continue;
         }

         if( IsPreserveToken( effectiveTokenName ) )
         {
            continue;
         }

         hasUnknownTokens = true;
      }

      if( hasUnknownTokens )
      {
         return new BracketTokenPolicyMetadata( BracketTokenPolicies.ReviewUnknown, tokenExamples, true, tokenCorrections );
      }

      return new BracketTokenPolicyMetadata(
         hasRewriteTokens ? BracketTokenPolicies.RewriteGrammar : BracketTokenPolicies.PreserveSlot,
         tokenExamples,
         false,
         tokenCorrections );
   }

   private static bool TryGetKnownTokenCorrection( string tokenName, out KnownTokenCorrection correction )
   {
      return KnownTokenCorrections.TryGetValue( tokenName, out correction! );
   }

   private bool IsRewriteToken( string tokenName )
   {
      if( _verbTokens.Contains( tokenName ) || PronounGrammarTokenRegex.IsMatch( tokenName ) )
      {
         return true;
      }

      var prefixedVerbMatch = PrefixedVerbTokenRegex.Match( tokenName );
      return prefixedVerbMatch.Success
         && _verbTokens.Contains( prefixedVerbMatch.Groups[ "verb" ].Value );
   }

   private bool IsPreserveToken( string tokenName )
   {
      return PlotSlotTokenRegex.IsMatch( tokenName )
         || PrereqTokenRegex.IsMatch( tokenName )
         || UppercasePlaceholderTokenRegex.IsMatch( tokenName )
         || SpecialCaseTokenRegex.IsMatch( tokenName )
         || PreserveSlotTokenRegex.IsMatch( tokenName )
         || string.Equals( tokenName, "us", StringComparison.OrdinalIgnoreCase )
         || string.Equals( tokenName, "them", StringComparison.OrdinalIgnoreCase )
         || string.Equals( tokenName, "3rd", StringComparison.OrdinalIgnoreCase )
         || _customTokens.Contains( tokenName );
   }

   private static bool LooksLikeControlString( string rawText )
   {
      var trimmed = rawText.Trim();
      return trimmed.Length > 0
         && trimmed.IndexOfAny( new[] { ' ', '\t', '\r', '\n' } ) < 0
         && ControlStringRegex.IsMatch( trimmed );
   }

   private static IReadOnlyList<string> ExtractTokenExamples( string rawText )
   {
      var results = new List<string>();
      var seen = new HashSet<string>( StringComparer.Ordinal );
      foreach( Match match in BracketTokenRegex.Matches( rawText ) )
      {
         var token = match.Value;
         if( seen.Add( token ) )
         {
            results.Add( token );
         }
      }

      return results;
   }

   private static Dictionary<string, string> ParseMetadata( string? metadataJson )
   {
      if( string.IsNullOrWhiteSpace( metadataJson ) )
      {
         return new Dictionary<string, string>( StringComparer.Ordinal );
      }

      try
      {
         var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>( metadataJson );
         return parsed == null
            ? new Dictionary<string, string>( StringComparer.Ordinal )
            : new Dictionary<string, string>( parsed, StringComparer.Ordinal );
      }
      catch( JsonException )
      {
         return new Dictionary<string, string>( StringComparer.Ordinal );
      }
   }

   private static IReadOnlyList<string> ParseTokenExamples( IReadOnlyDictionary<string, string> metadata )
   {
      if( !metadata.TryGetValue( "token_examples", out var rawExamples ) || string.IsNullOrWhiteSpace( rawExamples ) )
      {
         return Array.Empty<string>();
      }

      var results = new List<string>();
      var seen = new HashSet<string>( StringComparer.Ordinal );
      foreach( var part in rawExamples.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
      {
         if( seen.Add( part ) )
         {
            results.Add( part );
         }
      }

      return results;
   }

   private static bool ParseBoolean( IReadOnlyDictionary<string, string> metadata, string key )
   {
      if( !metadata.TryGetValue( key, out var rawValue ) || string.IsNullOrWhiteSpace( rawValue ) )
      {
         return false;
      }

      return bool.TryParse( rawValue, out var parsed ) && parsed;
   }

   private static IReadOnlyDictionary<string, string> ParseTokenCorrections( IReadOnlyDictionary<string, string> metadata )
   {
      if( !metadata.TryGetValue( "token_corrections_json", out var rawCorrections ) || string.IsNullOrWhiteSpace( rawCorrections ) )
      {
         return new Dictionary<string, string>( StringComparer.Ordinal );
      }

      try
      {
         var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>( rawCorrections );
         return parsed == null
            ? new Dictionary<string, string>( StringComparer.Ordinal )
            : new Dictionary<string, string>( parsed, StringComparer.Ordinal );
      }
      catch( JsonException )
      {
         return new Dictionary<string, string>( StringComparer.Ordinal );
      }
   }

   private static string FormatBracketToken( string tokenName )
   {
      return $"[{tokenName}]";
   }

   private static IEnumerable<string> LoadVerbTokens( string dataRoot )
   {
      var results = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
      var verbsRoot = Path.Combine( dataRoot, "verbs" );
      if( !Directory.Exists( verbsRoot ) ) return results;

      foreach( var filePath in Directory.EnumerateFiles( verbsRoot, "*.json", SearchOption.AllDirectories ) )
      {
         try
         {
            using var document = LooseJsonHelper.ParseDocumentFromFile( filePath );
            CollectVerbTokens( document.RootElement, results );
         }
         catch( JsonException )
         {
         }
      }

      return results;
   }

   private static IEnumerable<string> LoadCustomTokens( string dataRoot )
   {
      var results = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
      var tokensRoot = Path.Combine( dataRoot, "tokens" );
      if( !Directory.Exists( tokensRoot ) ) return results;

      foreach( var filePath in Directory.EnumerateFiles( tokensRoot, "*.json", SearchOption.AllDirectories ) )
      {
         try
         {
            using var document = LooseJsonHelper.ParseDocumentFromFile( filePath );
            CollectCustomTokens( document.RootElement, results );
         }
         catch( JsonException )
         {
         }
      }

      return results;
   }

   private static void CollectVerbTokens( JsonElement element, ISet<string> results )
   {
      switch( element.ValueKind )
      {
         case JsonValueKind.Object:
            foreach( var property in element.EnumerateObject() )
            {
               if( property.NameEquals( "verbs" ) && property.Value.ValueKind == JsonValueKind.Array )
               {
                  foreach( var verbDefinition in property.Value.EnumerateArray() )
                  {
                     if( verbDefinition.ValueKind != JsonValueKind.Array ) continue;

                     foreach( var value in verbDefinition.EnumerateArray() )
                     {
                        if( value.ValueKind != JsonValueKind.String ) continue;

                        var verbToken = value.GetString();
                        if( !string.IsNullOrWhiteSpace( verbToken ) )
                        {
                           results.Add( verbToken );
                        }

                        break;
                     }
                  }

                  continue;
               }

               CollectVerbTokens( property.Value, results );
            }

            break;
         case JsonValueKind.Array:
            foreach( var item in element.EnumerateArray() )
            {
               CollectVerbTokens( item, results );
            }

            break;
      }
   }

   private static void CollectCustomTokens( JsonElement element, ISet<string> results )
   {
      switch( element.ValueKind )
      {
         case JsonValueKind.Object:
            foreach( var property in element.EnumerateObject() )
            {
               if( property.NameEquals( "tokens" ) && property.Value.ValueKind == JsonValueKind.Array )
               {
                  foreach( var token in property.Value.EnumerateArray() )
                  {
                     if( token.ValueKind != JsonValueKind.String ) continue;

                     var tokenValue = token.GetString();
                     if( !string.IsNullOrWhiteSpace( tokenValue ) )
                     {
                        results.Add( tokenValue );
                     }
                  }

                  continue;
               }

               CollectCustomTokens( property.Value, results );
            }

            break;
         case JsonValueKind.Array:
            foreach( var item in element.EnumerateArray() )
            {
               CollectCustomTokens( item, results );
            }

            break;
      }
   }
}
