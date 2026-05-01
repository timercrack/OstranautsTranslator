using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OstranautsTranslator.Tool;

internal static class GenericGlossaryTranslationService
{
   private static readonly Encoding Utf8NoBom = new UTF8Encoding( encoderShouldEmitUTF8Identifier: false );
   private static readonly JsonSerializerOptions BatchContextJsonOptions = new()
   {
      WriteIndented = false,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
   };

   private static readonly JsonSerializerOptions OutputJsonOptions = new()
   {
      WriteIndented = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
   };

   private static readonly Regex MultiSpaceRegex = new( @"\s+", RegexOptions.Compiled );

   public static async Task<TranslationGlossary> TranslateAsync(
      GenericGlossary genericGlossary,
      TranslationGlossary existingGlossary,
      string outputPath,
      DeepSeekClient client,
      int batchSize,
      CancellationToken cancellationToken,
      bool overwriteExisting )
   {
      ArgumentNullException.ThrowIfNull( genericGlossary );
      ArgumentNullException.ThrowIfNull( existingGlossary );
      ArgumentException.ThrowIfNullOrWhiteSpace( outputPath );
      ArgumentNullException.ThrowIfNull( client );

      var existingEntriesBySourceTerm = existingGlossary.Entries
         .GroupBy( entry => entry.SourceTerm, StringComparer.OrdinalIgnoreCase )
         .ToDictionary( group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase );

      var termsToTranslate = overwriteExisting
         ? genericGlossary.Terms.ToList()
         : genericGlossary.Terms.Where( entry => !existingEntriesBySourceTerm.ContainsKey( entry.SourceTerm ) ).ToList();

      var translatedEntriesBySourceTerm = new Dictionary<string, TranslationGlossaryEntry>( existingEntriesBySourceTerm, StringComparer.OrdinalIgnoreCase );
      var safeBatchSize = Math.Max( 1, batchSize );
      var totalBatchCount = termsToTranslate.Count == 0
         ? 0
         : ( termsToTranslate.Count + safeBatchSize - 1 ) / safeBatchSize;

      using var progressBar = totalBatchCount > 0 ? new ConsoleProgressBar( "Glossary translation", totalBatchCount ) : null;
      for( int offset = 0; offset < termsToTranslate.Count; offset += safeBatchSize )
      {
         cancellationToken.ThrowIfCancellationRequested();

         var batch = termsToTranslate.Skip( offset ).Take( safeBatchSize ).ToList();
         var sourceTerms = batch.Select( entry => entry.SourceTerm ).ToList();
         var batchContext = CreateBatchContext( batch );
         var translations = await client.TranslateBatchAsync( sourceTerms, batchContext, cancellationToken ).ConfigureAwait( false );
         if( translations.Count != batch.Count )
         {
            throw new InvalidOperationException( $"DeepSeek returned {translations.Count} glossary translations for a batch of {batch.Count} terms." );
         }

         for( int i = 0; i < batch.Count; i++ )
         {
            var translatedTerm = NormalizeTranslatedTerm( translations[ i ] );
            if( string.IsNullOrWhiteSpace( translatedTerm ) )
            {
               throw new InvalidOperationException( $"DeepSeek returned an empty glossary translation for '{batch[ i ].SourceTerm}'." );
            }

            translatedEntriesBySourceTerm[ batch[ i ].SourceTerm ] = new TranslationGlossaryEntry(
               batch[ i ].SourceTerm,
               translatedTerm,
               null,
               batch[ i ].Category,
               existingEntriesBySourceTerm.TryGetValue( batch[ i ].SourceTerm, out var existingEntry ) && existingEntry.AlwaysInclude );
         }

         progressBar?.Report(
            offset / safeBatchSize + 1,
            $"terms {Math.Min( offset + batch.Count, termsToTranslate.Count )}/{termsToTranslate.Count}" );
      }

      var finalEntries = BuildFinalEntries( genericGlossary, existingGlossary, translatedEntriesBySourceTerm );

      var resolvedOutputPath = Path.GetFullPath( outputPath );
      var outputDirectoryPath = Path.GetDirectoryName( resolvedOutputPath );
      if( !string.IsNullOrWhiteSpace( outputDirectoryPath ) )
      {
         Directory.CreateDirectory( outputDirectoryPath );
      }

      var json = JsonSerializer.Serialize( finalEntries, OutputJsonOptions ) + "\n";
      await File.WriteAllTextAsync( resolvedOutputPath, json, Utf8NoBom, cancellationToken ).ConfigureAwait( false );
      return TranslationGlossary.Load( resolvedOutputPath );
   }

   private static IReadOnlyList<TranslationGlossaryEntry> BuildFinalEntries(
      GenericGlossary genericGlossary,
      TranslationGlossary existingGlossary,
      IReadOnlyDictionary<string, TranslationGlossaryEntry> translatedEntriesBySourceTerm )
   {
      var finalEntries = new List<TranslationGlossaryEntry>( Math.Max( genericGlossary.EntryCount, translatedEntriesBySourceTerm.Count ) );
      var includedSourceTerms = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

      foreach( var genericTerm in genericGlossary.Terms )
      {
         if( !translatedEntriesBySourceTerm.TryGetValue( genericTerm.SourceTerm, out var translatedEntry ) )
         {
            continue;
         }

         finalEntries.Add( new TranslationGlossaryEntry(
            genericTerm.SourceTerm,
            translatedEntry.TargetTerm,
            translatedEntry.Note,
            string.IsNullOrWhiteSpace( genericTerm.Category ) ? translatedEntry.Category : genericTerm.Category,
            translatedEntry.AlwaysInclude ) );
         includedSourceTerms.Add( genericTerm.SourceTerm );
      }

      foreach( var existingEntry in existingGlossary.Entries )
      {
         if( includedSourceTerms.Add( existingEntry.SourceTerm ) )
         {
            finalEntries.Add( existingEntry );
         }
      }

      return finalEntries;
   }

   private static string CreateBatchContext( IReadOnlyList<GenericGlossaryTerm> batch )
   {
      var payload = new Dictionary<string, object?>( StringComparer.Ordinal )
      {
         [ "task" ] = "Translate the next JSON array as standalone in-game glossary terms, not sentences. Keep each translated term concise and reusable in later translation batches.",
         [ "style_rules" ] = new[]
         {
            "Return only one JSON array with the same number of elements and the same order as the input.",
            "These inputs are glossary terms for people, places, factions, brands, ships, items, and institutions. Do not turn them into sentences or add explanations.",
            "Ostranauts is grounded, working-class hard sci-fi full of salvage crews, ship systems, labor exploitation, corporate bureaucracy, refugees, and dry black humor. Prefer grounded, industrial, near-future wording. Avoid fantasy, archaic, overly poetic, or internet-meme phrasing.",
            "For personal names, always use a stable transliteration into the target language, and preserve distinct cultural naming flavor instead of flattening names into one style.",
            "For places, stations, factions, organizations, institutions, brands, doctrines, gangs, items, and ship names, prefer natural semantic translation whenever the meaning is interpretable. Choose wording that sounds plausible in a grubby, industrial space setting instead of leaving English untouched.",
            "If a name contains an explicit code, acronym, callsign, registration ID, model number, hotkey, or other obvious technical identifier, keep that code component unchanged while translating the meaningful part when natural.",
            "Keep recurring translated terms stable across the batch.",
            "Keep model numbers, serial numbers, hotkeys, and obvious technical identifiers unchanged.",
         },
         [ "entries" ] = batch.Select( ( entry, index ) => new Dictionary<string, object?>( StringComparer.Ordinal )
         {
            [ "index" ] = index,
            [ "category" ] = entry.Category,
            [ "source_term" ] = entry.SourceTerm,
         } ).ToList(),
      };

      return "Glossary guidance for the next JSON array. Match each source term by zero-based index. Return only the translated JSON array for the next message.\n"
         + JsonSerializer.Serialize( payload, BatchContextJsonOptions );
   }

   private static string NormalizeTranslatedTerm( string value )
   {
      return MultiSpaceRegex.Replace( value.Trim(), " " );
   }
}