@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "DECAL=%ProgramFiles(x86)%\Decal 3.0\Decal.Adapter.dll"
set "CORE=%ProgramFiles(x86)%\Decal 3.0\.NET 4.0 PIA\Decal.Interop.Core.DLL"
set "INJECT=%ProgramFiles(x86)%\Decal 3.0\.NET 4.0 PIA\Decal.Interop.Inject.DLL"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

for %%F in ("%DECAL%" "%CORE%" "%INJECT%" "%CSC%") do (
  if not exist "%%~F" (
    echo ERROR: Required file not found:
    echo %%~F
    pause
    exit /b 1
  )
)

if exist "%~dp0build" rmdir /s /q "%~dp0build"
if exist "%~dp0release" rmdir /s /q "%~dp0release"
mkdir "%~dp0build"
mkdir "%~dp0release"

echo Building AC World Gamma v0.4.0 plugin...
"%CSC%" /nologo /target:library /platform:x86 /optimize+ ^
 /out:"%~dp0build\ACWorldGamma.dll" ^
 /reference:"%DECAL%" ^
 /reference:"%CORE%" ^
 /reference:"%INJECT%" ^
 /reference:System.dll ^
 "%~dp0PluginCore.cs" "%~dp0Properties\AssemblyInfo.cs"
if errorlevel 1 goto :fail

echo Building uninstaller...
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ ^
 /out:"%~dp0build\uninstall.exe" ^
 /reference:System.dll ^
 /reference:System.Windows.Forms.dll ^
 "%~dp0Uninstaller.cs"
if errorlevel 1 goto :fail

echo Building self-contained installer...
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ ^
 /out:"%~dp0release\AC World Gamma Setup v0.4.0.exe" ^
 /reference:System.dll ^
 /reference:System.Windows.Forms.dll ^
 /resource:"%~dp0build\ACWorldGamma.dll",ACWorldGamma.dll ^
 /resource:"%~dp0build\uninstall.exe",uninstall.exe ^
 /resource:"%~dp0README.txt",README.txt ^
 "%~dp0Installer.cs"
if errorlevel 1 goto :fail

echo.
echo BUILD OK:
echo %~dp0release\AC World Gamma Setup v0.4.0-rc1a.exe
echo.
echo Self-contained installer created.
echo No RenderHook.dll. No Interop.RenderHookLib.dll. No vtable patching.
pause
exit /b 0

:fail
echo.
echo BUILD FAILED.
pause
exit /b 1
