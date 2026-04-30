using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OstranautsTranslator.Core;
using OstranautsTranslator.Tool;

namespace OstranautsTranslator.Tool.Scanning;

internal sealed class AssemblyCSharpScanner
{
   private const string AssemblyRelativePath = "Ostranauts_Data/Managed/Assembly-CSharp.dll";

   private static readonly IReadOnlyList<CuratedTypeRule> Rules =
   [
      CuratedTypeRule.ByName( "LoadTip", "fallback-load-tips" ),
      CuratedTypeRule.ByName( "GUITooltip", "tooltip-ui" ),
      CuratedTypeRule.ByName( "GUITooltip2", "tooltip-ui" ),
      CuratedTypeRule.ByName( "GUIFinance", "finance-ui" ),
      CuratedTypeRule.ByName( "BeatManager", "beat-manager" ),
      CuratedTypeRule.ByName( "CondOwner", "cond-owner-runtime" ),
      CuratedTypeRule.ByName( "CrewSim", "crewsim-runtime" ),
      CuratedTypeRule.ByName( "CCTV", "cctv-runtime" ),
      CuratedTypeRule.ByName( "AIShipManager", "ai-ship-manager" ),
      CuratedTypeRule.ByName( "GUIDockSys", "docksys-runtime" ),
      CuratedTypeRule.ByName( "PDAVisualisers", "pda-visualisers" ),
      CuratedTypeRule.ByName( "Powered", "powered-runtime" ),
      CuratedTypeRule.ByName( "Ship", "ship-runtime" ),
      CuratedTypeRule.ByName( "WorkManager", "work-manager" ),
      CuratedTypeRule.ByFullName( "Ostranauts.Systems.Ferry", "ferry-runtime" ),
      CuratedTypeRule.ByNamespacePrefix( "Ostranauts.Core.Tutorials", "tutorial-runtime" ),
      CuratedTypeRule.ByFullName( "Ostranauts.Ships.Commands.Guard", "ships-command-guard" ),
      CuratedTypeRule.ByFullName( "Ostranauts.Ships.Commands.Lurk", "ships-command-lurk" ),
   ];

   public IEnumerable<SourceScanResult> Scan( string gameRootPath )
   {
      var assemblyPath = Path.Combine( gameRootPath, "Ostranauts_Data", "Managed", "Assembly-CSharp.dll" );
      if( !File.Exists( assemblyPath ) ) yield break;

      var result = ScanAssembly( assemblyPath );
      if( result.Occurrences.Count > 0 )
      {
         yield return result;
      }
   }

   private static SourceScanResult ScanAssembly( string assemblyPath )
   {
      var fileInfo = new FileInfo( assemblyPath );
      var occurrences = new List<ScanOccurrence>();
      var assembly = AssemblyDefinition.ReadAssembly( assemblyPath, new ReaderParameters
      {
         InMemory = true,
         ReadSymbols = false,
         ReadingMode = ReadingMode.Deferred,
      } );

      foreach( var module in assembly.Modules )
      {
         foreach( var type in module.Types )
         {
            ScanType( type, MatchRule( type ), occurrences );
         }
      }

      return new SourceScanResult(
         AssemblyRelativePath,
         RuntimeSourceOrigins.DecompiledDll,
         fileInfo.Exists ? fileInfo.Length : 0,
         new DateTimeOffset( fileInfo.LastWriteTimeUtc ),
         FileHashHelper.ComputeFileHash( assemblyPath ),
         occurrences );
   }

   private static void ScanType( TypeDefinition type, CuratedTypeRule? activeRule, List<ScanOccurrence> occurrences )
   {
      var rule = activeRule ?? MatchRule( type );
      if( rule != null )
      {
         foreach( var method in type.Methods )
         {
            ScanMethod( type, method, rule, occurrences );
         }
      }

      foreach( var nestedType in type.NestedTypes )
      {
         ScanType( nestedType, rule, occurrences );
      }
   }

   private static CuratedTypeRule? MatchRule( TypeDefinition type )
   {
      foreach( var rule in Rules )
      {
         if( rule.IsMatch( type ) )
         {
            return rule;
         }
      }

      return null;
   }

   private static void ScanMethod( TypeDefinition type, MethodDefinition method, CuratedTypeRule rule, List<ScanOccurrence> occurrences )
   {
      if( !method.HasBody ) return;

      var instructions = method.Body.Instructions;
      for( var i = 0; i < instructions.Count; i++ )
      {
         var instruction = instructions[ i ];
         if( instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string literal )
         {
            continue;
         }

         var nextCallDisplayName = GetNextCallDisplayName( instructions, i );
         if( !ShouldCaptureLiteral( literal, nextCallDisplayName ) )
         {
            continue;
         }

         occurrences.Add( new ScanOccurrence(
            literal,
            "managed-il-string-literal",
            $"{AssemblyRelativePath}::{GetTypeDisplayName( type )}::{GetMethodDisplayName( method )}@IL_{instruction.Offset:X4}",
            GetMethodDisplayName( method ),
            nextCallDisplayName,
            true,
            BracketTokenPolicyAnalyzer.EnrichMetadata(
               literal,
               CreateMetadata(
                  ( "category", rule.Category ),
                  ( "assembly", AssemblyRelativePath ),
                  ( "type", GetTypeDisplayName( type ) ),
                  ( "method", GetMethodDisplayName( method ) ),
                  ( "il_offset", instruction.Offset.ToString( "X4" ) ),
                  ( "type_rule", rule.RuleKey ) ) ) ) );
      }
   }

   private static string GetTypeDisplayName( TypeDefinition type )
   {
      return type.FullName.Replace( '/', '+' );
   }

   private static string GetMethodDisplayName( MethodDefinition method )
   {
      return method.FullName.Replace( '/', '+' );
   }

   private static string? GetNextCallDisplayName( IList<Instruction> instructions, int startIndex )
   {
      var endIndex = Math.Min( instructions.Count, startIndex + 5 );
      for( var i = startIndex + 1; i < endIndex; i++ )
      {
         var instruction = instructions[ i ];
         if( instruction.OpCode.Code is not ( Code.Call or Code.Callvirt or Code.Newobj ) )
         {
            continue;
         }

         if( instruction.Operand is MethodReference methodReference )
         {
            return methodReference.FullName.Replace( '/', '+' );
         }
      }

      return null;
   }

   private static bool ShouldCaptureLiteral( string literal, string? nextCallDisplayName )
   {
      var trimmed = literal.Trim();
      if( trimmed.Length < 2 ) return false;
      if( !trimmed.Any( char.IsLetter ) ) return false;
      if( LooksLikePlaceholderLiteral( trimmed ) ) return false;
      if( LooksLikeDeveloperNoiseLiteral( trimmed, nextCallDisplayName ) ) return false;
      if( trimmed.Contains( '/' ) || trimmed.Contains( '\\' ) ) return false;
      if( LooksLikeAudioOrUiIdentifier( trimmed ) ) return false;
      if( IsNumericFormatSpecifier( trimmed ) ) return false;

      var containsMarkup = ContainsUserFacingMarkup( trimmed );
      var looksTextRelated = LooksLikeTextRelatedUsage( nextCallDisplayName );
      if( LooksLikeCodeIdentifier( trimmed ) && !containsMarkup && !looksTextRelated ) return false;
      if( containsMarkup || looksTextRelated ) return true;
      return LooksLikeNaturalSingleToken( trimmed );
   }

   private static bool LooksLikeAudioOrUiIdentifier( string value )
   {
      return value.StartsWith( "pnl", StringComparison.Ordinal )
         || value.StartsWith( "btn", StringComparison.Ordinal )
         || value.StartsWith( "txt", StringComparison.Ordinal )
         || value.StartsWith( "chk", StringComparison.Ordinal )
         || value.StartsWith( "bmp", StringComparison.Ordinal )
         || value.StartsWith( "cg", StringComparison.Ordinal )
         || value.StartsWith( "GUI", StringComparison.Ordinal )
         || value.StartsWith( "ShipUI", StringComparison.Ordinal )
         || value.StartsWith( "Ostranauts.", StringComparison.Ordinal );
   }

   private static bool IsNumericFormatSpecifier( string value )
   {
      if( value.Length == 1 && char.IsLetter( value[ 0 ] ) )
      {
         return true;
      }

      return value.Length >= 2
         && char.IsLetter( value[ 0 ] )
         && value.Skip( 1 ).All( char.IsDigit );
   }

   private static bool LooksLikeCodeIdentifier( string value )
   {
      if( value.StartsWith( "str", StringComparison.Ordinal ) && value.Length > 3 && char.IsUpper( value[ 3 ] ) )
      {
         return true;
      }

      if( value.StartsWith( "m_", StringComparison.Ordinal ) || value.StartsWith( "_", StringComparison.Ordinal ) )
      {
         return true;
      }

      if( value.Contains( "::", StringComparison.Ordinal ) || value.Contains( '_', StringComparison.Ordinal ) )
      {
         return true;
      }

      if( char.IsLower( value[ 0 ] ) && value.Skip( 1 ).Any( char.IsUpper ) )
      {
         return true;
      }

      return value.Length > 24 && value.All( char.IsLetterOrDigit );
   }

   private static bool ContainsUserFacingMarkup( string value )
   {
      return value.Contains( '<' )
         || value.Contains( '>' )
         || value.Contains( '[' )
         || value.Contains( ']' )
         || value.Contains( '(' )
         || value.Contains( ')' )
         || value.Contains( ':' )
         || value.Contains( ',' )
         || value.Contains( ';' )
         || value.Contains( '?' )
         || value.Contains( '!' )
         || value.Contains( '%' )
         || value.Contains( '|' )
         || value.Contains( '\n' )
         || value.Contains( ' ' );
   }

   private static bool LooksLikeNaturalSingleToken( string value )
   {
      if( value.All( char.IsLower ) )
      {
         return true;
      }

      if( value.Length <= 3 && value.All( char.IsUpper ) )
      {
         return true;
      }

      return char.IsUpper( value[ 0 ] )
         && value.Skip( 1 ).All( char.IsLower );
   }

   private static bool LooksLikePlaceholderLiteral( string value )
   {
      if( string.Equals( value, "null", StringComparison.OrdinalIgnoreCase ) )
      {
         return true;
      }

      return value.Length >= 3 && value.All( ch => ch == 'X' || ch == 'x' );
   }

   private static bool LooksLikeTextRelatedUsage( string? nextCallDisplayName )
   {
      if( string.IsNullOrWhiteSpace( nextCallDisplayName ) )
      {
         return false;
      }

      return nextCallDisplayName.Contains( "StringBuilder::Append", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "System.String::Concat", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "System.String::Format", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "::LogMessage(", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "::SetToolTip(", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "::SetText(", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "::SetPersistentRef(", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "::set_text(", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "GUI::Label(", StringComparison.Ordinal )
         || nextCallDisplayName.Contains( "GUI::Button(", StringComparison.Ordinal );
   }

   private static bool LooksLikeDeveloperNoiseLiteral( string value, string? nextCallDisplayName )
   {
      if( !string.IsNullOrWhiteSpace( nextCallDisplayName )
         && ( nextCallDisplayName.Contains( "UnityEngine.Debug::Log(", StringComparison.Ordinal )
            || nextCallDisplayName.Contains( "UnityEngine.Debug::LogWarning(", StringComparison.Ordinal )
            || nextCallDisplayName.Contains( "UnityEngine.Debug::LogError(", StringComparison.Ordinal )
            || nextCallDisplayName.Contains( "UnityEngine.Object::set_name(", StringComparison.Ordinal ) ) )
      {
         return true;
      }

      return value.Contains( "**Debug Commands", StringComparison.Ordinal )
         || value.StartsWith( "Debug Command", StringComparison.Ordinal )
         || value.StartsWith( "Initializing Crewsim Debug", StringComparison.Ordinal );
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

   private sealed record CuratedTypeRule( string? TypeName, string? FullName, string? NamespacePrefix, string Category )
   {
      public string RuleKey => FullName ?? NamespacePrefix ?? TypeName ?? string.Empty;

      public static CuratedTypeRule ByName( string typeName, string category )
      {
         return new CuratedTypeRule( typeName, null, null, category );
      }

      public static CuratedTypeRule ByFullName( string fullName, string category )
      {
         return new CuratedTypeRule( null, fullName, null, category );
      }

      public static CuratedTypeRule ByNamespacePrefix( string namespacePrefix, string category )
      {
         return new CuratedTypeRule( null, null, namespacePrefix, category );
      }

      public bool IsMatch( TypeDefinition type )
      {
         var typeDisplayName = GetTypeDisplayName( type );

         if( !string.IsNullOrWhiteSpace( FullName ) && string.Equals( GetTypeDisplayName( type ), FullName, StringComparison.Ordinal ) )
         {
            return true;
         }

         if( !string.IsNullOrWhiteSpace( NamespacePrefix )
            && ( string.Equals( typeDisplayName, NamespacePrefix, StringComparison.Ordinal )
               || typeDisplayName.StartsWith( NamespacePrefix + ".", StringComparison.Ordinal )
               || typeDisplayName.StartsWith( NamespacePrefix + "+", StringComparison.Ordinal ) ) )
         {
            return true;
         }

         return !string.IsNullOrWhiteSpace( TypeName )
            && string.Equals( type.Name, TypeName, StringComparison.Ordinal );
      }
   }
}
