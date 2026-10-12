using UnityEngine;

namespace FBWAnalyserMod
{
    // Per-axis online identification from (target rate u, actual rate y).
    internal static class LagEstimator
    {
        public const int AxisCount = 3;

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
        public static readonly float[] R2 = new float[AxisCount];
        public static readonly float[] AdjGain = new float[AxisCount];
        public static readonly int[] AdjLag = new int[AxisCount];
        public static readonly bool[] Valid = new bool[AxisCount];
        public static readonly bool[] Gated = new bool[AxisCount];
        private static readonly float[] _ceil = new float[AxisCount];

        public static void Init()
        {
            _n = Mathf.Clamp(Plugin.RingSize.Value, 32, 4096);
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
            for (int a = 0; a < AxisCount; a++)
            {
                Tau[a] = 0f; Tau2[a] = 0f; Theta[a] = 0f; R2[a] = 0f;
                AdjGain[a] = 1f; AdjLag[a] = 0;
                Valid[a] = false; Gated[a] = false; _ceil[a] = 1f;
            }
        }

        public static void NoteAircraft(int id)
        {
            if (id != _lastAircraftId) { _lastAircraftId = id; Reset(); }
        }

        public static void Sample(Vector3 tgt, Vector3 omega, Vector3 ceiling)
        {
            if (!_inited) Init();
            _dt = Time.fixedDeltaTime;
            _ceil[0] = Mathf.Max(0.1f, ceiling.x);
            _ceil[1] = Mathf.Max(0.1f, ceiling.y);
            _ceil[2] = Mathf.Max(0.1f, ceiling.z);

            _u[0][_head] = tgt.x; _y[0][_head] = omega.x;
            _u[1][_head] = tgt.y; _y[1][_head] = omega.y;
            _u[2][_head] = tgt.z; _y[2][_head] = omega.z;

            _head = (_head + 1) % _n;
            if (_count < _n) _count++;

            if (++_frames >= Mathf.Max(1, Plugin.IntervalFrames.Value))
            {
                _frames = 0;
                for (int a = 0; a < AxisCount; a++) Estimate(a);
            }
        }

        private static int OrderOf(int a) => (a == 1 && Plugin.Yaw2ndOrder.Value) ? 2 : 1;

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
            vu /= N;
            float sd = Mathf.Sqrt(vu);

            float ceilA = _ceil[a];
            int sat = 0;
            for (int k = 0; k < N; k++)
            {
                if (Mathf.Abs(y[k]) >= 0.9f * ceilA) sat++;
            }
            float satFrac = (float)sat / N;

            bool excited = sd >= Plugin.UExcMin.Value && flips >= Plugin.MinSignFlips.Value;
            bool saturated = satFrac > Plugin.SatFracMax.Value;
            if (!excited || saturated) { Gated[a] = true; R2[a] = 0f; return; }
            Gated[a] = false;

            int W = Mathf.Clamp(Plugin.SmoothWindow.Value, 1, 15);
            if ((W & 1) == 0) W++;
            if (W > 1) { u = MovingAverage(u, W); y = MovingAverage(y, W); }

            float mu2 = 0f, my2 = 0f;
            for (int i = 0; i < N; i++) { mu2 += u[i]; my2 += y[i]; }
            mu2 /= N; my2 /= N;
            float vy = 0f;
            for (int i = 0; i < N; i++) { u[i] -= mu2; y[i] -= my2; vy += y[i] * y[i]; }
            vy /= N;
            if (vy < 1e-4f) { Valid[a] = false; Gated[a] = true; R2[a] = 0f; return; }

            int maxLag = Mathf.Min(N / 4, 60);

            int adjLag = 0; float adjR = float.NegativeInfinity;
            for (int lag = 0; lag <= maxLag; lag++)
            {
                float r = 0f;
                for (int i = 0; i + lag < N; i++) r += u[i] * y[i + lag];
                if (r > adjR) { adjR = r; adjLag = lag; }
            }
            float adjDen = 0f;
            for (int i = 0; i + adjLag < N; i++) adjDen += u[i] * u[i];
            float adjGain = adjDen > 1e-9f ? adjR / adjDen : 0f;

            int order = OrderOf(a);
            int H = Mathf.Clamp(Plugin.PredHorizon.Value, 1, Mathf.Max(1, N / 2));
            int bestLag = -1; float bestR2 = float.NegativeInfinity; double[] bestC = null;
            float bestTau = 0f, bestTau2 = 0f;
            var c = new double[order + 1];
            for (int lag = 0; lag <= maxLag; lag++)
            {
                if (!FitArx(u, y, N, lag, order, c)) continue;
                if (!Poles(order, c, Plugin.MaxPole.Value, out float tau, out float tau2)) continue;
                float r2 = SimR2(u, y, N, lag, order, c, H);
                if (r2 > bestR2)
                {
                    bestR2 = r2; bestLag = lag; bestTau = tau; bestTau2 = tau2;
                    bestC = (double[])c.Clone();
                }
            }
            if (bestLag < 0) { Valid[a] = false; Gated[a] = true; R2[a] = 0f; return; }

            R2[a] = Mathf.Max(0f, bestR2);
            double sumA = 0.0;
            for (int i = 0; i < order; i++) sumA += bestC[i];
            double dcGain = bestC[order] / (1.0 - sumA);
            if (bestR2 < Plugin.MinR2.Value || dcGain < Plugin.DcGainMin.Value || dcGain > Plugin.DcGainMax.Value)
            {
                Valid[a] = false;
                Gated[a] = true;
                return;
            }

            Tau[a] = Mathf.Clamp(Tau[a] * 0.6f + bestTau * 0.4f, 0f, 5f);
            Tau2[a] = Mathf.Clamp(Tau2[a] * 0.6f + bestTau2 * 0.4f, 0f, 5f);
            Theta[a] = Mathf.Clamp(Theta[a] * 0.6f + bestLag * _dt * 0.4f, 0f, 2f);
            AdjGain[a] = Mathf.Clamp(AdjGain[a] * 0.6f + adjGain * 0.4f, 0f, 2f);
            AdjLag[a] = adjLag;
            Valid[a] = true;
        }

        // Time constants from the AR part: slow -> tau, fast -> tau2. Complex conjugate
        // poles are reported as an envelope time constant (tau2 = 0).
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
}
