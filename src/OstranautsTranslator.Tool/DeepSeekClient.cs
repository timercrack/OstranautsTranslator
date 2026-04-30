using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OstranautsTranslator.Tool;

internal sealed class DeepSeekClient : IDisposable
{
   private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
   {
      WriteIndented = false,
   };

   private readonly HttpClient _httpClient;
   private readonly bool _disposeHttpClient;
   private readonly LlmTranslateSettings _settings;

   public DeepSeekClient( LlmTranslateSettings settings, HttpClient? httpClient = null )
   {
      _settings = settings;
      _httpClient = httpClient ?? new HttpClient();
      _disposeHttpClient = httpClient == null;
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

      using var request = new HttpRequestMessage( HttpMethod.Post, _settings.EndpointUrl )
      {
         Content = new StringContent( JsonSerializer.Serialize( requestPayload, JsonOptions ), Encoding.UTF8, "application/json" ),
      };
      request.Headers.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
      request.Headers.Authorization = new AuthenticationHeaderValue( "Bearer", _settings.ApiKey );

      using var response = await _httpClient.SendAsync( request, cancellationToken ).ConfigureAwait( false );
      var rawResponse = await response.Content.ReadAsStringAsync( cancellationToken ).ConfigureAwait( false );
      if( !response.IsSuccessStatusCode )
      {
         throw new InvalidOperationException( $"DeepSeek request failed with {(int)response.StatusCode} {response.ReasonPhrase}.{Environment.NewLine}{rawResponse}" );
      }

      var translations = TryParseBatchResponse( rawResponse, sourceTexts.Count );
      if( translations == null )
      {
         throw new InvalidOperationException(
            $"DeepSeek returned an invalid response. Expected a JSON array with {sourceTexts.Count} translations.{Environment.NewLine}{rawResponse}" );
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
      try
      {
         using var contentDocument = JsonDocument.Parse( data );
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
            return null;
         }

         if( arrayElement.GetArrayLength() != expectedCount )
         {
            return null;
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
         return null;
      }
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
}
