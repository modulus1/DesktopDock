@echo off
rem Start DesktopDock. pythonw keeps the console window hidden.
setlocal
cd /d "%~dp0"
where pythonw >nul 2>nul && (start "" pythonw "DesktopDock.pyw" & goto :eof)
python "DesktopDock.pyw"
