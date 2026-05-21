using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx.Hooks;

internal static class RuntimeTextComponentBypassHelper
{
   private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
   private static readonly ConditionalWeakTable<object, ComponentBypassState> ComponentBypassStates = new ConditionalWeakTable<object, ComponentBypassState>();
   private const string LoadMenuTypeName = "Ostranauts.UI.Loading.GUILoadMenu";
   private const string MessageDisplayTypeName = "Ostranauts.ShipGUIs.NavStation.GUIMessageDisplay";
   private static readonly string[] SocialCombatConfirmSourceTexts = { "Confirm", "Accept" };
   private static readonly string[] SocialCombatExitSourceTexts = { "Exit", "Cancel", "Close" };
   private const string SocialCombatActionsText = "行动";
   private const string SocialCombatPreviewText = "预览";
   private const string SocialCombatReviewText = "检视";
   private const string SocialCombatConfirmText = "确认";
   private const string SocialCombatExitText = "退出";

   public static bool ShouldBypassTranslation( object component )
   {
      if( component == null ) return false;

      var state = ComponentBypassStates.GetOrCreateValue( component );
      if( state.HasBypassDecision ) return state.ShouldBypass;

      var path = GetComponentPath( component );
      state.ShouldBypass = IsOptionsFilePanelPath( path )
         || IsLoadMenuPathText( component )
         || IsMessageDisplayText( component );
      state.HasBypassDecision = true;
      return state.ShouldBypass;
   }

   public static bool TryTranslateFixedText( object component, string value, out string translatedValue )
   {
      translatedValue = value;
      if( component == null ) return false;

      var path = GetComponentPath( component );
      if( string.IsNullOrWhiteSpace( path ) ) return false;

      if( path.EndsWith( "/pnlActions/ActionsLabel", StringComparison.OrdinalIgnoreCase )
      )
      {
         translatedValue = SocialCombatActionsText;
         return true;
      }

      if( path.EndsWith( "/pnlActions/ActionsLabelMandarin", StringComparison.OrdinalIgnoreCase ) )
      {
         translatedValue = SocialCombatActionsText;
         return true;
      }

      if( path.EndsWith( "/pnlPreview/txtEnglish", StringComparison.OrdinalIgnoreCase ) )
      {
         translatedValue = SocialCombatPreviewText;
         return true;
      }

      if( path.EndsWith( "/pnlPreview/txtMandarin", StringComparison.OrdinalIgnoreCase ) )
      {
         translatedValue = SocialCombatReviewText;
         return true;
      }

      if( path.EndsWith( "/pnlConfirm/btn/txtEnglish", StringComparison.OrdinalIgnoreCase ) )
      {
         translatedValue = SocialCombatConfirmText;
         return true;
      }

      if( path.EndsWith( "/pnlConfirm/btn/txtMandarin", StringComparison.OrdinalIgnoreCase ) )
      {
         translatedValue = SocialCombatConfirmText;
         return true;
      }

      if( path.EndsWith( "/pnlExit/btn/txtEnglish", StringComparison.OrdinalIgnoreCase ) )
      {
         translatedValue = SocialCombatExitText;
         return true;
      }

      if( path.EndsWith( "/pnlExit/btn/txtMandarin", StringComparison.OrdinalIgnoreCase ) )
      {
         translatedValue = SocialCombatExitText;
         return true;
      }

      if( string.IsNullOrWhiteSpace( value ) ) return false;

      var trimmed = value.Trim();
      if( path.Contains( "/pnlConfirm/", StringComparison.OrdinalIgnoreCase )
         && ( MatchesAnySourceOrTranslated( trimmed, SocialCombatConfirmSourceTexts, "UI.Text.Bypass.SocialCombat.ConfirmToken" )
            || MatchesAnySourceOrTranslated( trimmed, SocialCombatExitSourceTexts, "UI.Text.Bypass.SocialCombat.ExitToken" ) ) )
      {
         translatedValue = SocialCombatConfirmText;
         return true;
      }

      if( path.Contains( "/pnlExit/", StringComparison.OrdinalIgnoreCase )
         && ( MatchesAnySourceOrTranslated( trimmed, SocialCombatExitSourceTexts, "UI.Text.Bypass.SocialCombat.ExitToken" )
            || MatchesAnySourceOrTranslated( trimmed, SocialCombatConfirmSourceTexts, "UI.Text.Bypass.SocialCombat.ConfirmToken" ) ) )
      {
         translatedValue = SocialCombatExitText;
         return true;
      }

      return false;
   }

   private static bool MatchesAnySourceOrTranslated( string value, IReadOnlyList<string> sourceTexts, string hookName )
   {
      foreach( var sourceText in sourceTexts )
      {
         if( string.Equals( value, sourceText, StringComparison.OrdinalIgnoreCase ) )
         {
            return true;
         }

         var translatedSource = TranslateLiteral( sourceText, hookName + "." + sourceText );
         if( string.Equals( value, translatedSource, StringComparison.Ordinal ) )
         {
            return true;
         }
      }

      return false;
   }

   private static string TranslateLiteral( string value, string hookName )
   {
      return RuntimeTextHookHelper.TranslateTextValue( value, hookName );
   }

   private static bool IsOptionsFilePanelPath( string path )
   {
      return !string.IsNullOrWhiteSpace( path )
         && path.Contains( "/pnlFiles/", StringComparison.Ordinal )
         && path.EndsWith( "/boxFilePath/txt", StringComparison.Ordinal );
   }

   private static bool IsLoadMenuPathText( object component )
   {
      if( component == null ) return false;

      var current = RuntimeTextHookHelper.GetGameObject( component );
      while( current != null )
      {
         foreach( var owner in EnumerateComponents( current ) )
         {
            if( owner == null ) continue;

            var ownerType = owner.GetType();
            if( !string.Equals( ownerType.FullName, LoadMenuTypeName, StringComparison.Ordinal )
               && !string.Equals( ownerType.Name, "GUILoadMenu", StringComparison.Ordinal ) )
            {
               continue;
            }

            var textField = ownerType.GetField( "txtPath", InstanceFlags );
            if( ReferenceEquals( textField?.GetValue( owner ), component ) )
            {
               return true;
            }

            var textProperty = ownerType.GetProperty( "txtPath", InstanceFlags );
            if( ReferenceEquals( textProperty?.GetValue( owner, null ), component ) )
            {
               return true;
            }
         }

         current = RuntimeTextHookHelper.GetGameObject( RuntimeTextHookHelper.GetParentTransform( current ) );
      }

      return false;
   }

   private static bool IsMessageDisplayText( object component )
   {
      if( component == null ) return false;

      var current = RuntimeTextHookHelper.GetGameObject( component );
      while( current != null )
      {
         foreach( var owner in EnumerateComponents( current ) )
         {
            if( owner == null ) continue;

            var ownerType = owner.GetType();
            if( !string.Equals( ownerType.FullName, MessageDisplayTypeName, StringComparison.Ordinal )
               && !string.Equals( ownerType.Name, "GUIMessageDisplay", StringComparison.Ordinal ) )
            {
               continue;
            }

            if( IsOwnerTextField( ownerType, owner, "txtStatus", component )
               || IsOwnerTextField( ownerType, owner, "txtComms", component ) )
            {
               return true;
            }
         }

         current = RuntimeTextHookHelper.GetGameObject( RuntimeTextHookHelper.GetParentTransform( current ) );
      }

      return false;
   }

   private static bool IsOwnerTextField( Type ownerType, object owner, string memberName, object component )
   {
      var textField = ownerType.GetField( memberName, InstanceFlags );
      if( ReferenceEquals( textField?.GetValue( owner ), component ) )
      {
         return true;
      }

      var textProperty = ownerType.GetProperty( memberName, InstanceFlags );
      return ReferenceEquals( textProperty?.GetValue( owner, null ), component );
   }

   private static IEnumerable EnumerateComponents( GameObject gameObject )
   {
      if( gameObject == null ) yield break;

      var getComponentsMethod = typeof( GameObject ).GetMethod( "GetComponents", new[] { typeof( Type ) } );
      if( getComponentsMethod?.Invoke( gameObject, new object[] { typeof( Component ) } ) is not IEnumerable components ) yield break;

      foreach( var component in components )
      {
         if( component != null ) yield return component;
      }
   }

   internal static string GetComponentPath( object component )
   {
      if( component == null ) return string.Empty;

      var state = ComponentBypassStates.GetOrCreateValue( component );
      if( state.HasComponentPath ) return state.ComponentPath;

      var gameObject = RuntimeTextHookHelper.GetGameObject( component );
      if( gameObject == null )
      {
         state.ComponentPath = string.Empty;
         state.HasComponentPath = true;
         return string.Empty;
      }

      var segments = new Stack<string>();
      var current = gameObject;
      while( current != null )
      {
         var nameProperty = current.GetType().GetProperty( "name", InstanceFlags );
         segments.Push( nameProperty?.GetValue( current, null ) as string ?? string.Empty );

         var parentTransform = RuntimeTextHookHelper.GetParentTransform( current );
         if( parentTransform == null )
         {
            break;
         }

         var gameObjectProperty = parentTransform.GetType().GetProperty( "gameObject", InstanceFlags );
         current = gameObjectProperty?.GetValue( parentTransform, null ) as GameObject;
      }

      state.ComponentPath = segments.Count == 0 ? string.Empty : string.Join( "/", segments.ToArray() );
      state.HasComponentPath = true;
      return state.ComponentPath;
   }

   private sealed class ComponentBypassState
   {
      public bool HasBypassDecision { get; set; }
      public bool ShouldBypass { get; set; }
      public bool HasComponentPath { get; set; }
      public string ComponentPath { get; set; } = string.Empty;
   }
}

[HarmonyPatch]
internal static class UI_Text_OnEnable_Hook
{
   private static bool Prepare()
   {
      return UiTypeResolver.Get( "UnityEngine.UI.Text" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( UiTypeResolver.Get( "UnityEngine.UI.Text" ), "OnEnable" );
   }

   private static void Prefix( object __instance )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "UI.Text.OnEnable.before" );
   }

   private static void Postfix( object __instance )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "UI.Text.OnEnable.after" );
   }
}

[HarmonyPatch]
internal static class UI_Text_text_Hook
{
   private static bool Prepare()
   {
      return UiTypeResolver.Get( "UnityEngine.UI.Text" )?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public )?.GetSetMethod() != null;
   }

   private static MethodBase TargetMethod()
   {
      return UiTypeResolver.Get( "UnityEngine.UI.Text" )?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public )?.GetSetMethod();
   }

   private static void Prefix( object __instance, ref string value )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;

      if( RuntimeTextComponentBypassHelper.TryTranslateFixedText( __instance, value, out var fixedText ) )
      {
         value = fixedText;
         return;
      }

      value = OstranautsTranslatorPlugin.Translate( value, "UI.Text.text" );
      value = TooltipRuntimeTranslationHelper.TranslateEmbeddedPersonNames( value, "UI.Text.text" );
   }
}

[HarmonyPatch]
internal static class TMP_Text_text_Hook
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" )?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public )?.GetSetMethod() != null;
   }

   private static MethodBase TargetMethod()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" )?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public )?.GetSetMethod();
   }

   private static void Prefix( object __instance, ref string value )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;

      if( RuntimeTextComponentBypassHelper.TryTranslateFixedText( __instance, value, out var fixedText ) )
      {
         value = fixedText;
         return;
      }

      value = OstranautsTranslatorPlugin.Translate( value, "TMP_Text.text" );
      value = TooltipRuntimeTranslationHelper.TranslateEmbeddedPersonNames( value, "TMP_Text.text" );
   }
}

[HarmonyPatch]
internal static class TMP_Text_SetText_StringBuilder_Hook
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TMP_Text" ), "SetText", new[] { typeof( StringBuilder ) } );
   }

   private static void Prefix( object __instance, ref StringBuilder __0 )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateStringBuilder( ref __0, "TMP_Text.SetText(StringBuilder)" );
   }

   private static void Postfix( object __instance )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "TMP_Text.SetText(StringBuilder).post" );
   }
}

[HarmonyPatch]
internal static class TMP_Text_SetText_StringFloatFloatFloat_Hook
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TMP_Text" ), "SetText", new[] { typeof( string ), typeof( float ), typeof( float ), typeof( float ) } );
   }

   private static void Prefix( object __instance, ref string __0 )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;

      if( RuntimeTextComponentBypassHelper.TryTranslateFixedText( __instance, __0, out var fixedText ) )
      {
         __0 = fixedText;
         return;
      }

      __0 = OstranautsTranslatorPlugin.Translate( __0, "TMP_Text.SetText(string,float,float,float)" );
   }

   private static void Postfix( object __instance )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "TMP_Text.SetText(string,float,float,float).post" );
   }
}

[HarmonyPatch]
internal static class TMP_Text_SetCharArray_Hook1
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TMP_Text" ), "SetCharArray", new[] { typeof( char[] ) } );
   }

   private static void Prefix( ref char[] __0 )
   {
      // TMP frequently reuses internal buffers here. Translate the finalized text in Postfix instead.
   }

   private static void Postfix( object __instance )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "TMP_Text.SetCharArray(char[]).post" );
   }
}

[HarmonyPatch]
internal static class TMP_Text_SetCharArray_Hook2
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TMP_Text" ), "SetCharArray", new[] { typeof( char[] ), typeof( int ), typeof( int ) } );
   }

   private static void Prefix( ref char[] __0, ref int __1, ref int __2 )
   {
      // TMP passes array slices here; translating the slice corrupts rich-text boundaries.
   }

   private static void Postfix( object __instance )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "TMP_Text.SetCharArray(char[],int,int).post" );
   }
}

[HarmonyPatch]
internal static class TMP_Text_SetCharArray_Hook3
{
   private static MethodBase ResolveTargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TMP_Text" ), "SetCharArray", new[] { typeof( int[] ), typeof( int ), typeof( int ) } );
   }

   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" ) != null && ResolveTargetMethod() != null;
   }

   private static MethodBase TargetMethod()
   {
      return ResolveTargetMethod();
   }

   private static void Prefix( ref int[] __0, ref int __1, ref int __2 )
   {
      // TMP passes array slices here; translating the slice corrupts rich-text boundaries.
   }

   private static void Postfix( object __instance )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "TMP_Text.SetCharArray(int[],int,int).post" );
   }
}

[HarmonyPatch]
internal static class TextMesh_text_Hook
{
   private static bool Prepare()
   {
      var textMeshType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.TextMesh" );
      return textMeshType?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public )?.GetSetMethod() != null;
   }

   private static MethodBase TargetMethod()
   {
      var textMeshType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.TextMesh" );
      return textMeshType?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public )?.GetSetMethod();
   }

   private static void Prefix( ref string value )
   {
      value = OstranautsTranslatorPlugin.Translate( value, "TextMesh.text" );
   }
}

[HarmonyPatch]
internal static class GameObject_SetActive_Hook
{
   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GameObject ), "SetActive", new[] { typeof( bool ) } );
   }

   private static void Postfix( GameObject __instance, bool value )
   {
      if( value )
      {
         RuntimeTextHookHelper.TranslateHierarchy( __instance, "GameObject.SetActive" );
      }
   }
}

[HarmonyPatch]
internal static class UIElements_TextElement_text_Hook
{
   private static bool Prepare()
   {
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.UIElements.TextElement" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var textElementType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.UIElements.TextElement" );
      return textElementType?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetSetMethod( true );
   }

   private static void Prefix( ref string value )
   {
      value = OstranautsTranslatorPlugin.Translate( value, "UIElements.TextElement.text" );
   }
}

[HarmonyPatch]
internal static class NGUI_UILabel_text_Hook
{
   private static bool Prepare()
   {
      return RuntimeTypeResolver.FindLoadedType( "UILabel" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var labelType = RuntimeTypeResolver.FindLoadedType( "UILabel" );
      return labelType?.GetProperty( "text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )?.GetSetMethod( true );
   }

   private static void Prefix( ref string value )
   {
      value = OstranautsTranslatorPlugin.Translate( value, "UILabel.text" );
   }
}

[HarmonyPatch]
internal static class NGUI_UIRect_OnEnable_Hook
{
   private static bool Prepare()
   {
      return RuntimeTypeResolver.FindLoadedType( "UIRect" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( RuntimeTypeResolver.FindLoadedType( "UIRect" ), "OnEnable" );
   }

   private static void Prefix( object __instance )
   {
      if( IsUILabel( __instance ) )
      {
         RuntimeTextHookHelper.TranslateCurrentText( __instance, "UIRect.OnEnable.before" );
      }
   }

   private static void Postfix( object __instance )
   {
      if( IsUILabel( __instance ) )
      {
         RuntimeTextHookHelper.TranslateCurrentText( __instance, "UIRect.OnEnable.after" );
      }
   }

   private static bool IsUILabel( object instance )
   {
      var labelType = RuntimeTypeResolver.FindLoadedType( "UILabel" );
      return labelType != null && instance != null && labelType.IsInstanceOfType( instance );
   }
}

[HarmonyPatch]
internal static class GUI_Label_String_Hook
{
   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "Label", new[] { typeof( Rect ), typeof( string ) } );
   }

   private static void Prefix( ref string __1 )
   {
      __1 = OstranautsTranslatorPlugin.Translate( __1, "GUI.Label(string)" );
   }
}

[HarmonyPatch]
internal static class GUI_Box_String_Hook
{
   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "Box", new[] { typeof( Rect ), typeof( string ) } );
   }

   private static void Prefix( ref string __1 )
   {
      __1 = OstranautsTranslatorPlugin.Translate( __1, "GUI.Box(string)" );
   }
}

[HarmonyPatch]
internal static class GUI_Button_String_Hook
{
   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "Button", new[] { typeof( Rect ), typeof( string ) } );
   }

   private static void Prefix( ref string __1 )
   {
      __1 = OstranautsTranslatorPlugin.Translate( __1, "GUI.Button(string)" );
   }
}

[HarmonyPatch]
internal static class GUI_Window_String_Hook
{
   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "Window", new[] { typeof( int ), typeof( Rect ), typeof( GUI.WindowFunction ), typeof( string ) } );
   }

   private static void Prefix( ref string __3 )
   {
      __3 = OstranautsTranslatorPlugin.Translate( __3, "GUI.Window(string)" );
   }
}

[HarmonyPatch]
internal static class GUI_BeginGroup_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "BeginGroup", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "BeginGroup", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.BeginGroup" );
   }
}

[HarmonyPatch]
internal static class GUI_BeginGroup_Hook_New
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "BeginGroup", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), typeof( Vector2 ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "BeginGroup", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), typeof( Vector2 ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.BeginGroup.new" );
   }
}

[HarmonyPatch]
internal static class GUI_Box_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "Box", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "Box", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.Box" );
   }
}

[HarmonyPatch]
internal static class GUI_DoLabel_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoLabel", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( IntPtr ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoLabel", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( IntPtr ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoLabel" );
   }
}

[HarmonyPatch]
internal static class GUI_DoLabel_Hook_New
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoLabel", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoLabel", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoLabel.new" );
   }
}

[HarmonyPatch]
internal static class GUI_DoButton_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoButton", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( IntPtr ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoButton", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( IntPtr ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoButton" );
   }
}

[HarmonyPatch]
internal static class GUI_DoButton_Hook_New
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoButton", new[] { typeof( Rect ), typeof( int ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoButton", new[] { typeof( Rect ), typeof( int ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoButton.new" );
   }
}

[HarmonyPatch]
internal static class GUI_DoButtonGrid_Hook
{
   private static bool Prepare()
   {
      var guiContentArrayType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" )?.MakeArrayType();
      return AccessTools.Method( typeof( GUI ), "DoButtonGrid", new[] { typeof( Rect ), typeof( int ), guiContentArrayType, typeof( int ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var guiContentArrayType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" )?.MakeArrayType();
      return AccessTools.Method( typeof( GUI ), "DoButtonGrid", new[] { typeof( Rect ), typeof( int ), guiContentArrayType, typeof( int ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ) } );
   }

   private static void Prefix( Array contents )
   {
      RuntimeTextHookHelper.TranslateGuiContentArray( contents, "GUI.DoButtonGrid" );
   }
}

[HarmonyPatch]
internal static class GUI_DoButtonGrid_Hook_2018
{
   private static bool Prepare()
   {
      var toolbarButtonSizeType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUI+ToolbarButtonSize" );
      var guiContentArrayType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" )?.MakeArrayType();
      return AccessTools.Method( typeof( GUI ), "DoButtonGrid", new[] { typeof( Rect ), typeof( int ), guiContentArrayType, typeof( string[] ), typeof( int ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), toolbarButtonSizeType } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var toolbarButtonSizeType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUI+ToolbarButtonSize" );
      var guiContentArrayType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" )?.MakeArrayType();
      return AccessTools.Method( typeof( GUI ), "DoButtonGrid", new[] { typeof( Rect ), typeof( int ), guiContentArrayType, typeof( string[] ), typeof( int ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), toolbarButtonSizeType } );
   }

   private static void Prefix( Array contents )
   {
      RuntimeTextHookHelper.TranslateGuiContentArray( contents, "GUI.DoButtonGrid.2018" );
   }
}

[HarmonyPatch]
internal static class GUI_DoButtonGrid_Hook_2019
{
   private static bool Prepare()
   {
      var toolbarButtonSizeType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUI+ToolbarButtonSize" );
      var guiContentArrayType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" )?.MakeArrayType();
      return AccessTools.Method( typeof( GUI ), "DoButtonGrid", new[] { typeof( Rect ), typeof( int ), guiContentArrayType, typeof( string[] ), typeof( int ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), toolbarButtonSizeType, typeof( bool[] ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var toolbarButtonSizeType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUI+ToolbarButtonSize" );
      var guiContentArrayType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" )?.MakeArrayType();
      return AccessTools.Method( typeof( GUI ), "DoButtonGrid", new[] { typeof( Rect ), typeof( int ), guiContentArrayType, typeof( string[] ), typeof( int ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), typeof( GUIStyle ), toolbarButtonSizeType, typeof( bool[] ) } );
   }

   private static void Prefix( Array contents )
   {
      RuntimeTextHookHelper.TranslateGuiContentArray( contents, "GUI.DoButtonGrid.2019" );
   }
}

[HarmonyPatch]
internal static class GUI_DoToggle_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoToggle", new[] { typeof( Rect ), typeof( int ), typeof( bool ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( IntPtr ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoToggle", new[] { typeof( Rect ), typeof( int ), typeof( bool ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( IntPtr ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoToggle" );
   }
}

[HarmonyPatch]
internal static class GUI_DoToggle_Hook_New
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoToggle", new[] { typeof( Rect ), typeof( int ), typeof( bool ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoToggle", new[] { typeof( Rect ), typeof( int ), typeof( bool ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoToggle.new" );
   }
}

[HarmonyPatch]
internal static class GUI_DoRepeatButton_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoRepeatButton", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.FocusType" ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoRepeatButton", new[] { typeof( Rect ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.FocusType" ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoRepeatButton" );
   }
}

[HarmonyPatch]
internal static class GUI_DoModalWindow_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoModalWindow", new[] { typeof( int ), typeof( Rect ), typeof( GUI.WindowFunction ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), typeof( GUISkin ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoModalWindow", new[] { typeof( int ), typeof( Rect ), typeof( GUI.WindowFunction ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), typeof( GUISkin ) } );
   }

   private static void Prefix( object content )
   {
      RuntimeTextHookHelper.TranslateGuiContent( content, "GUI.DoModalWindow" );
   }
}

[HarmonyPatch]
internal static class GUI_DoWindow_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( GUI ), "DoWindow", new[] { typeof( int ), typeof( Rect ), typeof( GUI.WindowFunction ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), typeof( GUISkin ), typeof( bool ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GUI ), "DoWindow", new[] { typeof( int ), typeof( Rect ), typeof( GUI.WindowFunction ), RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ), typeof( GUIStyle ), typeof( GUISkin ), typeof( bool ) } );
   }

   private static void Prefix( object title )
   {
      RuntimeTextHookHelper.TranslateGuiContent( title, "GUI.DoWindow" );
   }
}

[HarmonyPatch]
internal static class Transform_SetParent_Hook
{
   private static bool Prepare()
   {
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.Transform" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var transformType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Transform" );
      return AccessTools.Method( transformType, "SetParent", new[] { transformType, typeof( bool ) } );
   }

   private static void Postfix( object __instance )
   {
      if( __instance is Component component )
      {
         RuntimeTextHookHelper.TranslateHierarchyIfChanged( RuntimeTextHookHelper.GetGameObject( component ), "Transform.SetParent" );
      }
   }
}

[HarmonyPatch]
internal static class GameObject_Internal_AddComponentWithType_Hook
{
   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( GameObject ), "Internal_AddComponentWithType", new[] { typeof( Type ) } );
   }

   private static void Postfix( Component __result )
   {
      if( __result != null )
      {
         RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __result, "GameObject.Internal_AddComponentWithType" );
      }
   }
}

[HarmonyPatch]
internal static class Object_Internal_InstantiateSingle_Hook
{
   private static bool Prepare()
   {
      var vector3Type = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Vector3" );
      var quaternionType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Quaternion" );
      return vector3Type != null && quaternionType != null
         && AccessTools.Method( typeof( UnityEngine.Object ), "Internal_InstantiateSingle", new[] { typeof( UnityEngine.Object ), vector3Type, quaternionType } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var vector3Type = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Vector3" );
      var quaternionType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Quaternion" );
      return AccessTools.Method( typeof( UnityEngine.Object ), "Internal_InstantiateSingle", new[] { typeof( UnityEngine.Object ), vector3Type, quaternionType } );
   }

   private static void Postfix( UnityEngine.Object __result )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __result, "Object.Internal_InstantiateSingle" );
   }
}

[HarmonyPatch]
internal static class Object_Internal_InstantiateSingleWithParent_Hook
{
   private static bool Prepare()
   {
      var transformType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Transform" );
      var vector3Type = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Vector3" );
      var quaternionType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Quaternion" );
      return transformType != null && vector3Type != null && quaternionType != null
         && AccessTools.Method( typeof( UnityEngine.Object ), "Internal_InstantiateSingleWithParent", new[] { typeof( UnityEngine.Object ), transformType, vector3Type, quaternionType } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var transformType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Transform" );
      var vector3Type = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Vector3" );
      var quaternionType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Quaternion" );
      return AccessTools.Method( typeof( UnityEngine.Object ), "Internal_InstantiateSingleWithParent", new[] { typeof( UnityEngine.Object ), transformType, vector3Type, quaternionType } );
   }

   private static void Postfix( UnityEngine.Object __result )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __result, "Object.Internal_InstantiateSingleWithParent" );
   }
}

[HarmonyPatch]
internal static class Object_Internal_CloneSingle_Hook
{
   private static bool Prepare()
   {
      return AccessTools.Method( typeof( UnityEngine.Object ), "Internal_CloneSingle", new[] { typeof( UnityEngine.Object ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( typeof( UnityEngine.Object ), "Internal_CloneSingle", new[] { typeof( UnityEngine.Object ) } );
   }

   private static void Postfix( UnityEngine.Object __result )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __result, "Object.Internal_CloneSingle" );
   }
}

[HarmonyPatch]
internal static class Object_Internal_CloneSingleWithParent_Hook
{
   private static bool Prepare()
   {
      var transformType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Transform" );
      return transformType != null && AccessTools.Method( typeof( UnityEngine.Object ), "Internal_CloneSingleWithParent", new[] { typeof( UnityEngine.Object ), transformType, typeof( bool ) } ) != null;
   }

   private static MethodBase TargetMethod()
   {
      var transformType = RuntimeTypeResolver.FindLoadedType( "UnityEngine.Transform" );
      return AccessTools.Method( typeof( UnityEngine.Object ), "Internal_CloneSingleWithParent", new[] { typeof( UnityEngine.Object ), transformType, typeof( bool ) } );
   }

   private static void Postfix( UnityEngine.Object __result )
   {
      RuntimeTextHookHelper.TranslateObjectHierarchyIfChanged( __result, "Object.Internal_CloneSingleWithParent" );
   }
}