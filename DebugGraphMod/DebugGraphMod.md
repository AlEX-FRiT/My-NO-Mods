# DebugGraphMod — 实时图表调试模组

## 项目概述

为 Nuclear Option 提供运行时数据可视化能力。其他模组（如 MouseAimMod）将内部信号注册为数据流，本模组负责渲染为实时滚动折线图。

**设计原则**：
- 和 ConfigurationManager 相同的交互范式：独立 BepInEx 插件，其他模组通过静态 API 注册数据
- 图表采用 Unity 原生 `GUI.Window`（与 ConfigManager F1 面板风格一致），支持鼠标拖动、点击置顶
- 数据采集与渲染分离：开关只控制渲染，采集不停，方便间歇性调试

**核心类比**：

```
ConfigurationManager:  Config.Bind()  → BepInEx 注册表 → ConfigManager 读取 → F1 窗口
DebugGraphMod:        GraphRegistry.CreateChart() → 内部注册表 → Plugin.OnGUI 渲染 → 开关控制
```

---

## 架构

```
DebugGraphMod/
├── Plugin.cs             # BepInEx 入口 + OnGUI 窗口循环 + Config 开关
├── GraphRegistry.cs      # 静态 API：CreateChart() → Chart
├── Chart.cs              # 单个图表窗口：标题栏(拖动) + 图表区(GL) + 图例
├── GraphStream.cs        # 环形缓冲区：Push / PushXy / DrawFlow / DrawCoord
├── DebugGraphMod.csproj
└── GameDir.targets.example
```

### 数据流

```
其他模组 (Awake/Start)
  chart = GraphRegistry.CreateChart(...)
  stream = chart.AddStream("name", color)

其他模组 (FixedUpdate)
  stream.Push(value)         ← O(1) 环形缓冲区写入，不受开关影响

DebugGraphMod.Plugin (OnGUI)
  if (!Enabled) return       ← Toggle 控制渲染
  foreach chart in Charts:
    GUI.Window(..., DrawWindow)
```

---

## API 设计

### 图表类型

| 类型 | X 轴 | Y 轴 | 点连接 | 典型用途 |
|------|------|------|--------|---------|
| `Flow` | 采样序号 0..(bufferSize-1)，等间距填满宽度 | 数据值，映射 yMin..yMax | 按写入时间顺序 | 时域信号（PID 误差、输出等） |
| `Coordinate` | 数据 x 值，映射 xMin..xMax | 数据 y 值，映射 yMin..yMax | 按写入时间顺序 | 相平面图、XY 散点轨迹 |

### 创建图表

```csharp
// Flow 图
var chart = GraphRegistry.CreateChart(
    ChartType.Flow,
    name: "MouseAim PID",
    width: 480, height: 150,
    yMin: -1f, yMax: 1f,
    bufferSize: 600
);

// Coordinate 图
var chart = GraphRegistry.CreateChart(
    ChartType.Coordinate,
    name: "Phase Plot",
    width: 300, height: 300,
    yMin: -1f, yMax: 1f,
    bufferSize: 500,
    xMin: -1f, xMax: 1f
);
```

### 添加数据流

```csharp
var stream = chart.AddStream("Pitch Error", Color.green);
stream.Push(pitchError);          // Flow: 只传 Y
stream.PushXy(xValue, yValue);    // Coordinate: 传 X, Y
```

### 开关

通过 ConfigurationManager 的 F1 面板控制。底层是 BepInEx 标准 `ConfigEntry<bool>`：

```csharp
Config.Bind("General", "Enabled", true, "Show debug graphs")
```

关闭时窗口隐藏但数据持续推入缓冲区，重新开启后立即显示最新数据。

---

## 渲染细节

### 图表布局

```
┌── Chart Name ────────────────────┐  ← GUI.Window 标题栏（可拖动）
│                                   │
│   ╱╲  绿色 (Pitch Err)            │  ← GL.LINE_STRIP 折线
│  ╱  ╲╱╲ 蓝色 (Pitch Out)          │     (GUILayoutUtility.GetRect)
│ ╱                                 │
│                                   │
│  ■ Pitch Err    ■ Pitch Out      │  ← GUILayout.Label 图例
└───────────────────────────────────┘
```

- 背景：半透明灰色（RGBA 38,38,38,191），通过 `GUIStyle` 自定义 `Texture2D` 背景贴图
- 标题栏：Unity 默认窗口皮肤，`GUI.DragWindow(Rect(0,0,width,20))` 限定拖动区域
- 折线：`Hidden/Internal-Colored` shader + `GL.LINE_STRIP` + `GL.LoadPixelMatrix()`

### 坐标映射

**Flow 图**：

```
X[i] = chartRect.x + chartRect.width × i / (bufferSize − 1)
Y = chartRect.y + chartRect.height × (1 − InverseLerp(yMin, yMax, value))
```

**Coordinate 图**：

```
X = chartRect.x + chartRect.width × (x − xMin) / (xMax − xMin)
Y = chartRect.y + chartRect.height × (1 − (y − yMin) / (yMax − yMin))
```

多条流按注册顺序依次绘制，后者覆盖前者（Z 序 = 注册序）。

### 初始位置

图表默认放置在屏幕右下角，多个图表垂直堆叠：

```
x = Screen.width  − width  − 20
y = Screen.height − (height + 40) × (n + 1) − 20
```

用户拖动后位置保留在 `Chart.Position` 中，`GUI.Window` 返回更新后的 Rect。

---

## 环形缓冲区

```csharp
float[] buffer = new float[bufferSize];
int writeIndex;

// 写入 O(1)
buffer[writeIndex] = value;
writeIndex = (writeIndex + 1) % bufferSize;

// 读取 oldest→newest
buffer[(writeIndex + i) % bufferSize];  // i=0→最旧, i=bufferSize−1→最新
```

- `Push()` / `PushXy()` 均为 O(1)，无 GC 分配
- 环形覆盖：bufferSize 点后最旧数据被覆盖
- 缓冲区满前，未写入位置默认值为 0，图表开局会有一段从 0 爬升的短暂过程

---

## 边界情况处理

| 场景 | 行为 |
|------|------|
| `bufferSize <= 1` | Flow 图不绘制（`return`），防止除零 |
| `yMin >= yMax` 或 `xMin >= xMax` | Coordinate 图不绘制 |
| 图表注册时无流 | 窗口渲染但图表区空白，图例区为空 |
| 开关关闭后重新开启 | 历史数据仍在缓冲区，立即恢复显示 |
| 插件卸载/热重载 | `OnDestroy()` 销毁 Material 和 Texture2D |
| `Screen.width/height` 为 0 | `CreateChart` 在 `Awake/Start` 阶段调用，此时 Screen 已初始化 |

---

## 依赖

| 依赖 | 来源 |
|------|------|
| BepInEx 5.x | NuGet |
| Assembly-CSharp | 游戏 Managed 目录 |
| UnityEngine.CoreModule | 游戏 Managed 目录 |
| UnityEngine.IMGUIModule | 游戏 Managed 目录（GUI/GL 类） |
| `Hidden/Internal-Colored` shader | Unity 内置 |

---

## 当前状态

- [x] `ChartType.Flow` — 时域滚动折线图
- [x] `ChartType.Coordinate` — XY 坐标图
- [x] 半透明灰色 `GUI.Window` 背景
- [x] 鼠标拖动改变窗口位置
- [x] 图例（`■ Name` 格式，底部水平排列）
- [x] ConfigurationManager 集成开关（`Config.Bind("Enabled")`）
- [x] 环形缓冲区边界保护
- [x] 材质/纹理资源清理
- [ ] 与其他模组的实际集成（MouseAimMod 等注册数据）
- [ ] 编译验证
- [ ] 游戏内测试
