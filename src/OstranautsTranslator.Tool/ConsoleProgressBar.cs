namespace OstranautsTranslator.Tool;

internal sealed class ConsoleProgressBar : IDisposable
{
   private readonly string _label;
   private readonly int _total;
   private readonly bool _interactive;
   private readonly int _barWidth;
   private int _lastRenderedLength;
   private bool _completed;

   public ConsoleProgressBar( string label, int total, int barWidth = 28 )
   {
      _label = string.IsNullOrWhiteSpace( label ) ? "Progress" : label.Trim();
      _total = Math.Max( 0, total );
      _barWidth = Math.Max( 10, barWidth );
      _interactive = !Console.IsOutputRedirected;
   }

   public void Report( int completed, string? detail = null )
   {
      var clampedCompleted = Math.Max( 0, Math.Min( completed, Math.Max( _total, completed ) ) );
      var effectiveTotal = Math.Max( _total, 1 );
      var ratio = Math.Clamp( (double)clampedCompleted / effectiveTotal, 0d, 1d );
      var filled = (int)Math.Round( ratio * _barWidth, MidpointRounding.AwayFromZero );
      filled = Math.Clamp( filled, 0, _barWidth );

      var line = $"{_label} [{new string( '#', filled )}{new string( '-', _barWidth - filled )}] {clampedCompleted}/{_total}";
      if( !string.IsNullOrWhiteSpace( detail ) )
      {
         line += " " + detail.Trim();
      }

      if( _interactive )
      {
         var paddedLine = line.PadRight( Math.Max( _lastRenderedLength, line.Length ) );
         Console.Write( "\r" + paddedLine );
         _lastRenderedLength = paddedLine.Length;

         if( _total > 0 && clampedCompleted >= _total )
         {
            Console.WriteLine();
            _completed = true;
         }

         return;
      }

      Console.WriteLine( line );
      if( _total > 0 && clampedCompleted >= _total )
      {
         _completed = true;
      }
   }

   public void Dispose()
   {
      if( _interactive && !_completed && _lastRenderedLength > 0 )
      {
         Console.WriteLine();
      }
   }
}