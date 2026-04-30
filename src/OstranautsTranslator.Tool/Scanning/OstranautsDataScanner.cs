using System.Text.Json;
using OstranautsTranslator.Tool;

namespace OstranautsTranslator.Tool.Scanning;

internal sealed class OstranautsDataScanner
{
   private static readonly IReadOnlyList<DirectoryRule> Rules =
   [
      DirectoryRule.SimplePairs( "strings", "string-table" ),
      DirectoryRule.NamePairs( "manpages", "manpages" ),
      DirectoryRule.ConditionsSimple( "conditions_simple", "conditions-simple" ),
      DirectoryRule.GuiPropMaps( "guipropmaps", "gui-prop-maps", "strFriendlyName", "strTitle", "strBrand", "strBrandSub" ),
      DirectoryRule.NamePairs( "names_first", "names-first" ),
      DirectoryRule.NamePairs( "names_full", "names-full" ),
      DirectoryRule.NamePairs( "names_last", "names-last" ),
      DirectoryRule.NamePairs( "names_robots", "names-robots" ),
      DirectoryRule.NamePairs( "names_ship", "names-ship" ),
      DirectoryRule.NamePairs( "names_ship_adjectives", "names-ship-adjectives" ),
      DirectoryRule.NamePairs( "names_ship_nouns", "names-ship-nouns" ),
      DirectoryRule.Structured( "attackmodes/coAttacks", "attackmodes-co-attacks", "strNameFriendly" ),
      DirectoryRule.Structured( "conditions", "conditions", "strNameFriendly", "strDesc", "strShort", "strDisplayBonus" ),
      DirectoryRule.Structured( "condowners", "condowners", "strNameFriendly", "strNameShort", "strDesc" ),
      DirectoryRule.Structured( "cooverlays", "cooverlays", "strNameFriendly", "strNameShort", "strDesc" ),
      DirectoryRule.Structured( "interactions", "interactions", "strTitle", "strDesc", "strTooltip", "strAttackerName" ),
      DirectoryRule.AssignmentArrays( "interaction_overrides", "interaction-overrides", ["aOverrideValues"], "strTitle", "strDesc", "strTooltip" ),
      DirectoryRule.Structured( "transit", "transit-labels", "strLabelNameOptional" ),
      DirectoryRule.Structured( "tips", "tips", "strBody" ),
      DirectoryRule.Structured( "headlines", "headlines", "strDesc", "strRegion" ),
      DirectoryRule.Structured( "info", "info", "strNodeLabel", "strArticleTitle", "strArticleBody" ),
      DirectoryRule.Structured( "context", "context", "strMainText" ),
      DirectoryRule.Structured( "jobitems", "jobitems", "strFriendlyName" ),
      DirectoryRule.Structured( "ledgerdefs", "ledgerdefs", "strDesc" ),
      DirectoryRule.Structured( "ads", "ads", "strDesc" ),
      DirectoryRule.Structured( "careers", "careers", "strNameFriendly" ),
      DirectoryRule.Structured( "homeworlds", "homeworlds", "strColonyName" ),
      DirectoryRule.Structured( "market/CoCollections", "market-co-collections", "strFriendlyName" ),
      DirectoryRule.Structured( "pda_apps", "pda-apps", "strFriendlyName" ),
      DirectoryRule.AssignmentArrays( "plot_beat_overrides", "plot-beat-overrides", ["aOverrideBeatValues", "aOverrideTriggerIAValues"], "strDesc" ),
      DirectoryRule.Structured( "plots", "plots", "strNameFriendly", "aPhaseTitles" ),
      DirectoryRule.Structured( "pledges", "pledges", "strNameFriendly" ),
      DirectoryRule.Structured( "racing/leagues", "racing-leagues", "strNameFriendly", "strDescription" ),
      DirectoryRule.Structured( "racing/tracks", "racing-tracks", "strNameFriendly", "strDescription" ),
      DirectoryRule.Structured( "rooms", "rooms", "strNameFriendly" ),
      DirectoryRule.Structured( "ships", "ships", "publicName", "make", "model", "designation", "dimensions", "origin", "description" ),
      DirectoryRule.Structured( "slots", "slots", "strNameFriendly" ),
      DirectoryRule.Structured( "star_systems", "star-systems", "strPublicName" ),
      DirectoryRule.Structured( "tsv/output/stakes/conditions", "stake-conditions", "strNameFriendly", "strDesc", "strShort", "strDisplayBonus" ),
      DirectoryRule.Structured( "tsv/output/stakes/interactions", "stake-interactions", "strTitle", "strDesc", "strTooltip", "strBubble", "strAttackerName", "strSocialCombatPreview" ),
      DirectoryRule.Structured( "tsv/output/stakes/contexts", "stake-context", "strTitle", "strMainText" ),
   ];

   public IEnumerable<SourceScanResult> Scan( string gameRootPath )
   {
      var dataRoot = Path.Combine( gameRootPath, "Ostranauts_Data", "StreamingAssets", "data" );
      if( !Directory.Exists( dataRoot ) ) yield break;

      foreach( var rule in Rules )
      {
         var directoryPath = Path.Combine( dataRoot, rule.RelativeDirectory );
         if( !Directory.Exists( directoryPath ) ) continue;

         foreach( var filePath in Directory.EnumerateFiles( directoryPath, "*.json", SearchOption.AllDirectories ) )
         {
            var result = ScanFile( dataRoot, filePath, rule );
            if( result.Occurrences.Count > 0 )
            {
               yield return result;
            }
         }
      }
   }

   private static SourceScanResult ScanFile( string dataRoot, string filePath, DirectoryRule rule )
   {
      var fileInfo = new FileInfo( filePath );
      var occurrences = new List<ScanOccurrence>();

      try
      {
         using var document = LooseJsonHelper.ParseDocumentFromFile( filePath );

         switch( rule.Mode )
         {
            case DirectoryRuleMode.Structured:
               ScanStructuredElement( document.RootElement, rule, CreateRelativeLocation( dataRoot, filePath, "$" ), null, occurrences );
               break;
            case DirectoryRuleMode.SimplePairs:
               ScanSimplePairs( document.RootElement, rule, dataRoot, filePath, occurrences );
               break;
            case DirectoryRuleMode.SimplePairKeys:
               ScanNamePairs( document.RootElement, rule, dataRoot, filePath, occurrences );
               break;
            case DirectoryRuleMode.SimpleValues:
               ScanSimpleValues( document.RootElement, rule, dataRoot, filePath, occurrences );
               break;
            case DirectoryRuleMode.ConditionsSimple:
               ScanConditionsSimple( document.RootElement, rule, dataRoot, filePath, occurrences );
               break;
            case DirectoryRuleMode.GuiPropMaps:
               ScanGuiPropMaps( document.RootElement, rule, dataRoot, filePath, occurrences );
               break;
            case DirectoryRuleMode.ArrayValues:
               ScanArrayValues( document.RootElement, rule, CreateRelativeLocation( dataRoot, filePath, "$" ), null, occurrences );
               break;
            case DirectoryRuleMode.AssignmentArrays:
               ScanAssignmentArrays( document.RootElement, rule, CreateRelativeLocation( dataRoot, filePath, "$" ), null, occurrences );
               break;
         }
      }
      catch( JsonException exception )
      {
         Console.Error.WriteLine( $"Skipping invalid JSON file '{filePath}': {exception.Message}" );
      }

      return new SourceScanResult(
         fileInfo.FullName,
         "ostranauts-data-json",
         fileInfo.Exists ? fileInfo.Length : 0,
         new DateTimeOffset( fileInfo.LastWriteTimeUtc ),
         FileHashHelper.ComputeFileHash( filePath ),
         occurrences );
   }

   private static void ScanStructuredElement( JsonElement element, DirectoryRule rule, string locationPath, string? recordName, List<ScanOccurrence> occurrences )
   {
      switch( element.ValueKind )
      {
         case JsonValueKind.Object:
         {
            var currentRecordName = TryGetPropertyValue( element, "strName" ) ?? recordName;
            foreach( var property in element.EnumerateObject() )
            {
               var propertyLocationPath = locationPath + "." + property.Name;
               if( rule.AllowedFields.Contains( property.Name ) )
               {
                  if( property.Value.ValueKind == JsonValueKind.String )
                  {
                     AddOccurrence(
                        occurrences,
                        property.Value.GetString(),
                        "json-structured-field",
                        propertyLocationPath,
                        currentRecordName,
                        property.Name,
                        rule.IsTranslatable,
                        CreateMetadata(
                           ( "category", rule.Category ),
                           ( "record_name", currentRecordName ),
                           ( "field_name", property.Name ) ) );
                  }
                  else if( property.Value.ValueKind == JsonValueKind.Array )
                  {
                     ScanStructuredStringArray(
                        property.Value,
                        rule,
                        propertyLocationPath,
                        currentRecordName,
                        property.Name,
                        occurrences );
                  }
               }

               ScanStructuredElement( property.Value, rule, propertyLocationPath, currentRecordName, occurrences );
            }

            break;
         }
         case JsonValueKind.Array:
         {
            var index = 0;
            foreach( var item in element.EnumerateArray() )
            {
               ScanStructuredElement( item, rule, $"{locationPath}[{index}]", recordName, occurrences );
               index++;
            }

            break;
         }
      }
   }

   private static void ScanStructuredStringArray( JsonElement arrayElement, DirectoryRule rule, string locationPath, string? recordName, string fieldName, List<ScanOccurrence> occurrences )
   {
      if( arrayElement.ValueKind != JsonValueKind.Array ) return;

      var index = 0;
      foreach( var item in arrayElement.EnumerateArray() )
      {
         var itemLocationPath = $"{locationPath}[{index}]";
         if( item.ValueKind == JsonValueKind.String )
         {
            AddOccurrence(
               occurrences,
               item.GetString(),
               "json-array-value",
               itemLocationPath,
               recordName,
               fieldName,
               rule.IsTranslatable,
               CreateMetadata(
                  ( "category", rule.Category ),
                  ( "record_name", recordName ),
                  ( "field_name", fieldName ),
                  ( "value_index", index.ToString() ) ) );
         }
         else if( item.ValueKind == JsonValueKind.Array )
         {
            ScanStructuredStringArray( item, rule, itemLocationPath, recordName, fieldName, occurrences );
         }

         index++;
      }
   }

   private static void ScanSimplePairs( JsonElement rootElement, DirectoryRule rule, string dataRoot, string filePath, List<ScanOccurrence> occurrences )
   {
      ScanPairs( rootElement, rule, dataRoot, filePath, occurrences, valuesPropertyName: "aValues", captureKeyText: false, locationKind: "json-simple-pair" );
   }

   private static void ScanNamePairs( JsonElement rootElement, DirectoryRule rule, string dataRoot, string filePath, List<ScanOccurrence> occurrences )
   {
      ScanPairs( rootElement, rule, dataRoot, filePath, occurrences, valuesPropertyName: "aValues", captureKeyText: true, locationKind: "json-simple-key" );
   }

   private static void ScanGuiPropMaps( JsonElement rootElement, DirectoryRule rule, string dataRoot, string filePath, List<ScanOccurrence> occurrences )
   {
      ScanPairs( rootElement, rule, dataRoot, filePath, occurrences, valuesPropertyName: "dictGUIPropMap", captureKeyText: false, locationKind: "json-gui-prop-map-field" );
   }

   private static void ScanPairs( JsonElement rootElement, DirectoryRule rule, string dataRoot, string filePath, List<ScanOccurrence> occurrences, string valuesPropertyName, bool captureKeyText, string locationKind )
   {
      if( rootElement.ValueKind != JsonValueKind.Array ) return;

      var recordIndex = 0;
      foreach( var record in rootElement.EnumerateArray() )
      {
         if( record.ValueKind != JsonValueKind.Object )
         {
            recordIndex++;
            continue;
         }

         var recordName = TryGetPropertyValue( record, "strName" );
         if( !record.TryGetProperty( valuesPropertyName, out var valuesElement ) || valuesElement.ValueKind != JsonValueKind.Array )
         {
            recordIndex++;
            continue;
         }

         var values = ReadStringArray( valuesElement );
         for( var i = 0; i + 1 < values.Count; i += 2 )
         {
            var key = values[ i ];
            var value = values[ i + 1 ];
            if( string.IsNullOrWhiteSpace( key ) ) continue;
            if( rule.AllowedFields.Count > 0 && !rule.AllowedFields.Contains( key ) ) continue;

            var capturedText = captureKeyText ? key : value;
            if( string.IsNullOrWhiteSpace( capturedText ) ) continue;

            var targetIndex = captureKeyText ? i : i + 1;
            var metadata = captureKeyText
               ? CreateMetadata(
                  ( "category", rule.Category ),
                  ( "record_name", recordName ),
                  ( "pair_value", value ) )
               : CreateMetadata(
                  ( "category", rule.Category ),
                  ( "record_name", recordName ),
                  ( "pair_key", key ) );

            AddOccurrence(
               occurrences,
               capturedText,
               locationKind,
               CreateRelativeLocation( dataRoot, filePath, $"$[{recordIndex}].{valuesPropertyName}[{targetIndex}]" ),
               recordName,
               captureKeyText ? value : key,
               rule.IsTranslatable,
               metadata );
         }

         recordIndex++;
      }
   }

   private static void ScanSimpleValues( JsonElement rootElement, DirectoryRule rule, string dataRoot, string filePath, List<ScanOccurrence> occurrences )
   {
      if( rootElement.ValueKind != JsonValueKind.Array ) return;

      var recordIndex = 0;
      foreach( var record in rootElement.EnumerateArray() )
      {
         if( record.ValueKind != JsonValueKind.Object )
         {
            recordIndex++;
            continue;
         }

         var recordName = TryGetPropertyValue( record, "strName" );
         if( !record.TryGetProperty( "aValues", out var valuesElement ) || valuesElement.ValueKind != JsonValueKind.Array )
         {
            recordIndex++;
            continue;
         }

         var values = ReadStringArray( valuesElement );
         for( var i = 0; i < values.Count; i++ )
         {
            AddOccurrence(
               occurrences,
               values[ i ],
               "json-simple-value",
               CreateRelativeLocation( dataRoot, filePath, $"$[{recordIndex}].aValues[{i}]" ),
               recordName,
               $"value[{i}]",
               rule.IsTranslatable,
               CreateMetadata(
                  ( "category", rule.Category ),
                  ( "record_name", recordName ),
                  ( "value_index", i.ToString() ) ) );
         }

         recordIndex++;
      }
   }

   private static void ScanConditionsSimple( JsonElement rootElement, DirectoryRule rule, string dataRoot, string filePath, List<ScanOccurrence> occurrences )
   {
      if( rootElement.ValueKind != JsonValueKind.Array ) return;

      var recordIndex = 0;
      foreach( var record in rootElement.EnumerateArray() )
      {
         if( record.ValueKind != JsonValueKind.Object )
         {
            recordIndex++;
            continue;
         }

         var recordName = TryGetPropertyValue( record, "strName" );
         if( !record.TryGetProperty( "aValues", out var valuesElement ) || valuesElement.ValueKind != JsonValueKind.Array )
         {
            recordIndex++;
            continue;
         }

         var values = ReadStringArray( valuesElement );
         for( var i = 0; i + 6 < values.Count; i += 7 )
         {
            AddOccurrence(
               occurrences,
               values[ i + 1 ],
               "json-conditions-simple-field",
               CreateRelativeLocation( dataRoot, filePath, $"$[{recordIndex}].aValues[{i + 1}]" ),
               values[ i ],
               "strNameFriendly",
               rule.IsTranslatable,
               CreateMetadata(
                  ( "category", rule.Category ),
                  ( "condition_name", values[ i ] ),
                  ( "field_name", "strNameFriendly" ) ) );

            AddOccurrence(
               occurrences,
               values[ i + 2 ],
               "json-conditions-simple-field",
               CreateRelativeLocation( dataRoot, filePath, $"$[{recordIndex}].aValues[{i + 2}]" ),
               values[ i ],
               "strDesc",
               rule.IsTranslatable,
               CreateMetadata(
                  ( "category", rule.Category ),
                  ( "condition_name", values[ i ] ),
                  ( "field_name", "strDesc" ) ) );
         }

         recordIndex++;
      }
   }

   private static void ScanArrayValues( JsonElement element, DirectoryRule rule, string locationPath, string? recordName, List<ScanOccurrence> occurrences )
   {
      switch( element.ValueKind )
      {
         case JsonValueKind.Object:
         {
            var currentRecordName = TryGetPropertyValue( element, "strName" ) ?? recordName;
            foreach( var property in element.EnumerateObject() )
            {
               var propertyLocationPath = locationPath + "." + property.Name;
               if( property.Value.ValueKind == JsonValueKind.Array && rule.AllowedFields.Contains( property.Name ) )
               {
                  var valueIndex = 0;
                  foreach( var item in property.Value.EnumerateArray() )
                  {
                     if( item.ValueKind == JsonValueKind.String )
                     {
                        AddOccurrence(
                           occurrences,
                           item.GetString(),
                           "json-array-value",
                           $"{propertyLocationPath}[{valueIndex}]",
                           currentRecordName,
                           property.Name,
                           rule.IsTranslatable,
                           CreateMetadata(
                              ( "category", rule.Category ),
                              ( "record_name", currentRecordName ),
                              ( "field_name", property.Name ),
                              ( "value_index", valueIndex.ToString() ) ) );
                     }

                     ScanArrayValues( item, rule, $"{propertyLocationPath}[{valueIndex}]", currentRecordName, occurrences );
                     valueIndex++;
                  }

                  continue;
               }

               ScanArrayValues( property.Value, rule, propertyLocationPath, currentRecordName, occurrences );
            }

            break;
         }
         case JsonValueKind.Array:
         {
            var index = 0;
            foreach( var item in element.EnumerateArray() )
            {
               ScanArrayValues( item, rule, $"{locationPath}[{index}]", recordName, occurrences );
               index++;
            }

            break;
         }
      }
   }

   private static void ScanAssignmentArrays( JsonElement element, DirectoryRule rule, string locationPath, string? recordName, List<ScanOccurrence> occurrences )
   {
      switch( element.ValueKind )
      {
         case JsonValueKind.Object:
         {
            var currentRecordName = TryGetPropertyValue( element, "strName" ) ?? recordName;
            foreach( var property in element.EnumerateObject() )
            {
               var propertyLocationPath = locationPath + "." + property.Name;
               if( property.Value.ValueKind == JsonValueKind.Array && rule.ContainerFields.Contains( property.Name ) )
               {
                  var valueIndex = 0;
                  foreach( var item in property.Value.EnumerateArray() )
                  {
                     if( item.ValueKind == JsonValueKind.String
                        && TryParseAssignmentValue( item.GetString(), out var fieldName, out var fieldValue )
                        && fieldName != null
                        && rule.AllowedFields.Contains( fieldName ) )
                     {
                        AddOccurrence(
                           occurrences,
                           fieldValue,
                           "json-assignment-array-field",
                           $"{propertyLocationPath}[{valueIndex}]",
                           currentRecordName,
                           fieldName,
                           rule.IsTranslatable,
                           CreateMetadata(
                              ( "category", rule.Category ),
                              ( "record_name", currentRecordName ),
                              ( "array_field_name", property.Name ),
                              ( "field_name", fieldName ) ) );
                     }

                     ScanAssignmentArrays( item, rule, $"{propertyLocationPath}[{valueIndex}]", currentRecordName, occurrences );
                     valueIndex++;
                  }

                  continue;
               }

               ScanAssignmentArrays( property.Value, rule, propertyLocationPath, currentRecordName, occurrences );
            }

            break;
         }
         case JsonValueKind.Array:
         {
            var index = 0;
            foreach( var item in element.EnumerateArray() )
            {
               ScanAssignmentArrays( item, rule, $"{locationPath}[{index}]", recordName, occurrences );
               index++;
            }

            break;
         }
      }
   }

   private static void AddOccurrence( List<ScanOccurrence> occurrences, string? rawText, string locationKind, string locationPath, string? contextBefore, string? contextAfter, bool isTranslatable, string? metadataJson )
   {
      if( string.IsNullOrWhiteSpace( rawText ) || !isTranslatable ) return;

      if( ShouldSkipRawText( rawText ) ) return;

      var enrichedMetadataJson = BracketTokenPolicyAnalyzer.EnrichMetadata( rawText, metadataJson );
      var tokenPolicyMetadata = BracketTokenPolicyAnalyzer.Resolve( rawText, enrichedMetadataJson );
      if( tokenPolicyMetadata.ShouldSkipAutomaticTranslation ) return;

      occurrences.Add( new ScanOccurrence(
         rawText,
         locationKind,
         locationPath,
         contextBefore,
         contextAfter,
         isTranslatable,
         enrichedMetadataJson ) );
   }

   private static bool ShouldSkipRawText( string rawText )
   {
      return rawText.Contains( "$TEMPLATE", StringComparison.OrdinalIgnoreCase );
   }

   private static string? TryGetPropertyValue( JsonElement element, string propertyName )
   {
      if( element.ValueKind == JsonValueKind.Object
         && element.TryGetProperty( propertyName, out var property )
         && property.ValueKind == JsonValueKind.String )
      {
         return property.GetString();
      }

      return null;
   }

   private static List<string?> ReadStringArray( JsonElement arrayElement )
   {
      var values = new List<string?>();
      foreach( var value in arrayElement.EnumerateArray() )
      {
         values.Add( value.ValueKind == JsonValueKind.String ? value.GetString() : null );
      }

      return values;
   }

   private static bool TryParseAssignmentValue( string? rawValue, out string? fieldName, out string? fieldValue )
   {
      fieldName = null;
      fieldValue = null;

      if( string.IsNullOrWhiteSpace( rawValue ) ) return false;

      var values = rawValue.Split( '|' );
      if( values.Length != 2 ) return false;

      fieldName = values[ 0 ];
      fieldValue = values[ 1 ];

      if( string.IsNullOrWhiteSpace( fieldName ) || string.IsNullOrWhiteSpace( fieldValue ) ) return false;
      if( string.Equals( fieldValue, "null", StringComparison.Ordinal ) ) return false;

      return true;
   }

   private static string CreateRelativeLocation( string dataRoot, string filePath, string jsonPath )
   {
      var relativePath = Path.GetRelativePath( dataRoot, filePath )
         .Replace( Path.DirectorySeparatorChar, '/' )
         .Replace( Path.AltDirectorySeparatorChar, '/' );

      return relativePath + "::" + jsonPath;
   }

   private static string? CreateMetadata( params (string Key, string? Value)[] values )
   {
      var metadata = new Dictionary<string, string>( StringComparer.Ordinal );
      foreach( var (key, value) in values )
      {
         if( string.IsNullOrWhiteSpace( value ) ) continue;
         metadata[ key ] = value;
      }

      return metadata.Count == 0 ? null : JsonSerializer.Serialize( metadata );
   }

   private enum DirectoryRuleMode
   {
      Structured,
      SimplePairs,
      SimplePairKeys,
      SimpleValues,
      ConditionsSimple,
      GuiPropMaps,
      ArrayValues,
      AssignmentArrays,
   }

   private sealed record DirectoryRule( string RelativeDirectory, string Category, DirectoryRuleMode Mode, bool IsTranslatable, HashSet<string> AllowedFields, HashSet<string> ContainerFields )
   {
      public static DirectoryRule Structured( string relativeDirectory, string category, params string[] fields )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.Structured, true, CreateFieldSet( fields ), CreateFieldSet() );
      }

      public static DirectoryRule SimplePairs( string relativeDirectory, string category )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.SimplePairs, true, CreateFieldSet(), CreateFieldSet() );
      }

      public static DirectoryRule NamePairs( string relativeDirectory, string category )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.SimplePairKeys, true, CreateFieldSet(), CreateFieldSet() );
      }

      public static DirectoryRule SimpleValues( string relativeDirectory, string category )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.SimpleValues, true, CreateFieldSet(), CreateFieldSet() );
      }

      public static DirectoryRule ConditionsSimple( string relativeDirectory, string category )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.ConditionsSimple, true, CreateFieldSet(), CreateFieldSet() );
      }

      public static DirectoryRule GuiPropMaps( string relativeDirectory, string category, params string[] allowedKeys )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.GuiPropMaps, true, CreateFieldSet( allowedKeys ), CreateFieldSet() );
      }

      public static DirectoryRule ArrayValues( string relativeDirectory, string category, params string[] allowedKeys )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.ArrayValues, true, CreateFieldSet( allowedKeys ), CreateFieldSet() );
      }

      public static DirectoryRule ArrayValuesIgnoreOnly( string relativeDirectory, string category, params string[] allowedKeys )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.ArrayValues, false, CreateFieldSet( allowedKeys ), CreateFieldSet() );
      }

      public static DirectoryRule AssignmentArrays( string relativeDirectory, string category, string[] arrayFields, params string[] allowedFields )
      {
         return new DirectoryRule( relativeDirectory, category, DirectoryRuleMode.AssignmentArrays, true, CreateFieldSet( allowedFields ), CreateFieldSet( arrayFields ) );
      }

      private static HashSet<string> CreateFieldSet( params string[] values )
      {
         return new HashSet<string>( values, StringComparer.Ordinal );
      }
   }
}
