# FolderSail

Windows 轻量多窗格文件管理器：Finder 式彩色标签 + 多窗格浏览。

## 功能

- 侧栏 **标签**：七色圆点列表（红/橙/黄/绿/蓝/紫/灰），点击查看该标签下的文件夹
- 把文件夹拖到标签上即可打标；右键可重命名、改色、清空
- 单窗口内 1 / 2 / 4 / 6 分屏布局切换，每窗格支持多标签页
- 每窗格独立路径、前进/后退/上级、面包屑与地址栏
- 复制、剪切、粘贴、删除（回收站）、新建文件夹
- 文件右键使用系统资源管理器菜单（含「在新标签页中打开」）
- 窗格间拖拽：默认复制，按住 Shift 移动

## 环境要求

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 构建与运行

```powershell
cd d:\Work\Project\C#\FolderSail
dotnet restore
dotnet build
dotnet run --project src\FolderSail\FolderSail.csproj
```

## 发布 EXE

框架依赖（体积小，需本机安装 .NET 8 运行时）：

```powershell
dotnet publish src\FolderSail\FolderSail.csproj -c Release -o publish\framework-dependent
```

自包含（拷走即用）：

```powershell
dotnet publish src\FolderSail\FolderSail.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\self-contained
```

输出：`publish\*\FolderSail.exe`

## 项目结构

```
FolderSail.sln
src/FolderSail.Core/   # 文件服务、收藏与设置持久化
src/FolderSail/        # WPF UI (MVVM)
```
