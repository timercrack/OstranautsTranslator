using System.Text.Json;

namespace OstranautsTranslator.Tool;

internal sealed record GenericGlossaryTerm( string SourceTerm, string Category );

internal sealed class GenericGlossary
{
   public static readonly GenericGlossary Empty = new( null, Array.Empty<GenericGlossaryTerm>() );

   private readonly IReadOnlyList<GenericGlossaryTerm> _terms;

   private GenericGlossary( string? path, IReadOnlyList<GenericGlossaryTerm> terms )
   {
      Path = path;
      _terms = terms;
   }

   public string? Path { get; }

   public bool Exists => !string.IsNullOrWhiteSpace( Path ) && File.Exists( Path );

   public int EntryCount => _terms.Count;

   public IReadOnlyList<GenericGlossaryTerm> Terms => _terms;

   public static GenericGlossary Load( string? glossaryPath )
   {
      if( string.IsNullOrWhiteSpace( glossaryPath ) )
      {
         return Empty;
      }

      var resolvedPath = System.IO.Path.GetFullPath( glossaryPath );
      if( !File.Exists( resolvedPath ) )
      {
         return new GenericGlossary( resolvedPath, Array.Empty<GenericGlossaryTerm>() );
      }

      try
      {
         using var stream = File.OpenRead( resolvedPath );
         using var document = JsonDocument.Parse( stream );
         if( document.RootElement.ValueKind != JsonValueKind.Object )
         {
            throw new InvalidOperationException( $"Failed to parse generic glossary JSON: {resolvedPath}" );
         }

         var seenTerms = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
         var terms = new List<GenericGlossaryTerm>();
         foreach( var property in document.RootElement.EnumerateObject() )
         {
            var category = property.Name.Trim();
            if( string.IsNullOrWhiteSpace( category ) || property.Value.ValueKind != JsonValueKind.Array )
            {
               continue;
            }

            foreach( var item in property.Value.EnumerateArray() )
            {
               if( item.ValueKind != JsonValueKind.String )
               {
                  continue;
               }

               var sourceTerm = item.GetString()?.Trim();
               if( string.IsNullOrWhiteSpace( sourceTerm ) || !seenTerms.Add( sourceTerm ) )
               {
                  continue;
               }

               terms.Add( new GenericGlossaryTerm( sourceTerm, category ) );
            }
         }

         return new GenericGlossary( resolvedPath, terms );
      }
      catch( JsonException )
      {
         throw new InvalidOperationException( $"Failed to parse generic glossary JSON: {resolvedPath}" );
      }
   }
}
