@echo off
rem Builds bin\xinput1_4.dll (x64, static CRT) -- the runtime-tweak proxy. Needs Visual Studio C++ tools.
setlocal
cd /d "%~dp0"
for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
if not exist bin mkdir bin
if not exist obj mkdir obj
ml64 /nologo /c /Foobj\stubs.obj stubs.asm || exit /b 1
cl /nologo /c /O2 /MT /EHsc /std:c++17 /W3 /D_CRT_SECURE_NO_WARNINGS /Foobj\dllmain.obj dllmain.cpp || exit /b 1
rc /nologo /foobj\winmm.res winmm.rc || exit /b 1
link /nologo /DLL /DEF:exports.def /OUT:bin\xinput1_4.dll obj\dllmain.obj obj\stubs.obj obj\winmm.res kernel32.lib || exit /b 1
echo built %~dp0bin\xinput1_4.dll
