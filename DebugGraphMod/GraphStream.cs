using UnityEngine;

namespace DebugGraphMod;

public class GraphStream
{
    public string Name;
    public Color Color;

    private readonly float[] yBuffer;
    private readonly float[] xBuffer;
    private int writeIndex;
    private readonly int capacity;
    private readonly bool hasX;
    private int count;

    internal float[] GetYData()
    {
        int n = count >= capacity ? capacity : count;
        if (n == 0) return new float[0];
        float[] result = new float[n];
        for (int i = 0; i < n; i++)
        {
            int idx = count >= capacity ? (writeIndex + i) % capacity : i;
            result[i] = yBuffer[idx];
        }
        return result;
    }

    public GraphStream(string name, Color color, int capacity, bool hasX)
    {
        Name = name;
        Color = color;
        this.capacity = capacity;
        this.hasX = hasX;
        yBuffer = new float[capacity];
        xBuffer = hasX ? new float[capacity] : null;
    }

    public void Push(float y)
    {
        yBuffer[writeIndex] = y;
        writeIndex = (writeIndex + 1) % capacity;
        if (count < capacity) count++;
    }

    public void PushXy(float x, float y)
    {
        xBuffer[writeIndex] = x;
        yBuffer[writeIndex] = y;
        writeIndex = (writeIndex + 1) % capacity;
        if (count < capacity) count++;
    }

    public void DrawFlow(float w, float h, float yMin, float yMax)
    {
        if (count <= 1) return;

        int n = count >= capacity ? capacity : count;
        int xOff = count >= capacity ? 0 : capacity - count;
        float oneOverN = 1f / (capacity - 1f);

        GL.Begin(GL.LINE_STRIP);

        for (int i = 0; i < n; i++)
        {
            int idx = count >= capacity ? (writeIndex + i) % capacity : i;
            float val = yBuffer[idx];
            float normalized = Mathf.InverseLerp(yMin, yMax, val);
            GL.Vertex3(w * (xOff + i) * oneOverN, h * normalized, 0);
        }

        GL.End();
    }

    public void DrawCoord(float w, float h, float xMin, float xMax, float yMin, float yMax)
    {
        if (count <= 1) return;

        float xRange = xMax - xMin;
        float yRange = yMax - yMin;

        if (xRange <= 0f || yRange <= 0f) return;

        GL.Begin(GL.LINE_STRIP);

        for (int i = 0; i < count; i++)
        {
            int idx = count < capacity ? i : (writeIndex + i) % capacity;
            float dx = xBuffer[idx];
            float dy = yBuffer[idx];
            GL.Vertex3(w * (dx - xMin) / xRange, h * (dy - yMin) / yRange, 0);
        }

        GL.End();
    }
}
