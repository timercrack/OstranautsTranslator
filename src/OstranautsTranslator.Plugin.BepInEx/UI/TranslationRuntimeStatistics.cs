using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OstranautsTranslator.Plugin.BepInEx.UI;

internal sealed class TranslationRuntimeStatistics
{
   private readonly ConcurrentDictionary<string, byte> _uniqueNativeModIgnoredTexts = new ConcurrentDictionary<string, byte>( StringComparer.Ordinal );
   private readonly ConcurrentDictionary<string, byte> _uniqueMissTexts = new ConcurrentDictionary<string, byte>( StringComparer.Ordinal );
   private readonly ConcurrentDictionary<string, byte> _uniqueReplacedTexts = new ConcurrentDictionary<string, byte>( StringComparer.Ordinal );
   private long _lookupCount;
   private long _databaseHitCount;
   private long _nativeModIgnoredCount;
   private long _replacementCount;
   private long _missCount;
   private long _capturedMissCount;
   private long _errorCount;

   public long LookupCount => Interlocked.Read( ref _lookupCount );

   public long DatabaseHitCount => Interlocked.Read( ref _databaseHitCount );

   public long NativeModIgnoredCount => Interlocked.Read( ref _nativeModIgnoredCount );

   public long ReplacementCount => Interlocked.Read( ref _replacementCount );

   public int UniqueReplacementCount => _uniqueReplacedTexts.Count;

   public int UniqueNativeModIgnoredCount => _uniqueNativeModIgnoredTexts.Count;

   public long MissCount => Interlocked.Read( ref _missCount );

   public int UniqueMissCount => _uniqueMissTexts.Count;

   public long CapturedMissCount => Interlocked.Read( ref _capturedMissCount );

   public long ErrorCount => Interlocked.Read( ref _errorCount );

   public void RecordLookup()
   {
      Interlocked.Increment( ref _lookupCount );
   }

   public void RecordDatabaseHit( string originalText, string translatedText )
   {
      Interlocked.Increment( ref _databaseHitCount );

      if( !string.Equals( originalText, translatedText, StringComparison.Ordinal ) )
      {
         Interlocked.Increment( ref _replacementCount );
         if( !string.IsNullOrEmpty( originalText ) )
         {
            _uniqueReplacedTexts.TryAdd( originalText, 0 );
         }
      }
   }

   public void RecordIgnoredNativeModSource( string originalText )
   {
      Interlocked.Increment( ref _nativeModIgnoredCount );
      if( !string.IsNullOrEmpty( originalText ) )
      {
         _uniqueNativeModIgnoredTexts.TryAdd( originalText, 0 );
      }
   }

   public void RecordDatabaseMiss( string originalText )
   {
      Interlocked.Increment( ref _missCount );
      if( !string.IsNullOrEmpty( originalText ) )
      {
         _uniqueMissTexts.TryAdd( originalText, 0 );
      }
   }

   public void RecordCapturedMiss()
   {
      Interlocked.Increment( ref _capturedMissCount );
   }

   public void RecordError()
   {
      Interlocked.Increment( ref _errorCount );
   }
}

internal sealed class TranslationStatusSnapshot
{
   public TranslationStatusSnapshot(
      string pluginStatus,
   string currentGameVersion,
   string recordedGameVersion,
   string gameVersionStatus,
   string gameVersionReminder,
      string targetLanguage,
      string databaseStatus,
      int loadedEntryCount,
      long lookupCount,
      long databaseHitCount,
      long nativeModIgnoredCount,
      long replacementCount,
      int uniqueReplacementCount,
      int uniqueNativeModIgnoredCount,
      long missCount,
      int uniqueMissCount,
      long capturedMissCount,
      long errorCount )
   {
      PluginStatus = pluginStatus ?? string.Empty;
      CurrentGameVersion = currentGameVersion ?? string.Empty;
      RecordedGameVersion = recordedGameVersion ?? string.Empty;
      GameVersionStatus = gameVersionStatus ?? string.Empty;
      GameVersionReminder = gameVersionReminder ?? string.Empty;
      TargetLanguage = targetLanguage ?? string.Empty;
      DatabaseStatus = databaseStatus ?? string.Empty;
      LoadedEntryCount = loadedEntryCount;
      LookupCount = lookupCount;
      DatabaseHitCount = databaseHitCount;
      NativeModIgnoredCount = nativeModIgnoredCount;
      ReplacementCount = replacementCount;
      UniqueReplacementCount = uniqueReplacementCount;
      UniqueNativeModIgnoredCount = uniqueNativeModIgnoredCount;
      MissCount = missCount;
      UniqueMissCount = uniqueMissCount;
      CapturedMissCount = capturedMissCount;
      ErrorCount = errorCount;
   }

   public string PluginStatus { get; }

   public string CurrentGameVersion { get; }

   public string RecordedGameVersion { get; }

   public string GameVersionStatus { get; }

   public string GameVersionReminder { get; }

   public string TargetLanguage { get; }

   public string DatabaseStatus { get; }

   public int LoadedEntryCount { get; }

   public long LookupCount { get; }

   public long DatabaseHitCount { get; }

   public long NativeModIgnoredCount { get; }

   public long ReplacementCount { get; }

   public int UniqueReplacementCount { get; }

   public int UniqueNativeModIgnoredCount { get; }

   public long MissCount { get; }

   public int UniqueMissCount { get; }

   public long CapturedMissCount { get; }

   public long ErrorCount { get; }

   public double HitRate => LookupCount > 0
      ? (double)DatabaseHitCount / LookupCount
      : 0d;
}