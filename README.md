<img width="1355" height="858" alt="image" src="https://github.com/user-attachments/assets/e9b74122-0c73-4aa5-aad1-8dbd468a1f9f" /># FolderSail

Windows 上的轻量多窗格文件管理器。界面接近 Finder：彩色标签、多分屏、顶栏一体，适合日常整理文件。

发给别人时只需一个 `FolderSail.exe`，**不用装 .NET，也不用装 Ollama**。

## 功能

- **多分屏**：1 / 2 / 4 / 6 等布局，每个窗格独立浏览、独立标签页
- **彩色标签**：红/橙/黄/绿/蓝/紫/灰，拖文件夹到标签即可收藏；右键可改名、改色、清空
- **磁盘侧栏**：C / D 等盘符与容量条，点击进入盘符**根目录**
- **搜索**：文件名包含匹配（输入 `youyu` 会找出名字里带 youyu 的项）
- **自然语言搜索**（规则解析，无模型）：例如  
  `帮我搜索电脑中带有youyu字样的文件，是excel表格格式`  
  `原理图字样的文件夹`  
  会抽出关键字和类型（Excel / Word / PDF / 图片 / 文件夹等）再搜
- 复制、剪切、粘贴、删除到回收站、新建文件夹、F2 重命名
- 右键使用系统资源管理器菜单
- 窗格间拖拽：默认复制，按住 Shift 为移动
<img width="1355" height="858" alt="image" src="https://github.com/user-attachments/assets/64fd2913-7987-468a-b1fb-d6e8308f2aae" />

## 发给客户

1. 先关掉正在运行的 FolderSail
2. 打包（自包含单文件，约 70MB）：

```powershell
dotnet publish src\FolderSail\FolderSail.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist\win-x64
```

3. 把 `dist\win-x64\FolderSail.exe` 发出去即可

客户电脑：Windows 10 / 11 **64 位**。第一次启动会稍慢（自解压），属正常现象。

## 开发环境

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
dotnet restore
dotnet build
dotnet run --project src\FolderSail\FolderSail.csproj
```

## 项目结构

```
FolderSail.sln
src/FolderSail.Core/   文件服务、搜索、标签与设置
src/FolderSail/        WPF 界面（MVVM）
```

## 快捷键

| 快捷键 | 作用 |
|--------|------|
| Ctrl+F / Ctrl+K | 聚焦顶栏搜索 |
| Ctrl+T / Ctrl+W | 新建 / 关闭标签页 |
| Ctrl+C / X / V | 复制 / 剪切 / 粘贴 |
| Delete | 删除到回收站 |
| F2 | 重命名 |
| F5 | 刷新 |

## 许可

个人与分发请按仓库内许可约定使用。未单独声明时，默认仅供本项目作者及授权客户使用。
