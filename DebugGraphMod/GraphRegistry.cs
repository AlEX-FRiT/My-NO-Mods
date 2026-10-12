using System.Collections.Generic;
using UnityEngine;

namespace DebugGraphMod;

public enum ChartType { Flow, Coordinate }

public static class GraphRegistry
{
    internal static readonly List<Chart> Charts = new();
    internal static Material LineMat;
    private static int nextWindowId = 9820;
    private static int chartCount;

    public static Chart CreateChart(ChartType type, string name, float width, float height,
        float yMin, float yMax, int bufferSize, float? xMin = null, float? xMax = null)
    {
        var chart = new Chart(nextWindowId++)
        {
            Type = type,
            Name = name,
            Width = width,
            Height = height,
            YMin = yMin,
            YMax = yMax,
            XMin = xMin ?? 0f,
            XMax = xMax ?? 1f,
            BufferSize = bufferSize
        };

        float totalH = height + 80f;
        const float margin = 20f;
        const float gapX = 20f;
        int rowsPerCol = Mathf.Max(1, Mathf.FloorToInt((Screen.height - 2f * margin) / totalH));
        int col = chartCount / rowsPerCol;
        int row = chartCount % rowsPerCol;
        float x = Screen.width - width - margin - col * (width + gapX);
        float y = Screen.height - totalH * (row + 1) - margin;
        chart.Position = new Rect(x, y, width, totalH);
        chartCount++;

        Charts.Add(chart);
        return chart;
    }
}
