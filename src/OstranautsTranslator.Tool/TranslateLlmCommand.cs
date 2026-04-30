using System.Text.Json;
using System.Text.Json.Serialization;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal static class TranslateLlmCommand
{
   private static readonly JsonSerializerOptions BatchContextJsonOptions = new()
   {
      WriteIndented = false,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
   };

   public static async Task<int> ExecuteAsync(
      TranslateLlmCommandOptions options,
      CancellationToken cancellationToken,
      TranslateLlmExecutionSettings? translationSettingsOverride = null )
   {
      var gameRootPath = options.GameRootPath;
      var resolvedTargetLanguage = RuntimeTranslationDeployment.ResolveTargetLanguage( options.ToLanguage );
      var fromLanguage = RuntimeTranslationDeployment.SourceLanguage;

      var workspace = new TranslationWorkspace( options.WorkspacePath );
      workspace.EnsureCreated();

      var databasePath = workspace.GetTranslationDatabasePath( resolvedTargetLanguage );
      var metadataDatabase = new TranslationDatabase( databasePath );
      metadataDatabase.EnsureExists();

      var toLanguage = resolvedTargetLanguage;

      var translationDatabase = new TranslationDatabase( databasePath, fromLanguage, toLanguage );
      translationDatabase.Initialize();

      var translatedGlossaryPath = workspace.GetGlossaryPath( fromLanguage, toLanguage );
      var genericGlossaryPath = workspace.GetGenericGlossaryPath();
      var glossary = TranslationGlossary.Load( translatedGlossaryPath );
      var genericGlossary = GenericGlossary.Load( genericGlossaryPath );

      var configuration = ToolConfigurationResolver.ResolveLlmConfiguration( fromLanguage, toLanguage );
      var glossaryClientSettings = configuration.GlossaryClientSettings;
      var translationClientSettings = configuration.TranslationClientSettings;
      var translationSettings = translationSettingsOverride ?? configuration.TranslationSettings;
      var translatorName = string.IsNullOrWhiteSpace( translationSettings.Translator )
         ? $"deepseek:{translationClientSettings.Model}"
         : translationSettings.Translator;

      Console.WriteLine( $"Game root: {gameRootPath}" );
      Console.WriteLine( $"Workspace: {workspace.RootPath}" );
      Console.WriteLine( $"Config: {configuration.ConfigPath}" );
      Console.WriteLine( $"Database: {translationDatabase.DatabasePath}" );
      Console.WriteLine( $"Language: {fromLanguage} -> {toLanguage}" );
      Console.WriteLine( $"Endpoint: {translationClientSettings.EndpointUrl}" );
      Console.WriteLine( $"Model: {translationClientSettings.Model}" );
      Console.WriteLine( $"Prompt source: {translationClientSettings.ConfigurationSource}" );
      Console.WriteLine( $"Batch: {translationClientSettings.BatchSize}" );
      Console.WriteLine( $"Glossary target: {translatedGlossaryPath}" );
      Console.WriteLine( $"Existing glossary entries: {glossary.EntryCount}" );
      Console.WriteLine( $"Generic glossary: {( genericGlossary.Exists ? genericGlossary.Path : "(none)" )}" );
      Console.WriteLine( $"Generic glossary entries: {genericGlossary.EntryCount}" );
      Console.WriteLine( $"Limit: {( translationSettings.Limit == int.MaxValue ? "all" : translationSettings.Limit.ToString() )}" );
      Console.WriteLine( $"Glossary first: {translationSettings.TranslateGenericGlossaryFirst}" );
      Console.WriteLine( $"Refresh glossary: {translationSettings.RefreshGlossary}" );
      Console.WriteLine( $"Overwrite existing: {translationSettings.OverwriteExisting}" );
      Console.WriteLine( $"Include draft: {translationSettings.IncludeDraft}" );

      var selectedEntries = translationDatabase.GetEntriesForTranslation( translationSettings.Limit, translationSettings.IncludeDraft, translationSettings.OverwriteExisting );
      if( selectedEntries.Count == 0 )
      {
         Console.WriteLine( "No translation entries matched the selection criteria." );
         return 0;
      }

      var eligibleEntries = selectedEntries
         .Where( entry => !entry.NeedsManualReview && !string.Equals( entry.TokenPolicy, BracketTokenPolicies.SkipControl, StringComparison.Ordinal ) )
         .ToList();
      var skippedByPolicy = selectedEntries.Count - eligibleEntries.Count;
      var requiresGlossaryGeneration = translationSettings.TranslateGenericGlossaryFirst
         && genericGlossary.Exists
         && ( translationSettings.RefreshGlossary || !glossary.Exists );

      if( translationSettings.RefreshGlossary && translationSettings.TranslateGenericGlossaryFirst && !genericGlossary.Exists )
      {
         throw new InvalidOperationException( $"Cannot refresh translated glossary because generic glossary was not found: {genericGlossaryPath}" );
      }

      Console.WriteLine( $"Translate: selected={selectedEntries.Count}, eligible={eligibleEntries.Count}, skippedByPolicy={skippedByPolicy}." );
      if( eligibleEntries.Count == 0 )
      {
         Console.WriteLine( "No entries were eligible for automatic LLM translation after token policy filtering." );
         return 0;
      }

      if( requiresGlossaryGeneration )
      {
         Console.WriteLine( $"Glossary prompt source: {glossaryClientSettings.ConfigurationSource}" );
         Console.WriteLine( $"Glossary: translate {genericGlossary.EntryCount} terms before text batches..." );
         using var glossaryClient = new DeepSeekClient( glossaryClientSettings );
         glossary = await GenericGlossaryTranslationService.TranslateAsync(
            genericGlossary,
            glossary,
            translatedGlossaryPath,
            glossaryClient,
            glossaryClientSettings.BatchSize,
            cancellationToken,
            translationSettings.RefreshGlossary ).ConfigureAwait( false );
         Console.WriteLine( $"Glossary ready: {glossary.Path} ({glossary.EntryCount} entries)." );
      }

      var pendingUpdates = new List<TranslationUpdate>( eligibleEntries.Count );
      var submittedCount = 0;
      var safeBatchSize = Math.Max( 1, translationClientSettings.BatchSize );
      var totalBatchCount = ( eligibleEntries.Count + safeBatchSize - 1 ) / safeBatchSize;
      Console.WriteLine( $"Translate: {eligibleEntries.Count} entries across {totalBatchCount} batches." );
      using var progressBar = new ConsoleProgressBar( "Text translation", totalBatchCount );
      using var client = new DeepSeekClient( translationClientSettings );
      for( int offset = 0; offset < eligibleEntries.Count; offset += safeBatchSize )
      {
         cancellationToken.ThrowIfCancellationRequested();

         var batch = eligibleEntries.Skip( offset ).Take( safeBatchSize ).ToList();
         var batchTexts = batch.Select( entry => entry.RawText ).ToList();
         var batchNumber = offset / safeBatchSize + 1;
         Console.WriteLine( $"Batch {batchNumber}/{totalBatchCount} ({batch.Count})..." );

         var batchContext = CreateBatchContext( batch, glossary );
         var translations = await client.TranslateBatchAsync( batchTexts, batchContext, cancellationToken ).ConfigureAwait( false );
         if( translations.Count != batch.Count )
         {
            throw new InvalidOperationException( $"DeepSeek returned {translations.Count} translations for a batch of {batch.Count} entries." );
         }

         for( int i = 0; i < batch.Count; i++ )
         {
            pendingUpdates.Add( new TranslationUpdate(
               batch[ i ].SourceKind,
               batch[ i ].SourceId,
               translations[ i ],
               translationSettings.TranslationState,
               translatorName ) );
         }

         submittedCount += batch.Count;
         progressBar.Report( batchNumber, $"entries {submittedCount}/{eligibleEntries.Count}" );
         Console.WriteLine( $"Done  {batchNumber}/{totalBatchCount}. Total {submittedCount}/{eligibleEntries.Count}." );
      }

      var appliedCount = translationDatabase.ApplyTranslations( pendingUpdates, translationSettings.OverwriteExisting );
      Console.WriteLine( $"Translate complete. Selected={selectedEntries.Count}, Eligible={eligibleEntries.Count}, Submitted={submittedCount}, Applied={appliedCount}, SkippedByPolicy={skippedByPolicy}, SkippedExisting={pendingUpdates.Count - appliedCount}." );
      return 0;
   }

   private static string? CreateBatchContext( IReadOnlyList<TranslationEntry> batch, TranslationGlossary glossary )
   {
      var entries = new List<object>( batch.Count );
      for( var i = 0; i < batch.Count; i++ )
      {
         var entry = batch[ i ];
         var guidance = new BracketTokenPolicyMetadata( entry.TokenPolicy, entry.TokenExamples, entry.NeedsManualReview, entry.TokenCorrections ).CreateLlmInstruction();
         var contextRow = new Dictionary<string, object?>( StringComparer.Ordinal )
         {
            [ "index" ] = i,
            [ "source_kind" ] = entry.SourceKind,
            [ "source_key" ] = entry.SourceKey,
            [ "token_policy" ] = entry.TokenPolicy,
            [ "token_examples" ] = entry.TokenExamples.Count > 0 ? entry.TokenExamples : null,
            [ "token_corrections" ] = entry.TokenCorrections.Count > 0 ? entry.TokenCorrections : null,
            [ "rule" ] = guidance,
            [ "sample_source_path" ] = entry.SampleSourcePath,
            [ "sample_context_before" ] = entry.SampleContextBefore,
            [ "sample_context_after" ] = entry.SampleContextAfter,
         };

         entries.Add( contextRow );
      }

      if( entries.Count == 0 ) return null;

      var matchedGlossaryEntries = glossary.GetBatchMatches( batch );
      var payload = new Dictionary<string, object?>( StringComparer.Ordinal )
      {
         [ "style_rules" ] = new[]
         {
            "Keep repeated item names, station names, organization names, ship component names, ship names, and person names consistent across the batch.",
            "Ostranauts is grounded hard sci-fi about station life, salvage, precarious labor, institutional decay, survival hazards, corporate bureaucracy, and dry gallows humor. Keep the translation contemporary, grounded, and lived-in. Avoid fantasy, archaic, overly literary, overly heroic, or internet-meme phrasing.",
            "Preserve register by context: technical UI should stay terse and precise; warnings, legal text, and system notices should stay clipped and bureaucratic; tutorial/help text should keep its conversational deadpan and slight snark; news-style text should read like natural headlines or reportage instead of literal word-for-word translations.",
            "For personal names, always use a stable transliteration into the target language, and preserve distinct cultural naming flavor instead of flattening names into one style.",
            "For places, stations, factions, organizations, institutions, brands, doctrines, gangs, items, and ship names, prefer natural semantic translation whenever the meaning is interpretable. If a name contains an explicit code, acronym, callsign, registration ID, model number, hotkey, or other obvious technical identifier, keep that code component unchanged.",
            "Do not smooth away black humor, class tension, bureaucratic coldness, or blue-collar spacer flavor when the source carries it.",
            "Keep model numbers, serial numbers, hotkeys, and obvious technical identifiers unchanged.",
         },
         [ "entries" ] = entries,
         [ "glossary" ] = matchedGlossaryEntries.Count > 0 ? matchedGlossaryEntries.Select( entry => new
         {
            source_term = entry.SourceTerm,
            target_term = entry.TargetTerm,
            note = entry.Note,
            category = entry.Category,
         } ).ToList() : null,
      };

      return "Per-entry translation guidance for the next JSON array. Match each source string by zero-based index. Return only the translated JSON array for the next message.\n"
         + JsonSerializer.Serialize( payload, BatchContextJsonOptions );
   }
}
