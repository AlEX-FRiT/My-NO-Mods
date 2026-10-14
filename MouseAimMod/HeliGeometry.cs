using UnityEngine;

namespace MouseAimMod;

/// <summary>
/// 直升机模式误差几何（与 body-frame 律平行）。
/// 机体系 x=右, y=上, z=前；aim=目标单位向量(机体系)；groundDir=世界"下"在机体系(任意,随姿态变)。
///
///   L     = 机体水平面(x-z) ∩ AOG 平面 = ay*groundDir - gy*aim
///   ∠AOA' = 目标相对机体水平面的仰角
///   ∠BOB' = 机体水平面 与 AOG 平面 的二面角 (水平平飞=90 => roll 改平)
///   ∠yOA' = 目标方位(x-z 平面内, 从 +z 朝 +x)
///
///   e_roll  = ∠BOB' - 90        (左坡 => 正 => 指令右滚改平)
///   e_pitch = -sign(ay)*∠AOA'   (目标在上 => 负 => 指令抬头)
///   分量 /90 -> 长度归一到 <=1 -> 按 ∠yOA' 顺时针旋转 -> 输出 rollErr/pitchErr
///   yawErr  = aim.x             (yaw 独立指向)
/// </summary>
internal static class HeliGeometry
{
    public static bool AircraftHasRotor(Aircraft aircraft)
    {
        foreach (var engine in aircraft.engines)
            if (engine is RotorShaft) return true;
        return false;
    }

    public static void Compute(Vector3 aim, Vector3 groundDir, out float rollErr, out float pitchErr, out float yawErr)
    {
        yawErr = aim.x;

        float ay = Vector3.Dot(aim, Vector3.up);
        float gy = Vector3.Dot(groundDir, Vector3.up);
        Vector3 L = ay * groundDir - gy * aim;
        Vector3 n = Vector3.Cross(aim, groundDir);

        if (L.sqrMagnitude < 1e-12f || n.sqrMagnitude < 1e-12f)
        {
            rollErr = 0f;
            pitchErr = 0f;
            return;
        }

        L.Normalize();
        float aoa = Mathf.Acos(Mathf.Clamp(Vector3.Dot(aim, L), -1f, 1f)) * Mathf.Rad2Deg;
        float bob = Mathf.Acos(Mathf.Clamp(Vector3.Dot(Vector3.up, n) / n.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
        float yOA = Mathf.Atan2(Vector3.Dot(L, Vector3.right), Vector3.Dot(L, Vector3.forward)) * Mathf.Rad2Deg;

        float r = (bob - 90f) / 90f;
        float p = -Mathf.Sign(ay) * aoa / 90f;
        float len = Mathf.Sqrt(r * r + p * p);
        if (len > 1f)
        {
            r /= len;
            p /= len;
        }

        float phi = yOA * Mathf.Deg2Rad;
        float c = Mathf.Cos(phi);
        float s = Mathf.Sin(phi);
        rollErr = r * c + p * s;
        pitchErr = -r * s + p * c;
    }
}
