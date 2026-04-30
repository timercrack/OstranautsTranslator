using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using OstranautsTranslator.Core.Processing;

namespace OstranautsTranslator.Core;

public enum RuntimeTranslationLookupResult
{
   NoMatch,
   IgnoredNativeModSource,
   Translated
}

public sealed class RuntimeTranslationCatalog
{
   private static readonly RuntimeTranslationCatalog EmptyCatalog = new RuntimeTranslationCatalog(
      RuntimeTextProcessingConfiguration.Default,
      string.Empty,
      new HashSet<string>( StringComparer.Ordinal ),
      new Dictionary<string, string>( StringComparer.Ordinal ),
      new Dictionary<string, string>( StringComparer.Ordinal ),
      new Dictionary<string, string>( StringComparer.Ordinal ),
      0,
      0,
      0 );

   private readonly HashSet<string> _ignoredNativeModSourceLookupKeys;
   private readonly IReadOnlyDictionary<string, string> _translationsByRawText;
   private readonly IReadOnlyDictionary<string, string> _translationsByRuntimeKey;
   private readonly IReadOnlyDictionary<string, string> _translationsByRenderKey;
   private readonly ConcurrentDictionary<string, byte> _ignoredValueCache;
   private readonly ConcurrentDictionary<string, string> _translationCache;

   internal RuntimeTranslationCatalog(
      RuntimeTextProcessingConfiguration configuration,
      string databasePath,
      HashSet<string> ignoredNativeModSourceLookupKeys,
      IReadOnlyDictionary<string, string> translationsByRawText,
      IReadOnlyDictionary<string, string> translationsByRuntimeKey,
      IReadOnlyDictionary<string, string> translationsByRenderKey,
      int loadedTranslationCount,
      int runtimeKeyCollisionCount,
      int renderKeyCollisionCount )
   {
      Configuration = configuration;
      DatabasePath = databasePath;
      _ignoredNativeModSourceLookupKeys = ignoredNativeModSourceLookupKeys ?? new HashSet<string>( StringComparer.Ordinal );
      _translationsByRawText = translationsByRawText;
      _translationsByRuntimeKey = translationsByRuntimeKey;
      _translationsByRenderKey = translationsByRenderKey;
      LoadedTranslationCount = loadedTranslationCount;
      RuntimeKeyCollisionCount = runtimeKeyCollisionCount;
      RenderKeyCollisionCount = renderKeyCollisionCount;
      _ignoredValueCache = new ConcurrentDictionary<string, byte>( StringComparer.Ordinal );
      _translationCache = new ConcurrentDictionary<string, string>( StringComparer.Ordinal );
   }

   public static RuntimeTranslationCatalog Empty => EmptyCatalog;

   public RuntimeTextProcessingConfiguration Configuration { get; }

   public string DatabasePath { get; }

   public int LoadedTranslationCount { get; }

   public int RuntimeKeyCollisionCount { get; }

   public int RenderKeyCollisionCount { get; }

   public int IgnoredNativeModSourceCount => _ignoredNativeModSourceLookupKeys.Count;

   public bool HasTranslations => LoadedTranslationCount > 0;

   public RuntimeTranslationLookupResult Lookup( string value, out string translated )
   {
      translated = value;
      if( string.IsNullOrEmpty( value ) )
      {
         return RuntimeTranslationLookupResult.NoMatch;
      }

      if( _ignoredValueCache.ContainsKey( value ) )
      {
         return RuntimeTranslationLookupResult.IgnoredNativeModSource;
      }

      if( _translationCache.TryGetValue( value, out translated ) )
      {
         return !string.Equals( value, translated, StringComparison.Ordinal )
            ? RuntimeTranslationLookupResult.Translated
            : RuntimeTranslationLookupResult.NoMatch;
      }

      var projection = RuntimeTextProjector.CreateProjection( value, Configuration );
      var sourceLookupKey = CreateRenderLookupKey( projection.TextKind, projection.RenderKey );

      if( _ignoredNativeModSourceLookupKeys.Contains( sourceLookupKey ) )
      {
         _ignoredValueCache.TryAdd( value, 0 );
         return RuntimeTranslationLookupResult.IgnoredNativeModSource;
      }

      if( !HasTranslations )
      {
         _translationCache.TryAdd( value, value );
         translated = value;
         return RuntimeTranslationLookupResult.NoMatch;
      }

      if( _translationsByRawText.TryGetValue( value, out translated ) )
      {
         translated = MaterializeTranslation( projection, translated );
         _translationCache.TryAdd( value, translated );
         return !string.Equals( value, translated, StringComparison.Ordinal )
            ? RuntimeTranslationLookupResult.Translated
            : RuntimeTranslationLookupResult.NoMatch;
      }

      if( !string.IsNullOrWhiteSpace( projection.RuntimeKey ) && _translationsByRuntimeKey.TryGetValue( projection.RuntimeKey, out translated ) )
      {
         translated = MaterializeTranslation( projection, translated );
         _translationCache.TryAdd( value, translated );
         return !string.Equals( value, translated, StringComparison.Ordinal )
            ? RuntimeTranslationLookupResult.Translated
            : RuntimeTranslationLookupResult.NoMatch;
      }

      var renderLookupKey = CreateRenderLookupKey( projection.TextKind, projection.RenderKey );
      if( _translationsByRenderKey.TryGetValue( renderLookupKey, out translated ) )
      {
         translated = MaterializeTranslation( projection, translated );
         _translationCache.TryAdd( value, translated );
         return !string.Equals( value, translated, StringComparison.Ordinal )
            ? RuntimeTranslationLookupResult.Translated
            : RuntimeTranslationLookupResult.NoMatch;
      }

      _translationCache.TryAdd( value, value );
      translated = value;
      return RuntimeTranslationLookupResult.NoMatch;
   }

   internal static string CreateRenderLookupKey( string textKind, string renderKey )
   {
      return textKind + "\u001F" + renderKey;
   }

   private static string MaterializeTranslation( RuntimeTextProjection projection, string translatedText )
   {
      var result = translatedText ?? string.Empty;
      if( projection.RuntimeTemplate != null && projection.RuntimeTemplate.HasTokens )
      {
         result = projection.RuntimeTemplate.Apply( result );
      }

      if( projection.RichTextTemplate != null && projection.RichTextTemplate.HasTokens )
      {
         result = projection.RichTextTemplate.Apply( result );
      }

      return result;
   }
}
