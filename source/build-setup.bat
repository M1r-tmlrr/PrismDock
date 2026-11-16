@echo off
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF
cd /d "%~dp0"
if not exist bin\PrismDock.exe (
  echo.
  echo HATA: bin\PrismDock.exe bulunamadi. Once build.bat ile uygulamayi derle.
  exit /b 1
)
if not exist setup\prism.ico (
  powershell -NoProfile -ExecutionPolicy Bypass -File setup\make-icon.ps1 -Out setup\prism.ico
)
set ICON=
if exist setup\prism.ico set ICON=/win32icon:setup\prism.ico
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /out:bin\PrismDock-Setup.exe /win32manifest:setup\setup.manifest %ICON% ^
  /resource:bin\PrismDock.exe,PrismDock.exe ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" /r:System.dll /r:System.Core.dll /r:Microsoft.CSharp.dll ^
  /r:System.Windows.Forms.dll ^
  setup\Setup.cs
if errorlevel 1 (echo. & echo KURULUM DERLEMESI BASARISIZ & exit /b 1)
echo Derlendi: bin\PrismDock-Setup.exe
