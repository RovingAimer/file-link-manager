# 文件链接管理器有界简化设计

状态：公开架构说明
日期：2026-08-09
范围：现有 Windows 文件链接管理器的架构与实现简化，不新增用户功能

## 1. 背景与结论

当前四层结构（App、Application、Infrastructure、Domain）方向正确，Windows 链接安全边界、便携 JSON 恢复策略和多设备记录模型也有实际价值。本轮不重写项目、不合并项目、不引入第三方 MVVM 或依赖注入框架，而采用“方案 2：有界简化”：删除没有生产价值的抽象和重复状态，统一不可逆操作后的结果语义，并在不降低安全性的前提下拆分热点文件。

本轮最重要的正确性修复是明确原生创建/删除的提交点。调用者取消只能在不可逆原生操作之前终止。一旦原生入口已经创建或删除，应用层必须完成状态协调，或返回能如实表达“操作已经发生但登记未完成”的结果，不能再以普通取消掩盖设备上的真实变化。

## 2. 目标与成功标准

### 2.1 目标

- 修复创建、删除在原生操作成功后因取消而造成的记录漂移。
- 将每条记录的规范请求收口为一个权威字段，同时兼容既有 JSON 数据。
- 移除仅用于跨层转发、没有替换价值的接口与领域类型。
- 消除查询和 WPF ViewModel 中的明确重复。
- 将超大 Windows 链接服务按职责拆分，并复用符号链接删除校验。
- 保持现有四种真实 Windows 链接能力、安全删除语义、便携存储和多设备视图。

### 2.2 成功标准

- 全部现有有效行为测试通过，并新增提交点、旧数据兼容和重构回归测试。
- Release x64 严格构建为 0 warnings / 0 errors。
- 发布仍为 win-x64、自包含、单文件 WPF 可执行文件。
- 旧版 `link-records.json` 可加载；新写出的数据仍能被当前发布前版本安全读取。
- 不降低稳定句柄、重解析点解析、冲突拒绝、原子存储或只读降级的安全边界。
- 不遗留 TODO、占位实现或仅为迁移临时保留的双模型。

## 3. 设计约束与非目标

### 3.1 必须保留

- 硬链接和 Junction 的稳定句柄身份验证与 handle-based disposition。
- 重解析点缓冲区的 tag、长度、偏移、溢出和 UTF-16 边界校验。
- JSON 的进程内锁、命名 Mutex、同目录临时文件、原子安装、备份恢复、语义校验和失败只读。
- 每条记录检查异常隔离，以及取消时不保存部分检查结果。
- 普通文件/目录、不同链接类型或不同目标替换时的冲突拒绝。
- 非递归、只删除链接入口的行为。
- 四项目依赖方向和手工组合根。

### 3.2 非目标

- 不改变 UI 信息架构或新增页面。
- 不改变四种链接的用户可见含义。
- 不引入数据库、日志框架、MVVM 框架或 DI 容器。
- 不统一改造所有 Windows 删除实现；既有文件/目录符号链接的路径删除 TOCTOU 仍按已接受设计限制记录。
- 不为追求文件数量减少而合并 Domain、Application、Infrastructure、App。

## 4. 不可逆操作的提交点

### 4.1 创建流程

流程分为两个阶段：

1. 提交前：路径验证、原生创建调用开始前，完整尊重调用者的 `CancellationToken`。
2. 提交后：原生链接创建成功即越过提交点。后续回读、设备身份获取和记录保存使用不受调用者取消中断的协调路径。

结果规则：

- 提交前取消：抛出 `OperationCanceledException`，设备无变化。
- 原生创建失败：返回或抛出现有创建失败语义，设备无已知新链接。
- 原生创建成功且登记成功：返回已创建并已登记。
- 原生创建成功，但回读、设备身份或保存失败（包括后提交依赖自身抛出取消）：返回 `CreatedButUnregistered`，携带 canonical request、可用的 inspection/fingerprint 和明确原因；绝不再次调用创建。原调用令牌在提交点后被取消本身不改变结果；若登记仍成功，应返回已创建并已登记。

实现上，越过提交点后应使用 `CancellationToken.None` 完成最佳努力协调。只捕获能够转换为“已创建但未登记”的后提交失败；编程错误不应被泛化吞掉。若现有结果模型无法携带缺失 inspection 的失败，应以最小方式扩展结果，而不是伪造指纹。

### 4.2 删除流程

删除同样分为两个阶段：

1. 提交前：加载、定位、检查、确认和原生删除开始前尊重取消。
2. 提交后：`IFileLinkService.DeleteAsync` 已确认删除入口后即越过提交点。登记 Deleted 状态和保存数据库不再被原调用令牌中断。

结果规则：

- 提交前取消：抛出取消，链接未由本次用例删除。
- Missing 且用户确认：没有原生删除，但“登记为 Deleted”本身是状态提交；保存过程应返回准确结果。
- 原生删除成功且保存成功：返回 Deleted。
- 原生删除成功但保存失败或后提交依赖自身抛出取消：返回 `DeletedButUnregistered`，并立即触发既有存储只读/命令禁用语义（若失败来自存储）。原调用令牌在提交点后被取消本身不再中断登记。
- Conflict、Unknown、类型或指纹不匹配：仍拒绝原生删除。

不得在原生删除成功后传播普通取消，使调用方误以为没有副作用。源存在性复核继续作为结果诊断，但不能撤销已经发生的删除。

## 5. 记录模型与 JSON 兼容

### 5.1 单一 canonical Request

新建和新写回的模型中 `LinkRecord.Request` 始终是规范化绝对路径请求，并成为创建、展示、检查和删除的唯一请求字段。旧版仅有绝对 `request` 的记录允许作为兼容输入，但在下一次写回前必须经既有 Windows 路径规范化流程收口。删除：

- `LinkRecord.CanonicalRequest`
- `LinkRecord.RequestForDeletion`
- 仅为双字段同步存在的构造器与验证分支

保留 `LinkFingerprint.NormalizedLinkPath`。它仍是创建时采集的验证证据，并帮助旧版本读取新数据；本轮不把所有路径证据压缩成一个字段。

### 5.2 读取兼容规则

JSON 读取器同时接受旧、新布局：

| 输入形态 | 有效 Request | 结果 |
|---|---|---|
| 含 `canonicalRequest` 的旧记录 | 优先使用 `canonicalRequest` | 加载并在内存中收口为单一 canonical Request |
| 无 `canonicalRequest`、`request` 为绝对规范路径 | 使用 `request` | 加载 |
| 无 `canonicalRequest`、`request` 为旧版绝对但未标准化路径 | 作为兼容输入，经既有 Windows 路径规范化流程收口；失败则报告诊断 | 加载或安全拒绝 |
| 无 `canonicalRequest`、`request` 为相对路径 | 无安全删除依据 | 拒绝并报告诊断 |
| 两字段存在但互相矛盾 | `canonicalRequest` 作为旧格式权威字段，同时执行类型与指纹一致性校验 | 不静默采用不一致记录 |

反序列化完成后，业务层只能看到收口后的 `Request`，不得继续携带 legacy 双态。

### 5.3 写入兼容规则

- 新写入不再输出 `canonicalRequest`。
- 继续输出 `request`、fingerprint 的规范路径字段以及其他既有字段。
- schema version 保持 1：这是可选冗余字段的删除，当前旧版本读取器能以绝对 `request` 和 fingerprint 完成安全验证。
- 必须增加“旧程序模型读取新 JSON fixture”的兼容测试，证明不是仅凭推断维持兼容。
- backup 和 primary 的恢复、future schema 只读规则不变。

## 6. 分层与抽象简化

### 6.1 路径服务

`IPathService` 目前只在 Infrastructure 内被生产代码使用，没有 Application 用例依赖，也没有有价值的替换点。调整为：

- 删除 Application 中的 `IPathService`。
- `WindowsFileLinkService` 直接依赖 `WindowsPathService`。
- 将 `NormalizedPath`、`PathValidationError` 及路径验证结果从 Domain 移到 `Infrastructure/Paths`；能设为 internal 的类型设为 internal。
- 保留 `WindowsPathService` 内用于测试实际卷身份的窄依赖。
- 删除没有生产调用的 `NormalizedPath.IsSamePath`，对应测试改为覆盖实际规范化/验证行为。

Domain 只保留业务可观察的链接请求、类型、状态、指纹和记录，不承载 Windows 路径算法的数据传输类型。

### 6.2 单实例保护

`IApplicationInstanceGuard` 不是业务端口，只由 WPF 启动和 Windows 实现使用。调整为：

- 删除 Application 接口。
- App 组合根直接持有 `WindowsApplicationInstanceGuard`（或 App 内部的窄 `IDisposable` 字段）。
- 保留 Mutex 名称、线程所有权、AbandonedMutexException 和释放行为。
- 保留第二实例在创建 store、设备 provider 和窗口之前退出的测试。

### 6.3 设备合并小重复

`LinkRegistration` 与 `InspectLinksUseCase` 的设备元数据添加/刷新、保持顺序规则应收口为一个 Application 内部纯函数。该函数只处理 Ordinal key 匹配和顺序，不扩展成仓储或通用集合框架。

## 7. 查询与统计简化

- `LinkRecordFilter` 只保留生产 UI 使用的 `SearchText`、设备和状态条件。
- 删除仅测试使用的 `SourcePath`、`LinkPath` 和重复的 `MatchesPath` 分支。
- `SearchText` 继续以 OrdinalIgnoreCase 对 source 或 link 做 OR 子串匹配。
- `GetStatistics` 改为单次枚举，同时计算历史总数和当前 Valid 且 `DeletedUtc` 为空的数量。
- 保持保存顺序、设备键 Ordinal 精确匹配和 legacy 记录查询兼容。

## 8. WPF ViewModel 基础设施

在 App/Presentation 新增本地 `ObservableObject`：

- 实现 `INotifyPropertyChanged`。
- 提供 `OnPropertyChanged` 与 `SetField<T>`。
- `CreateLinkPageViewModel`、`LinkRecordsPageViewModel`、`StorageAccessViewModel`、`AboutPageViewModel` 继承它。

不抽象命令状态机、不引入反射或 source generator。重构必须保持现有 IsBusy 输入冻结、只读状态传播、CanExecute 刷新和页面导航测试。

## 9. WindowsFileLinkService 拆分

保留单一公开 `IFileLinkService` 和一个 `WindowsFileLinkService` 类型，采用 partial 文件按能力拆分：

- `WindowsFileLinkService.cs`：构造、公共 dispatch、共享 inspection/结果映射。
- `WindowsFileLinkService.HardLinks.cs`
- `WindowsFileLinkService.SymbolicLinks.cs`
- `WindowsFileLinkService.Junctions.cs`

文件与目录符号链接删除中的共同校验提取为私有 helper，参数明确包含期望目录类型、期望 tag 和删除入口。文件删除仍走 `DeleteFileW`，目录删除仍走 `RemoveDirectoryW`；不得因复用改变已验收的 API 或安全边界。

Junction 与硬链接的 stable-handle 删除、placeholder rollback、1-byte `FILE_DISPOSITION_INFO` ABI 保持原样。拆分后聚焦测试必须继续覆盖替换竞态、不同 tag/目标、普通入口保护和源保留。

## 10. 存储内部状态简化

删除 `JsonLinkRecordStore` 中与 `LastLoadDiagnostics.IsReadOnly` 重复的 `_isReadOnly` 字段，写入守卫统一从共享诊断状态派生。状态更新必须仍通过单一方法原子发布诊断和 `StorageAccess` 通知。

不改动 Mutex/Task.Run 的线程所有权策略，不把同步文件 API 机械包装成新的抽象，也不削弱异常分类：取消、语义性 `LinkRecordStoreException`、安装阶段 IO/JSON/InvalidData 失败必须保持不同处理路径。

## 11. 测试与无效模板清理

- 删除 Domain、Application、Infrastructure 测试项目中的空 `UnitTest1.cs`。
- 测试总数下降本身不是回归，验收以有效行为覆盖为准。
- 对本轮每个行为变更先建立失败测试，再修改实现。
- 纯移动/partial 拆分可用现有测试证明等价，但必须在拆分前后分别运行聚焦套件。

新增或调整的关键测试：

1. 创建在 native 成功后收到取消，返回 CreatedButUnregistered，且不会二次创建。
2. 创建登记保存被取消时，返回 truthful pending 结果。
3. 删除在 native 成功后收到取消，仍完成登记；若登记失败则返回 DeletedButUnregistered。
4. 取消发生在 native 操作前时，仍无设备副作用。
5. 旧 JSON（含 canonicalRequest）加载后收口为单一 Request。
6. 新 JSON 不写 canonicalRequest，并由冻结的旧版 DTO/读取规则成功读取。
7. legacy relative 且缺 canonicalRequest 继续安全拒绝。
8. WPF 四个 ViewModel 的通知、命令和忙碌/只读状态不回归。
9. 符号链接 helper 重构后，文件与目录各自仍调用正确的非递归删除 API。
10. 单实例第二进程/线程路径不触碰 store 和窗口。

## 12. 建议实施顺序

为降低跨切片风险，按以下顺序实施，每一步独立提交并验收：

1. 修复创建/删除提交点与取消语义。
2. 收口 canonical Request，并完成双向 JSON 兼容证明。
3. 删除 `IPathService`、移动路径类型、删除死方法。
4. 删除 `IApplicationInstanceGuard`，保持单实例行为。
5. 简化查询/统计并提取设备合并纯函数。
6. 引入 `ObservableObject`，迁移四个 ViewModel。
7. 拆分 `WindowsFileLinkService` 并复用符号链接删除校验。
8. 收口存储只读状态，删除空测试模板。
9. 运行完整测试、严格构建、单文件发布和实际启动冒烟。

提交点正确性排在第一，因为它是已发现的行为风险；JSON 模型排在第二，因为后续代码应基于最终记录形态。结构性移动在行为问题修复后进行，便于复核差异。

## 13. 最终验收清单

- [ ] 原生操作提交前取消不产生副作用。
- [ ] 原生操作提交后取消不掩盖已发生的创建或删除。
- [ ] CreatedButUnregistered / DeletedButUnregistered 能支撑 UI 后续恢复。
- [ ] 旧 primary/backup JSON 均能加载，新 JSON 不再包含 canonicalRequest。
- [ ] 新 JSON 对当前发布前版本保持安全可读。
- [ ] 四种链接的创建、检查、冲突保护和删除入口行为不变。
- [ ] 只读存储下创建、重试、检查和删除命令仍正确禁用。
- [ ] 单实例、导航、启动布局和 XAML 绑定测试通过。
- [ ] 全量测试 0 skipped；Release|x64 `-warnaserror` 为 0/0。
- [ ] self-contained single-file publish 只有一个 EXE，实际启动显示主窗口。
- [ ] `git diff --check` 通过，工作树无本轮遗留临时文件。
