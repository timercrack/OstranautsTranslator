using UnityEngine;

namespace OstranautsTranslator.Plugin.BepInEx;

public sealed class OstranautsTranslatorRuntimeProxyBehaviour : MonoBehaviour
{
   private OstranautsTranslatorPlugin _plugin;

   public void Initialize( OstranautsTranslatorPlugin plugin )
   {
      _plugin = plugin;
   }

   public void Update()
   {
      ( _plugin ?? OstranautsTranslatorPlugin.Instance )?.TickRuntime();
   }

   public void OnGUI()
   {
      ( _plugin ?? OstranautsTranslatorPlugin.Instance )?.RenderRuntimeGui();
   }
}