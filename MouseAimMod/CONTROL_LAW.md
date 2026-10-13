# MouseAimMod 控制律设计文档

> 版本对应提交 `0af4248`。文件:`Plugin.cs` / `PilotPlayerStatePatch.cs` / `LagEstimator.cs` / `HoverThrottleController.cs`。
> 本文只描述**当前实现**,不做理想化描述。已知缺陷在第 9 节汇总。

---

## 0. 文件职责

| 文件 | 职责 |
|---|---|
| `Plugin.cs` | BepInEx 入口;全部配置项(`Config.Bind`);预设存取;DebugGraphMod 图表注册(`Pitch/Roll/Yaw` 三张图 + `Stick XY`)。 |
| `PilotPlayerStatePatch.cs` | 核心 Harmony Patch。`PlayerAxisControls` 的 postfix 里计算三轴控制量并写入 `controlInputs`。 |
| `LagEstimator.cs` | 在线辨识 `u→ω` 的 `(K, τ, θ)`,供自适应增益使用。 |
| `HoverThrottleController.cs` | 独立的悬停油门辅助(与瞄准控制无关,用反射复用游戏 `AutoHover` 的 PID 参数)。 |

---

## 1. 执行时机与总体流程图

- 挂载点:`[HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]` 的 **`HarmonyPostfix`**。
- 时机:**每个物理帧**(`FixedUpdate`,`dt = Time.fixedDeltaTime ≈ 0.0214s`,约 47Hz)。
- 输入:`___controlInputs`(`PilotPlayerState` 的 `ControlInputs` 字段,归一化 pitch/roll/yaw ∈ [-1,1])。
- 输出:直接改写 `controlInputs.pitch/roll/yaw`。FBW(`ControlsFilter.FlyByWire`)随后把它转成舵面/速率指令。

```
PlayerAxisControlsPostfix(每物理帧)
│
├─[门控] MouseAimEnabled && StabilizerEnabled()     否则 return
│        !virtualJoystickEnabled && CanvasEnabled    否则 return
│
├─[悬停油门] HoverActive && verticalLanding → HoverThrottleController.ApplyHoverThrottle()
│
├─[瞄准使能] aimActive = InvertFreeLook ? freeLook : !freeLook   否则 return
│            Camera.main != null
│
├─[目标低通] _viewFilt ← 一阶滤波(Camera.forward, TargetTau=0.05)
│            viewDirection = _viewFilt.normalized
│            localTarget = Quaternion.Inverse(aircraft.rotation) * viewDirection
│
├─[误差模型] 由 localTarget=(dx,dy,dz) 算 (pitchErr, rollErr, yawErr)  ← 见第 2 节
│
├─[增益]     gp/gy/gr = 固定 Gain*;若 Adaptive 且 LagEstimator.Valid[a] → KpFromFit(...)
│            率阻尼时间常数 tP/tR/tY = clamp(LagEstimator.Tau[a], TauMin, TauMax)
│
├─[比例]     u = ( Prop(pitchErr,gp), Prop(rollErr,gr), Prop(yawErr,gy) )   Prop=clamp(err*gain,-1,1)
│
├─[速率阻尼] kdP/kdR/kdY = min(FeedbackGain*gp*τ, KdMax)
│            pitchOut = clamp(u.x - kdP*ω.x,   -1,1)      ω = 机体系角速度
│            rollOut  = clamp(u.y - kdR*(-ω.z),-1,1)
│            yawOut   = clamp(u.z - kdY*ω.y,   -1,1)
│
├─[写入门控] 若 pitch/roll/yaw 任一玩家输入 != 0 → 一轴都不写(整体让位)
│            否则按 AimPitch/AimRoll/AimYaw 分别写入
│
├─[辨识]     LagEstimator.NoteAircraft(id); Sample(0,pitch,ω.x) (1,roll,ω.z) (2,yaw,ω.y)
│
├─[调试]     PushDebugData/Err/Tau/Coord
│
└─[稳定性]   keyboardPitch && StabilityKill → aircraft.flightAssist=false(下一帧 FilterInputs 恢复)
```

---

## 2. 误差模型(yaw / roll 平衡算法)

### 2.1 坐标系与符号

- 机体系:`x = 正右`,`y = 正上`,`z = 正前`。
- `localTarget = (dx, dy, dz)` 为单位矢量,表示"视线方向在机体系下的分量"。
- 角速度 `ω = aircraft.transform.InverseTransformDirection(rb.angularVelocity)` = `(ω.x, ω.y, ω.z)`。

### 2.2 O-B-A 几何构造

在 **x-y 平面(正右-正上平面)** 上:

记 `L = |OA| + |OB|`(`[Geometry] OaObSum`,默认 1,范围 1–2)。

1. 取视线在该平面的投影 `B = (dx, dy, 0)`,记 `bLen = |OB| = √(dx²+dy²)`(即离轴角的 `sinθ`,`θ = 离轴角`;`r=sinθ`)。
2. 在 `-y` 方向取点 `A`,使 `|OA| = L - |OB|`,即 `A = (0, bLen-L, 0)`。
3. 于是 `AO = (0, L-bLen, 0)`(沿 +y),`AB = (dx, dy+L-bLen, 0)`。

由此得到:

```
rollErr = ∠OAB / π                    // AB 与垂直轴的夹角(∠OAB),归一到 ±1
yawErr  = (L-bLen)/L · ∠OAB · (2/π)   // 弧长 |AO|·∠OAB,除以 L 归一到 ±1(L=1 时即原式)
pitchErr = -dy                        // 垂直角(取负:正 dy=目标在上 → 需要抬头)
```

其中 `∠OAB = atan2(AB.x, AB.y) = atan2(dx, dy+L-bLen)`(因为 AO 沿 +y,AB 相对 +y 的极角即 ∠OAB)。

**几何直觉**:`|OA| + |OB| = L`,把水平偏差同时表达成"一个夹角(roll)"和"一段弧长(yaw)";半径 `|AO| = L-bLen` 充当 yaw 的天然权重。`L` 越大 → A 越靠下 → roll 角整体变小、yaw 半径变大;`L` 同时决定 2.6 的跳变位置。

### 2.3 后半球处理(z < 0)

投影 `B` 丢弃了 `dz` 的符号,若直接用会**在后方出现镜像**(目标到正后方时误差反而变小)。规则:

```
若 dz < 0 且 bLen > 0:
    B' = B / bLen        // 沿 O→B 方向推到单位圆上,|OB'| = 1,方向(方位角)不变
    即 dx/=bLen, dy/=bLen, bLen = 1
```

于是后半球恒有 `bLen = 1` ⇒ `|AO| = L-1`。`L=1` 时 `|AO|=0` ⇒ **`yawErr ≡ 0`**(后半球完全交给 roll);`L>1` 时后半球仍有 yaw `= (L-1)/L·∠OAB·(2/π)`。`rollErr = atan2(dx, dy+L-1)/π`,`pitchErr = -B'.y`。
`dz = 0`(赤道面)处 `bLen = 1`,前/后两分支取值相同 → **在 z=0 边界连续**。

### 2.4 归一化

| 轴 | 原始范围 | 归一系数 | 归一后 |
|---|---|---|---|
| roll | ∠OAB ∈ (-π, π] | ÷ π | [-1, +1];正下方 180° → **±1 满舵**(仅 L<2 成立,见 2.6) |
| yaw | 弧长 = (L-bLen)·∠OAB,全局极值 ≈ L·(π/2) | × 2/(π·L) | [-1, +1](L=1 时即 2/π) |
| pitch | dy ∈ [-1, 1] | × 1 | [-1, +1](B'.y 本身已归一) |

数值复核:`L=1` 时全域 `max(|rollErr|,|yawErr|,|pitchErr|) = 1.000`;`L=1.5` 时 yaw 归一后峰值 ≈ 0.997。

### 2.5 平衡(yaw↔roll 分配)特性

`yawErr / rollErr ≈ (L - bLen)/L = 1 - sinθ/L`。`L=1` 时即 `1 - sinθ`。即(L=1):

| 离轴角 θ | bLen=sinθ | yaw 占比 (1-sinθ) | 行为 |
|---|---|---|---|
| 0°(正前) | 0 | 1.0 | roll 与 yaw 等权 |
| 30° | 0.5 | 0.5 | yaw 半权 |
| 60° | 0.87 | 0.13 | yaw 弱 |
| ≥90° | 1.0 | 0 | yaw 关闭,roll 独担 |

`L>1` 时 yaw 占比整体抬高(下限 `(L-1)/L`;`L=2` 时后半球 yaw 权重仍有 0.5)。

**近距(小离轴)用 yaw、远距(大离轴)交给 roll** —— 分配完全由几何自身产生,没有查表 / 权重拍板。
`yawErr` 的绝对值在 θ≈30° 附近达到峰值(≈满量程),再随离轴增大而衰减到 0。

### 2.6 ⚠️ 正下方跳变成因(重要)

`rollAngle = atan2(AB.x, AB.y)`,其中 `AB.y = dy + L - bLen`。

- 该式本质是 **AB 相对 +y 轴的极角**。当 `AB.x ≈ 0` 且 **`AB.y` 变号**时,极角在 `0` 与 `±π` 之间**硬跳变**。
- 对"正前下方"的目标(`dx = 0`,`dy = -bLen`):`AB.y = -bLen + L - bLen = L - 2·bLen`。
  - `bLen < L/2`(俯角 < asin(L/2)):`AB.y > 0` ⇒ `∠OAB = 0` ⇒ `rollErr = 0`。
  - `bLen > L/2`(俯角 > asin(L/2)):`AB.y < 0` ⇒ `atan2(0, 负) = π` ⇒ `rollErr = ±1`。
  - ⇒ **在俯角 = asin(L/2) 处 roll 从 0 直接切到满舵**(`L=1` → **30°**;`L=1.5` → 48.6°;`L=2` → 90°)。
- 根因:`A` 随 `bLen` 移动,参考方向 `AO` 固定沿 +y,而 `AB` 会在 `bLen = L/2` 处翻到 +y 反向;`atan2` 的分支在此处不连续。符号由 `dx` 的正负决定(略偏左/右即翻转)。

> **设计取舍**:正下方(`-1` 方向)要求"满舵翻转",该跳变保证了这一点;跳变位置随 `L` 后移(`asin(L/2)`)。但 **`L=2` 时跳变恰落在正下方**,`AB.y=0` → `atan2(0,0)=0` → **"正下方满舵"失效**(roll 变 0,全域峰值掉到 0.5),故 `L` 实际建议用在 <2。

---

## 3. ⚠️ 当前算法**没有改平(leveling)功能** → 不适合直升机

- `rollErr` 只表达"水平指向偏差",**没有任何参考重力/机翼水平的项**。
- 旧版 A/B blend 里 A 模式曾有 `errRollLevel = clamp(transform.right.y, -1, 1)`(机翼改平);本轮重构后**已删除**,不再有滚转改平。
- 后果:目标在视线上(`err=0`)时三轴输出为 `0 - Kd·ω`,**只阻尼角速率,不会把机翼摆平、也不保持姿态**。即没有 attitude hold / leveling。
- **直升机不适用**:直升机悬停/低速依赖姿态保持与自动改平,本算法在无误差时不提供任何恢复力矩。若要在直升机上使用,应换用游戏自带的 Helo/Tiltwing 指向律:
  - `AutopilotHelo.BoresightAim`(`Assembly-CSharp.decompiled.cs:15533`)+ `HoverController.ApplyInputs`(`:15280`):含 `Vector3.up` 参考(改平/侧倾转弯)与陀螺速率阻尼。
  - `Autopilot.Hover`(`:15453`)同样以 `Vector3.up` 为参考。
  - `AutopilotTiltwing.AutoAim`(`:15961`)按速度在 hover↔平飞间混合。

---

## 4. 控制律 / "PID"

### 4.1 结构:P + 速率阻尼(D),**无 I**

本 mod **没有积分项**,也没有独立的误差微分项:

```
每轴输出 = clamp( Kp · e  −  Kd · ω_corrective , -1, +1 )
   e           : 第 2 节的归一化误差(pitch/roll/yaw)
   ω_corrective: pitch→ω.x   roll→(-ω.z)   yaw→ω.y     (symbol 与 FBW 输入正方向一致)
   Kp          : 固定增益或自适应增益(见 4.2)
   Kd          : min(FeedbackGain · Kp · τ, KdMax)(见 4.3)
```

- 速率阻尼用的是**实测角速率**,不是误差微分 —— 等价于对二阶对象做速率反馈,避免纯比例在带延迟回路上激起极限环(历史教训见 4.3)。
- **无积分** ⇒ 存在稳态误差,且不会累积;这是刻意的(避免饱和卷绕)。

### 4.2 增益:固定 / 自适应

`gp, gy, gr` 初始取 `[Proportional]` 的 `GainPitch/GainYaw/GainRoll`(默认均为 **1**)。
当 `[Adaptive] Enabled = true` **且** `LagEstimator.Valid[axis]` 为真时,改用:

```
Kp = clamp( Wc / ( max(|K|, 0.05) · max(θ, ThetaMin) ), KpMin, KpMax )
```

- `K = LagEstimator.Gain[axis]`(带符号的直流增益,**取 |K|**),`τ = Tau`,`θ = Theta`。
- 默认:`Wc=0.4`,`KpMin=0.2`,`KpMax=4`,`ThetaMin=0.02`(θ 下限,防止 θ→0 时 Kp 爆掉)。
- 形式来源:一阶带延迟对象的 IMC 型整定 `Kp ≈ Wc / (K·θ)`;`Wc` 为期望带宽。
- **注意**:辨识质量不足(`Valid=false`)时自动回落到固定增益。

### 4.3 速率阻尼增益与 Kd 上限

```
Kd_axis = min( FeedbackGain · Kp_axis · τ_axis ,  KdMax )
τ_axis  = clamp(LagEstimator.Tau[axis], TauMin=0.05, TauMax=1)
FeedbackGain = [Feedback] Gain = 0.2 ; KdMax = [Adaptive] KdMax = 1.0
```

**为什么要独立设 KdMax**:`Kd` 若随 `Kp·τ` 无界增长,在带延迟的 roll/yaw 回路上会激起**极限环**。实测(日志 `190204/190239`):roll 曾出现 50–63% 满量程饱和、2–3.5Hz 整幅振荡;`Kd` 由 `≈7~17` 限到 `≤1.0` 后消失(饱和降至 0–3.6%)。

---

## 5. τ 估算算法(`LagEstimator`)

目的:在线辨识 `u → ω` 的一阶(或 yaw 二阶)模型,输出 `(Gain, Tau, Theta, R2, Valid)`。
轴映射:`axis0 = pitch(u=pitch, y=ω.x)`;`axis1 = roll(u=roll, y=ω.z)`;`axis2 = yaw(u=yaw, y=ω.y, 二阶)`。

### 5.1 采样与环形缓冲

- 每物理帧 `Sample(axis, u, y)` 写入环形缓冲(`RingSize`,默认 256 样本/轴)。
- 仅当写入最后一个轴时推进 `_head`;每 `IntervalFrames`(默认 30)帧对三轴各跑一次 `Estimate`。
- 换机(`NoteAircraft` 实例 ID 变化)或 `RingSize` 变化 → `Reset()`。

### 5.2 激励门(excitation gate)

若 `std(u) < UExcMin(0.03)` 或 符号翻转次数 `< MinSignFlips(2)` → 判定无激励,设 `Gated=true`,`R2=0`,跳过(此时 `Valid=false`,自适应回落到固定增益)。

### 5.3 平滑

```
若 AdaptiveSmooth && Tau[axis] > 0.02:  sw = clamp(round(AdaptiveK·Tau/dt), 3, 13)
否则:                                    sw = clamp(SmoothWindow, 1, 15)
对 u、y 各做长度 sw 的滑动平均后再辨识(去噪,并消除辨识偏置)
```

### 5.4 ARX 最小二乘

对每个候选滞后 `lag ∈ [0, maxLag]`(`maxLag = min(N/4, 60)`)、阶次 `order ∈ {1,2}`:
解最小二乘 `y[k] = Σ a_i·y[k-1-i] + b·u[k-lag]`,用正规方程 + 高斯消元(`FitArx`)。`cnt < 24` 则放弃。

### 5.5 极点 → τ

- 一阶:要求 `0 < c0 < MaxPole(0.985)`,`τ = -dt / ln(c0)`。
- 二阶:判别式 `disc = a1² + 4a2`;
  - 实根:两实根都在 `(0, MaxPole)` → `τ = max(t1,t2)`,`τ2 = min(t1,t2)`;
  - 复根:`mag = √(-a2)`,`τ = -dt/ln(mag)`,`τ2 = 0`(取包络时间常数)。
- 直流增益 `dc = c[order] / (1 - Σa_i)`。

### 5.6 用 H 步仿真 R² 选滞后

对每个 `lag` 用 ARX 系数做 **H 步开环仿真**(`H = PredHorizon`,默认 20),计算预测的 `R²`;取 `R²` 最大的 `lag` 作为最优(`SimR2`)。
> 这是为了修复"一步预测 R²≡1"的退化,使 `R²` 真正反映模型质量。

### 5.7 质量门与 EMA 更新

```
bestLag < 0                 → Valid=false, Gated=true
bestR2 < MinR2(0.5)         → Valid=false, Gated=true
否则 Valid=true,并做指数滑动平均(旧 0.6 + 新 0.4):
    Tau   = clamp(0.6·Tau   + 0.4·bestTau,   0, 2)
    Tau2  = clamp(0.6·Tau2  + 0.4·bestTau2,  0, 2)
    Theta = clamp(0.6·Theta + 0.4·bestLag·dt, 0, 2)
    Gain  = 保符号:g=0.6·Gain+0.4·bestDc;  Gain = sign(g)·clamp(|g|, 0.02, 50)
```

### 5.8 常量一览

| 常量 | 值 |
|---|---|
| `MinR2` | 0.5 |
| `MaxPole` | 0.985 |
| `UExcMin` | 0.03 |
| `MinSignFlips` | 2 |
| axis 阶次 | pitch=1, roll=1, yaw=2 |

**实测辨识质量(参考)**:pitch R²≈0.69(可用);roll R²≈0.46(常低于 0.5 被 gate);yaw 常因无激励而无效 —— 即自适应在 roll/yaw 上**多数时间未生效**,回落到固定增益。

---

## 6. 输出门控与手动让位

- **整体让位**:`anyManualInput = (controlInputs.pitch != 0) || (roll != 0) || (yaw != 0)`。只要任一轴玩家有输入,**三轴全部不写**。
- 无手动输入时,再按 `[Control] AimPitch/AimRoll/AimYaw` 分别决定是否写对应轴。
- 目的:玩家一旦介入,mod 立刻整体退出,避免"你松一轴、另一轴还在被你抢"。

---

## 7. 调试图表(DebugGraphMod,软依赖)

| 图 | 流 | 含义 |
|---|---|---|
| `Pitch` | `Err`(绿)/`Out`(黄)/`Tau`(青) | pitch 误差 / 实际写入的 controlInputs.pitch / 辨识 τ |
| `Roll` | 同上 | roll |
| `Yaw` | 同上 | yaw |
| `Stick XY` | `P/R`、`Yaw` | `PushDebugCoord(yaw, pitch, roll, -1)`,坐标图仅 5 点缓冲(基本为空,见第 9 节) |

图表缓冲 1800 帧。DebugGraphMod 缺失时静默降级(反射失败 → `_debugAvailable=false`)。

---

## 8. 配置项一览(默认值)

| 段 | 键 | 默认 | 说明 |
|---|---|---|---|
| General | MouseAimEnabled | true | 总开关 |
| General | InvertFreeLook | false | true 时仅按住 Free Look 才生效 |
| General | StabilityKill | false | 键盘俯仰时临时关飞控增稳 |
| Hover | ExitAngle | 20 | 俯仰/滚转超此角关闭悬停油门 |
| Proportional | GainPitch / GainYaw / GainRoll | 1 / 1 / 1 | 固定比例增益 |
| Feedback | Gain | 0.2 | 速率阻尼总系数 |
| Adaptive | Enabled | true | 启用在线自适应 Kp/Kd |
| Adaptive | Wc | 0.4 | 带宽(Kp 整定) |
| Adaptive | KpMin / KpMax | 0.2 / 4 | Kp 限幅 |
| Adaptive | TauMin / TauMax | 0.05 / 1 | τ 限幅(用于 Kd) |
| Adaptive | ThetaMin | 0.02 | θ 下限(防 Kp 爆) |
| Adaptive | KdMax | 1.0 | **Kd 硬上限(防极限环)** |
| Geometry | OaObSum | 1 | \|OA\|+\|OB\| 总长 L(范围 1–2);影响 roll/yaw 分配与 2.6 跳变位置 |
| Filter | TargetTau | 0.05 | 目标方向低通(0=关) |
| Lag | IntervalFrames / RingSize | 30 / 256 | 辨识周期 / 窗口 |
| Lag | PredHorizon | 20 | H 步仿真 R² 的 H |
| Lag | SmoothWindow | 5 | 固定平滑窗 |
| Lag | AdaptiveSmooth / AdaptiveK | true / 0.3 | 自适应平滑窗 |
| Control | AimPitch / AimRoll / AimYaw | true | 各轴输出开关 |
| Presets | ActivePreset / SavePreset / LoadPreset | Slot1 / - / - | 预设槽(5 个) |

> 注:`PropGain*` 的 `ConfigDescription` 文案仍写着 "sin(vertical/horizontal deviation)",与当前几何误差模型**不一致**(描述陈旧,数值无影响)。

---

## 9. 已知问题 / 待办

1. **正下方 roll 硬跳变**(见 2.6):`dx≈0` 且向下、`bLen` 越过 `L/2` 时 `rollErr` 从 0 切到满舵;跳变俯角 = `asin(L/2)`。正下方满舵是设计目标,这处提前触发的不连续是否可接受待定;若要消除需加滞回或改用有符号夹角(会失去"正下方满舵")。**`L=2` 会使"正下方满舵"失效**(见 2.6),建议 `L<2`。
2. **无改平 / 无姿态保持**(见第 3 节):`err=0` 时只阻尼角速率,不恢复机翼水平。**不适合直升机**;直升机应改用游戏自带 `AutopilotHelo.BoresightAim` / `HoverController`(`Vector3.up` 参考)或 `AutopilotTiltwing.AutoAim`。
3. **后半球 `yawErr`**:`L=1` 时恒为 0(`|OB'|=1 ⇒ |AO|=0`);`L>1` 时保留 `(L-1)/L` 权重。若后半球要用**原始** `bLen` 计算 `|AO|`,需另改。
4. **正后方极点**(`dx=dy=0`,`dz<0`):`bLen=0`,归一化被跳过 → 三误差全 0(测度零,但飞越时可能出现一次"误差塌陷")。需要可加后极点滞回。
5. **roll/yaw 辨识多被 gate**(R²<0.5 / 无激励)→ 自适应常回落到固定增益;`Tau` 在无效时是**陈旧值**(未记录 `Valid`,日志里看不出来)。
6. **`Stick XY` 图基本为空**:`PushDebugCoord(yaw, pitch, roll, -1)` 的第 4 参为常量、坐标图仅 5 点缓冲。可改推 `localTarget.x/y` 以观察模式切换,或删除该图。
7. **无积分项**:存在稳态误差(见 4.1),为防卷绕刻意为之。
