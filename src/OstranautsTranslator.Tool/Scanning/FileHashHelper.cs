using System.Security.Cryptography;
using System.Text;

namespace OstranautsTranslator.Tool.Scanning;

internal static class FileHashHelper
{
   public static string ComputeFileHash( string path )
   {
      using var stream = File.OpenRead( path );
      var hash = SHA256.HashData( stream );
      return Convert.ToHexString( hash ).ToLowerInvariant();
   }

   public static string ComputeTextHash( string value )
   {
      var hash = SHA256.HashData( Encoding.UTF8.GetBytes( value ) );
      return Convert.ToHexString( hash ).ToLowerInvariant();
   }
}
