@echo off
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF
set WM=%WINDIR%\System32\WinMetadata
set GAC=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL
set RT=/r:"%GAC%\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll" /r:"%GAC%\System.Runtime.InteropServices.WindowsRuntime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.InteropServices.WindowsRuntime.dll"
if "%OUT%"=="" set OUT=bin\PrismDock.exe
cd /d "%~dp0"
if not exist bin mkdir bin
set ICON=
if not exist setup\prism.ico if exist setup\make-icon.ps1 powershell -NoProfile -ExecutionPolicy Bypass -File setup\make-icon.ps1 >nul 2>&1
if exist setup\prism.ico set ICON=/win32icon:setup\prism.ico
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /unsafe /platform:anycpu /out:%OUT% /win32manifest:app.manifest %ICON% ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:System.dll /r:System.Core.dll /r:Microsoft.CSharp.dll /r:System.Data.dll ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:"%WM%\Windows.Foundation.winmd" /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Storage.winmd" %RT% ^
  src\*.cs
if errorlevel 1 (echo. & echo DERLEME BASARISIZ & exit /b 1)
echo Derlendi: %OUT%
