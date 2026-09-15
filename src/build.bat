@echo off
setlocal

REM ================================================================
REM  build.bat - DLG QA Plan Generator
REM
REM  Compiles src\DLGGenerator.cs into an ESAPI binary plugin.
REM  Nothing here is specific to one computer:
REM    - paths are resolved relative to this file (works from any
REM      folder, including double-click in Explorer);
REM    - csc.exe is taken from %WINDIR% (.NET Framework 4.x);
REM    - the ESAPI folder is found in this order:
REM        1. environment variable ESAPI_ROOT
REM        2. file esapi_path.txt in the repository root
REM        3. common Varian installation folders
REM    - the version in the DLL name is read from APP_VERSION in
REM      the source file.
REM ================================================================

REM ---------------------------------------------------------------
REM Repository paths (this file lives in <repo>\src)
REM ---------------------------------------------------------------
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "SRC=%ROOT%\src\DLGGenerator.cs"
set "CONFIG=%ROOT%\esapi_path.txt"
set "OUTDIR=%ROOT%\bin"

echo ================================================================
echo   DLG QA PLAN GENERATOR - BUILD
echo ================================================================
echo.

if not exist "%SRC%" goto :no_source

REM ---------------------------------------------------------------
REM Version, read from: private const string APP_VERSION = "x.y.z";
REM ---------------------------------------------------------------
set "VERSION="
for /f "tokens=2 delims==" %%A in ('findstr /c:"APP_VERSION = " "%SRC%"') do set "VERSION=%%A"

if defined VERSION goto :version_clean
echo [WARN] APP_VERSION not found in the source. Using an unversioned name.
set "OUT=%OUTDIR%\DLGGenerator.esapi.dll"
goto :find_csc

:version_clean
REM The line gives  "0.9.2";  -> remove quotes, semicolon and spaces
set "VERSION=%VERSION:"=%"
set "VERSION=%VERSION:;=%"
set "VERSION=%VERSION: =%"
set "OUT=%OUTDIR%\DLGGenerator_v%VERSION:.=_%.esapi.dll"

REM ---------------------------------------------------------------
REM C# compiler
REM ---------------------------------------------------------------
:find_csc
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if exist "%CSC%" goto :find_esapi
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if exist "%CSC%" goto :find_esapi
goto :no_csc

REM ---------------------------------------------------------------
REM ESAPI folder
REM ---------------------------------------------------------------
:find_esapi
set "ESAPI_DIR="

REM 1. Environment variable ESAPI_ROOT
if not defined ESAPI_ROOT goto :try_config
set "ESAPI_CANDIDATE=%ESAPI_ROOT:"=%"
if exist "%ESAPI_CANDIDATE%\VMS.TPS.Common.Model.API.dll" goto :found_env
echo [WARN] ESAPI_ROOT does not contain the ESAPI DLLs: "%ESAPI_CANDIDATE%"

REM 2. esapi_path.txt in the repository root (first line = folder)
:try_config
if not exist "%CONFIG%" goto :try_auto
set "ESAPI_CANDIDATE="
set /p ESAPI_CANDIDATE=<"%CONFIG%"
if not defined ESAPI_CANDIDATE goto :config_invalid
set "ESAPI_CANDIDATE=%ESAPI_CANDIDATE:"=%"
if exist "%ESAPI_CANDIDATE%\VMS.TPS.Common.Model.API.dll" goto :found_config
:config_invalid
echo [WARN] esapi_path.txt does not point to the ESAPI DLLs: "%ESAPI_CANDIDATE%"

REM 3. Common installation folders, newest version first
:try_auto
for %%V in (18.0 17.0 16.1 16.0 15.6) do (
    for %%P in ("C:\Program Files (x86)\Varian\RTM" "C:\Program Files\Varian\RTM" "D:\Program Files (x86)\Varian\RTM" "D:\Program Files\Varian\RTM") do (
        if not defined ESAPI_DIR if exist "%%~P\%%V\esapi\API\VMS.TPS.Common.Model.API.dll" set "ESAPI_DIR=%%~P\%%V\esapi\API"
    )
)
if not defined ESAPI_DIR goto :no_esapi
set "ESAPI_FROM=auto-detected"
goto :compile

:found_env
set "ESAPI_DIR=%ESAPI_CANDIDATE%"
set "ESAPI_FROM=from ESAPI_ROOT"
goto :compile

:found_config
set "ESAPI_DIR=%ESAPI_CANDIDATE%"
set "ESAPI_FROM=from esapi_path.txt"
goto :compile

REM ---------------------------------------------------------------
REM Compile
REM ---------------------------------------------------------------
:compile
set "API=%ESAPI_DIR%\VMS.TPS.Common.Model.API.dll"
set "TYPES=%ESAPI_DIR%\VMS.TPS.Common.Model.Types.dll"
if not exist "%TYPES%" goto :no_types

echo Source   : %SRC%
echo Version  : %VERSION%
echo Compiler : %CSC%
echo ESAPI    : %ESAPI_DIR%  [%ESAPI_FROM%]
echo Output   : %OUT%
echo.

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

"%CSC%" /nologo /target:library /out:"%OUT%" /reference:"%API%" /reference:"%TYPES%" /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%SRC%"
if errorlevel 1 goto :build_fail

echo.
echo ================================================================
echo   BUILD SUCCESSFUL
echo ================================================================
echo.
echo Next steps:
echo   1. Copy the DLL above to your Eclipse PublishedScripts folder.
echo   2. Approve the script in Eclipse (Script Approvals) if your
echo      database requires approval for write-enabled scripts.
echo   3. Open a QA patient and run it from the Scripts menu.
goto :end

REM ---------------------------------------------------------------
REM Errors
REM ---------------------------------------------------------------
:no_source
echo [ERROR] Source file not found: "%SRC%"
echo         build.bat must stay in the src folder of the repository.
goto :fail

:no_csc
echo [ERROR] C# compiler csc.exe not found in %WINDIR%\Microsoft.NET.
echo         Install .NET Framework 4.8.
goto :fail

:no_esapi
echo [ERROR] ESAPI installation not found.
echo.
echo Do ONE of the following, then run build.bat again:
echo.
echo   1. Create a file named esapi_path.txt in the repository root
echo      containing only the ESAPI API folder, for example:
echo        C:\Program Files (x86)\Varian\RTM\16.1\esapi\API
echo.
echo   2. Or set the environment variable ESAPI_ROOT, for example:
echo        setx ESAPI_ROOT "C:\Program Files (x86)\Varian\RTM\16.1\esapi\API"
echo      and open a new command window.
goto :fail

:no_types
echo [ERROR] VMS.TPS.Common.Model.Types.dll not found in "%ESAPI_DIR%"
goto :fail

:build_fail
echo.
echo ================================================================
echo   BUILD FAILED
echo ================================================================
echo Copy the complete error output above when reporting an issue.
goto :fail

:fail
echo.
pause
exit /b 1

:end
echo.
pause
exit /b 0
