@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "DECAL=%ProgramFiles(x86)%\Decal 3.0\Decal.Adapter.dll"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%DECAL%" (
  echo ERROR: Decal.Adapter.dll was not found at:
  echo "%DECAL%"
  pause
  exit /b 1
)

if not exist "%CSC%" (
  echo ERROR: 32-bit .NET Framework compiler not found at:
  echo "%CSC%"
  pause
  exit /b 1
)

if exist "%~dp0build" rmdir /s /q "%~dp0build"
if exist "%~dp0release" rmdir /s /q "%~dp0release"
mkdir "%~dp0build"
mkdir "%~dp0release"

echo Building AC World Gamma native-hook prototype...
"%CSC%" /nologo /target:library /platform:x86 /optimize+ ^
 /out:"%~dp0build\ACWorldGamma.dll" ^
 /reference:"%DECAL%" ^
 /reference:System.dll ^
 "%~dp0PluginCore.cs" "%~dp0Direct3DLightHook.cs" "%~dp0Properties\AssemblyInfo.cs"
if errorlevel 1 goto :fail

echo Building uninstaller...
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ ^
 /out:"%~dp0build\uninstall.exe" ^
 /reference:System.dll ^
 /reference:System.Windows.Forms.dll ^
 "%~dp0Uninstaller.cs"
if errorlevel 1 goto :fail

echo Building self-contained experimental installer...
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ ^
 /out:"%~dp0release\AC World Gamma Native Test v0.4.0-alpha1.exe" ^
 /reference:System.dll ^
 /reference:System.Windows.Forms.dll ^
 /resource:"%~dp0build\ACWorldGamma.dll",ACWorldGamma.dll ^
 /resource:"%~dp0build\uninstall.exe",uninstall.exe ^
 /resource:"%~dp0README.txt",README.txt ^
 /resource:"%~dp0NATIVE_HOOK_NOTES.md",NATIVE_HOOK_NOTES.md ^
 "%~dp0Installer.cs"
if errorlevel 1 goto :fail

echo.
echo BUILD OK:
echo %~dp0release\AC World Gamma Native Test v0.4.0-alpha1.exe
echo.
echo This is an experimental native-hook build.
pause
exit /b 0

:fail
echo.
echo BUILD FAILED.
pause
exit /b 1
