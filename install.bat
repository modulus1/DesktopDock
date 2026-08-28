@echo off
rem Install the three dependencies DesktopDock needs.
setlocal
cd /d "%~dp0"
python -m pip install --upgrade -r requirements.txt
echo.
echo Done. Start the dock with run.bat
pause
