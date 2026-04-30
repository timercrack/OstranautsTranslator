using System.Text.Json;
using System.Text.Json.Serialization;

namespace OstranautsTranslator.Tool;

internal sealed record TranslationGlossaryEntry(
   [property: JsonPropertyName( "source_term" )] string SourceTerm,
   [property: JsonPropertyName( "target_term" )] string TargetTerm,
   [property: JsonPropertyName( "note" )] string? Note,
   [property: JsonPropertyName( "category" )] string? Category,
   [property: JsonPropertyName( "always_include" )] bool AlwaysInclude );

internal sealed class TranslationGlossary
{
   private static readonly JsonSerializerOptions JsonOptions = new()
   {
      PropertyNameCaseInsensitive = true,
   };

   public static readonly TranslationGlossary Empty = new( null, Array.Empty<TranslationGlossaryEntry>() );

   private readonly IReadOnlyList<TranslationGlossaryEntry> _entries;

   private TranslationGlossary( string? path, IReadOnlyList<TranslationGlossaryEntry> entries )
   {
      Path = path;
      _entries = entries;
   }

   public string? Path { get; }

   public bool Exists => !string.IsNullOrWhiteSpace( Path ) && File.Exists( Path );

   public int EntryCount => _entries.Count;

   public IReadOnlyList<TranslationGlossaryEntry> Entries => _entries;

   public static TranslationGlossary Load( string? glossaryPath )
   {
      if( string.IsNullOrWhiteSpace( glossaryPath ) )
      {
         return Empty;
      }

      var resolvedPath = System.IO.Path.GetFullPath( glossaryPath );
      if( !File.Exists( resolvedPath ) )
      {
         return new TranslationGlossary( resolvedPath, Array.Empty<TranslationGlossaryEntry>() );
      }

      try
      {
         using var stream = File.OpenRead( resolvedPath );
         var entries = JsonSerializer.Deserialize<List<TranslationGlossaryEntry>>( stream, JsonOptions )
            ?? new List<TranslationGlossaryEntry>();
         var normalizedEntries = entries
            .Where( entry => !string.IsNullOrWhiteSpace( entry.SourceTerm ) && !string.IsNullOrWhiteSpace( entry.TargetTerm ) )
            .Select( entry => new TranslationGlossaryEntry(
               entry.SourceTerm.Trim(),
               entry.TargetTerm.Trim(),
               string.IsNullOrWhiteSpace( entry.Note ) ? null : entry.Note.Trim(),
               string.IsNullOrWhiteSpace( entry.Category ) ? null : entry.Category.Trim(),
               entry.AlwaysInclude ) )
            .DistinctBy( entry => entry.SourceTerm, StringComparer.OrdinalIgnoreCase )
            .OrderByDescending( entry => entry.AlwaysInclude )
            .ThenByDescending( entry => entry.SourceTerm.Length )
            .ToList();

         return new TranslationGlossary( resolvedPath, normalizedEntries );
      }
      catch( JsonException )
      {
         throw new InvalidOperationException( $"Failed to parse glossary JSON: {resolvedPath}" );
      }
   }

   public IReadOnlyList<TranslationGlossaryEntry> GetBatchMatches( IReadOnlyList<TranslationEntry> batch )
   {
      if( _entries.Count == 0 || batch.Count == 0 )
      {
         return Array.Empty<TranslationGlossaryEntry>();
      }

      var batchTexts = batch
         .SelectMany( entry => new[]
         {
            entry.RawText,
            entry.SampleContextBefore,
            entry.SampleContextAfter,
         } )
         .Where( value => !string.IsNullOrWhiteSpace( value ) )
         .ToList();

      return _entries
         .Where( entry => entry.AlwaysInclude || batchTexts.Any( text => text!.Contains( entry.SourceTerm, StringComparison.OrdinalIgnoreCase ) ) )
         .Take( 40 )
         .ToList();
   }
}