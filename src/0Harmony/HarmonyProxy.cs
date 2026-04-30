using System;
using System.Reflection;

namespace HarmonyLib;

public sealed class Harmony
{
   public Harmony( string id )
   {
   }

   public void PatchAll( Assembly assembly ) => throw new NotImplementedException();

   public void UnpatchSelf() => throw new NotImplementedException();
}

[AttributeUsage( AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false )]
public sealed class HarmonyPatch : Attribute
{
   public HarmonyPatch()
   {
   }
}

public static class AccessTools
{
   public static MethodInfo Method( Type type, string name, Type[] parameters = null, Type[] generics = null ) => throw new NotImplementedException();
}
