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
mkdir "%~dp0build"

echo Building AC World Gamma v0.4.0-rc1a...
"%CSC%" /nologo /target:library /platform:x86 /optimize+ ^
 /out:"%~dp0build\ACWorldGamma.dll" ^
 /reference:"%DECAL%" ^
 /reference:"%CORE%" ^
 /reference:"%INJECT%" ^
 /reference:System.dll ^
 "%~dp0PluginCore.cs" "%~dp0Properties\AssemblyInfo.cs"
if errorlevel 1 goto :fail

echo.
echo BUILD OK:
echo %~dp0build\ACWorldGamma.dll
echo.
echo World-only pre-UI renderer. No RenderHook.dll. No vtable patching.
pause
exit /b 0

:fail
echo.
echo BUILD FAILED.
pause
exit /b 1
