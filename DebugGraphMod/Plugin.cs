using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace DebugGraphMod;

[BepInPlugin("nuclearoption.debuggraphmod", "Debug Graph Mod", "1.0.0")]
[BepInProcess("NuclearOption.exe")]
public class Plugin : BaseUnityPlugin
{
    public static ConfigEntry<bool> Enabled;

    private GUIStyle _windowStyle;
    private Texture2D _bgTex;
    private bool _styleReady;

    private void Awake()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Show debug graphs");
    }

    private void EnsureStyle()
    {
        if (_styleReady) return;

        if (GraphRegistry.LineMat == null)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                Logger.LogError("Hidden/Internal-Colored shader not found — GL lines won't render");
            }
            else
            {
                GraphRegistry.LineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                GraphRegistry.LineMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                GraphRegistry.LineMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                GraphRegistry.LineMat.SetInt("_Cull", (int)CullMode.Off);
                GraphRegistry.LineMat.SetInt("_ZWrite", 0);
            }
        }

        _bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _bgTex.SetPixel(0, 0, new Color(0.15f, 0.15f, 0.15f, 0.75f));
        _bgTex.Apply();

        _windowStyle = new GUIStyle(GUI.skin.window);
        _windowStyle.normal.background = _bgTex;
        _windowStyle.onNormal.background = _bgTex;

        _styleReady = true;
    }

    private void OnGUI()
    {
        if (!Enabled.Value)
            return;

        EnsureStyle();

        foreach (var chart in GraphRegistry.Charts)
        {
            chart.Position = GUI.Window(chart.Id, chart.Position, chart.DrawWindow, chart.Name, _windowStyle);
        }
    }

    private void OnDestroy()
    {
        if (GraphRegistry.LineMat != null)
            Destroy(GraphRegistry.LineMat);
        if (_bgTex != null)
            Destroy(_bgTex);
    }
}
