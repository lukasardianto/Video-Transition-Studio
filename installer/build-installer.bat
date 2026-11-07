@echo off
REM Build exe + installer Video Transition Studio
cd /d "%~dp0.."
echo [1/2] Build exe...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /win32icon:src\icon.ico /out:build\VideoTransitionStudio.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll src\Program.cs src\MainForm.cs src\MaskGenerator.cs src\VideoProcessor.cs
if errorlevel 1 (echo BUILD GAGAL & exit /b 1)
echo [2/2] Build installer...
"%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" /Q "%~dp0VideoTransitionStudio.iss"
if errorlevel 1 (echo INSTALLER GAGAL & exit /b 1)
echo Selesai: Output di installer\Output\
pause
