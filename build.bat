@echo off
setlocal
cd /d "%~dp0"
rem Builds CustomRes.exe with the C# compiler that ships with Windows (.NET Framework 4) - nothing to install.
rem Every .cs file in this folder is compiled, so the file names do not matter.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo The .NET Framework 4 C# compiler was not found.
    pause
    exit /b 1
)
set "ICONARG="
if exist app.ico set "ICONARG=/win32icon:app.ico"
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /out:CustomRes.exe /win32manifest:app.manifest %ICONARG% /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll *.cs
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)
echo Built CustomRes.exe
pause