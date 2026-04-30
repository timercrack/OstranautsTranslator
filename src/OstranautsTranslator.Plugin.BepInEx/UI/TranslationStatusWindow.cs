using System;
using System.Globalization;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx.UI;

internal sealed class TranslationStatusWindow
{
   private const int WindowId = 5464332;
   private const float DefaultWindowWidth = 560f;
   private const float DefaultWindowHeight = 564f;
   private const float MinWindowWidth = 460f;
   private const float MinWindowHeight = 548f;
   private const float ResizeHandleSize = 20f;
   private const float ResizeHandleMargin = 6f;
   private const float WindowEdgeMargin = 8f;
   private const float WarningBoxHeight = 56f;
   private readonly Func<TranslationStatusSnapshot> _getSnapshot;
   private Rect _windowRect;
   private bool _displayScaleInitialized;
   private float _displayScale = 1f;
   private float _detectedDpi = 96f;
   private bool _isResizing;
   private Vector2 _resizeStartMousePosition;
   private float _resizeStartWidth;
   private float _resizeStartHeight;

   public TranslationStatusWindow( Func<TranslationStatusSnapshot> getSnapshot )
   {
      _getSnapshot = getSnapshot ?? throw new ArgumentNullException( nameof( getSnapshot ) );
   }

   public bool IsShown { get; set; }

   public void OnGUI()
   {
      ApplyDisplayScale( DisplayScaleHelper.GetCurrent() );

      var previousMatrix = GUI.matrix;
      var guiSkinState = GuiSkinFontState.CaptureAndApply( GUI.skin, 16f, 15f, 15f, 17f );
      try
      {
         if( Math.Abs( _displayScale - 1f ) >= 0.01f )
         {
            GUIUtility.ScaleAroundPivot( new Vector2( _displayScale, _displayScale ), new Vector2( 0f, 0f ) );
         }

         GUI.Box( _windowRect, string.Empty );
         _windowRect = GUI.Window( WindowId, _windowRect, CreateWindowUI, "---- OstranautsTranslator Status ----" );
      }
      finally
      {
         GUI.matrix = previousMatrix;
         guiSkinState.Restore();
      }
   }

   private void CreateWindowUI( int id )
   {
      var windowWidth = _windowRect.width;
      var windowHeight = _windowRect.height;
      var buttonWidth = 22f;
      var buttonHeight = 18f;
      var buttonTop = 4f;
      var closeButtonLeft = windowWidth - buttonWidth - 6f;

      if( GUI.Button( new Rect( closeButtonLeft, buttonTop, buttonWidth, buttonHeight ), "X" ) )
      {
         IsShown = false;
      }

      var snapshot = _getSnapshot();
      var y = 30f;

      GUI.Label( new Rect( 12f, y, windowWidth - 24f, 22f ), "F6 切换窗口" );
      y += 22f;

      GUI.Label( new Rect( 12f, y, windowWidth - 24f, 24f ), $"DPI {Math.Round( _detectedDpi, MidpointRounding.AwayFromZero ):0}   缩放 {Math.Round( _displayScale * 100f, MidpointRounding.AwayFromZero ):0}%   右下角拖拽调整宽高" );
      y += 28f;

      if( !string.IsNullOrWhiteSpace( snapshot.GameVersionReminder ) )
      {
         GUI.Box( new Rect( 12f, y, windowWidth - 24f, WarningBoxHeight ), string.Empty );
         GUI.Label( new Rect( 18f, y + 8f, windowWidth - 36f, WarningBoxHeight - 16f ), snapshot.GameVersionReminder );
         y += WarningBoxHeight + 8f;
      }

      GUI.Box( new Rect( 8f, y, windowWidth - 16f, windowHeight - y - 32f ), string.Empty );
      y += 12f;

      DrawRow( ref y, "插件状态", snapshot.PluginStatus, windowWidth );
      DrawRow( ref y, "当前游戏版本", snapshot.CurrentGameVersion, windowWidth );
      DrawRow( ref y, "已记录版本", snapshot.RecordedGameVersion, windowWidth );
      DrawRow( ref y, "版本检查", snapshot.GameVersionStatus, windowWidth );
      DrawRow( ref y, "目标语言", snapshot.TargetLanguage, windowWidth );
      DrawRow( ref y, "数据库状态", snapshot.DatabaseStatus, windowWidth );
      DrawRow( ref y, "已加载条目", snapshot.LoadedEntryCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "查库次数", snapshot.LookupCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "数据库命中", snapshot.DatabaseHitCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "native_mod 直忽略", snapshot.NativeModIgnoredCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "唯一 native_mod 忽略", snapshot.UniqueNativeModIgnoredCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "命中率", ( snapshot.HitRate * 100d ).ToString( "0.0", CultureInfo.InvariantCulture ) + "%", windowWidth );
      DrawRow( ref y, "实际替换", snapshot.ReplacementCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "唯一已替换", snapshot.UniqueReplacementCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "未命中", snapshot.MissCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "唯一未命中", snapshot.UniqueMissCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "已写入 runtime_source", snapshot.CapturedMissCount.ToString( CultureInfo.InvariantCulture ), windowWidth );
      DrawRow( ref y, "错误次数", snapshot.ErrorCount.ToString( CultureInfo.InvariantCulture ), windowWidth );

      var resizeHandleRect = GetResizeHandleRect( windowWidth, windowHeight );
      GUI.Box( resizeHandleRect, "↘" );
      HandleResize( Event.current, windowWidth, windowHeight );

      GUI.DragWindow( new Rect( 0f, 0f, windowWidth, 24f ) );
   }

   private void DrawRow( ref float y, string label, string value, float windowWidth )
   {
      var labelLeft = 18f;
      var labelWidth = Math.Max( 138f, Math.Min( 220f, windowWidth * 0.32f ) );
      var valueLeft = labelLeft + labelWidth + 14f;
      var valueWidth = Math.Max( 160f, windowWidth - valueLeft - 18f );
      GUI.Label( new Rect( labelLeft, y, labelWidth, 26f ), label );
      GUI.Label( new Rect( valueLeft, y, valueWidth, 26f ), value ?? string.Empty );
      y += 26f;
   }

   private void ApplyDisplayScale( DisplayScaleInfo displayScaleInfo )
   {
      var newScale = Clamp( displayScaleInfo.Scale, 1f, 3f );
      _detectedDpi = displayScaleInfo.Dpi;

      if( !_displayScaleInitialized )
      {
         _displayScaleInitialized = true;
         _displayScale = newScale;
         _windowRect = new Rect( 20f, 20f, DefaultWindowWidth, DefaultWindowHeight );
         return;
      }

      if( Math.Abs( newScale - _displayScale ) < 0.01f )
      {
         return;
      }

      _displayScale = newScale;
   }

   private void HandleResize( Event currentEvent, float windowWidth, float windowHeight )
   {
      if( currentEvent == null )
      {
         return;
      }

      var resizeHandleRect = GetResizeHandleRect( windowWidth, windowHeight );
      var mousePosition = currentEvent.mousePosition;

      switch( currentEvent.type )
      {
         case EventType.MouseDown:
            if( IsPointInsideRect( resizeHandleRect, mousePosition ) )
            {
               _isResizing = true;
               _resizeStartMousePosition = mousePosition;
               _resizeStartWidth = windowWidth;
               _resizeStartHeight = windowHeight;
               currentEvent.Use();
            }
            break;
         case EventType.MouseDrag:
            if( _isResizing )
            {
               ResizeWindow(
                  _resizeStartWidth + ( mousePosition.x - _resizeStartMousePosition.x ),
                  _resizeStartHeight + ( mousePosition.y - _resizeStartMousePosition.y ) );
               currentEvent.Use();
            }
            break;
         case EventType.MouseUp:
            if( _isResizing )
            {
               _isResizing = false;
               currentEvent.Use();
            }
            break;
      }
   }

   private void ResizeWindow( float desiredWidth, float desiredHeight )
   {
      _windowRect.width = Clamp( desiredWidth, MinWindowWidth, GetMaxWindowWidth() );
      _windowRect.height = Clamp( desiredHeight, MinWindowHeight, GetMaxWindowHeight() );
   }

   private float GetMaxWindowWidth()
   {
      if( Screen.width <= 0 )
      {
         return Math.Max( MinWindowWidth, _windowRect.width );
      }

      return Math.Max( MinWindowWidth, ( Screen.width / _displayScale ) - _windowRect.x - WindowEdgeMargin );
   }

   private float GetMaxWindowHeight()
   {
      if( Screen.height <= 0 )
      {
         return Math.Max( MinWindowHeight, _windowRect.height );
      }

      return Math.Max( MinWindowHeight, ( Screen.height / _displayScale ) - _windowRect.y - WindowEdgeMargin );
   }

   private Rect GetResizeHandleRect( float windowWidth, float windowHeight )
   {
      return new Rect(
         windowWidth - ResizeHandleSize - ResizeHandleMargin,
         windowHeight - ResizeHandleSize - ResizeHandleMargin,
         ResizeHandleSize,
         ResizeHandleSize );
   }

   private static bool IsPointInsideRect( Rect rect, Vector2 point )
   {
      return point.x >= rect.x
         && point.x <= rect.x + rect.width
         && point.y >= rect.y
         && point.y <= rect.y + rect.height;
   }

   private static float Clamp( float value, float min, float max )
   {
      if( value < min ) return min;
      if( value > max ) return max;
      return value;
   }

   private readonly struct GuiSkinFontState
   {
      private readonly GUISkin _skin;
      private readonly int? _labelFontSize;
      private readonly int? _buttonFontSize;
      private readonly int? _boxFontSize;
      private readonly int? _windowFontSize;

      private GuiSkinFontState( GUISkin skin )
      {
         _skin = skin;
         _labelFontSize = skin?.label != null ? skin.label.fontSize : null;
         _buttonFontSize = skin?.button != null ? skin.button.fontSize : null;
         _boxFontSize = skin?.box != null ? skin.box.fontSize : null;
         _windowFontSize = skin?.window != null ? skin.window.fontSize : null;
      }

      public static GuiSkinFontState CaptureAndApply( GUISkin skin, float labelFontSize, float buttonFontSize, float boxFontSize, float windowFontSize )
      {
         var state = new GuiSkinFontState( skin );
         if( skin?.label != null ) skin.label.fontSize = (int)Math.Round( labelFontSize );
         if( skin?.button != null ) skin.button.fontSize = (int)Math.Round( buttonFontSize );
         if( skin?.box != null ) skin.box.fontSize = (int)Math.Round( boxFontSize );
         if( skin?.window != null ) skin.window.fontSize = (int)Math.Round( windowFontSize );
         return state;
      }

      public void Restore()
      {
         if( _skin?.label != null && _labelFontSize.HasValue ) _skin.label.fontSize = _labelFontSize.Value;
         if( _skin?.button != null && _buttonFontSize.HasValue ) _skin.button.fontSize = _buttonFontSize.Value;
         if( _skin?.box != null && _boxFontSize.HasValue ) _skin.box.fontSize = _boxFontSize.Value;
         if( _skin?.window != null && _windowFontSize.HasValue ) _skin.window.fontSize = _windowFontSize.Value;
      }
   }
}