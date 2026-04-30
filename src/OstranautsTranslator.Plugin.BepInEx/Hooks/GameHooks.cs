using System;
using System.Reflection;
using HarmonyLib;

namespace OstranautsTranslator.Plugin.BepInEx.Hooks;

internal static class GameTypeResolver
{
   public static Type Get( string typeName )
   {
      return Type.GetType( typeName + ", Assembly-CSharp", false );
   }
}

[HarmonyPatch]
internal static class DataHandler_GetString_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "DataHandler" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "DataHandler" ), "GetString", new[] { typeof( string ), typeof( bool ) } );
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "DataHandler.GetString" );
   }
}

[HarmonyPatch]
internal static class GrammarUtils_GetInflectedString_ConditionOwner_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GrammarUtils" ) != null
         && GameTypeResolver.Get( "Condition" ) != null
         && GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GrammarUtils" ),
         "GetInflectedString",
         new[] { typeof( string ), GameTypeResolver.Get( "Condition" ), GameTypeResolver.Get( "CondOwner" ) } );
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "GrammarUtils.GetInflectedString(string,Condition,CondOwner)" );
   }
}

[HarmonyPatch]
internal static class GrammarUtils_GetInflectedString_Object_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GrammarUtils" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GrammarUtils" ), "GetInflectedString", new[] { typeof( string ), typeof( object ) } );
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "GrammarUtils.GetInflectedString(string,object)" );
   }
}

[HarmonyPatch]
internal static class GrammarUtils_GetInflectedString_Interaction_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GrammarUtils" ) != null && GameTypeResolver.Get( "Interaction" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method(
         GameTypeResolver.Get( "GrammarUtils" ),
         "GetInflectedString",
         new[] { typeof( string ), GameTypeResolver.Get( "Interaction" ) } );
   }

   private static void Postfix( ref string __result )
   {
      __result = OstranautsTranslatorPlugin.Translate( __result, "GrammarUtils.GetInflectedString(string,Interaction)" );
   }
}

[HarmonyPatch]
internal static class CondOwner_LogMessage_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "CondOwner" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "CondOwner" ), "LogMessage", new[] { typeof( string ), typeof( string ), typeof( string ), typeof( string ) } );
   }

   private static void Prefix( ref string strMsg, ref string strShort )
   {
      strMsg = OstranautsTranslatorPlugin.Translate( strMsg, "CondOwner.LogMessage" );
      if( !string.IsNullOrEmpty( strShort ) )
      {
         strShort = OstranautsTranslatorPlugin.Translate( strShort, "CondOwner.LogMessage.short" );
      }
   }
}

[HarmonyPatch]
internal static class Ship_LogAdd_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "Ship" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "Ship" ), "LogAdd", new[] { typeof( string ), typeof( double ), typeof( bool ) } );
   }

   private static void Prefix( ref string strEntry )
   {
      strEntry = OstranautsTranslatorPlugin.Translate( strEntry, "Ship.LogAdd" );
   }
}

[HarmonyPatch]
internal static class GUITooltip2_SetToolTip_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip2" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUITooltip2" ), "SetToolTip", new[] { typeof( string ), typeof( string ), typeof( bool ), typeof( bool ) } );
   }

   private static void Prefix( ref string strTitle, ref string strBody )
   {
      strTitle = OstranautsTranslatorPlugin.Translate( strTitle, "GUITooltip2.SetToolTip.title" );
      strBody = OstranautsTranslatorPlugin.Translate( strBody, "GUITooltip2.SetToolTip.body" );
   }
}

[HarmonyPatch]
internal static class GUITooltip2_SetToolTip_1_Hook
{
   private static bool Prepare()
   {
      return GameTypeResolver.Get( "GUITooltip2" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( GameTypeResolver.Get( "GUITooltip2" ), "SetToolTip_1", new[] { typeof( string ), typeof( string ), typeof( string ), typeof( bool ) } );
   }

   private static void Prefix( ref string strSubtitle, ref string strTitle, ref string strBody )
   {
      strSubtitle = OstranautsTranslatorPlugin.Translate( strSubtitle, "GUITooltip2.SetToolTip_1.subtitle" );
      strTitle = OstranautsTranslatorPlugin.Translate( strTitle, "GUITooltip2.SetToolTip_1.title" );
      strBody = OstranautsTranslatorPlugin.Translate( strBody, "GUITooltip2.SetToolTip_1.body" );
   }
}
