using System.Text;
using OstranautsTranslator.Core;

namespace OstranautsTranslator.Tool;

internal static class TranslateLlmCommand
{
   private static readonly IReadOnlyList<string> BatchStyleRules = new[]
   {
      "Keep repeated item names, station names, organization names, ship component names, ship names, and person names consistent across the batch.",
      "Do not merge, drop, reorder, or deduplicate entries. The output array must contain exactly one translated element for every input element in the same order, even when entries look repetitive, synonymous, abbreviated, or nearly identical.",
      "If one label uses an abbreviation or code-like short form and another spells it out, keep that distinction in translation instead of normalizing both to the same wording.",
      "Some entries are UI template fragments rather than complete sentences. Translate each fragment as a single fragment, not as multiple pieces and not as a completed full sentence.",
      "Preserve leading and trailing spaces, line breaks, and boundary punctuation exactly when they are present in the source fragment. Do not move those boundary characters into separate output elements.",
      "Some entries are standalone description lines that intentionally do not have a separate short label in the input. Translate those description lines as exactly one output element and do not invent a missing label or heading.",
      "Ostranauts is grounded hard sci-fi about station life, salvage, precarious labor, institutional decay, survival hazards, corporate bureaucracy, and dry gallows humor. Keep the translation contemporary, grounded, and lived-in. Avoid fantasy, archaic, overly literary, overly heroic, or internet-meme phrasing.",
      "Preserve register by context: technical UI should stay terse and precise; warnings, legal text, and system notices should stay clipped and bureaucratic; tutorial/help text should keep its conversational deadpan and slight snark; news-style text should read like natural headlines or reportage instead of literal word-for-word translations.",
      "For personal names, always use a stable transliteration into the target language, and preserve distinct cultural naming flavor instead of flattening names into one style.",
      "For places, stations, factions, organizations, institutions, brands, doctrines, gangs, items, and ship names, prefer natural semantic translation whenever the meaning is interpretable. If a name contains an explicit code, acronym, callsign, registration ID, model number, hotkey, or other obvious technical identifier, keep that code component unchanged.",
      "Do not smooth away black humor, class tension, bureaucratic coldness, or blue-collar spacer flavor when the source carries it.",
      "Keep model numbers, serial numbers, hotkeys, and obvious technical identifiers unchanged.",
   };

   private sealed record BatchApplySummary(
      int SubmittedCount,
      int AppliedCount,
      int SkippedCount );

   public static async Task<int> ExecuteAsync(
      TranslateLlmCommandOptions options,
      CancellationToken cancellationToken,
      TranslateLlmExecutionSettings? translationSettingsOverride = null,
      int? maxCompletedBatchCountOverride = null,
      bool testMode = false )
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
      if( testMode )
      {
         translationSettings = translationSettings with
         {
            TranslateGenericGlossaryFirst = false,
            RefreshGlossary = false,
         };
      }

      var safeBatchSize = Math.Max( 1, translationClientSettings.BatchSize );
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
      Console.WriteLine( $"Glossary first: {translationSettings.TranslateGenericGlossaryFirst}" );
      Console.WriteLine( $"Refresh glossary: {translationSettings.RefreshGlossary}" );
      Console.WriteLine( $"Overwrite existing: {translationSettings.OverwriteExisting}" );
      Console.WriteLine( $"Include draft: {translationSettings.IncludeDraft}" );
      Console.WriteLine( "Persistence: apply each completed batch immediately." );
      if( testMode )
      {
         Console.WriteLine( "Mode: test (exactly one body-text batch, glossary generation disabled, request/response logging enabled)." );
      }
      else if( maxCompletedBatchCountOverride.HasValue )
      {
         Console.WriteLine( $"Mode: test ({maxCompletedBatchCountOverride.Value} completed batch max)." );
      }

      var selectedEntries = translationDatabase.GetEntriesForTranslation( translationSettings.IncludeDraft, translationSettings.OverwriteExisting );
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
         using var glossaryClient = new DeepSeekClient(
            glossaryClientSettings,
            diagnosticsDirectoryPath: workspace.LlmDiagnosticsDirectoryPath,
            diagnosticLabel: "translate-glossary" );
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

      var submittedCount = 0;
      var appliedCount = 0;
      var skippedExistingCount = 0;
      var totalBatchCount = ( eligibleEntries.Count + safeBatchSize - 1 ) / safeBatchSize;
      var plannedBatchCount = maxCompletedBatchCountOverride.HasValue
         ? Math.Min( totalBatchCount, Math.Max( 1, maxCompletedBatchCountOverride.Value ) )
         : totalBatchCount;
      var plannedEntryCount = Math.Min( eligibleEntries.Count, plannedBatchCount * safeBatchSize );
      Console.WriteLine( $"Translate: {eligibleEntries.Count} entries across {totalBatchCount} batches." );
      if( plannedBatchCount != totalBatchCount )
      {
         Console.WriteLine( $"Translate: running {plannedEntryCount} entries across {plannedBatchCount} batch(es) in test mode." );
      }

      using var progressBar = new ConsoleProgressBar( "Text translation", plannedBatchCount );
      using var client = new DeepSeekClient(
         translationClientSettings,
         diagnosticsDirectoryPath: workspace.LlmDiagnosticsDirectoryPath,
         diagnosticLabel: testMode ? "translate-body-test" : "translate-body",
         logSuccessfulCalls: testMode );
      for( int offset = 0, batchIndex = 0; offset < eligibleEntries.Count && batchIndex < plannedBatchCount; offset += safeBatchSize, batchIndex++ )
      {
         cancellationToken.ThrowIfCancellationRequested();

         var batch = eligibleEntries.Skip( offset ).Take( safeBatchSize ).ToList();
         var batchNumber = batchIndex + 1;
         var overallBatchNumber = offset / safeBatchSize + 1;
         if( plannedBatchCount == totalBatchCount )
         {
            Console.WriteLine( $"Batch {batchNumber}/{plannedBatchCount} ({batch.Count})..." );
         }
         else
         {
            Console.WriteLine( $"Batch {batchNumber}/{plannedBatchCount} (overall {overallBatchNumber}/{totalBatchCount}, {batch.Count})..." );
         }

         var batchSummary = await TranslateAndApplyBatchAsync(
            client,
            translationDatabase,
            batch,
            glossary,
            translationSettings,
            translatorName,
            cancellationToken ).ConfigureAwait( false );

         appliedCount += batchSummary.AppliedCount;
         skippedExistingCount += batchSummary.SkippedCount;
         submittedCount += batchSummary.SubmittedCount;
         progressBar.Report( batchNumber, $"entries {submittedCount}/{plannedEntryCount}, saved {appliedCount}" );
         Console.WriteLine( $"Done  {batchNumber}/{plannedBatchCount}. Total {submittedCount}/{plannedEntryCount}. Saved {batchSummary.AppliedCount}/{batchSummary.SubmittedCount} this batch, {appliedCount} overall." );
      }

      Console.WriteLine( $"Translate complete. Selected={selectedEntries.Count}, Eligible={eligibleEntries.Count}, Submitted={submittedCount}, Applied={appliedCount}, SkippedByPolicy={skippedByPolicy}, SkippedExisting={skippedExistingCount}." );
      return 0;
   }

   private static async Task<BatchApplySummary> TranslateAndApplyBatchAsync(
      DeepSeekClient client,
      TranslationDatabase translationDatabase,
      IReadOnlyList<TranslationEntry> batch,
      TranslationGlossary glossary,
      TranslateLlmExecutionSettings translationSettings,
      string translatorName,
      CancellationToken cancellationToken )
   {
      cancellationToken.ThrowIfCancellationRequested();

      var batchTexts = batch.Select( entry => entry.RawText ).ToList();
      var batchContext = CreateBatchContext( batch, glossary );
      var translations = await client.TranslateBatchAsync( batchTexts, batchContext, cancellationToken ).ConfigureAwait( false );
      if( translations.Count != batch.Count )
      {
         throw new InvalidOperationException( $"DeepSeek returned {translations.Count} translations for a batch of {batch.Count} entries." );
      }

      var batchUpdates = new List<TranslationUpdate>( batch.Count );
      for( int i = 0; i < batch.Count; i++ )
      {
         batchUpdates.Add( new TranslationUpdate(
            batch[ i ].SourceKind,
            batch[ i ].SourceId,
            translations[ i ],
            translationSettings.TranslationState,
            translatorName ) );
      }

      var appliedForBatch = translationDatabase.ApplyTranslations( batchUpdates, translationSettings.OverwriteExisting );
      return new BatchApplySummary(
         batchUpdates.Count,
         appliedForBatch,
         batchUpdates.Count - appliedForBatch );
   }

   private static string? CreateBatchContext( IReadOnlyList<TranslationEntry> batch, TranslationGlossary glossary )
   {
      if( batch.Count == 0 ) return null;

      var builder = new StringBuilder();
      builder.AppendLine( "Per-entry translation guidance for the next JSON array. Match each source string by zero-based index." );
      builder.AppendLine( "The next user message contains only the source strings as a JSON array. Return only the translated JSON array." );
      builder.AppendLine( $"There are exactly {batch.Count} source strings in that final JSON array, so your reply must also contain exactly {batch.Count} translated strings." );
      builder.AppendLine( "Every input index must produce exactly one output element. Never merge, drop, reorder, or deduplicate entries, including abbreviation/full-form pairs." );
      builder.AppendLine( "Only the indexes explicitly listed below have extra handling notes; all other indexes should follow the general style rules and glossary matches only." );
      builder.AppendLine();
      builder.AppendLine( "Style rules:" );
      foreach( var rule in BatchStyleRules )
      {
         builder.Append( "- " ).AppendLine( rule );
      }

      var matchedGlossaryEntries = glossary.GetBatchMatches( batch );
      if( matchedGlossaryEntries.Count > 0 )
      {
         builder.AppendLine();
         builder.AppendLine( "Glossary matches:" );
         foreach( var glossaryEntry in matchedGlossaryEntries )
         {
            builder.AppendLine( BuildGlossaryContextLine( glossaryEntry ) );
         }
      }

      var entryContextLines = BuildEntryContextLines( batch );
      if( entryContextLines.Count > 0 )
      {
         builder.AppendLine();
         builder.AppendLine( "Entry-specific notes by index:" );
         foreach( var entryContextLine in entryContextLines )
         {
            builder.AppendLine( entryContextLine );
         }
      }

      return builder.ToString().TrimEnd();
   }

   private static IReadOnlyList<string> BuildEntryContextLines( IReadOnlyList<TranslationEntry> batch )
   {
      var lines = new List<string>();
      lines.AddRange( BuildEntryTypeHintLines( batch ) );
      lines.AddRange( BuildTemplateFragmentHintLines( batch ) );
      lines.AddRange( BuildStandaloneDescriptionHintLines( batch ) );
      lines.AddRange( BuildVariantDistinctionHintLines( batch ) );
      lines.AddRange( BuildTokenRuleHintLines( batch ) );
      return lines;
   }

   private static IEnumerable<string> BuildTemplateFragmentHintLines( IReadOnlyList<TranslationEntry> batch )
   {
      var fragmentIndices = new List<int>();
      var whitespaceSensitiveIndices = new List<int>();
      var punctuationBoundaryIndices = new List<int>();

      for( var i = 0; i < batch.Count; i++ )
      {
         var rawText = batch[ i ].RawText;
         if( IsLikelyTemplateFragment( rawText ) )
         {
            fragmentIndices.Add( i );
         }

         if( HasBoundaryWhitespace( rawText ) )
         {
            whitespaceSensitiveIndices.Add( i );
         }

         if( HasBoundaryPunctuationOrNewline( rawText ) )
         {
            punctuationBoundaryIndices.Add( i );
         }
      }

      if( fragmentIndices.Count > 0 )
      {
         yield return $"- Entries {FormatIndexList( fragmentIndices )} are UI template fragments or sentence pieces. Translate each one as exactly one fragmentary output element. Do not split one fragment into multiple array items, and do not complete it into a fuller sentence by borrowing words from neighboring entries.";
      }

      if( whitespaceSensitiveIndices.Count > 0 )
      {
         yield return $"- Entries {FormatIndexList( whitespaceSensitiveIndices )} have meaningful leading or trailing spaces. Preserve those edge spaces exactly inside the same output string; never peel them off into separate output elements.";
      }

      if( punctuationBoundaryIndices.Count > 0 )
      {
         yield return $"- Entries {FormatIndexList( punctuationBoundaryIndices )} begin or end with boundary punctuation or line breaks that belong to that same string. Keep those boundary characters attached to the same translated element instead of splitting around them.";
      }
   }

   private static IEnumerable<string> BuildStandaloneDescriptionHintLines( IReadOnlyList<TranslationEntry> batch )
   {
      var indices = new List<int>();
      for( var i = 0; i < batch.Count; i++ )
      {
         if( IsLikelyStandaloneDescriptionWithoutLabel( batch, i ) )
         {
            indices.Add( i );
         }
      }

      if( indices.Count > 0 )
      {
         yield return $"- Entries {FormatIndexList( indices )} are standalone description lines without a separate short label in the input. Output exactly one translated description for each such entry and do not invent an extra label, heading, or preceding title for them.";
      }
   }

   private static string BuildGlossaryContextLine( TranslationGlossaryEntry entry )
   {
      var builder = new StringBuilder();
      builder.Append( "- " ).Append( entry.SourceTerm ).Append( " => " ).Append( entry.TargetTerm );

      if( !string.IsNullOrWhiteSpace( entry.Category ) )
      {
         builder.Append( " [" ).Append( entry.Category ).Append( ']' );
      }

      if( !string.IsNullOrWhiteSpace( entry.Note ) )
      {
         builder.Append( " — " ).Append( entry.Note );
      }

      return builder.ToString();
   }

   private static IEnumerable<string> BuildEntryTypeHintLines( IReadOnlyList<TranslationEntry> batch )
   {
      var groupedIndices = new Dictionary<string, List<int>>( StringComparer.Ordinal );
      for( var i = 0; i < batch.Count; i++ )
      {
         if( !TryGetEntryTypeHint( batch[ i ], out var entryTypeHint ) || string.IsNullOrWhiteSpace( entryTypeHint ) )
         {
            continue;
         }

         if( !groupedIndices.TryGetValue( entryTypeHint, out var indices ) )
         {
            indices = new List<int>();
            groupedIndices.Add( entryTypeHint, indices );
         }

         indices.Add( i );
      }

      foreach( var entryTypeHint in new[]
      {
         "person_given_name",
         "person_family_name",
         "ship_name",
         "ship_name_modifier",
         "ship_name_noun",
         "status_label",
      } )
      {
         if( !groupedIndices.TryGetValue( entryTypeHint, out var indices ) || indices.Count == 0 )
         {
            continue;
         }

         yield return BuildEntryTypeHintLine( entryTypeHint, indices );
      }
   }

   private static IEnumerable<string> BuildTokenRuleHintLines( IReadOnlyList<TranslationEntry> batch )
   {
      var groupedHints = new Dictionary<string, TokenRuleHintGroup>( StringComparer.Ordinal );
      var orderedGroups = new List<TokenRuleHintGroup>();

      for( var i = 0; i < batch.Count; i++ )
      {
         var entry = batch[ i ];
         if( !HasSpecialTokenHandling( entry ) ) continue;

         var guidance = new BracketTokenPolicyMetadata( entry.TokenPolicy, entry.TokenExamples, entry.NeedsManualReview, entry.TokenCorrections ).CreateLlmInstruction();
         var tokenExamplesText = entry.TokenExamples.Count > 0 ? string.Join( ", ", entry.TokenExamples ) : null;
         var tokenCorrectionsText = entry.TokenCorrections.Count > 0
            ? string.Join( "; ", entry.TokenCorrections.Select( pair => $"{pair.Key}->{pair.Value}" ) )
            : null;
         var key = string.Join( "\u001f", guidance ?? string.Empty, tokenExamplesText ?? string.Empty, tokenCorrectionsText ?? string.Empty );

         if( !groupedHints.TryGetValue( key, out var group ) )
         {
            group = new TokenRuleHintGroup( new List<int>(), guidance, tokenExamplesText, tokenCorrectionsText );
            groupedHints.Add( key, group );
            orderedGroups.Add( group );
         }

         group.Indices.Add( i );
      }

      foreach( var group in orderedGroups )
      {
         var builder = new StringBuilder();
         builder.Append( "- Entries " ).Append( FormatIndexList( group.Indices ) );

         if( !string.IsNullOrWhiteSpace( group.TokenExamplesText ) )
         {
            builder.Append( " use tokens " ).Append( group.TokenExamplesText );
         }

         if( !string.IsNullOrWhiteSpace( group.TokenCorrectionsText ) )
         {
            builder.Append( "; corrections: " ).Append( group.TokenCorrectionsText );
         }

         if( !string.IsNullOrWhiteSpace( group.Guidance ) )
         {
            builder.Append( ". " ).Append( group.Guidance );
         }

         yield return builder.ToString();
      }
   }

   private static IEnumerable<string> BuildVariantDistinctionHintLines( IReadOnlyList<TranslationEntry> batch )
   {
      var groups = new Dictionary<string, List<(int Index, string SourceText)>>( StringComparer.OrdinalIgnoreCase );

      for( var i = 0; i < batch.Count; i++ )
      {
         if( !TryGetVariantDistinctionGroupKey( batch[ i ].RawText, out var groupKey )
            || string.IsNullOrWhiteSpace( groupKey ) )
         {
            continue;
         }

         if( !groups.TryGetValue( groupKey, out var items ) )
         {
            items = new List<(int Index, string SourceText)>();
            groups.Add( groupKey, items );
         }

         items.Add( ( i, batch[ i ].RawText ) );
      }

      foreach( var group in groups.Values )
      {
         var distinctSourceTexts = group
            .Select( item => item.SourceText )
            .Distinct( StringComparer.Ordinal )
            .ToList();
         if( distinctSourceTexts.Count < 2 )
         {
            continue;
         }

         var indices = group
            .Select( item => item.Index )
            .OrderBy( index => index )
            .ToList();
         yield return $"- Entries {FormatIndexList( indices )} are distinct label variants for the same slot ({string.Join( " vs ", distinctSourceTexts )}). Translate every index separately, do not merge or deduplicate them, and keep abbreviated variants abbreviated instead of normalizing them to the full-word form.";
      }
   }

   private static string BuildEntryTypeHintLine( string entryTypeHint, IReadOnlyList<int> indices )
   {
      var indexList = FormatIndexList( indices );
      return entryTypeHint switch
      {
         "person_given_name" => $"- Entries {indexList} are personal first names. Follow the system prompt rule for personal names: transliterate them as names instead of translating their dictionary meaning.",
         "person_family_name" => $"- Entries {indexList} are personal family names. Keep them name-like and transliterate them instead of translating them as common nouns.",
         "ship_name" => $"- Entries {indexList} are standalone ship names. Translate them as concise ship names, not as sentence fragments.",
         "ship_name_modifier" => $"- Entries {indexList} are ship-name modifiers/components. Keep them concise and suitable for generated ship names.",
         "ship_name_noun" => $"- Entries {indexList} are ship-name nouns/components. Keep them concise and suitable for generated ship names.",
         "status_label" => $"- Entries {indexList} are short condition or status labels. Keep them concise and code-like instead of turning them into explanatory sentences.",
         _ => $"- Entries {indexList} need special handling.",
      };
   }

   private static bool HasSpecialTokenHandling( TranslationEntry entry )
   {
      if( entry.TokenExamples.Count > 0 ) return true;
      if( entry.TokenCorrections.Count > 0 ) return true;

      return !string.IsNullOrWhiteSpace(
         new BracketTokenPolicyMetadata( entry.TokenPolicy, entry.TokenExamples, entry.NeedsManualReview, entry.TokenCorrections ).CreateLlmInstruction() );
   }

   private static bool TryGetEntryTypeHint( TranslationEntry entry, out string? entryTypeHint )
   {
      var sourcePath = entry.SampleSourcePath ?? string.Empty;
      if( sourcePath.Contains( "names_first/", StringComparison.OrdinalIgnoreCase )
         || sourcePath.Contains( "names-first/", StringComparison.OrdinalIgnoreCase ) )
      {
         entryTypeHint = "person_given_name";
         return true;
      }

      if( sourcePath.Contains( "names_last/", StringComparison.OrdinalIgnoreCase )
         || sourcePath.Contains( "names-last/", StringComparison.OrdinalIgnoreCase ) )
      {
         entryTypeHint = "person_family_name";
         return true;
      }

      if( sourcePath.Contains( "names_ship/", StringComparison.OrdinalIgnoreCase )
         || sourcePath.Contains( "names-ship/", StringComparison.OrdinalIgnoreCase ) )
      {
         entryTypeHint = "ship_name";
         return true;
      }

      if( sourcePath.Contains( "names_ship_adjectives/", StringComparison.OrdinalIgnoreCase )
         || sourcePath.Contains( "names-ship-adjectives/", StringComparison.OrdinalIgnoreCase ) )
      {
         entryTypeHint = "ship_name_modifier";
         return true;
      }

      if( sourcePath.Contains( "names_ship_nouns/", StringComparison.OrdinalIgnoreCase )
         || sourcePath.Contains( "names-ship-nouns/", StringComparison.OrdinalIgnoreCase ) )
      {
         entryTypeHint = "ship_name_noun";
         return true;
      }

      if( sourcePath.Contains( "conditions/", StringComparison.OrdinalIgnoreCase )
         && string.Equals( entry.SampleContextAfter, "strNameFriendly", StringComparison.OrdinalIgnoreCase )
         && IsLikelyCodeLikeText( entry.RawText ) )
      {
         entryTypeHint = "status_label";
         return true;
      }

      entryTypeHint = null;
      return false;
   }

   private static bool TryGetVariantDistinctionGroupKey( string? value, out string? groupKey )
   {
      if( string.IsNullOrWhiteSpace( value ) )
      {
         groupKey = null;
         return false;
      }

      if( value.StartsWith( "L ", StringComparison.Ordinal ) )
      {
         groupKey = "Left " + value.Substring( 2 );
         return true;
      }

      if( value.StartsWith( "R ", StringComparison.Ordinal ) )
      {
         groupKey = "Right " + value.Substring( 2 );
         return true;
      }

      if( value.StartsWith( "Left ", StringComparison.Ordinal )
         || value.StartsWith( "Right ", StringComparison.Ordinal ) )
      {
         groupKey = value;
         return true;
      }

      groupKey = null;
      return false;
   }

   private static bool IsLikelyTemplateFragment( string? value )
   {
      if( string.IsNullOrEmpty( value ) ) return false;
      if( HasBoundaryWhitespace( value ) ) return true;

      var trimmed = value.Trim();
      if( string.IsNullOrEmpty( trimmed ) ) return false;
      if( HasBoundaryPunctuationOrNewline( value ) ) return true;

      if( trimmed.Length <= 32 )
      {
         if( char.IsLower( trimmed[ 0 ] ) ) return true;
         if( trimmed.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).Length <= 4 && !trimmed.EndsWith( ".", StringComparison.Ordinal ) ) return true;
      }

      return false;
   }

   private static bool HasBoundaryWhitespace( string? value )
   {
      if( string.IsNullOrEmpty( value ) ) return false;
      return char.IsWhiteSpace( value[ 0 ] ) || char.IsWhiteSpace( value[ value.Length - 1 ] );
   }

   private static bool HasBoundaryPunctuationOrNewline( string? value )
   {
      if( string.IsNullOrEmpty( value ) ) return false;

      return value.Contains( '\n', StringComparison.Ordinal )
         || value.StartsWith( ".", StringComparison.Ordinal )
         || value.StartsWith( ",", StringComparison.Ordinal )
         || value.StartsWith( ":", StringComparison.Ordinal )
         || value.StartsWith( ";", StringComparison.Ordinal )
         || value.EndsWith( ")", StringComparison.Ordinal );
   }

   private static bool IsLikelyStandaloneDescriptionWithoutLabel( IReadOnlyList<TranslationEntry> batch, int index )
   {
      var current = batch[ index ].RawText;
      if( !IsLikelyDescriptionSentence( current ) ) return false;

      var previous = index > 0 ? batch[ index - 1 ].RawText : null;
      var next = index + 1 < batch.Count ? batch[ index + 1 ].RawText : null;

      return IsLikelyDescriptionSentence( previous )
         && IsLikelyShortLabel( next );
   }

   private static bool IsLikelyDescriptionSentence( string? value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return false;

      var trimmed = value.Trim();
      if( trimmed.Length < 24 ) return false;
      if( IsLikelyShortLabel( trimmed ) ) return false;

      return trimmed.StartsWith( "Current ", StringComparison.Ordinal )
         || trimmed.StartsWith( "This ", StringComparison.Ordinal )
         || trimmed.StartsWith( "How ", StringComparison.Ordinal )
         || trimmed.StartsWith( "When ", StringComparison.Ordinal )
         || trimmed.StartsWith( "Will ", StringComparison.Ordinal )
         || trimmed.StartsWith( "Access ", StringComparison.Ordinal )
         || trimmed.StartsWith( "Switch ", StringComparison.Ordinal )
         || trimmed.StartsWith( "Set ", StringComparison.Ordinal )
         || trimmed.StartsWith( "Quick ", StringComparison.Ordinal )
         || trimmed.StartsWith( "Drag ", StringComparison.Ordinal )
         || trimmed.StartsWith( "Move ", StringComparison.Ordinal );
   }

   private static bool IsLikelyShortLabel( string? value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return false;

      var trimmed = value.Trim();
      if( trimmed.Length == 0 || trimmed.Length > 36 ) return false;
      if( HasBoundaryWhitespace( value ) ) return false;
      if( HasBoundaryPunctuationOrNewline( trimmed ) && !trimmed.EndsWith( ")", StringComparison.Ordinal ) ) return false;
      if( trimmed.Contains( ". ", StringComparison.Ordinal ) ) return false;
      if( trimmed.EndsWith( ".", StringComparison.Ordinal ) ) return false;

      var words = trimmed.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
      return words.Length <= 5;
   }

   private static bool IsLikelyCodeLikeText( string? value )
   {
      if( string.IsNullOrWhiteSpace( value ) ) return false;
      if( value.Contains( '-', StringComparison.Ordinal ) ) return true;
      if( value.Any( char.IsDigit ) ) return true;

      var letters = value.Where( char.IsLetter ).ToArray();
      return letters.Length > 1 && letters.All( char.IsUpper );
   }

   private static string FormatIndexList( IReadOnlyList<int> indices )
   {
      return string.Join( ", ", indices );
   }

   private sealed record TokenRuleHintGroup(
      List<int> Indices,
      string? Guidance,
      string? TokenExamplesText,
      string? TokenCorrectionsText );
}
