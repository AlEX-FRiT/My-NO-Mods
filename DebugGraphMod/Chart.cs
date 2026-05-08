using System.Collections.Generic;
using UnityEngine;

namespace DebugGraphMod;

public class Chart
{
    public ChartType Type;
    public string Name;
    public float Width;
    public float Height;
    public float YMin, YMax;
    public float XMin, XMax;
    public int BufferSize;

    internal Rect Position;

    private readonly int id;
    private readonly List<GraphStream> streams = new();

    public Chart(int id)
    {
        this.id = id;
    }

    public int Id => id;

    public GraphStream AddStream(string name, Color color)
    {
        var s = new GraphStream(name, color, BufferSize, Type == ChartType.Coordinate);
        streams.Add(s);
        return s;
    }

    internal void DrawWindow(int windowId)
    {
        var chartRect = GUILayoutUtility.GetRect(Width, Height);
        DrawLegend();

        if (Event.current.type == EventType.Repaint && streams.Count > 0 && GraphRegistry.LineMat != null)
        {
            float chartScreenX = Position.x + chartRect.x;
            float chartScreenY = Position.y + chartRect.y;

            GL.PushMatrix();
            GL.Viewport(new Rect(
                chartScreenX,
                Screen.height - chartScreenY - chartRect.height,
                chartRect.width,
                chartRect.height
            ));

            Matrix4x4 proj = Matrix4x4.Ortho(0, chartRect.width, 0, chartRect.height, -1, 1);
            GL.LoadProjectionMatrix(proj);
            GL.modelview = Matrix4x4.identity;

            if (Type == ChartType.Flow)
            {
                foreach (var s in streams)
                {
                    GraphRegistry.LineMat.color = s.Color;
                    GraphRegistry.LineMat.SetPass(0);
                    s.DrawFlow(chartRect.width, chartRect.height, YMin, YMax);
                }
            }
            else
            {
                foreach (var s in streams)
                {
                    GraphRegistry.LineMat.color = s.Color;
                    GraphRegistry.LineMat.SetPass(0);
                    s.DrawCoord(chartRect.width, chartRect.height, XMin, XMax, YMin, YMax);
                }
            }

            float zeroY = chartRect.height * Mathf.InverseLerp(YMin, YMax, 0f);
            GraphRegistry.LineMat.color = new Color(1f, 1f, 1f, 0.2f);
            GraphRegistry.LineMat.SetPass(0);
            GL.Begin(GL.LINES);
            GL.Vertex3(0, zeroY, 0);
            GL.Vertex3(chartRect.width, zeroY, 0);
            GL.End();

            GL.PopMatrix();
            GL.Viewport(new Rect(0, 0, Screen.width, Screen.height));
        }

        GUI.DragWindow();
    }

    private void DrawLegend()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(4f);

        var prevColor = GUI.color;
        foreach (var s in streams)
        {
            GUI.color = s.Color;
            GUILayout.Label($"■ {s.Name}");
        }
        GUI.color = prevColor;

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }
}
