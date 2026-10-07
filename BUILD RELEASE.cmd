@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "DECAL=%ProgramFiles(x86)%\Decal 3.0\Decal.Adapter.dll"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "THIRDPARTY=%~dp0ThirdParty"

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

if not exist "%THIRDPARTY%\RenderHook.dll" (
  echo ERROR: ThirdParty\RenderHook.dll was not found.
  pause
  exit /b 1
)

if not exist "%THIRDPARTY%\Interop.RenderHookLib.dll" (
  echo ERROR: ThirdParty\Interop.RenderHookLib.dll was not found.
  pause
  exit /b 1
)

if exist "%~dp0build" rmdir /s /q "%~dp0build"
if exist "%~dp0release" rmdir /s /q "%~dp0release"
mkdir "%~dp0build"
mkdir "%~dp0release"

echo Copying private RenderHook components...
copy /y "%THIRDPARTY%\RenderHook.dll" "%~dp0build\RenderHook.dll" >nul
if errorlevel 1 goto :fail
copy /y "%THIRDPARTY%\Interop.RenderHookLib.dll" "%~dp0build\Interop.RenderHookLib.dll" >nul
if errorlevel 1 goto :fail

echo Building AC World Gamma plugin...
"%CSC%" /nologo /target:library /platform:x86 /optimize+ ^
 /out:"%~dp0build\ACWorldGamma.dll" ^
 /reference:"%DECAL%" ^
 /reference:"%~dp0build\Interop.RenderHookLib.dll" ^
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
 /out:"%~dp0release\AC World Gamma Setup v0.3.0.exe" ^
 /reference:System.dll ^
 /reference:System.Windows.Forms.dll ^
 /resource:"%~dp0build\ACWorldGamma.dll",ACWorldGamma.dll ^
 /resource:"%~dp0build\RenderHook.dll",RenderHook.dll ^
 /resource:"%~dp0build\Interop.RenderHookLib.dll",Interop.RenderHookLib.dll ^
 /resource:"%~dp0build\uninstall.exe",uninstall.exe ^
 /resource:"%~dp0README.txt",README.txt ^
 /resource:"%~dp0THIRD_PARTY_NOTES.txt",THIRD_PARTY_NOTES.txt ^
 "%~dp0Installer.cs"
if errorlevel 1 goto :fail

echo.
echo BUILD OK:
echo %~dp0release\AC World Gamma Setup v0.3.0.exe
echo.
echo Release installer created successfully.
pause
exit /b 0

:fail
echo.
echo BUILD FAILED.
pause
exit /b 1
