@echo off
REM Build GhosttyBridge.dll with MSVC.
REM Requires MSVC toolchain in PATH and INCLUDE/LIB set (see ~/.bashrc).

set MSVC_VER=14.50.35717
set VS=C:\Program Files\Microsoft Visual Studio\18\Community
set MSVC=%VS%\VC\Tools\MSVC\%MSVC_VER%
set SDK=C:\Program Files (x86)\Windows Kits\10
set SDK_VER=10.0.26100.0

set PATH=%MSVC%\bin\Hostx64\x64;%PATH%
set INCLUDE=%MSVC%\include;%SDK%\Include\%SDK_VER%\ucrt;%SDK%\Include\%SDK_VER%\um;%SDK%\Include\%SDK_VER%\shared
set LIB=%MSVC%\lib\x64;%SDK%\Lib\%SDK_VER%\ucrt\x64;%SDK%\Lib\%SDK_VER%\um\x64

cd /d "%~dp0"
cl /LD /MD /EHsc /O2 /DNDEBUG GhosttyBridge.cpp /Fe:..\Ghostty\x86_64\GhosttyBridge.dll /link d3d11.lib dxgi.lib kernel32.lib ucrt.lib vcruntime.lib ..\Ghostty\x86_64\ghostty.lib
