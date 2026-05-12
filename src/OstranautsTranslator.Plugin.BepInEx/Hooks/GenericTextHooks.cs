using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx.Hooks;

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
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "UI.Text.OnEnable.before" );
   }

   private static void Postfix( object __instance )
   {
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "UI.Text.OnEnable.after" );
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

   private static void Prefix( ref StringBuilder __0 )
   {
      RuntimeTextHookHelper.TranslateStringBuilder( ref __0, "TMP_Text.SetText(StringBuilder)" );
   }

   private static void Postfix( object __instance )
   {
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

   private static void Prefix( ref string __0 )
   {
      __0 = OstranautsTranslatorPlugin.Translate( __0, "TMP_Text.SetText(string,float,float,float)" );
   }

   private static void Postfix( object __instance )
   {
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
      RuntimeTextHookHelper.TranslateCurrentText( __instance, "TMP_Text.SetCharArray(char[],int,int).post" );
   }
}

[HarmonyPatch]
internal static class TMP_Text_SetCharArray_Hook3
{
   private static bool Prepare()
   {
      return TmpTypeResolver.Get( "TMPro.TMP_Text" ) != null;
   }

   private static MethodBase TargetMethod()
   {
      return AccessTools.Method( TmpTypeResolver.Get( "TMPro.TMP_Text" ), "SetCharArray", new[] { typeof( int[] ), typeof( int ), typeof( int ) } );
   }

   private static void Prefix( ref int[] __0, ref int __1, ref int __2 )
   {
      // TMP passes array slices here; translating the slice corrupts rich-text boundaries.
   }

   private static void Postfix( object __instance )
   {
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUI+ToolbarButtonSize" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUI+ToolbarButtonSize" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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
      return RuntimeTypeResolver.FindLoadedType( "UnityEngine.GUIContent" ) != null;
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