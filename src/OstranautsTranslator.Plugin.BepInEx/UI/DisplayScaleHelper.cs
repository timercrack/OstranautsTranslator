using System;
using System.Runtime.InteropServices;

namespace OstranautsTranslator.Plugin.BepInEx.UI;

internal readonly struct DisplayScaleInfo
{
   public DisplayScaleInfo( float scale, float dpi )
   {
      Scale = scale;
      Dpi = dpi;
   }

   public float Scale { get; }

   public float Dpi { get; }
}

internal static class DisplayScaleHelper
{
   private const float BaseDpi = 96f;
   private const int LogPixelsX = 88;
   private const int LogPixelsY = 90;

   public static DisplayScaleInfo GetCurrent()
   {
      var dpi = TryGetWindowDpi();
      if( dpi <= 0f )
      {
         dpi = TryGetSystemDpi();
      }

      if( dpi <= 0f )
      {
         dpi = BaseDpi;
      }

      var scale = Clamp( dpi / BaseDpi, 1f, 3f );
      return new DisplayScaleInfo( scale, dpi );
   }

   private static float TryGetWindowDpi()
   {
      if( !IsWindows() ) return 0f;

      try
      {
         var windowHandle = GetActiveWindow();
         if( windowHandle == IntPtr.Zero )
         {
            windowHandle = GetForegroundWindow();
         }

         return windowHandle == IntPtr.Zero
            ? 0f
            : GetDpiForWindow( windowHandle );
      }
      catch( EntryPointNotFoundException )
      {
         return 0f;
      }
      catch( DllNotFoundException )
      {
         return 0f;
      }
   }

   private static float TryGetSystemDpi()
   {
      if( !IsWindows() ) return 0f;

      try
      {
         var dpi = GetDpiForSystem();
         if( dpi > 0 )
         {
            return dpi;
         }
      }
      catch( EntryPointNotFoundException )
      {
      }
      catch( DllNotFoundException )
      {
      }

      IntPtr deviceContext = IntPtr.Zero;
      try
      {
         deviceContext = GetDC( IntPtr.Zero );
         if( deviceContext == IntPtr.Zero ) return 0f;

         var dpiX = GetDeviceCaps( deviceContext, LogPixelsX );
         var dpiY = GetDeviceCaps( deviceContext, LogPixelsY );
         return Math.Max( dpiX, dpiY );
      }
      catch( DllNotFoundException )
      {
         return 0f;
      }
      finally
      {
         if( deviceContext != IntPtr.Zero )
         {
            ReleaseDC( IntPtr.Zero, deviceContext );
         }
      }
   }

   private static bool IsWindows()
   {
      return Environment.OSVersion.Platform == PlatformID.Win32NT;
   }

   private static float Clamp( float value, float min, float max )
   {
      if( value < min ) return min;
      if( value > max ) return max;
      return value;
   }

   [DllImport( "user32.dll" )]
   private static extern uint GetDpiForWindow( IntPtr windowHandle );

   [DllImport( "user32.dll" )]
   private static extern uint GetDpiForSystem();

   [DllImport( "user32.dll" )]
   private static extern IntPtr GetActiveWindow();

   [DllImport( "user32.dll" )]
   private static extern IntPtr GetForegroundWindow();

   [DllImport( "user32.dll" )]
   private static extern IntPtr GetDC( IntPtr windowHandle );

   [DllImport( "user32.dll" )]
   private static extern int ReleaseDC( IntPtr windowHandle, IntPtr deviceContext );

   [DllImport( "gdi32.dll" )]
   private static extern int GetDeviceCaps( IntPtr deviceContext, int index );
}