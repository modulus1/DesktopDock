@echo off
rem Builds DesktopDock.exe into publish\.
rem
rem   publish.bat          one self-contained exe - runs anywhere, about 90 MB
rem   publish.bat small    needs the .NET 8 runtime installed - about 3 MB
rem
rem Building needs the .NET 8 SDK: https://dotnet.microsoft.com/download
setlocal
cd /d "%~dp0"

if /i "%~1"=="small" (
  set SELFCONTAINED=false
) else (
  set SELFCONTAINED=true
)

dotnet publish src\DesktopDock\DesktopDock.csproj ^
  -c Release -r win-x64 --self-contained %SELFCONTAINED% ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish

if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

echo.
echo Done. Run publish\DesktopDock.exe
pause
