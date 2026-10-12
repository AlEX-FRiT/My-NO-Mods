using UnityEngine;

namespace MouseAimMod;

internal static class LagEstimator
{
    public const int AxisCount = 3;

    private const float MinR2 = 0.5f;
    private const float MaxPole = 0.985f;
    private const float UExcMin = 0.03f;
    private const int MinSignFlips = 2;

    private static int _n = 256;
    private static int _head;
    private static int _count;
    private static int _frames;
    private static float _dt = 0.02f;
    private static bool _inited;
    private static int _lastAircraftId = int.MinValue;

    private static readonly float[][] _u = new float[AxisCount][];
    private static readonly float[][] _y = new float[AxisCount][];

    public static readonly float[] Tau = new float[AxisCount];
    public static readonly float[] Tau2 = new float[AxisCount];
    public static readonly float[] Theta = new float[AxisCount];
    public static readonly float[] Gain = new float[AxisCount];
    public static readonly float[] R2 = new float[AxisCount];
    public static readonly bool[] Valid = new bool[AxisCount];
    public static readonly bool[] Gated = new bool[AxisCount];

    private static int OrderOf(int a) => a == 2 ? 2 : 1;

    private static void Init()
    {
        _n = Mathf.Clamp(Plugin.LagRingSize.Value, 32, 4096);
        for (int a = 0; a < AxisCount; a++)
        {
            _u[a] = new float[_n];
            _y[a] = new float[_n];
        }
        _inited = true;
        Reset();
    }

    public static void Reset()
    {
        _head = 0; _count = 0; _frames = 0;
        for (int a = 0; a < AxisCount; a++) { Tau[a] = 0f; Tau2[a] = 0f; Theta[a] = 0f; Gain[a] = 1f; R2[a] = 0f; Valid[a] = false; Gated[a] = false; }
    }

    public static void NoteAircraft(int id)
    {
        if (id != _lastAircraftId) { _lastAircraftId = id; Reset(); }
    }

    public static void Sample(int axis, float u, float y)
    {
        if (!_inited) Init();
        _dt = Time.fixedDeltaTime > 1e-5f ? Time.fixedDeltaTime : 0.02f;
        _u[axis][_head] = u;
        _y[axis][_head] = y;

        if (axis != AxisCount - 1) return;
        _head = (_head + 1) % _n;
        if (_count < _n) _count++;
        if (++_frames >= Mathf.Max(1, Plugin.LagIntervalFrames.Value))
        {
            _frames = 0;
            for (int a = 0; a < AxisCount; a++) Estimate(a);
        }
    }

    private static void Estimate(int a)
    {
        int N = _count;
        if (N < 32) { Valid[a] = false; return; }

        int start = (_count < _n) ? 0 : _head;
        var u = new float[N];
        var y = new float[N];
        for (int i = 0; i < N; i++)
        {
            int idx = (start + i) % _n;
            u[i] = _u[a][idx];
            y[i] = _y[a][idx];
        }

        float mu = 0f, my = 0f;
        for (int i = 0; i < N; i++) { mu += u[i]; my += y[i]; }
        mu /= N; my /= N;

        float vu = 0f;
        int flips = 0;
        for (int i = 0; i < N; i++)
        {
            float du = u[i] - mu;
            vu += du * du;
            if (i > 0 && du * (u[i - 1] - mu) < 0f) flips++;
        }
        float sd = Mathf.Sqrt(vu / N);
        if (!(sd >= UExcMin && flips >= MinSignFlips)) { Gated[a] = true; R2[a] = 0f; return; }
        Gated[a] = false;

        int sw;
        if (Plugin.LagAdaptiveSmooth.Value && Tau[a] > 0.02f)
            sw = Mathf.Clamp(Mathf.RoundToInt(Plugin.LagAdaptiveK.Value * Tau[a] / _dt), 3, 13);
        else
            sw = Mathf.Clamp(Plugin.LagSmoothWindow.Value, 1, 15);
        if (sw > 1) { u = MovingAverage(u, sw); y = MovingAverage(y, sw); }

        float mu2 = 0f, my2 = 0f;
        for (int i = 0; i < N; i++) { mu2 += u[i]; my2 += y[i]; }
        mu2 /= N; my2 /= N;
        float vy = 0f;
        for (int i = 0; i < N; i++) { u[i] -= mu2; y[i] -= my2; vy += y[i] * y[i]; }
        vy /= N;
        if (vy < 1e-4f) { Valid[a] = false; Gated[a] = true; R2[a] = 0f; return; }

        int order = OrderOf(a);
        int maxLag = Mathf.Min(N / 4, 60);
        int ph = Mathf.Clamp(Plugin.LagPredHorizon.Value, 2, Mathf.Max(2, N / 2));
        int bestLag = -1; float bestR2 = float.NegativeInfinity;
        float bestTau = 0f, bestTau2 = 0f, bestDc = 1f;
        var c = new double[order + 1];
        for (int lag = 0; lag <= maxLag; lag++)
        {
            if (!FitArx(u, y, N, lag, order, c)) continue;
            if (!Poles(order, c, MaxPole, out float tau, out float tau2)) continue;
            float r2 = SimR2(u, y, N, lag, order, c, ph);
            double sumA = 0.0;
            for (int q = 0; q < order; q++) sumA += c[q];
            float dc = (1.0 - sumA) != 0.0 ? (float)(c[order] / (1.0 - sumA)) : 1f;
            if (r2 > bestR2) { bestR2 = r2; bestLag = lag; bestTau = tau; bestTau2 = tau2; bestDc = dc; }
        }
        if (bestLag < 0) { Valid[a] = false; Gated[a] = true; R2[a] = 0f; return; }

        R2[a] = Mathf.Max(0f, bestR2);
        if (bestR2 < MinR2) { Valid[a] = false; Gated[a] = true; return; }

        Tau[a] = Mathf.Clamp(Tau[a] * 0.6f + bestTau * 0.4f, 0f, 2f);
        Tau2[a] = Mathf.Clamp(Tau2[a] * 0.6f + bestTau2 * 0.4f, 0f, 2f);
        Theta[a] = Mathf.Clamp(Theta[a] * 0.6f + bestLag * _dt * 0.4f, 0f, 2f);
        Gain[a] = Mathf.Clamp(Gain[a] * 0.6f + bestDc * 0.4f, 0.02f, 50f);
        Valid[a] = true;
    }

    // Slow time constant -> tau, fast -> tau2. Complex conjugate poles report an envelope tau.
    private static bool Poles(int order, double[] c, float maxPole, out float tau, out float tau2)
    {
        tau = 0f; tau2 = 0f;
        if (order == 1)
        {
            if (!(c[0] > 0.0 && c[0] < maxPole)) return false;
            tau = (float)(-_dt / System.Math.Log(c[0]));
            return true;
        }
        double a1 = c[0], a2 = c[1];
        double disc = a1 * a1 + 4.0 * a2;
        if (disc >= 0.0)
        {
            double r1 = (a1 + System.Math.Sqrt(disc)) * 0.5;
            double r2 = (a1 - System.Math.Sqrt(disc)) * 0.5;
            if (!(r1 > 0.0 && r1 < maxPole && r2 > 0.0 && r2 < maxPole)) return false;
            float t1 = (float)(-_dt / System.Math.Log(r1));
            float t2 = (float)(-_dt / System.Math.Log(r2));
            tau = Mathf.Max(t1, t2);
            tau2 = Mathf.Min(t1, t2);
            return true;
        }
        double mag = System.Math.Sqrt(-a2);
        if (!(mag > 0.0 && mag < maxPole)) return false;
        tau = (float)(-_dt / System.Math.Log(mag));
        tau2 = 0f;
        return true;
    }

    private static bool FitArx(float[] u, float[] y, int N, int lag, int order, double[] c)
    {
        int m = order + 1;
        var a = new double[m, m];
        var rhs = new double[m];
        int cnt = 0;
        for (int k = order; k < N; k++)
        {
            int j = k - lag;
            if (j < 0) continue;
            for (int i = 0; i < order; i++)
            {
                double yi = y[k - 1 - i];
                for (int q = 0; q < order; q++) a[i, q] += yi * y[k - 1 - q];
                a[i, order] += yi * u[j];
                rhs[i] += yi * y[k];
            }
            double uj = u[j];
            for (int q = 0; q < order; q++) a[order, q] += uj * y[k - 1 - q];
            a[order, order] += uj * uj;
            rhs[order] += uj * y[k];
            cnt++;
        }
        if (cnt < 24) return false;

        var aug = new double[m, m + 1];
        for (int i = 0; i < m; i++)
        {
            for (int q = 0; q < m; q++) aug[i, q] = a[i, q];
            aug[i, m] = rhs[i];
        }
        for (int col = 0; col < m; col++)
        {
            int piv = col;
            for (int r = col + 1; r < m; r++) if (System.Math.Abs(aug[r, col]) > System.Math.Abs(aug[piv, col])) piv = r;
            if (System.Math.Abs(aug[piv, col]) < 1e-10) return false;
            if (piv != col)
            {
                for (int q = col; q <= m; q++) { double tmp = aug[col, q]; aug[col, q] = aug[piv, q]; aug[piv, q] = tmp; }
            }
            for (int r = 0; r < m; r++)
            {
                if (r == col) continue;
                double f = aug[r, col] / aug[col, col];
                for (int q = col; q <= m; q++) aug[r, q] -= f * aug[col, q];
            }
        }
        for (int i = 0; i < m; i++) c[i] = aug[i, m] / aug[i, i];
        return true;
    }

    private static float SimR2(float[] u, float[] y, int N, int lag, int order, double[] c, int H)
    {
        double ssRes = 0.0, ssTot = 0.0; int m = 0;
        var hist = new double[order];
        for (int k0 = Mathf.Max(order, lag + 1); k0 < N - H; k0++)
        {
            for (int i = 0; i < order; i++) hist[i] = y[k0 - 1 - i];
            for (int s = 0; s < H; s++)
            {
                int k = k0 + s;
                int j = k - lag;
                double yh = 0.0;
                for (int i = 0; i < order; i++) yh += c[i] * hist[i];
                yh += c[order] * (j >= 0 ? u[j] : 0.0);
                for (int i = order - 1; i > 0; i--) hist[i] = hist[i - 1];
                hist[0] = yh;
            }
            double e = y[k0 + H - 1] - hist[0];
            ssRes += e * e;
            ssTot += y[k0 + H - 1] * y[k0 + H - 1];
            m++;
        }
        return (m > 0 && ssTot > 1e-9) ? (float)(1.0 - ssRes / ssTot) : 0f;
    }

    private static float[] MovingAverage(float[] x, int w)
    {
        int n = x.Length; int h = w / 2; var o = new float[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Mathf.Max(0, i - h), hi = Mathf.Min(n, i + h + 1);
            float s = 0f;
            for (int j = lo; j < hi; j++) s += x[j];
            o[i] = s / (hi - lo);
        }
        return o;
    }
}
