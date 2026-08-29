@echo off
rem Builds if needed and starts the dock. For a standalone exe use publish.bat.
setlocal
cd /d "%~dp0"
dotnet run --project src\DesktopDock\DesktopDock.csproj -c Release
