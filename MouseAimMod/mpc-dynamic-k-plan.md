# MPC 模型参数 k 动态计算方案

## 一、问题背景

当前 MPC 预测模型为 `dw/dt = k·(u - w)`，k 来自配置文件固定值（默认 50）。

**实际物理**：飞机角速率响应速度随飞行状态剧烈变化。悬停时 yaw 自如旋转（k 大），高速时垂尾气动阻尼极大（k 小）。固定 k 会导致：
- 高速时模型以为自己转得快 → 预测乐观 → 指令过猛 → 过冲
- 低速时模型以为自己转得慢 → 预测悲观 → 指令不够 → 迟钝

---

## 二、反编译代码中的关键发现

游戏飞控系统（`ControlsFilter.cs`）已经内置了速度相关的控制力衰减。核心参数：

### 2.1 `qRatio` — 动态压力比（第 251 行）

```csharp
// ControlsFilter.FlyByWire.Filter() 第 251 行
float num9 = Mathf.Clamp01(
    cornerSpeed² · 1.225 / max(airDensity · speed², 50)
);
```

物理含义：

```
qRatio = min(1, corner_dynamic_pressure / current_dynamic_pressure)

speed < cornerSpeed  →  qRatio ≈ 1        （满控制力）
speed > cornerSpeed  →  qRatio ≈ (Vcorner/V)²  （控制力平方衰减）
```

**FBW 所有轴的控制输出都乘以 qRatio**：

```csharp
// pitch（第 252 行）
inputs.pitch = Clamp(-error * directControlFactor * qRatio + pitchAdjuster, -1, 1);

// yaw（第 260 行）
inputs.yaw = -Clamp(yawTightness * qRatio * (localAV.y - cmd_yaw), -1, 1);

// roll（第 262 行）
inputs.roll = ...涉及 qRatio...
```

### 2.2 `yawTightness` — 偏航紧度参数

`FlyByWire.GetParameters()` 返回的第 14 个参数。各飞机配不同值，影响 yaw 轴增益。

### 2.3 直升机风标效应（`HeloControlsFilter.cs` 第 46-51 行）

```csharp
// 仅直升机有
yawWeathervaneStrength = 0.4        // 风标效应强度
yawWeathervaneMinSpeed  = 40 kt     // 开始生效的最低速度
yawWeathervaneMaxSpeed  = 60 kt     // 完全生效的速度

// 物理：高速时垂尾/机身像风向标，自动把机头对准来流
// 效果：额外 yaw 阻尼，等效 k 进一步增大
```

### 2.4 `cornerSpeed` — 角速度

`GetFlyByWireParameters()` 返回的第 3 个参数。飞控在此速度以下有满控制力。

---

## 三、动态 k 计算公式

### 3.1 通用部分

```csharp
float rho    = aircraft.airDensity;
float speed  = Mathf.Max(aircraft.speed, 7.07f);  // 保证分母 ≥ 50
float cornerSpeed = fbwParams[2];

float qRatio = Mathf.Clamp01(
    cornerSpeed * cornerSpeed * 1.225f / (rho * speed * speed)
);
```

### 3.2 分轴计算

```
kPitch = kBase * qRatio

kRoll  = kBase * qRatio

kYaw   = kBase * qRatio * yawTightness      // yaw 额外乘 tightness
```

### 3.3 直升机 yaw 附作风标补偿

```
// 仅当 controlsFilter is HeloControlsFilter 时生效
float wvT = Clamp01((speed - minSpeed) / (maxSpeed - minSpeed));
float wvFactor = yawWeathervaneStrength * wvT;

kYaw += kBase * wvFactor * 0.5f;    // 0.5 是经验缩放系数
```

---

## 四、代码修改位置

文件：`PilotPlayerStatePatch.cs`

### 4.1 新增方法

```csharp
private static (float pitch, float yaw, float roll) ComputeDynamicK(
    Aircraft aircraft, float kBase, float[] fbwParams)
{
    float cornerSpeed = fbwParams[2];
    float yawTightness = fbwParams[13];

    float rho   = aircraft.airDensity;
    float speed = Mathf.Max(aircraft.speed, 7.07f);
    float qRatio = Mathf.Clamp01(
        cornerSpeed * cornerSpeed * 1.225f / (rho * speed * speed));

    float kPitch = kBase * qRatio;
    float kYaw   = kBase * qRatio * yawTightness;
    float kRoll  = kBase * qRatio;

    // 直升机风标效应
    if (aircraft.GetControlsFilter() is HeloControlsFilter heloCf)
    {
        var hfbw = Traverse.Create(heloCf).Field("heloFlyByWire");
        float wvs = hfbw.Field("yawWeathervaneStrength").GetValue<float>();
        float wvMin = hfbw.Field("yawWeathervaneMinSpeed").GetValue<float>();
        float wvMax = hfbw.Field("yawWeathervaneMaxSpeed").GetValue<float>();
        float t = Mathf.Clamp01((speed - wvMin) / (wvMax - wvMin));
        kYaw += kBase * wvs * t * 0.5f;
    }

    return (kPitch, kYaw, kRoll);
}
```

### 4.2 修改调用点（第 87-90 行附近）

**当前代码**：
```csharp
float dt = Time.fixedDeltaTime;
float k = Plugin.MpcK.Value;
float pitchOut = Mpc(pitchError, localAV.x, dt, k, horizon, iters, penalty);
float rollOut  = Mpc(rollError,  localAV.z, dt, k, horizon, iters, penalty);
float yawOut   = Mpc(yawError,   localAV.y, dt, k, horizon, iters, penalty);
```

**改后代码**：
```csharp
float dt = Time.fixedDeltaTime;
float kBase = Plugin.MpcK.Value;
var (kPitch, kYaw, kRoll) = ComputeDynamicK(aircraft, kBase, fbwParams);

float pitchOut = Mpc(pitchError, localAV.x, dt, kPitch, horizon, iters, penalty);
float rollOut  = Mpc(rollError,  localAV.z, dt, kRoll,  horizon, iters, penalty);
float yawOut   = Mpc(yawError,   localAV.y, dt, kYaw,   horizon, iters, penalty);
```

### 4.3 复用已有的 fbwParams 读取

当前 `ApplyPaCompensation` 已经在读 FBW 参数（第 170-178 行）。需要将 fbwParams 提升为 `PlayerAxisControlsPostfix` 内的局部变量，或缓存到静态字段，避免两次 Traverse。

---

## 五、与扰动 d 估计的协同

动态 k 和扰动 d 估计各自解决不同层面的问题：

```
dw/dt = k(V, AOA)·(u - w)  +  d

k(V, AOA)  ← 从 qRatio 动态算（管系统特性变化）
d          ← 从角速率残差反推（管未知外力）
```

两者不冲突，可以同时使用。建议先上线动态 k，验证效果后再加扰动估计。

---

## 六、效果预期

| 飞行状态 | qRatio | k_pitch | 模型行为 |
|----------|--------|---------|----------|
| 起飞/悬停 (V=20kt) | 1.0 | 50 | 快速响应，保持当前手感 |
| 巡航 (V=200kt) | ~0.3 | ~15 | 预测里角速率爬得慢，指令自动柔和 |
| 高速俯冲 (V=400kt) | ~0.08 | ~4 | 预测正确反映"基本拉不动"，不会猛拉杆 |
| 超音速 (V=600kt) | ~0.03 | ~1.5 | 自动极度保守 |

**最显著的改善场景**：

1. **高速 BFM 格斗**：急剧转弯后 k 自动下降，预测模型"知道"惯性大，不会再次猛推杆导致过冲震荡
2. **俯冲改出**：拉起时 k 自然很小，预测模型自动提前收杆，不会拉过头诱发黑视
3. **悬停→加速过渡**：k 平滑从 50 降到 15，控制器连续适应，无极切换

---

## 七、代码量

- 新增 `ComputeDynamicK` 方法：~25 行
- 修改调用点：~8 行（3 行改 + 5 行新增）
- fbwParams 缓存优化：~3 行

**总计约 35 行**，纯计算无副作用，零外部依赖。
