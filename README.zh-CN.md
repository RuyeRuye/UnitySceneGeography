# Scene Geography 使用手册

[English](README.md) | **简体中文**

GeoReference 用于把经纬度和高度转换为 Unity 场景位置，也可以把场景位置反算为地理坐标。通过几个已知对应点完成标定后，即可放置 POI、设置地理朝向，或读取玩家所在位置的经纬度。

系统适用于街区、园区等几公里范围的场景。它不依赖高斯渲染器，也不负责加载点云、解析业务配置表或控制玩家移动。

- 第 1 节：按步骤接入场景。
- 第 2 节：理解 Calculate、Apply，以及 Profile、Reference、Snapshot 的关系。
- 第 3～5 节：查阅配置参数。
- 第 6～7 节：调用运行时与编辑器 API。
- 第 8～9 节：了解坐标限制、运行样例和排查问题。

## 1. 配置一个可用的场景

### 1.1 先体验样例

通过 **Window > Package Manager > + > Add package from disk** 选择本包的 package.json，或将包放入项目 Packages 目录作为嵌入包。包 ID 是 io.github.ruyeruye.scene-geography；嵌入目录建议与该 ID 相同，显示名称仍为 Scene Geography。

最低声明版本为 Unity 2021.3 LTS。编辑器对象查找通过版本适配封装，在 2021.3 下不要求较新的 FindObjectsByType API。

正式程序集不依赖样例和 Input System。业务使用 asmdef 时，应引用 SceneGeography.Core 和 SceneGeography.Unity；编辑器工具还需引用 SceneGeography.Editor。

体验可选样例：

1. 在 Package Manager 安装 **Input System**（com.unity.inputsystem）。在 **Player > Active Input Handling** 中启用 **Input System Package (New)** 或 **Both**，如 Unity 提示则重启。
2. 在 Package Manager 选中 **Scene Geography**，展开 **Samples**，导入 **Basic Sample**。
3. 在 Assets/Samples/Scene Geography/<版本>/Basic Sample 中打开 Scenes/GeoReference-Upright.unity，进入 Play 模式。

场景包含合成建筑、五个 POI、自由相机和操作面板，不需要额外挂组件。包内 Samples~/BasicSample 默认不参与编译，应打开导入后的副本，而不是直接打开包内场景。

样例坐标与模型都是合成的。接入真实高斯模型时，需要建立自己的控制点和 Profile，不能沿用样例的标定数据。

样例自带简单的无光照 Shader，用于 Built-in 和 URP，不会因为安装了 URP 就自动选择 URP Shader。它通过导入样例中的 `Resources/SceneGeographySample` 加载，保证 Player 构建保留该 Shader；使用演示时请保留该目录。样例渲染不支持 HDRP；正式地理 API 与渲染管线无关。

### 1.2 接入自己的模型

1. **确定模型根节点。** 建议把模型放在稳定的 GeographicRoot 下。这个 Transform 将作为地理系统的 Root；它不需要挂地理组件。
2. **检查缩放。** Root 及其父级的组合变换必须只有平移、旋转和正的统一缩放，不能有非均匀缩放、镜像或剪切。POI 的父级也有相同要求。
3. **创建管理对象。** 在空对象上挂 GeoReference，指定 Root。Root 留空表示直接使用世界空间。填写 Local Frame Id，例如 block-a-model-v1，用来区分不同模型的局部坐标约定。
4. **创建标定配置。** 在 Project 中选择 **Create > Scene Geography > Calibration Profile**，填写与 Reference 相同的 Local Frame Id，并选择标定模式。
5. **准备对应点。** 为每个点填写模型局部位置、现实经纬高、水平坐标类型和高度参考。通常建议使用分散的三个 Fit 点，再留一至两个独立 Check 点。
6. **计算并检查。** 将 Profile 赋给 Reference，点击 **Calculate Candidate (no object writes)**，检查比例、残差和警告。
7. **添加 POI。** 在 POI 对象上挂 GeoAnchor，指定 Reference、地理坐标和偏移。需要朝向时，再挂 GeoOrientation。
8. **应用预览。** 在 Reference 上点击 **Apply Candidate**，已启用的绑定组件会更新一次。检查 POI 是否出现在预期位置。
9. **配置运行时初始化。** 静态场景可开启 Reference 的 Initialize On Enable；异步加载模型时，在 Root、模型和配置就绪后手动调用 TryInitialize。
10. **接入玩家。** 玩家继续由自己的移动器控制。需要显示经纬度时添加 GeoPositionReporter；不要给可自由移动的玩家挂持续定位用的 Anchor。

建议让多个 POI 锚点在 Root 下保持平级，把标记模型、文字等视觉对象放到各锚点的子节点。避免让一个地理锚点成为另一个地理锚点的父级，以免出现额外的更新顺序依赖。

### 1.3 选择标定模式

| 模式 | 需要已知什么 | 适合的情况 |
| --- | --- | --- |
| SingleAnchor | 一个 Fit 点、局部 Up、一个方向对应的地理航向、每米对应的局部单位数 | 比例和方向已经确定，只需确定位置 |
| Upright | 至少两个有水平间距的 Fit 点、局部 Up | 建筑基本竖直的街区场景，通常优先选择 |
| Spatial | 至少三个非共线 Fit 点 | 模型有倾斜，或不能预先确定局部 Up |

Upright 和 Spatial 都可估算统一比例，也可使用已知比例。三个点可以共面，但 Spatial 不能使用全在同一直线上的点。

Fit 点用于求解。Check 点不参与求解，只用来检查其他位置的误差；因此仅有 Fit 残差很小，不代表整个场景都已准确对齐。

### 1.4 使用标定工作台采集和更新控制点

编辑器界面默认使用 **English**。在工作台顶部的 **Language > 简体中文** 切换语言，工作台及相关 Inspector 会同时刷新。偏好只保存在当前用户的 EditorPrefs，不写入 Profile、场景或项目设置；切换不会丢失工作草稿、待处理的点操作、采集选择或候选结果。以下按钮名称按中文界面描述；固定菜单路径仍使用英文。API 名称、ID、枚举标识和底层错误诊断保持原样，不引入 Unity Localization 或 Addressables 依赖。

在模型对应位置放置空对象并挂 GeoControlPointMarker，指定 Reference、唯一 Point Id、Role、Weight 及明确参考的地理坐标。打开 **Window > Scene Geography > Calibration Workbench**，或从 Marker Inspector 跳转。Marker 采集、创建和更新均通过工作台完成。

- 顶部集中提供读取/刷新场景、计算候选、保存、应用等操作；中间左右分栏，底部一行状态提示（悬停可看完整状态）。顶部不再放 Reference 对象选择框，也不再提供重复的配置资产定位按钮。
- 左侧按“场景 → Reference → 相关对象”分层，用色块和文字标签区分 Marker、Anchor、Orientation、Reporter。每个 Reference 显示只读场景引用、关联 Profile、资产路径及组件数量；点击“编辑标定”切换工作配置。包含未启用对象和禁用组件；未绑定/无效 Reference 的对象按所属场景单独列出，Prefab/预览场景不参与。
- 同一对象、同一 Reference 的多个组件合为一条；同一对象的组件若引用不同 Reference，则分别归组。归属按组件引用判断，不依赖 Transform 父子层级。
- 支持组件类型多选筛选（并集）及对象名称、层级路径、Point ID 搜索。筛选只改变显示，不清空已勾选 Marker；“勾选/取消可见 Marker”只作用于当前 Reference 的可见点，并保留被筛选隐藏的勾选。
- 点击对象名称在右侧“对象详情”查看/编辑场景组件；“定位”选择对象，“预览”在 Scene 视图框选，不移动物体。Anchor、Orientation、Reporter 只用于浏览和编辑，不作为控制点采集。右侧另有“标定配置”“计算报告”分页。
- “读取当前场景”读取所有已加载普通场景并重置筛选；“刷新场景”保留筛选。两者都保留当前有效工作草稿，不自动保存或应用。
- 选择目标 Profile 和待采集 Marker。默认按 Point Id 合并，保留未选点及原有模式、航向、比例、验收设置；完整替换会列出待删除 ID，并要求确认。改 ID 视为新增，不会悄悄删除旧点。
- 显式选择 SingleAnchor、Upright、Spatial 模式预设，不重置其他参数。下方序列化参数是临时工作草稿；通过 Profile 对象字段查看和定位原资产，不在工作台重复嵌入资产 Inspector。在外部修改资产后，须重新加载草稿才能保存，避免覆盖外部修改。
- **计算候选**不写资产、不移动物体；**保存到 Profile**只保存通过校验的配置；**应用候选（仅预览）**只应用；**保存并应用**执行两步；**另存为新 Profile…**不覆盖已有资产，是否绑定到 Reference 显式选择。
- 输入、选点或模式变化会使候选失效。保存保留资产 GUID 并支持 Undo，完成后重建工作副本基线；撤销或脚本重载不能恢复内存标定解，需要重新计算并应用。

关闭修改过的工作草稿（参数或控制点操作）时，会提示保存、放弃或取消。参数输入和点操作可在程序集重载后恢复，候选结果不恢复；恢复后仍检查外部 Profile 是否改变，避免覆盖外部编辑。

同步比较仅吸收 double 序列化舍入，不使用固定的模型单位容差。Root 后代 Marker 沿局部变换链采集，避免移动 Root 后的世界浮点往返产生虚假修改。

采集得到 Reference.Root 局部位置，Marker 不必是 Root 的直属子节点。移动 Marker 不会自动写入 Profile。“Marker 是否已保存”和“配置是否已应用”分别显示。Reference 原有计算、应用按钮保留。

#### 维护改名或缺少 Marker 的配置点

Reference 分组也会展示仅存在于配置中的控制点，状态为 **仅配置：未找到场景 Marker**。匹配范围是当前已加载的普通场景，按相同 Reference 和 Point ID 查找，包含未启用对象。未找到不代表已删除：控制点可以纯数值填写，对应场景也可能尚未加载。占位行可查看配置值，不提供场景定位、预览或采集勾选框；受 Marker 类型筛选与 Point ID 搜索控制。先点击对应 Reference 的 **编辑标定**，再维护其配置点。

- 红色 **从配置移除** 仅暂存草稿操作。状态变为 **待移除**，可 **撤销移除**；不删除任何 GameObject，保存前不修改 Profile。
- Marker 从 A 改名为 B 后，在 B 行点击 **替换已有配置点…**，明确选择旧点 A，暂存一次 **A → B** 替换。B 自动纳入采集，其他配置点保留，候选不会同时留下两个 Fit 点。不按位置或单点模式自动推断改名。
- **撤销替换** 恢复操作前的采集勾选状态。替换期间目标保持勾选，须先撤销替换再取消勾选。若目标再次改名、销毁、换 Reference，或旧 ID 在场景中重新出现，则替换失效，不悄悄改变操作含义；撤销后重新选择。
- 变更汇总和保存确认分别列出新增、更新、替换与移除。显式移除／替换可与完整替换集合组合使用；最终候选仍须满足所选模式的 Fit 条件，移除唯一 Fit 点后不能保存。
- 参数修改和待处理点操作均纳入关闭保护、切换上下文确认与程序集重载恢复。放弃草稿清除操作，不撤销场景 Marker 的编辑。外部 Profile 修改仍阻止旧草稿覆盖；除非选择 **保存并应用**，保存不会应用标定。

### 1.5 快速填写 Reference 和 Marker Id

Marker Inspector 的 **Auto Fill Reference & Point ID** 按钮可补齐空字段，支持多选：

- Reference 为空，且对象所属场景恰好有一个 GeoReference 时，填入该引用。停用的对象和组件也计入数量。
- 场景中没有或有多个 GeoReference 时不做猜测，显示提示，由你手动选择。
- Point Id 为空或仅有空白时，使用该对象的名称；已有 Id 和 Reference 都不覆盖。
- 多个 Marker 同名可能产生重复 Id，按钮会提示，需手动改为唯一值，不自动追加编号。
- 只查找对象所属场景，不使用其他已加载场景的 Reference；Prefab 资产和预览编辑环境不自动绑定场景对象。

Anchor、Orientation、Reporter 和样例组件的 Inspector 也提供 **Auto Assign Reference**。按钮只用于编辑模式，支持 Undo，不自动保存场景，也不会计算或应用标定。它不会改写 Profile 内已经保存的控制点 Id。

自定义 Inspector 可继承 SceneGeography.Editor.GeoReferenceConsumerEditor，复用默认字段绘制和自动填写按钮；目标组件须有名为 reference 的序列化 GeoReference 字段。

## 2. 配置、计算、应用和快照

### 2.1 Profile 是配置文件，Reference 是当前正在使用的坐标系

例如，Profile 中记录了“模型局部位置 A 对应某个经纬高”“模型竖直方向是 +Y”“需要估算比例”。这些是**计算坐标关系所需的数据**。

Calculate 根据这些数据计算平移、旋转和比例，得到一个待应用结果，称为 Candidate。此时只显示计算报告，不改变当前坐标系，也不移动 POI。

Apply 才把这个结果交给 GeoReference 使用。此后，通过 Reference 查询位置、方向，或由组件更新 POI，都会使用这份已应用的标定结果，不会每次重新求解 Profile。

因此，**修改 Profile 不会立即改变已经应用的坐标系**。需要重新 Calculate，再 Apply。计算或应用失败时，原先可用的标定仍然保留。这样在输入尚未调整完时，不会让已定位的 POI 随之跳动。

### 2.2 编辑模式下，Apply 是一次预览，不是持续跟随

未进入 Play 时，也可以使用 Reference 的 Calculate / Apply 按钮：

| 操作 | 是否重新求解 | 是否更新物体 |
| --- | --- | --- |
| Calculate Candidate | 是，读取当前 Profile | 否，只生成候选和报告 |
| Apply Candidate | 否，提交刚才算出的候选 | 是，更新已启用的 Anchor、Orientation，并采样 Reporter |
| Anchor：Apply Position Now | 否，使用已生效坐标系和 Anchor 当前参数 | 只更新当前对象的位置 |
| Orientation：Apply Orientation Now | 否，使用已生效坐标系和 Orientation 当前参数 | 只更新当前对象的旋转 |
| Reporter：Sample Coordinates Now | 否 | 只读取坐标，不移动对象 |
| Clear Calibration | 否，清除生效标定 | 不归零，也不撤销已有姿态 |

编辑模式下不会每帧自动重写物体。Apply 后再修改 Anchor 坐标或 Orientation 目标，需要再次点击对应组件的立即应用按钮；若修改的是 Profile，则应重新 Calculate / Apply。

移动同一个 Root 不需要重新标定，但编辑模式下的绑定不会持续刷新；可以重新应用候选来统一预览，或按位置、朝向的顺序手动应用相关组件。不要通过随意拖动一个 POI 后观察它是否回弹来判断标定是否生效。

编辑器应用支持物体姿态 Undo，并把相关场景标记为待保存；不会自动保存场景。Undo 只恢复 Transform，不恢复内存中的标定结果。撤销预览后，建议 Clear 或重新 Calculate / Apply。

### 2.3 怎么判断当前是否算过、是否应用过

Reference Inspector 中的 **Calibration status** 分别显示：

| 状态 | 含义 |
| --- | --- |
| Candidate ready to apply = Yes | 已成功计算，存在尚未应用的候选；Apply 按钮可用 |
| Active calibration = Yes | Reference 已有一份生效标定，可以进行坐标换算 |
| Calibration version | 成功应用或 Clear 时递增，用来区分不同状态；不是“应用次数” |
| Candidate report (not applied) | 尚未应用的计算结果 |
| Last calculation report | 最近一次未产生可用候选的计算报告 |
| Active calibration report | 当前真正用于换算的标定报告 |

候选属于当前 Inspector 的临时状态，切换选择导致 Inspector 被重建时可能丢失。这里显示的是当前可用状态，不记录历史上是否曾经点击过按钮。

两个 Yes 可以同时出现：表示旧标定仍在使用，同时又计算了一份新候选。应用后候选被消耗，Active calibration 保持 Yes。

代码中可读取 IsInitialized 和 CurrentReport 判断是否已有生效标定；需要检查当前 Root 是否也可用时，调用 TryCaptureSnapshot，并处理返回的错误。IsInitialized 为 true 不代表后来修改的 Profile 已经应用。

Profile 和场景中的序列化参数可以保存到磁盘；计算出的候选和生效标定只在内存中。脚本重载或重新打开场景后，需要重新初始化。运行时也应按自己的加载流程初始化，不要依赖编辑器预览残留的状态。

#### 报告怎么看

将鼠标停在报告的字段名或数值上，可以查看含义、单位和注意事项；逐点残差及诊断提示也有说明。

| 报告项 | 含义 |
| --- | --- |
| Success | 数学求解和已启用的精度限制是否通过；未启用限制时，不表示达到业务精度要求 |
| Fit / Check | 参与拟合的点数 / 独立检查的点数 |
| Local units / meter | 现实一米对应的 Root 局部单位数，不包含 Root 世界缩放 |
| Fit RMS (meters) | Fit 点三维误差的加权均方根，反映整体误差，不是最大值 |
| Check max (meters) | 最差 Check 点的三维位置误差，单位现实米，不是配置的允许上限 |
| 每点 E / N / U | 填写的模型位置减去预测位置后，换算出的东、北、上有符号残差；单位现实米 |

AccuracyThresholdNotConfigured 表示四个 Fit/Check 精度限制开关均未开启。此时报告仍会显示实际误差，但不据此判定业务精度是否合格；这条警告本身不代表误差超标。只修改阈值数字而不勾选对应开关，也不会启用限制。

### 2.4 Snapshot 是一份固定的坐标映射

Snapshot 用于让**一批计算使用同一份标定和 Root 变换**。它保存捕获时的映射，不保存整个 Inspector，也不保存所有 POI 的输入。

例如：

1. 当前 Root 在位置 A，捕获 snapshotA。
2. 用 snapshotA 转换一批 POI。
3. 后来 Root 移到位置 B，或 Reference 应用了新标定。
4. snapshotA 仍按旧映射计算；重新捕获的 snapshotB 才使用新映射。

每次传给 Snapshot 的地理坐标、偏移和朝向请求仍由调用方决定，并没有被冻结。它固定的是“这些输入如何换算到场景”，不是输入本身。

只转换一个位置时，直接调用 Reference.TryGeoToWorld 等方法即可，内部会获取当前映射。需要处理一整批 POI 时，可先 TryCaptureSnapshot，再让整批复用它。不要把一份旧 Snapshot 长期当作会自动更新的 Reference 使用。

### 2.5 运行时如何更新

运行时，Reference 默认在 LateUpdate 中依次更新所有位置、所有朝向、所有 Reporter，然后发出 Changed 通知。这样同一批 look-at 可以读取到目标已经更新的位置。

- Profile 改动：重新初始化，或先准备候选再应用。
- 同一个 Root 平移、旋转、统一缩放：下次同步使用新变换，无需重新拟合。
- 更换 Root 对象或 Local Frame Id：先 Clear，再更换并初始化。
- Anchor / Orientation 配置改动：优先调用 SetPlacement、SetRequest 等方法；它们标记待更新，不立即写 Transform。需要立刻生效时再 TryApply。
- 禁用绑定组件：停止该组件的自动更新。手动调用 TryApply 仍是一次明确的写入请求。

### 2.6 哪些参数是实时读取的

| 数据 | 读取时机 |
| --- | --- |
| Profile 控制点、模式、比例策略 | 构建请求并 Calculate / 初始化时读取；不会在每次坐标转换时重新求解 |
| Root 的世界变换 | Reference 获取当前映射或同步时检查；不是永久固定为 Calculate 时的位置 |
| Anchor / Orientation 参数 | 实际应用时从当前组件配置构建；运行时修改应使用配置 API 标记待更新 |
| 已捕获 Snapshot 的标定和 Root 变换 | 固定为捕获时的状态，后续转换不会重新读取 Reference 或 Transform |

因此，编辑中的 Profile、生效标定和一次转换用的映射是不同层次。普通业务调用 Reference 即可，不需要手动管理 Snapshot。

## 3. 标定 Profile 参数

GeoCalibrationProfile 是 Project 中的资产，不是挂在 GameObject 上的组件。Inspector 会将当前模式不使用的字段置灰；切换模式不会清空这些字段。

### 3.1 参数表

#### 基础设置

| 字段 | 含义、适用条件与默认值 |
| --- | --- |
| Schema Version | 格式版本，当前 1，Inspector 只读 |
| Local Frame Id | 模型局部坐标身份，建议必填并与 Reference 一致；Profile 非空标识不匹配时拒绝初始化 |
| Mode | SingleAnchor / Upright / Spatial，默认 Upright |
| Points | 控制点列表，默认空；模式所需 Fit 数量不满足时失败 |
| Local Up | Root 局部空间的现实 Up，默认 (0,1,0)，SingleAnchor/Upright 使用；Spatial 置灰 |
| Local Heading Direction | 已知地理方向的 Root 局部向量，默认 (0,0,1)，仅 SingleAnchor；先投影到 Up 水平面，不能与 Up 平行 |
| Bound Heading Degrees | 上述向量对应的地理航向，默认 0°，仅 SingleAnchor |
| Estimate Scale | 默认 true，Upright/Spatial 估算统一比例；SingleAnchor 置灰，始终用固定比例 |
| Local Units Per Meter | 默认 1，必须正数；SingleAnchor 或关闭 Estimate Scale 时可编辑 |
| Options | 原点、数值退化与业务验收选项 |

#### 每个控制点

| 字段 | 含义 |
| --- | --- |
| Id | 唯一非空标识，重复时拒绝求解 |
| Role | Fit 参与求解；Check 只检查结果，不影响拟合和自动原点 |
| Weight | 大于零，默认 1。Fit 权重影响求解；Check 权重影响同组 RMS；最大误差不加权 |
| Local Position | Root 局部位置，模型单位，不是世界位置或 ENU 现实米偏移 |
| Coordinate | 同一现实位置的经纬高及参考；见第 4 节 |

#### 原点和数值条件

| 字段 | 含义、默认值与可编辑条件 |
| --- | --- |
| Use Explicit Origin | 默认 false；关闭时自动从 Fit 的 ECEF 加权中心计算 |
| Origin Latitude Degrees | 显式原点的 WGS84 纬度，仅开关开启可编辑 |
| Origin Longitude Degrees | 显式原点的 WGS84 经度，仅开关开启可编辑 |
| Origin Ellipsoid Height Meters | 显式原点绝对椭球高，米；不继承控制点的 GCJ/BD 或相对高度参考 |
| Minimum Baseline Meters | 默认 0.01，现实米；Upright/Spatial 的最小地理基线检查，SingleAnchor 置灰 |
| Near Collinear Ratio | 默认 0.0001，越大越严格，仅 Spatial 可编辑；检验两个独立空间方向，不拒绝所有共面点 |
| Minimum Scale / Maximum Scale | 默认 0.000001 / 1000000，允许的局部单位/现实米比例范围 |

#### 误差和距离限制

| 开关 | 配套数值（默认） | 限制 |
| --- | --- | --- |
| Limit Fit Rms | Fit Rms Meters=1 | Fit 三维加权 RMS |
| Limit Vertical Rms | Vertical Rms Meters=0.5 | Fit 沿参考 Up 的加权 RMS |
| Limit Check Rms | Check Rms Meters=1 | Check 三维加权 RMS |
| Limit Check Maximum | Check Maximum Meters=2 | Check 单点最大三维残差，不加权 |
| Limit Distance | Maximum Distance Meters=5000 | 到 ENU 原点的三维半径，包含高度和最终位置偏移 |

Local Position 始终是 Reference.Root 的局部坐标，不一定等于 Marker 直属父级下的 localPosition。Local Units Per Meter 表示“现实一米等于多少 Root 局部单位”，不是它的倒数。

SingleAnchor 示例：若局部 +Y 是上，+X 是现实正东，局部单位与米一致，则填写 Local Up=(0,1,0)、Local Heading Direction=(1,0,0)、Bound Heading Degrees=90、Local Units Per Meter=1。

### 3.2 原点和误差限制怎么选

首次接入可以使用自动原点；系统从 Fit 点计算参考原点，Check 不参与。若要让多次标定使用相同的地理基准，可启用 Use Explicit Origin，填写固定的 WGS84 经纬度和椭球高。

Fit/Check 的误差单位都是现实米。所有 Limit 开关默认关闭，启用后才检查对应阈值；开启 Check 限制时必须提供 Check 点。超限会拒绝新标定，而不是只显示警告。

应根据控制点测量精度和业务需求设置阈值。样例使用合成数据，其毫米级残差不代表实际 GPS 或点云也能达到这个精度。

## 4. 地理坐标参数

控制点、Anchor 和地理朝向目标都使用同一组坐标字段：

| 字段 | 含义 |
| --- | --- |
| Horizontal Type | Wgs84 / Gcj02 / Bd09，必须选择，Unspecified 无效 |
| Height Kind | 必须明确高度参考，见下表 |
| Latitude Degrees | 纬度，北正南负，度 |
| Longitude Degrees | 经度，东正西负，度 |
| Height Meters | 当前高度参考下的高度，现实米，不是 Unity Y |
| Ellipsoid Zero Meters | 相对高度零点的绝对椭球高，仅 RelativeToEllipsoidZero 可编辑 |
| Assumed Height Zero | 零点是否为假定值，仅 RelativeToEllipsoidZero 可编辑；标定报告会警告，不增加精度 |
| Height Model Id | 仅 Orthometric 可编辑；当前没有可用高程模型，界面明确警告 |

| Height Kind | Height Meters 语义 |
| --- | --- |
| Ellipsoidal | 绝对椭球高，其他零点配置不生效 |
| RelativeToEllipsoidZero | 绝对椭球高＝Ellipsoid Zero Meters＋Height Meters；例如零点 20m 加相对 7m 得 27m |
| Orthometric | 正高/海拔；目前转换返回 MissingHeightModel，不能只填模型名称就启用 |

**水平类型必须来自数据来源说明，不能根据数值猜测。** 把 GCJ-02 改标成 WGS84 不会完成转换，只会让系统错误解释数值。

高度也要明确来源。只有相对高度时，可以使用 RelativeToEllipsoidZero 并声明一个假定零点；控制点与 POI 应采用一致约定。海拔不能直接当作椭球高。当前尚未接入高程模型，Orthometric 会返回 MissingHeightModel。

## 5. 场景组件参数

### 5.1 GeoReference：管理坐标系

挂在独立管理对象上即可。管理对象自身的位置不定义坐标系，Root 才是局部空间的参考。

| 字段 | 含义 |
| --- | --- |
| Root | 参考 Transform；留空使用世界模式；已配置 Root 被销毁会报错，不自动回退。初始化后只读，Clear 后可换 |
| Local Frame Id | 模型坐标身份；初始化后只读，Clear 后可换 |
| Profile | Inspector 候选计算和启用时初始化的输入资产；修改资产会使当前 Inspector 候选失效，需重算 |
| Initialize On Enable | 默认 false；每次启用时尝试用 Profile 初始化，异步加载应关闭 |
| Synchronize Automatically | 默认 true；LateUpdate 按位置→朝向→坐标读取同步。关闭后由业务调用 SynchronizeBindings |
| Gizmo Length Meters | 默认 5，选中时参考轴显示长度，现实米，仅辅助显示 |

### 5.2 GeoControlPointMarker：采集对应点

| 字段 | 含义 |
| --- | --- |
| Reference | 采集时使用的坐标系，未初始化也可采集 |
| Point Id | 唯一非空控制点名称 |
| Role / Weight | Fit 或 Check；正数权重，默认 1 |
| Coordinate | Marker 当前模型位置对应的现实坐标 |
| Transform Position | 对应模型点的世界位置，采集会转换到 Reference.Root 局部 |

Marker 仅用于采集，不是运行时必须保留的对象。采集完成后，标定读取的是 Profile 中保存的数值。

### 5.3 GeoAnchor：管理位置

| 字段 | 含义 |
| --- | --- |
| Reference | 使用的地理系统，未就绪时等待，不使用默认零坐标 |
| Coordinate | 锚点原始地理坐标；运行时更新使用 SetPlacement |
| East Offset Meters | 向东现实米偏移，负值向西，不是世界 X |
| North Offset Meters | 向北现实米偏移，负值向南，不是世界 Z |
| Up Offset Meters | 沿参考 Up 的现实米偏移，负值向下，不修改源坐标高度 |
| Update Policy | OnChange（默认）：配置/标定/Root/父级映射变化时定位；Continuous：每次 Reference 同步都写入 |
| Start Automatically | 默认 true，自动开始；false 时先手动成功 TryApply 一次，之后参与同步。它不是一次性定位开关 |

OnChange 不会持续纠正其他脚本对位置的修改；Continuous 会在每次同步时写位置。不要让 Anchor 和玩家移动器同时控制同一对象。

TryGetPlacement 可以读取并验证当前配置，不需要 Reference 已初始化，也不会写 Transform。没有可用映射时，Anchor 保留最后位置，并通过 HasAppliedPosition 和 LastError 表示状态。

### 5.4 GeoOrientation：管理旋转

| 字段 | 含义、互斥关系 |
| --- | --- |
| Reference | 使用的地理系统 |
| Kind | Heading、GeoTarget、WorldTarget；Use Transform Target 开启时置灰 |
| Heading Degrees | 仅 Heading 可编辑，北 0°、东 90° |
| Target Coordinate | 仅 GeoTarget 可编辑，完整坐标参考 |
| Target World Position | 仅 WorldTarget 可编辑，固定世界位置，单位世界单位 |
| Use Transform Target | 默认 false；开启后覆盖 Kind 的目标来源 |
| Transform Target | 仅开关开启时可编辑，持续追踪场景 Transform；目标销毁保留旋转并报错 |
| Look Mode | Horizontal 忽略高度差；Full3D 保留高度差。固定 Heading 模式置灰 |
| Yaw Offset Degrees | 基础朝向上的右转偏角，正值顺时针，度 |
| Pitch Up Offset Degrees | 抬头偏角，正值抬头，度 |
| Roll Clockwise Offset Degrees | 沿前向轴的顺时针侧倾偏角，度 |
| Model Correction | 地理旋转后乘的模型局部修正，单位四元数，默认 identity；不是三个欧拉角，可优先旋转视觉子节点 |
| Start Automatically | 默认 true；关闭时先手动成功 TryApply，再参与同步 |

SetRequest 切换为固定航向或地理/世界位置目标；SetTransformTarget 切换为 Transform 目标。目标被销毁时会报错并保留最后旋转，不会自动改为其他目标。

Model Correction 用于修正模型自身的前向轴，要求单位四元数。一般更容易维护的做法是保留锚点的 +Z 前、+Y 上约定，只旋转视觉子节点。

### 5.5 GeoPositionReporter：读取坐标

| 字段 | 含义 |
| --- | --- |
| Reference | 使用的地理系统 |
| Target | 初始留空读取自身；明确指定的目标被销毁后报错，不回退为自身。运行时切换使用 Configure |
| Output Type | 输出 Wgs84 / Gcj02 / Bd09，默认 Wgs84 |
| Output Height | 输出高度参考，默认 Ellipsoidal；Orthometric 会明确显示未支持警告 |
| Ellipsoid Zero Meters / Assumed Height Zero | 仅相对高度输出可编辑，含义同 Coordinate |
| Height Model Id | 仅 Orthometric 可编辑，目前无模型 |
| Sampling Interval Seconds | 默认 0.2，秒；0 表示每次 Reference 同步均采样。TrySample 可立即采样，不受间隔限制 |

读取 LastValue 前先检查 HasValue。采样失败时可能保留最后成功值，但它不再表示当前有效结果。PositionUpdated 仅在成功采样时触发。

### 5.6 GeoOrientationReporter：读取实际姿态

这是 GeoOrientation 对应的只读测量组件，可以测量没有地理绑定的玩家、相机或任意 Transform，不控制目标的位置或旋转。

| 字段 | 含义 |
| --- | --- |
| Reference | 提供生效标定和固定地理基底 |
| Target | 初始为空表示自身；显式指定的目标销毁后返回错误，不回退到自身 |
| Model Correction | 测量前从实际旋转右侧移除的单位四元数；identity 表示直接测量目标自身轴。读取 GeoOrientation 的逻辑地理姿态时，使用与其相同的模型修正 |
| Sampling Interval Seconds | 默认 0.2 秒；0 表示每次同步采样。TrySample 不受间隔限制 |

Configure(reference, target) 直接测量目标轴；Configure(reference, target, correction) 先移除模型修正。TrySample 返回 GeoAttitude；使用 LastValue 前检查 HasValue，显示航向、侧倾前再检查 HasHeading/HasRoll。竖直时完整四元数和俯仰仍有效。成功采样触发 OrientationUpdated；失败保留 LastValue，但清除 HasValue。禁用组件停止自动采样，仍可显式调用 TrySample。两种 Reporter 均在所有位置和朝向绑定更新后执行。

## 6. 运行时 API

运行时命名空间为 SceneGeography。经纬度使用度，高度和 ENU 偏移使用现实米；地理偏移参数顺序为 East、North、Up。航向从北向东递增：北 0°、东 90°、南 180°、西 270°。

### 6.1 常用入口

| 目标 | API |
| --- | --- |
| 选择空间身份 | TrySetRoot、TrySetLocalFrameId；已初始化先 Clear |
| 一步初始化/重标定 | Profile.TryBuildRequest → Reference.TryInitialize |
| 先评审后提交 | TryPrepareCalibration → 检查 candidate.Report → TryApplyCalibration |
| 同批一致性 | TryCaptureSnapshot；本批位置、朝向、单位换算使用同一 snapshot |
| 原始坐标位置 | TryGeoToWorld、TryWgs84ToWorld、TryGeoToLocal、TryWgs84ToLocal |
| 带偏移放置 | TryGetWorldPosition(GeoPlacement) |
| 反向读取 | TryWorldToWgs84、TryWorldToCoordinate、TryLocalToWgs84 |
| 方位与旋转 | TryGetWorldAxes、TryGetLocalAxes、TryWorldDirectionToHeading、TryGetWorldRotation、TryGetWorldPose |
| 米制换算 | TryMetersOffsetToWorld、TryMetersToWorldUnits、TryWorldUnitsToMeters |
| Mono 更新 | Anchor.SetPlacement、Orientation.SetRequest / SetTransformTarget、Reporter.Configure |
| 立即执行 | Anchor.TryApply、Orientation.TryApply、Reporter.TrySample；不等同于仅修改配置 |
| 无 Unity 数学 | GeoCalibrationEngine.TrySolve → solution.Frame |

Try 方法返回 false 时应处理 error，不要使用失败输出。不可变值的构造器和部分配置方法会对非法参数抛出 ArgumentException。

Reference 和组件操作应在 Unity 主线程执行。Snapshot 捕获后不再读取 Transform；同一批转换复用它，也可以减少重复检查 Root 的开销。

### 6.2 初始化、放置 POI、跳转与读取坐标

下面是完整示例类。调用前应准备有效的 Reference、Transform 和地理坐标，并使 Reference 的 Local Frame Id 与 Profile 一致。

```csharp
using UnityEngine;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoRuntimeExamples
    {
        public static bool Initialize(
            GeoReference reference,
            GeoCalibrationProfile profile,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            report = null;
            if (reference == null || profile == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Reference/Profile required.");
                return false;
            }

            if (!profile.TryBuildRequest(out var request, out error))
            {
                return false;
            }

            return reference.TryInitialize(request, out report, out error);
        }

        public static bool PlacePoi(GeoReference reference, GeoCoordinate coordinate, Transform target, out GeoError error)
        {
            // Use one snapshot for position and rotation: 1 m east, 2 m up, heading 105 degrees.
            if (!reference.TryCaptureSnapshot(out var snapshot, out error))
            {
                return false;
            }

            var placement = new GeoPlacement(coordinate, new GeoEnuOffset(1, 0, 2));
            var orientation = GeoOrientationRequest.ForHeading(GeoHeading.FromCardinal(GeoCardinalDirection.East, 15));
            if (!snapshot.TryGetWorldPose(placement, orientation, out var pose, out error))
            {
                return false;
            }

            target.SetPositionAndRotation(pose.position, pose.rotation);
            return true;
        }

        public static bool GetVisitPose(GeoReference reference, GeoCoordinate poi, out Pose pose, out GeoError error)
        {
            // Keep the visit pose separate from the visual anchor: 22 m south and up, looking at the POI.
            var placement = new GeoPlacement(poi, new GeoEnuOffset(0, -22, 22));
            var look = GeoOrientationRequest.ForGeoTarget(poi, GeoLookMode.Full3D);
            return reference.TryGetWorldPose(placement, look, out pose, out error);
        }

        public static bool ConfigureAndApply(
            GeoReference reference,
            GeoAnchor anchor,
            GeoOrientation orientation,
            GeoCoordinate poi,
            out GeoError error)
        {
            anchor.Configure(reference, new GeoPlacement(poi, new GeoEnuOffset(0, 0, 2)));
            orientation.Configure(
                reference,
                GeoOrientationRequest.ForHeading(GeoHeading.FromDegrees(90)),
                Quaternion.identity);
            // Configure updates input only. Apply position before orientation for an immediate update.
            return anchor.TryApply(out error) && orientation.TryApply(out error);
        }

        public static bool ReadPlayer(GeoReference reference, Transform player, out GeoCoordinate coordinate, out GeoError error)
        {
            // Specify output references explicitly; RelativeToEllipsoidZero(20) uses a 20 m ellipsoid-height zero.
            return reference.TryWorldToCoordinate(player.position, GeoCoordinateReference.Wgs84Ellipsoidal, out coordinate, out error);
        }
    }
}
```

PlacePoi 直接设置 Transform，适用于没有其他脚本同时控制姿态的对象。玩家跳转应优先调用移动器提供的 Teleport，以同步移动器内部状态；样例 GeoDemoCamera.Teleport 会同时更新俯仰角缓存。

### 6.3 不使用 Profile，直接创建标定请求

```csharp
using System.Collections.Generic;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoCalibrationExamples
    {
        public static GeoCalibrationRequest Single(
            GeoControlPoint anchor,
            GeoVector3d localUp,
            GeoVector3d knownEast,
            double localUnitsPerMeter)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            // knownEast is geographic east in Root-local space, so its bound heading is 90 degrees.
            return GeoCalibrationRequest.SingleAnchor(
                anchor,
                new GeoSingleAnchorSettings(localUp, knownEast, GeoHeading.FromDegrees(90), localUnitsPerMeter, options));
        }

        public static GeoCalibrationRequest Upright(IReadOnlyList<GeoControlPoint> controls, GeoVector3d localUp)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            options.MaximumFitRmsMeters = 0.5;
            // Example acceptance threshold; choose it from measurement quality and application requirements.
            return GeoCalibrationRequest.Upright(controls, new GeoUprightSettings(localUp, GeoScalePolicy.Estimate, options));
        }

        public static GeoCalibrationRequest Spatial(IReadOnlyList<GeoControlPoint> controls)
        {
            var options = GeoCalibrationOptions.CreateDefault();
            return GeoCalibrationRequest.Spatial(controls, new GeoSpatialSettings(GeoScalePolicy.Estimate, options));
        }

        public static bool SolveWithoutUnity(
            GeoCalibrationRequest request,
            Wgs84Coordinate coordinate,
            out GeoVector3d localPosition,
            out GeoError error)
        {
            localPosition = default;
            if (!GeoCalibrationEngine.TrySolve(request, GeoCoordinateConverter.CreateDefault(), out var solution, out _, out error))
            {
                return false;
            }

            return solution.Frame.TryWgs84ToLocal(coordinate, out localPosition, out error);
        }
    }
}
```

控制点使用 new GeoControlPoint(id, rootLocalPosition, coordinate, weight, role) 构造。固定比例用 GeoScalePolicy.Fixed(localUnitsPerMeter)，固定参考原点用 options.ReferenceOrigin。

设置对象和请求会复制选项与点列表。创建请求后再改原列表，不会修改已经创建的请求；需要重新构建。

### 6.4 不依赖场景绑定的数值转换

这些 API 直接接收数值并返回数值，不需要 Anchor、Orientation、目标 Transform，也不会写入场景。按需求选择入口：

| 需求 | 入口 |
| --- | --- |
| 转换 WGS84 / GCJ-02 / BD-09 与高度参考 | GeoCoordinateConverter.TryConvert；不需要标定 |
| 经纬高、地理姿态与模型局部数值互转 | 从 solution.Frame 构造 GeoLocalMapping；不需要 GameObject |
| 使用场景中已生效的标定 | Reference.TryCaptureLocalMapping 或 Reference 的数值方法 |
| 使用固定 Root 变换进行世界数值转换 | Reference.TryCaptureSnapshot 及其数值方法 |

这里的局部空间始终是 **Reference.Root 局部空间**，不是任意对象直属父级的 localPosition/localRotation。GeoLocalPose 保存模型单位的 double 位置和 Unity Quaternion 值。GeoAttitude 的旋转基底固定为 +X 东、+Y 上、+Z 北，既不是传统 ENU 分量排列，也不是世界欧拉角。

- TryGeoToLocalPose / TryLocalPoseToGeo 完成数值 Pose 双向转换；TryLocalToCoordinate 可指定输出水平标准和高度参考。
- TryGetLocalPose 接收 GeoPlacement，可包含 ENU 偏移。反向转换得到偏移后的最终位置，不能推断并撤销未知的原始偏移。
- TryGeoToWorldPose / TryWorldPoseToGeo 提供世界空间版本。
- TryLocalRotationToAttitude / TryWorldRotationToAttitude 测量实际数值旋转，不推断原先的 Heading/LookAt 请求，也不自动撤销模型轴修正。
- GeoAttitude.FromDegrees 按航向 → 抬头 → 顺时针侧倾构造，与现有旋转偏角约定一致。完整 GeographicRotation 是权威数据；分解后的 PitchUpDegrees 范围为 [-90,90]，航向为 [0,360)，侧倾为 [-180,180)。
- 前向竖直时，四元数和俯仰仍然有效，但航向与侧倾无法唯一分离。读取前检查 HasHeading 和 HasRoll，不要把不可用字段当作零度测量结果。
- WGS/GCJ/BD 只改变位置表达，不改变姿态基底；不提供磁北、任意投影网格北或逐点变化的切平面。

LocalMapping 不可变，也不依赖 Root 世界变换。捕获后即使 Root 无效/销毁、Reference 重新标定或调用 Clear，旧映射仍可用于历史数值转换；Reference 的新查询需要已有生效标定，世界查询还需要当前 Root 有效。独立映射与 Snapshot 使用 Unity 数学值类型，不访问场景对象；Core 坐标标准转换和标定求解仍不依赖 UnityEngine。

```csharp
using UnityEngine;
using SceneGeography;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoNumericExamples
    {
        public static bool ConvertCoordinate(
            GeoCoordinate input,
            GeoCoordinateReference outputReference,
            out GeoCoordinate output,
            out GeoError error)
        {
            // Coordinate-standard conversion needs no calibration or scene.
            return GeoCoordinateConverter.CreateDefault().TryConvert(input, outputReference, out output, out error);
        }

        public static bool CreateMapping(
            GeoCalibrationRequest request,
            out GeoLocalMapping mapping,
            out GeoError error)
        {
            mapping = null;
            var converter = GeoCoordinateConverter.CreateDefault();
            if (!GeoCalibrationEngine.TrySolve(request, converter, out var solution, out _, out error))
            {
                return false;
            }

            // No GeoReference, GameObject or Transform is required.
            mapping = new GeoLocalMapping(solution.Frame, converter);
            return true;
        }

        public static bool LocalPoseRoundTrip(
            GeoLocalMapping mapping,
            GeoCoordinate coordinate,
            GeoCoordinateReference outputReference,
            out GeoLocalPose localPose,
            out GeoGeographicPose geographicPose,
            out GeoError error)
        {
            geographicPose = default;
            var attitude = GeoAttitude.FromDegrees(90, 10, -5);
            return mapping.TryGeoToLocalPose(coordinate, attitude, out localPose, out error) &&
                mapping.TryLocalPoseToGeo(localPose, outputReference, out geographicPose, out error);
        }

        public static bool FromActiveReference(
            GeoReference reference,
            GeoCoordinate coordinate,
            out GeoLocalPose localPose,
            out GeoError error)
        {
            // Uses the active calibration, but never reads or writes a target Transform.
            return reference.TryGeoToLocalPose(coordinate, GeoAttitude.FromDegrees(90), out localPose, out error);
        }

        public static bool MeasureWorldRotation(
            GeoReference reference,
            Quaternion actualRotation,
            Quaternion modelCorrection,
            out GeoAttitude attitude,
            out GeoError error)
        {
            attitude = default;
            if (!GeoAttitude.TryFromRotation(modelCorrection, out _, out error))
            {
                return false;
            }

            var geographicWorldRotation = actualRotation * Quaternion.Inverse(modelCorrection);
            return reference.TryWorldRotationToAttitude(geographicWorldRotation, out attitude, out error);
        }
    }
}
```

## 7. 编辑器 API

编辑器代码放在 Editor 文件夹，或仅用于 Editor 的程序集里，引用 SceneGeography.Editor。

- 只检查 Profile：TryBuildRequest 后调用 GeoCalibrationEngine.TrySolve。
- 分步计算和应用：Reference.TryPrepareCalibration → 检查报告 → GeoCalibrationEditorUtility.TryApplyCalibration。
- 一步应用预览：GeoCalibrationEditorUtility.TryInitializeFromProfile。

Editor 工具会记录绑定姿态 Undo，按位置、朝向、坐标采样的顺序更新已启用的绑定，并标记相关场景待保存。它不会自动保存场景，也不会改写 Profile。

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SceneGeography;
using SceneGeography.Editor;

namespace SceneGeography.DocumentationExamples
{
    public static class GeoEditorExamples
    {
        public static bool Preview(
            GeoReference reference,
            GeoCalibrationProfile profile,
            out GeoCalibrationReport report,
            out GeoError error)
        {
            return GeoCalibrationEditorUtility.TryInitializeFromProfile(reference, profile, out report, out error);
        }

        public static bool Capture(
            IReadOnlyList<GeoControlPointMarker> markers,
            GeoCalibrationMode mode,
            out GeoCalibrationProfile profile,
            out GeoError error)
        {
            profile = null;
            error = default;
            if (markers == null || markers.Count == 0 || markers[0] == null)
            {
                error = new GeoError(GeoErrorCode.InvalidConfiguration, "Markers required.");
                return false;
            }

            var reference = markers[0].reference;
            var points = new List<GeoControlPointSettings>();
            foreach (var marker in markers)
            {
                if (marker == null || marker.reference != reference)
                {
                    error = new GeoError(GeoErrorCode.InvalidConfiguration, "Use one Reference.");
                    return false;
                }

                if (!marker.TryCapture(out var point, out error))
                {
                    return false;
                }

                points.Add(GeoControlPointSettings.From(point));
            }

            profile = ScriptableObject.CreateInstance<GeoCalibrationProfile>();
            profile.localFrameId = reference.LocalFrameId;
            profile.mode = mode;
            profile.points = points.ToArray();
            // Capture numbers only; configure Up, direction, scale and limits, then solve and review.
            return true;
        }

        public static string SaveNewProfile(GeoCalibrationProfile profile, string assetPath)
        {
            // For example Assets/Calibration/BlockA.asset; the parent folder must already exist.
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            AssetDatabase.CreateAsset(profile, uniquePath);
            AssetDatabase.SaveAssets();
            return uniquePath;
        }
    }
}
```

Capture 生成的是尚未保存的资产；调用 SaveNewProfile 才保存，目标父目录需要事先存在。采集后仍要检查模式、方向、比例和误差限制。

编辑器工具返回 true 表示标定已经应用。若某个 POI 的输入、父级变换或朝向目标无效，该组件仍可能应用失败，需要检查它自己的 LastError。

## 8. 坐标类型、范围与精度

### 8.1 转换过程

系统先把输入转换为 WGS84 经纬度和椭球高，再通过地心三维坐标 ECEF 转到一个固定参考原点的 ENU 空间，最后利用标定结果转到 Root 局部和 Unity 世界空间。

ENU 分别表示东、北、上。它让场景附近的位置可以用米描述；系统内部按 Unity 常用排列使用 (East, Up, North)，但 GeoEnuOffset 的参数仍是 East、North、Up。

参考东、北、上固定在所选原点，并非每个 POI 都重新计算一组真北和天顶方向。系统保留地球曲率带来的高差，不是简单地把经纬度差乘固定常数。

### 8.2 GCJ-02 / BD-09 转换的范围

“正向转换”在这里指 WGS84 → GCJ-02，以及 GCJ-02 → BD-09。使用的是公开流传的近似转换公式，来源为 MIT 许可的 wandergis/coordtransform；不是官方公布的完整加密算法，也不提供测绘精度保证。许可见 [coordtransform-LICENSE.txt](ThirdParty/coordtransform-LICENSE.txt)。

GCJ-02 偏移随位置变化，不是给所有坐标加同一个经纬度偏移。BD-09 转换在 GCJ-02 基础上进一步处理。逆转换通过迭代寻找能产生目标值的输入；0.01 米是数值收敛阈值，不是实际坐标精度。

当前转换器只在以下矩形内处理 GCJ/BD，边界不包含在内：

- 经度：73.66°E ～ 135.05°E。
- 纬度：3.86°N ～ 53.55°N。

这是跨越约 61.4° 经度和 49.7° 纬度的大片区域，覆盖中国大部分陆地区域及周边地区；南北约 5500 公里，在北纬 30° 附近东西约 5900 公里。**它不是中国行政边界，不能用它判断某点是否应该使用 GCJ/BD。**

这个检查是当前转换实现的使用限制，目的是避免在没有支持依据的位置继续套公式，或把范围外数据悄悄当成 WGS84。它不是 ENU 或整个系统的全球经纬度限制：

| 数据 | 在上述矩形外怎么处理 |
| --- | --- |
| 明确为 WGS84 | 可继续使用 WGS84 转换和局部场景标定，不受这个矩形限制 |
| 明确为 GCJ-02 / BD-09 | 当前转换器返回 UnsupportedCoordinateRegion，输入和输出方向都会检查 |
| 海外地图或边界附近的数据 | 向数据提供方确认实际坐标类型，或先用可靠服务转换为 WGS84；不要只改枚举名称 |

北京、上海等位于范围内的街区场景不会因为“场景只有几公里”触及这个大陆级范围限制。接近矩形边界或用于其他地区时，应在导入数据时先检查；转换前后的坐标都可能影响边界检查。

### 8.3 EPSG、WKT、PRJ、UTM 是什么

它们常见于 GIS 数据交换，但并不是四种相互替代的 GPS 格式：

| 名称 | 含义 | 常见例子 |
| --- | --- | --- |
| EPSG | 坐标参考系统等地理定义的登记编号 | EPSG:4326 标识常用的二维 WGS84 经纬度坐标参考系统 |
| WKT | Well-Known Text，一种文本表示法；此处指描述坐标参考系统的 WKT | 用文本说明基准、轴、单位和投影参数 |
| PRJ | 常见的坐标参考描述文件扩展名，内容通常是 WKT | 与 Shapefile 一同提供的 .prj 文件 |
| UTM | Universal Transverse Mercator，通用横轴墨卡托投影分带系统 | 按分带与半球，使用米制 Easting / Northing 坐标 |

本模块不直接读取这些定义或接收 UTM 米制坐标。若上游提供这类 GIS 数据，应先在导入环节转换为明确的 WGS84 经纬高，再交给本模块。EPSG:4326 本身也不替输入数据解决高度参考问题。

### 8.4 使用边界

- 每个 Reference 服务于一个局部场景，不用于全球漫游，也不会自动切换参考原点。当前不接受纬度绝对值超过 89.9° 的参考原点。
- 高度模型尚未接入，因此不支持直接把海拔转换为椭球高。
- double 计算不会恢复原先已在 Unity float Transform 中丢失的精度。
- 系统不负责贴地、碰撞、寻路或提取高斯表面；POI 高度仍来自输入或业务修正。
- 当前高斯渲染器使用世界空间缓存；地理系统不会替它重建缓存。接入真实高斯时应优先保持渲染 Root 稳定，动态变换需要另行同步渲染器。
- 绑定更新按三类组件排序，不求解任意父子依赖或相互追踪关系。

## 9. 样例、测试和排查

### 9.1 样例操作

- WASD 移动，Q/E 升降，右键旋转，Shift 加速。
- Visit POI 跳到 POI 附近的访问位置并看向它，而不是把相机放到标记中心。
- Apply alternate calibration 提交另一份标定；Try invalid calibration 检查失败后保留原标定的行为。
- Rotate Root / Toggle Root scale 检查 Root 变换跟随；Invalid nonuniform Root / restore 检查非法缩放及恢复。

样例的 POI 视觉位置额外向上偏移 16 现实米，黄色条表示朝向。它们用于演示，不是通用业务 POI 管理器。

| 组件 | 字段 | 含义 |
| --- | --- | --- |
| GeoReferenceDemo | Reference、Scene Root、Profile、Viewer | 样例系统、模型根、合成标定和自由相机引用 |
| GeoReferenceDemo | Create Proxy Geometry | 默认 true，创建合成地面和建筑；关闭后仍生成合成 POI，不能用来接管真实业务 POI |
| GeoReferenceDemo | Show Controls | 默认 true，显示 IMGUI 测试面板 |
| GeoDemoCamera | Reference | 提供参考 Up 与现实米比例；未初始化时不移动 |
| GeoDemoCamera | Speed Meters Per Second | 默认 15，现实米/秒，不带碰撞或重力 |
| GeoDemoCamera | Mouse Degrees Per Pixel | 默认 0.15，鼠标右键旋转灵敏度 |

### 9.2 运行自动化测试

程序集按以下边界组织：

| 目录 | 程序集 / 用途 |
| --- | --- |
| Runtime/Core | SceneGeography.Core：double 数学，不引用 UnityEngine |
| Runtime/Unity | SceneGeography.Unity：场景映射和组件，只依赖 Core |
| Editor | SceneGeography.Editor：配置与预览，只依赖正式运行时 |
| Tests/Shared | SceneGeography.Tests.Shared：包测试独立使用的合成数据 |
| Tests/EditMode、Tests/PlayMode | 包测试，不依赖样例 |
| Samples~/BasicSample | 可选演示及其独立 Editor、EditMode / PlayMode 测试 |

运行测试需要安装 **Unity Test Framework**（com.unity.test-framework）。嵌入包的测试可在 Test Runner 中使用；安装为非嵌入包时，在项目 Packages/manifest.json 顶层 testables 数组中加入 "io.github.ruyeruye.scene-geography"，与已有条目合并，而不是加到 dependencies 中。

在 **Window > General > Test Runner** 按程序集筛选 SceneGeography.Tests.EditMode 或 SceneGeography.Tests.PlayMode。这些包测试不需要导入 Basic Sample，也不需要 Input System。导入样例并安装 Input System 后，还可以运行 SceneGeography.Samples.Tests.EditMode 和 SceneGeography.Samples.Tests.PlayMode。

连接当前工程的 CLI 命令：

~~~powershell
unity command test run --project-path . --format json -- --mode editor --filter SceneGeography.Tests.EditMode --filter_type assembly
unity command test run --project-path . --format json -- --mode playmode --filter SceneGeography.Tests.PlayMode --filter_type assembly --async_tests true
unity command test status --project-path . --format json
~~~

PlayMode 异步命令返回后，还需要读取最终 status。不要在手动 Play 或有未处理场景编辑时启动测试。

文中四个 C# 示例同时保存在 [Tests/EditMode/Documentation](Tests/EditMode/Documentation) 并参与编译；自动化测试检查中英文文档中的代码与源码一致。文档测试通过 UPM 的 resolvedPath 定位包，不依赖包在磁盘上的目录名称。

### 9.3 常见问题

| 现象/错误 | 优先检查 |
| --- | --- |
| CoordinateTypeUnspecified / HeightReferenceUnspecified | 必须明确水平类型及高度参考，default 外部坐标无效 |
| LocalFrameIdMismatch | Profile 和模型 Reference 的局部坐标身份不一致 |
| MissingHeightModel | 使用了 Orthometric，当前没有模型；不能靠填 ID 解决 |
| UnsupportedCoordinateRegion | GCJ/BD 输入或输出超出数值支持范围 |
| DegenerateBaseline / NearCollinearPoints | 点重复、水平基线太短或 Spatial 近共线 |
| InvalidRootTransform / InvalidTargetParentTransform | 检查整条父级链的非均匀、镜像、剪切与零缩放 |
| RootDestroyed | 已绑定 Root 被销毁；Clear 后显式选择新 Root 或世界模式 |
| ResidualLimitExceeded | 地理来源、高度约定、模型位置、Up 或精度阈值有问题；查看逐点残差 |
| StaleCalibrationCandidate | 候选实例、版本或坐标身份已过期，重新计算 |
| MissingLookTarget / DegenerateLookDirection | 目标缺失、销毁、位置重合或与 Up 几乎平行 |
| OutOfConfiguredRange | 最终 ENU 三维位置超限，包括放置偏移 |
| POI 总被拉回 / 相机跳转后回弹 | 多个脚本争夺姿态；玩家不要挂持续 Anchor，相机移动器需同步自身缓存 |
| 编辑器 Apply 后 Play 不工作 | 解不序列化，运行时仍需初始化；检查加载时机 |

样例提示缺少 Unity.InputSystem 时，请先安装 Input System。嵌入包目录名与包 ID 不同产生的是目录命名警告，与 C# 编译错误不同。

## 10. 许可证

Scene Geography 使用 [MIT 许可证](LICENSE.md)。版本说明见 [CHANGELOG](CHANGELOG.md)。改写的坐标转换公式保留独立的 [coordtransform MIT 声明](ThirdParty/coordtransform-LICENSE.txt)。
