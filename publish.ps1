@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [.NET SDK 未找到] 请先安装 .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
  exit /b 1
)

dotnet publish src\FolderSail\FolderSail.csproj -c Release -o publish\framework-dependent
if errorlevel 1 exit /b 1

echo.
echo 发布完成:
echo   publish\framework-dependent\FolderSail.exe
echo.
echo 自包含版本需要网络还原 NuGet 包，可手动运行:
echo   dotnet restore src\FolderSail\FolderSail.csproj -r win-x64
echo   dotnet publish src\FolderSail\FolderSail.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\self-contained
endlocal
