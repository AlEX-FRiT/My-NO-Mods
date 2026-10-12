using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FBWAnalyserMod
{
    // Reflection access to the game's FBW internals. All lookups are cached; every read is
    // fail-soft (missing field => fallback), so a game update degrades rather than throws.
    internal static class FbwParams
    {
        private static readonly Dictionary<(System.Type, string), FieldInfo> _cache =
            new Dictionary<(System.Type, string), FieldInfo>();

        private static FieldInfo Field(System.Type t, string name)
        {
            var key = (t, name);
            if (!_cache.TryGetValue(key, out var fi))
            {
                fi = t == null ? null : AccessTools.Field(t, name);
                _cache[key] = fi;
            }
            return fi;
        }

        private static object Raw(object obj, string name)
        {
            if (obj == null) return null;
            var fi = Field(obj.GetType(), name);
            return fi?.GetValue(obj);
        }

        public static float GetFloat(object obj, string name, float fallback)
        {
            var v = Raw(obj, name);
            return v is float f ? f : fallback;
        }

        public static bool GetBool(object obj, string name, bool fallback)
        {
            var v = Raw(obj, name);
            return v is bool b ? b : fallback;
        }

        public static Vector3 GetVec3(object obj, string name, Vector3 fallback)
        {
            var v = Raw(obj, name);
            return v is Vector3 x ? x : fallback;
        }

        // Resolve base-type members on typeof(ControlsFilter): Type.GetField misses non-public
        // fields inherited from a base class (HeloControlsFilter would otherwise lose them).
        private static readonly Dictionary<string, FieldInfo> _cfCache = new Dictionary<string, FieldInfo>();
        private static FieldInfo CfField(string name)
        {
            if (!_cfCache.TryGetValue(name, out var fi))
            {
                fi = AccessTools.Field(typeof(ControlsFilter), name);
                _cfCache[name] = fi;
            }
            return fi;
        }
        private static object CfRaw(ControlsFilter cf, string name) => cf == null ? null : CfField(name)?.GetValue(cf);

        public static Aircraft GetAircraft(ControlsFilter cf) => CfRaw(cf, "aircraft") as Aircraft;
        public static ControlsFilter.FlyByWire GetFlyByWire(ControlsFilter cf) => CfRaw(cf, "flyByWire") as ControlsFilter.FlyByWire;
        public static float GetMinSpeed(ControlsFilter cf) => CfRaw(cf, "minSpeed") is float a ? a : 0f;
        public static float GetMinAlt(ControlsFilter cf) => CfRaw(cf, "minAlt") is float b ? b : 0f;

        private static readonly Dictionary<string, FieldInfo> _fbwCache = new Dictionary<string, FieldInfo>();
        private static FieldInfo FbwField(string name)
        {
            if (!_fbwCache.TryGetValue(name, out var fi))
            {
                fi = AccessTools.Field(typeof(ControlsFilter.FlyByWire), name);
                _fbwCache[name] = fi;
            }
            return fi;
        }
        private static float FbwFloat(ControlsFilter.FlyByWire f, string name, float fallback)
        {
            var v = f == null ? null : FbwField(name)?.GetValue(f);
            return v is float x ? x : fallback;
        }

        public static float MaxRollAngularVel(ControlsFilter.FlyByWire f) => FbwFloat(f, "maxRollAngularVel", 6f);
        public static float MaxRollSpeed(ControlsFilter.FlyByWire f) => FbwFloat(f, "maxRollSpeed", 300f);
        public static float GLimitPositive(ControlsFilter.FlyByWire f) => FbwFloat(f, "gLimitPositive", 9f);

        // FBW SmoothDamps pitch/roll but never yaw (the game writes the yaw smoother's output into
        // .roll), so the yaw command stays raw and only roll takes the smoothed value.
        public static Vector3 ResolveCommand(ControlsFilter.FlyByWire fbw, Vector3 raw)
        {
            if (GetVec3(fbw, "inputSmoothing", Vector3.zero) == Vector3.zero) return raw;
            Vector3 sm = GetVec3(fbw, "inputsSmoothed", raw);
            return new Vector3(raw.x, raw.y, sm.z);
        }

        // HeloControlsFilter (private nested HeloFlyByWire).
        public static object GetHeloFlyByWire(HeloControlsFilter cf) => Raw(cf, "heloFlyByWire");
    }
}
