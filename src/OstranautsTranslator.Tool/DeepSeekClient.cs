using System.Text.Encodings.Web;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OstranautsTranslator.Tool;

internal sealed class DeepSeekClient : IDisposable
{
   private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
   {
      WriteIndented = false,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
   };

   private static readonly JsonSerializerOptions DiagnosticJsonOptions = new JsonSerializerOptions
   {
      WriteIndented = true,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
   };

   private readonly HttpClient _httpClient;
   private readonly bool _disposeHttpClient;
   private readonly LlmTranslateSettings _settings;
   private readonly string? _diagnosticsDirectoryPath;
   private readonly string? _diagnosticLabel;
   private readonly bool _logSuccessfulCalls;
   private const string DiagnosticLogFileName = "last_batch.log";

   public DeepSeekClient(
      LlmTranslateSettings settings,
      HttpClient? httpClient = null,
      string? diagnosticsDirectoryPath = null,
      string? diagnosticLabel = null,
      bool logSuccessfulCalls = false )
   {
      _settings = settings;
      _httpClient = httpClient ?? new HttpClient();
      _disposeHttpClient = httpClient == null;
      _diagnosticsDirectoryPath = diagnosticsDirectoryPath;
      _diagnosticLabel = diagnosticLabel;
      _logSuccessfulCalls = logSuccessfulCalls;
   }

   public async Task<IReadOnlyList<string>> TranslateBatchAsync( IReadOnlyList<string> sourceTexts, CancellationToken cancellationToken )
   {
      return await TranslateBatchAsync( sourceTexts, null, cancellationToken ).ConfigureAwait( false );
   }

   public async Task<IReadOnlyList<string>> TranslateBatchAsync( IReadOnlyList<string> sourceTexts, string? batchContext, CancellationToken cancellationToken )
   {
      if( sourceTexts.Count == 0 ) return Array.Empty<string>();

      var jsonArrayPayload = JsonSerializer.Serialize( sourceTexts, JsonOptions );
      var messages = new List<object>
      {
         new
         {
            role = "system",
            content = _settings.SystemPrompt,
         },
      };

      if( !string.IsNullOrWhiteSpace( batchContext ) )
      {
         messages.Add( new
         {
            role = "user",
            content = batchContext,
         } );
      }

      messages.Add( new
      {
         role = "user",
         content = jsonArrayPayload,
      } );

      var requestPayload = new
      {
         model = _settings.Model,
         messages,
         temperature = _settings.Temperature,
         max_tokens = _settings.MaxTokens,
         thinking = new
         {
            type = "disabled",
         },
      };

      var requestJson = JsonSerializer.Serialize( requestPayload, JsonOptions );
      var diagnosticRequestJson = JsonSerializer.Serialize( requestPayload, DiagnosticJsonOptions );

      using var request = new HttpRequestMessage( HttpMethod.Post, _settings.EndpointUrl )
      {
         Content = new StringContent( requestJson, Encoding.UTF8, "application/json" ),
      };
      request.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
      request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", _settings.ApiKey );

      using var response = await _httpClient.SendAsync( request, cancellationToken ).ConfigureAwait( false );
      var rawResponse = await response.Content.ReadAsStringAsync( cancellationToken ).ConfigureAwait( false );
      if( !response.IsSuccessStatusCode )
      {
         var diagnosticPath = WriteDiagnosticLog(
            "http-error",
            rawResponse,
            sourceTexts.Count,
            batchContext,
            diagnosticRequestJson,
            jsonArrayPayload,
            TryExtractAssistantContent( rawResponse ),
            (int)response.StatusCode,
            response.ReasonPhrase );

         throw new InvalidOperationException( BuildFailureMessage(
            $"DeepSeek request failed with {(int)response.StatusCode} {response.ReasonPhrase}.",
            rawResponse,
            diagnosticPath ) );
      }

      if( _logSuccessfulCalls )
      {
         WriteDiagnosticLog(
            "request-response",
            rawResponse,
            sourceTexts.Count,
            batchContext,
            diagnosticRequestJson,
            jsonArrayPayload,
            TryExtractAssistantContent( rawResponse ),
            (int)response.StatusCode,
            response.ReasonPhrase );
      }

      var translations = TryParseBatchResponse( rawResponse, sourceTexts.Count );
      if( translations == null )
      {
         var diagnosticPath = WriteDiagnosticLog(
            "invalid-response",
            rawResponse,
            sourceTexts.Count,
            batchContext,
            diagnosticRequestJson,
            jsonArrayPayload,
            TryExtractAssistantContent( rawResponse ) );

         throw new InvalidOperationException( BuildFailureMessage(
            $"DeepSeek returned an invalid response. Expected a JSON array with {sourceTexts.Count} translations.",
            rawResponse,
            diagnosticPath ) );
      }

      return translations;
   }

   public void Dispose()
   {
      if( _disposeHttpClient )
      {
         _httpClient.Dispose();
      }
   }

   private static IReadOnlyList<string>? TryParseBatchResponse( string data, int expectedCount )
   {
      if( string.IsNullOrWhiteSpace( data ) ) return null;

      try
      {
         using var responseDocument = JsonDocument.Parse( data );
         if( !responseDocument.RootElement.TryGetProperty( "choices", out var choicesElement )
            || choicesElement.ValueKind != JsonValueKind.Array
            || choicesElement.GetArrayLength() == 0 )
         {
            return null;
         }

         var firstChoice = choicesElement[ 0 ];
         if( !firstChoice.TryGetProperty( "message", out var messageElement )
            || !messageElement.TryGetProperty( "content", out var contentElement )
            || contentElement.ValueKind != JsonValueKind.String )
         {
            return null;
         }

         var assistantContent = contentElement.GetString();
         if( string.IsNullOrWhiteSpace( assistantContent ) ) return null;

         return ParseJsonTranslations( assistantContent, expectedCount );
      }
      catch( Exception )
      {
         return null;
      }
   }

   private static IReadOnlyList<string>? ParseJsonTranslations( string data, int expectedCount )
   {
      var candidatePayloads = EnumerateJsonTranslationCandidates( data );
      foreach( var candidatePayload in candidatePayloads )
      {
         try
         {
            using var contentDocument = JsonDocument.Parse( candidatePayload );
            var root = contentDocument.RootElement;
            JsonElement arrayElement;

            if( root.ValueKind == JsonValueKind.Array )
            {
               arrayElement = root;
            }
            else if( root.ValueKind == JsonValueKind.Object
               && root.TryGetProperty( "translations", out var translationsElement )
               && translationsElement.ValueKind == JsonValueKind.Array )
            {
               arrayElement = translationsElement;
            }
            else
            {
               continue;
            }

            if( arrayElement.GetArrayLength() != expectedCount )
            {
               continue;
            }

            var results = new List<string>( expectedCount );
            foreach( var item in arrayElement.EnumerateArray() )
            {
               results.Add( ConvertElementToString( item ) );
            }

            return results;
         }
         catch( Exception )
         {
            continue;
         }
      }

      return null;
   }

   private static IEnumerable<string> EnumerateJsonTranslationCandidates( string data )
   {
      if( string.IsNullOrWhiteSpace( data ) ) yield break;

      var trimmed = data.Trim();
      if( trimmed.Length == 0 ) yield break;

      var seen = new HashSet<string>( StringComparer.Ordinal );
      if( seen.Add( trimmed ) )
      {
         yield return trimmed;
      }

      var unfenced = TryStripMarkdownCodeFence( trimmed );
      if( !string.IsNullOrWhiteSpace( unfenced ) && seen.Add( unfenced ) )
      {
         yield return unfenced;
      }

      if( !trimmed.EndsWith( "]", StringComparison.Ordinal ) )
      {
         var appendedClosingBracket = trimmed + "]";
         if( seen.Add( appendedClosingBracket ) )
         {
            yield return appendedClosingBracket;
         }
      }

      var lastClosingBracketIndex = trimmed.LastIndexOf( ']' );
      if( lastClosingBracketIndex >= 0 && lastClosingBracketIndex < trimmed.Length - 1 )
      {
         var truncatedAtLastBracket = trimmed.Substring( 0, lastClosingBracketIndex + 1 );
         if( seen.Add( truncatedAtLastBracket ) )
         {
            yield return truncatedAtLastBracket;
         }
      }

      if( !string.IsNullOrWhiteSpace( unfenced ) )
      {
         if( !unfenced.EndsWith( "]", StringComparison.Ordinal ) )
         {
            var unfencedWithClosingBracket = unfenced + "]";
            if( seen.Add( unfencedWithClosingBracket ) )
            {
               yield return unfencedWithClosingBracket;
            }
         }

         var unfencedLastClosingBracketIndex = unfenced.LastIndexOf( ']' );
         if( unfencedLastClosingBracketIndex >= 0 && unfencedLastClosingBracketIndex < unfenced.Length - 1 )
         {
            var unfencedTruncatedAtLastBracket = unfenced.Substring( 0, unfencedLastClosingBracketIndex + 1 );
            if( seen.Add( unfencedTruncatedAtLastBracket ) )
            {
               yield return unfencedTruncatedAtLastBracket;
            }
         }
      }
   }

   private static string? TryStripMarkdownCodeFence( string value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return null;
      if( !value.StartsWith( "```", StringComparison.Ordinal ) ) return null;

      var firstLineBreakIndex = value.IndexOf( '\n' );
      if( firstLineBreakIndex < 0 ) return null;

      var withoutOpeningFence = value.Substring( firstLineBreakIndex + 1 );
      var closingFenceIndex = withoutOpeningFence.LastIndexOf( "```", StringComparison.Ordinal );
      if( closingFenceIndex < 0 ) return null;

      return withoutOpeningFence.Substring( 0, closingFenceIndex ).Trim();
   }

   private static string ConvertElementToString( JsonElement element )
   {
      return element.ValueKind switch
      {
         JsonValueKind.String => element.GetString() ?? string.Empty,
         JsonValueKind.Null => string.Empty,
         _ => element.GetRawText(),
      };
   }

   private static string BuildFailureMessage( string header, string rawResponse, string? diagnosticPath )
   {
      if( string.IsNullOrWhiteSpace( diagnosticPath ) )
      {
         return $"{header}{Environment.NewLine}{rawResponse}";
      }

      return $"{header}{Environment.NewLine}Diagnostic log: {diagnosticPath}";
   }

   private static string? TryExtractAssistantContent( string rawResponse )
   {
      if( string.IsNullOrWhiteSpace( rawResponse ) ) return null;

      try
      {
         using var responseDocument = JsonDocument.Parse( rawResponse );
         if( !responseDocument.RootElement.TryGetProperty( "choices", out var choicesElement )
            || choicesElement.ValueKind != JsonValueKind.Array
            || choicesElement.GetArrayLength() == 0 )
         {
            return null;
         }

         var firstChoice = choicesElement[ 0 ];
         if( !firstChoice.TryGetProperty( "message", out var messageElement )
            || !messageElement.TryGetProperty( "content", out var contentElement )
            || contentElement.ValueKind != JsonValueKind.String )
         {
            return null;
         }

         return contentElement.GetString();
      }
      catch( Exception )
      {
         return null;
      }
   }

   private string? WriteDiagnosticLog(
      string failureKind,
      string rawResponse,
      int expectedCount,
      string? batchContext,
      string? requestJson,
      string? finalUserJsonArray,
      string? assistantContent,
      int? statusCode = null,
      string? reasonPhrase = null )
   {
      if( string.IsNullOrWhiteSpace( _diagnosticsDirectoryPath ) ) return null;

      try
      {
         Directory.CreateDirectory( _diagnosticsDirectoryPath );

         var timestamp = DateTime.UtcNow;
         var label = string.IsNullOrWhiteSpace( _diagnosticLabel ) ? "deepseek" : _diagnosticLabel;
         var filePath = Path.Combine( _diagnosticsDirectoryPath, DiagnosticLogFileName );

         var builder = new StringBuilder();
         builder.AppendLine( $"timestamp_utc={timestamp:O}" );
         builder.AppendLine( $"failure_kind={failureKind}" );
         builder.AppendLine( $"label={label}" );
         builder.AppendLine( $"endpoint={_settings.EndpointUrl}" );
         builder.AppendLine( $"model={_settings.Model}" );
         builder.AppendLine( $"expected_translation_count={expectedCount}" );
         var actualTranslationCount = TryGetTranslationArrayCount( assistantContent );
         if( actualTranslationCount.HasValue )
         {
            builder.AppendLine( $"actual_translation_count={actualTranslationCount.Value}" );
         }

         if( statusCode.HasValue )
         {
            builder.AppendLine( $"status_code={statusCode.Value}" );
         }

         if( !string.IsNullOrWhiteSpace( reasonPhrase ) )
         {
            builder.AppendLine( $"reason_phrase={reasonPhrase}" );
         }

         if( !string.IsNullOrWhiteSpace( _settings.SystemPrompt ) )
         {
            builder.AppendLine();
            builder.AppendLine( "=== System Prompt ===" );
            builder.AppendLine( _settings.SystemPrompt );
         }

         if( !string.IsNullOrWhiteSpace( batchContext ) )
         {
            builder.AppendLine();
            builder.AppendLine( "=== Batch Context ===" );
            builder.AppendLine( batchContext );
         }

         if( !string.IsNullOrWhiteSpace( finalUserJsonArray ) )
         {
            builder.AppendLine();
            builder.AppendLine( "=== Final User Message JSON Array ===" );
            builder.AppendLine( finalUserJsonArray );
         }

         if( !string.IsNullOrWhiteSpace( requestJson ) )
         {
            builder.AppendLine();
            builder.AppendLine( "=== Request Payload ===" );
            builder.AppendLine( requestJson );
         }

         builder.AppendLine();
         builder.AppendLine( "=== Raw Response ===" );
         builder.AppendLine( rawResponse );

         if( !string.IsNullOrWhiteSpace( assistantContent ) )
         {
            builder.AppendLine();
            builder.AppendLine( "=== choices[0].message.content ===" );
            builder.AppendLine( assistantContent );
         }

         File.WriteAllText( filePath, builder.ToString(), new UTF8Encoding( encoderShouldEmitUTF8Identifier: false ) );
         return filePath;
      }
      catch( Exception )
      {
         return null;
      }
   }

   private static int? TryGetTranslationArrayCount( string? assistantContent )
   {
      if( string.IsNullOrWhiteSpace( assistantContent ) ) return null;

      try
      {
         using var contentDocument = JsonDocument.Parse( assistantContent );
         var root = contentDocument.RootElement;
         if( root.ValueKind == JsonValueKind.Array )
         {
            return root.GetArrayLength();
         }

         if( root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty( "translations", out var translationsElement )
            && translationsElement.ValueKind == JsonValueKind.Array )
         {
            return translationsElement.GetArrayLength();
         }

         return null;
      }
      catch( Exception )
      {
         return null;
      }
   }
}
