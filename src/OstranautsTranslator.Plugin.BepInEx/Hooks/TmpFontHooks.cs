using System;
using System.Reflection;
using HarmonyLib;
using OstranautsTranslator.Plugin.BepInEx.Fonts;

namespace OstranautsTranslator.Plugin.BepInEx.Hooks;

internal static class TmpTypeResolver
{
   public static Type Get( string typeName )
   {
      return Type.GetType( typeName + ", Unity.TextMeshPro", false );
   }
}

internal static class UiTypeResolver
{
   public static Type Get( string typeName )
   {
      return Type.GetType( typeName + ", UnityEngine.UI", false );
   }
}

[HarmonyPatch]
internal static class TextMeshProUGUI_OnEnable_Hook
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TextMeshProUGUI" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TextMeshProUGUI" ), "OnEnable" );
   }

   private static void Postfix( object __instance )
   {
      if( !RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) )
      {
         RuntimeTextHookHelper.TranslateCurrentText( __instance, "TextMeshProUGUI.OnEnable" );
      }

      TmpFontManager.ApplyOverrideFont( __instance );
   }
}

[HarmonyPatch]
internal static class TextMeshPro_OnEnable_Hook
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TextMeshPro" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TextMeshPro" ), "OnEnable" );
   }

   private static void Postfix( object __instance )
   {
      if( !RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) )
      {
         RuntimeTextHookHelper.TranslateCurrentText( __instance, "TextMeshPro.OnEnable" );
      }

      TmpFontManager.ApplyOverrideFont( __instance );
   }
}

[HarmonyPatch]
internal static class TMP_Text_SetText_Hook
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TMP_Text" ), "SetText", new[] { typeof( string ), typeof( bool ) } );
   }

   private static void Prefix( object __instance, ref string __0 )
   {
      if( RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) ) return;
      __0 = OstranautsTranslatorPlugin.Translate( __0, "TMP_Text.SetText" );
   }

   private static void Postfix( object __instance )
   {
      if( !RuntimeTextComponentBypassHelper.ShouldBypassTranslation( __instance ) )
      {
         RuntimeTextHookHelper.TranslateCurrentText( __instance, "TMP_Text.SetText.post" );
      }

      TmpFontManager.ApplyOverrideFont( __instance );
   }
}
