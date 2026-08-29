@echo off
rem Runs the unit tests.
setlocal
cd /d "%~dp0"
dotnet run --project tests\DesktopDock.Tests -c Release
