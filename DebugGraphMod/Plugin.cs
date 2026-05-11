using System;
using System.IO;
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
    public static ConfigEntry<bool> SaveData;
    public static ConfigEntry<bool> DeleteData;

    private GUIStyle _windowStyle;
    private Texture2D _bgTex;
    private bool _styleReady;

    private void Awake()
    {
        Enabled = Config.Bind("General", "Enabled", true, "Show debug graphs");
        SaveData = Config.Bind("General", "SaveData", false, "Toggle ON to save all chart data to file");
        DeleteData = Config.Bind("General", "DeleteData", false, "Toggle ON to delete all saved CSV files");
        SaveData.SettingChanged += (_, _) => { if (SaveData.Value) { DoSaveData(); SaveData.Value = false; } };
        DeleteData.SettingChanged += (_, _) => { if (DeleteData.Value) { DoClearData(); DeleteData.Value = false; } };
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
            string title = $"{chart.Name} [{chart.YMin},{chart.YMax}]";
            chart.Position = GUI.Window(chart.Id, chart.Position, chart.DrawWindow, title, _windowStyle);
        }
    }

    private void OnDestroy()
    {
        if (GraphRegistry.LineMat != null)
            Destroy(GraphRegistry.LineMat);
        if (_bgTex != null)
            Destroy(_bgTex);
    }

    private void DoSaveData()
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "nuclearoption.debuggraphmod_data");
            Directory.CreateDirectory(dir);
            DateTime now = DateTime.Now;

            foreach (var chart in GraphRegistry.Charts)
            {
                string baseName = $"{chart.Name}_{now:HHmmss}";
                string path = Path.Combine(dir, $"{baseName}.csv");
                if (File.Exists(path))
                {
                    var rng = new System.Random();
                    path = Path.Combine(dir, $"{baseName}_{rng.Next(1000, 9999)}.csv");
                }

                using var sw = new StreamWriter(path);
                var streams = chart.Streams;
                if (streams.Count == 0) continue;

                sw.Write("Index");
                foreach (var s in streams) sw.Write($",{s.Name}");
                sw.WriteLine();

                int maxLen = 0;
                var data = new float[streams.Count][];
                for (int i = 0; i < streams.Count; i++)
                {
                    data[i] = streams[i].GetYData();
                    if (data[i].Length > maxLen) maxLen = data[i].Length;
                }

                for (int i = 0; i < maxLen; i++)
                {
                    sw.Write(i.ToString());
                    for (int j = 0; j < streams.Count; j++)
                        sw.Write(i < data[j].Length ? $",{data[j][i]}" : ",");
                    sw.WriteLine();
                }
            }
            Logger.LogInfo("Chart data saved");
        }
        catch (Exception ex) { Logger.LogError($"SaveData failed: {ex.Message}"); }
    }

    private void DoClearData()
    {
        try
        {
            string dir = Path.Combine(Paths.ConfigPath, "nuclearoption.debuggraphmod_data");
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.csv"))
                    File.Delete(f);
            }
            Logger.LogInfo("Saved CSV files cleared");
        }
        catch (Exception ex) { Logger.LogError($"ClearData failed: {ex.Message}"); }
    }
}
