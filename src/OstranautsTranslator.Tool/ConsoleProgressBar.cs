namespace OstranautsTranslator.Tool;

internal sealed class ConsoleProgressBar : IDisposable
{
   private readonly string _label;
   private readonly int _total;
   private readonly bool _interactive;
   private readonly int _barWidth;
   private readonly bool _showEta;
   private readonly System.Diagnostics.Stopwatch? _stopwatch;
   private int _lastRenderedLength;
   private bool _completed;

   public ConsoleProgressBar( string label, int total, int barWidth = 28, bool showEta = false )
   {
      _label = string.IsNullOrWhiteSpace( label ) ? "Progress" : label.Trim();
      _total = Math.Max( 0, total );
      _barWidth = Math.Max( 10, barWidth );
      _interactive = !Console.IsOutputRedirected;
      _showEta = showEta;
      _stopwatch = showEta ? System.Diagnostics.Stopwatch.StartNew() : null;
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

      var elapsed = TryFormatElapsed();
      if( elapsed is not null )
      {
         line += " elapsed " + elapsed;
      }

      var averageRate = TryFormatAverageRate( clampedCompleted );
      if( averageRate is not null )
      {
         line += " avg " + averageRate;
      }

      var eta = TryFormatEta( clampedCompleted );
      if( eta is not null )
      {
         line += " ETA " + eta;
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

   private string? TryFormatEta( int completed )
   {
      if( !_showEta || _stopwatch is null || completed <= 0 || completed >= _total )
      {
         return null;
      }

      var remainingUnits = _total - completed;
      var remainingTicks = (long)Math.Round( _stopwatch.Elapsed.Ticks * ( remainingUnits / (double)completed ), MidpointRounding.AwayFromZero );
      var remaining = TimeSpan.FromTicks( Math.Max( 0, remainingTicks ) );
      if( remaining.TotalHours >= 1d )
      {
         return remaining.ToString( @"hh\:mm\:ss" );
      }

      return FormatDuration( remaining );
   }

   private string? TryFormatElapsed()
   {
      if( !_showEta || _stopwatch is null )
      {
         return null;
      }

      return FormatDuration( _stopwatch.Elapsed );
   }

   private string? TryFormatAverageRate( int completed )
   {
      if( !_showEta || _stopwatch is null || completed <= 0 )
      {
         return null;
      }

      var averageSecondsPerUnit = _stopwatch.Elapsed.TotalSeconds / completed;
      if( averageSecondsPerUnit >= 1d )
      {
         return $"{averageSecondsPerUnit:0.0}s/batch";
      }

      var averageUnitsPerSecond = completed / _stopwatch.Elapsed.TotalSeconds;
      if( double.IsFinite( averageUnitsPerSecond ) && averageUnitsPerSecond > 0d )
      {
         return $"{averageUnitsPerSecond:0.00} batch/s";
      }

      return null;
   }

   private static string FormatDuration( TimeSpan duration )
   {
      if( duration.TotalHours >= 1d )
      {
         return duration.ToString( @"hh\:mm\:ss" );
      }

      return duration.ToString( @"mm\:ss" );
   }
}