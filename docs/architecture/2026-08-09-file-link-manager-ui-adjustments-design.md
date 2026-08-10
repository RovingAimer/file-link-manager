# 文件链接管理器 UI 调整设计

状态：历史设计说明
日期：2026-08-09
范围：创建链接页卡片与主窗口左侧导航的可见性、默认值、文案和布局稳定性

## 1. 目标与约束

本轮只调整现有 WPF 创建链接页和主窗口导航，不改变链接创建领域模型、原生链接行为或页面信息架构。

成功标准：

- 非高亮导航按钮在普通 Windows 主题下始终显示清晰的侧栏文字；
- 创建链接页首次打开默认选中“目录符号链接”；
- 卡片顺序精确为“目录符号链接、目录联接、文件符号链接、文件硬链接”；
- 每张卡片都简洁说明其硬盘空间影响，并保留类型限制；
- 选中卡片前后，卡片的外部尺寸和相对位置不变化；
- 自动化测试覆盖 ViewModel 数据契约、导航模板的实际文字颜色和真实 STA WPF 布局测量。

## 2. 方案与取舍

### 2.1 导航文字

现有 `NavigationButtonStyle` 依赖 WPF 默认 Button 模板。截图表明普通状态的 `Foreground` 没有稳定传递给按钮内容。采用局部显式 `ControlTemplate`：模板用 `Border` 承载背景和内边距，并用 `TextBlock` 绑定 `Content`、`Foreground`、字体属性和内容对齐属性。这样模板不再依赖系统主题的 `ContentPresenter` 对文字颜色的隐式处理，同时保留既有颜色、尺寸、悬停背景和禁用色。

不把全局 Button 模板重写为统一模板，因为本轮只需修复文本导航，且其他按钮可能需要系统模板的交互行为。

### 2.2 链接类型卡片

`CreateLinkPageViewModel` 继续作为卡片排序和显示文案的单一来源。构造时先设置 `SelectedType = DirectorySymbolicLink` 对应的内部状态，再生成卡片，使默认类型与首屏高亮一致。卡片列表按产品裁决的目录优先顺序生成，不改变领域枚举的声明顺序。

说明文案分为两段：`Description` 负责类型作用和空间影响，`Limitation` 负责源类型、卷范围和网络路径限制。符号链接和目录联接分别写明只增加很小的文件系统元数据、不复制源内容、源数据只占一份；文件硬链接写明不复制文件内容、只增加目录项/元数据、最后一个硬链接删除后才释放内容空间。

### 2.3 卡片布局稳定性

`TypeCardButtonStyle` 的普通态和选中态均使用 `BorderThickness=1`。选中态仅改变 `Background` 和 `BorderBrush`，不改变任何影响测量的属性。现有四列 `UniformGrid` 和卡片间距保持不变。

## 3. 测试设计

- `CreateLinkPageViewModelTests`：断言默认 `SelectedType`、四类的精确顺序、只有默认卡片选中；分别断言三类非硬链接的空间语义和硬链接的释放语义，且每张卡片作用/限制说明非空。
- `MainWindowTests`：在现有单线程 STA 基础设施中创建真实 `MainWindow`，显示并更新布局后取得导航按钮模板中的 `TextBlock`，断言可用导航文字绑定到清晰的白色 Foreground；不依赖系统默认模板的实现细节。
- `MainWindowTests`：取得真实生成的四个卡片 Button，记录其相对窗口的 `Rect` 和宽高，切换到另一张卡片并再次 `UpdateLayout`，逐项断言位置与尺寸在容差内一致。

测试只验证用户可观察行为，不为静态 XAML 字符串添加脆弱断言。布局测试使用实际 WPF Measure/Arrange/UpdateLayout 路径，确保 `BorderThickness` 变化不会再次造成位移。

## 4. 变更边界

预期修改：

- `src/FileLinkManager.App/App.xaml`
- `src/FileLinkManager.App/Presentation/CreateLinkPageViewModel.cs`
- `tests/FileLinkManager.App.Tests/Presentation/CreateLinkPageViewModelTests.cs`
- `tests/FileLinkManager.App.Tests/Presentation/MainWindowTests.cs`

本设计记录只描述公开产品行为与验证边界；本机运行产物和内部实施记录不属于项目快照，也不引入新的运行时依赖。
