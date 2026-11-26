# 文件链接管理器

[English](README.en.md)

一个面向 Windows 的便携式 WPF 工具，用 Windows 原生文件系统链接为已有文件或目录创建等价访问路径，并记录由本软件执行的操作。

从 [GitHub Release](https://github.com/RovingAimer/file-link-manager/releases/tag/v1.1.0) 下载 Windows x64 单文件版和 SHA-256 校验文件。

## 项目简介

文件链接不会复制源内容。用户提供一个已存在的源路径和一个尚不存在的链接路径后，应用创建链接入口；源路径保持原位，链接路径可用于访问同一内容。

## 核心功能

- 创建文件符号链接、目录符号链接、文件硬链接和目录联接（Junction）。
- 支持手动输入、粘贴和 Windows 路径选择器。
- 实时检查源路径、链接路径、文件/目录类型、父目录、卷和网络路径限制。
- 记录当前设备、其他设备和离线设备的链接状态。
- 检查已登记链接，并只删除由本软件创建且通过身份校验的链接入口。
- 保留删除历史；删除历史记录只会修改软件数据，不会再次操作磁盘路径。
- 使用便携 JSON 数据文件和备份文件，不依赖数据库服务。
- 创建时可选填自定义名称（最多 20 个 Unicode 文本元素，支持中文和标点特殊字符），创建后可编辑名称和分组；名称不受 Windows 文件名规则限制，也不会改变磁盘路径或文件名。
- 创建页直接显示可选分组，可新增分组，或在名称旁重命名、删除。删除分组会保留记录并归入“未分组”，不删除磁盘链接。
- 按分组筛选，按名称、源路径或链接路径字段搜索；组内按创建时间倒序（最新在前，同时间保持原顺序），界面没有手动排序入口。

## 四种 Windows 原生链接方式

| 类型 | 作用与空间占用 | 主要限制 |
| --- | --- | --- |
| 目录符号链接 | 目录的跨卷等价路径；只占少量元数据，不复制内容。 | 源必须是目录。 |
| 目录联接 | 将目录重定向到本机另一目录；只占少量元数据，不复制内容。 | 仅目录；可跨本机卷；不支持映射网络卷。 |
| 文件符号链接 | 文件的跨卷等价路径；只占少量元数据，不复制内容。 | 源必须是文件。 |
| 文件硬链接 | 共享同一文件内容，不复制数据；仅占少量目录项/元数据，最后一个链接删除后才释放内容空间。 | 仅文件；源和链接必须位于同一卷。 |

符号链接和目录联接只增加很小的文件系统元数据，源数据仍只占一份空间。文件硬链接共享同一文件内容；删除其中一个链接不会释放内容，只有最后一个硬链接删除后才会释放内容空间。

## 界面与便携数据

应用包含三个页面：

1. **创建链接**：选择类型、填写路径、查看实时校验和创建预览；可选填名称、选择或新建分组。
2. **链接记录**：按设备、状态和分组查看，按名称或路径搜索；编辑名称/分组、检查、删除链接入口或移除已删除历史记录。
3. **关于**：查看应用信息、当前设备身份摘要和数据位置。

数据目录位于可执行文件旁的 `data/` 目录，主要文件为 `link-records.json` 和 `link-records.json.bak`。数据包含设备摘要、链接路径、源路径、状态、时间戳、自定义名称和分组；请将它视为敏感的本机应用数据，不要提交到版本库或公开分享。

1.1.0 可读取 schema 1，仅读取时不自动改写磁盘；首次正常保存（包括检查后保存）会写入 schema 2。1.0.0 读取 schema 2 时会进入只读模式，避免保存时丢掉新增元数据。升级前请自行备份整个 `data/`，同时保留 JSON 主文件及 `.bak`。先关闭旧版，再启动新程序；如需沿用记录，可将新 EXE 放在已有 `data/` 旁边。程序不会替你搬迁数据。

## 系统要求与权限

- Windows 10 或 Windows 11，x64。
- 从源码构建需要 .NET 10 SDK；发布包为自包含应用，不要求目标机器另装 .NET 运行时。
- 应用清单请求管理员权限（`requireAdministrator`）。这是为了覆盖 Windows 文件链接创建和检查所需的权限边界。
- 符号链接是否可用还受本机安全策略、管理员批准和 Windows 配置影响。应用不会通过降低安全策略来绕过失败。

## 快速使用

1. 启动应用并在“创建链接”页选择链接类型。
2. 选择或输入已存在的源文件/目录。
3. 选择链接父目录并填写一个尚不存在的链接路径。
4. 可在“记录信息（选填）”中填写名称和选择分组；空名称/未分组也可创建。超过 20 个文本元素时显示就地错误，禁用创建或编辑保存，不截断输入。
5. 确认卡片限制和实时校验均通过后，点击“创建链接”。
6. 在“链接记录”页检查状态、编辑名称/分组或管理分组；删除操作只针对链接入口，不会删除源内容。

建议先在专用测试目录中验证路径和权限，再处理重要数据。不要把源路径和链接路径指向不确定的系统目录。

## 从源码构建

在仓库根目录使用 .NET 10 SDK：

```powershell
dotnet restore FileLinkManager.sln
dotnet test FileLinkManager.sln --no-restore
dotnet build FileLinkManager.sln --no-restore -c Release -p:Platform=x64 -warnaserror
```

当前验证环境中的窗口用例 `CreatePage_PathStatusesRefreshInRealLayoutAndFollowSelectedType` 可能挂起；完整测试应加上 `--blame-hang-timeout 30s --blame-hang-dump-type none` 保护。非窗口全套可单独运行，不能将其结果当作完整窗口测试通过：

```powershell
dotnet test FileLinkManager.sln --no-restore --filter 'FullyQualifiedName!~FileLinkManager.App.Tests.Presentation.MainWindowTests'
```

发布 Release、x64、win-x64、自包含单文件版本：

```powershell
dotnet publish src/FileLinkManager.App/FileLinkManager.App.csproj `
  --no-restore -c Release -p:Platform=x64 -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:DebugType=None -p:DebugSymbols=false -o publish
```

发布前应确保输出目录是新的空目录；验收时应确认目录中只有 `FileLinkManager.App.exe`，没有 PDB、DLL、runtimeconfig、deps 或运行时 `data/`。

## 项目结构

```text
src/FileLinkManager.Domain          领域模型和链接状态
src/FileLinkManager.Application     用例、记录查询和存储边界
src/FileLinkManager.Infrastructure  Windows API、路径、设备和 JSON 存储
src/FileLinkManager.App             WPF 界面、ViewModel 和组合根
tests/                              Domain、Application、Infrastructure、App 测试
docs/architecture/                  产品架构与设计说明
```

## 数据与隐私

应用从 Windows 读取本机设备标识材料，生成稳定的设备键摘要，并保存设备显示名及链接记录。原始 MachineGuid 不写入 JSON；不过路径、设备显示名和时间戳仍可能暴露本机信息。分享问题报告前请先移除 `data/` 目录和日志中的路径。

## 安全注意事项

- 只对自己拥有或明确获准操作的路径创建、检查和删除链接。
- 文件硬链接、符号链接和目录联接的语义不同；创建前请确认类型、卷和目标路径。
- 应用的删除流程验证链接身份，并只删除链接入口；它不会递归删除源目录或源文件。
- 不要运行来源不明的发布包，也不要把 JSON 数据、崩溃转储或日志上传到公开 issue。

## 许可证

本项目采用 [MIT License](LICENSE)。

## 已知限制

- 仅支持 Windows；不支持 Linux、macOS 或跨设备远程创建。
- 不支持目录硬链接、批量创建、云同步或自动扫描并导入其他工具创建的链接。
- 目录联接不支持映射网络卷；文件硬链接必须同卷。
- 符号链接和原生文件系统行为仍受 Windows 权限、策略、卷类型和文件系统状态影响。
- 名称和分组只组织本软件的记录，不重命名文件，不建立额外磁盘目录；分组为数据库全局的单层分组。
- 文件和目录符号链接仍按路径删除：如果其他进程在校验后、删除前替换了入口，仍存在竞态。删除时请避免其他程序同时改动该路径。
- 已收到文件链接管理器获得焦点时 Snipaste 的 F1 截图快捷键不触发的报告。静态检查未发现本程序显式绑定、拦截或注册 F1，原因尚未定位，1.1.0 未修复此问题。

## 贡献方式

提交改动前请运行完整解决方案测试、Release x64 严格构建和 `git diff --check`，并避免提交 `bin/`、`obj/`、`data/`、发布目录、日志、转储和本机工作记录。
