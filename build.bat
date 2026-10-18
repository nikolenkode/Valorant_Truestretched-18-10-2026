@echo off
setlocal
cd /d "%~dp0"
rem Builds CustomRes.exe with the C# compiler that ships with Windows (.NET Framework 4) - nothing to install.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo The .NET Framework 4 C# compiler was not found.
    pause
    exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /out:CustomRes.exe /win32manifest:app.manifest /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll CustomRes_Core.cs CustomRes_Gui.cs
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)
echo Built CustomRes.exe
pause