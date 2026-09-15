@echo off
setlocal EnableDelayedExpansion

REM ================================================================
REM  build.bat
REM  DLG QA Plan Generator
REM  Auto-detects ESAPI installation and compiles the script.
REM ================================================================

set "SRC=src\DLGGenerator.cs"
set "OUT=DLGGenerator.esapi.dll"

echo ================================================================
echo   DLG QA PLAN GENERATOR - BUILD
echo ================================================================
echo.
echo Source : %SRC%
echo Output : %OUT%
echo.

REM ----------------------------------------------------------------
REM Step 1 - Locate csc.exe (C# compiler)
REM ----------------------------------------------------------------
set "CSC="

if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" (
    set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
if "%CSC%"=="" if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" (
    set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if "%CSC%"=="" (
    echo [ERROR] C# compiler csc.exe not found.
    echo         Please install .NET Framework 4.8 or later.
    goto :end
)

echo [OK] Compiler : %CSC%

REM ----------------------------------------------------------------
REM Step 2 - Locate ESAPI API folder
REM ----------------------------------------------------------------
set "ESAPI_DIR="

REM 2a - Check environment variable ESAPI_ROOT
if defined ESAPI_ROOT (
    if exist "%ESAPI_ROOT%\VMS.TPS.Common.Model.API.dll" (
        set "ESAPI_DIR=%ESAPI_ROOT%"
        echo [OK] ESAPI    : %ESAPI_DIR%  (from ESAPI_ROOT)
    )
)

REM 2b - Check optional external config file
if "%ESAPI_DIR%"=="" if exist "esapi_path.txt" (
    set /p CUSTOM_PATH=<esapi_path.txt
    if exist "!CUSTOM_PATH!\VMS.TPS.Common.Model.API.dll" (
        set "ESAPI_DIR=!CUSTOM_PATH!"
        echo [OK] ESAPI    : !ESAPI_DIR!  (from esapi_path.txt)
    )
)

REM 2c - Auto-detect common installation paths
if "%ESAPI_DIR%"=="" (
    for %%V in (18.0 17.0 16.1 16.0 15.6) do (
        if "!ESAPI_DIR!"=="" (
            for %%P in (
                "C:\Program Files\Varian\RTM"
                "C:\Program Files (x86)\Varian\RTM"
                "D:\Program Files\Varian\RTM"
                "D:\Program Files (x86)\Varian\RTM"
            ) do (
                set "CANDIDATE=%%~P\%%V\esapi\API"
                if exist "!CANDIDATE!\VMS.TPS.Common.Model.API.dll" (
                    set "ESAPI_DIR=!CANDIDATE!"
                    echo [OK] ESAPI    : !ESAPI_DIR!  ^(auto-detected^)
                )
            )
        )
    )
)

if "%ESAPI_DIR%"=="" (
    echo.
    echo [ERROR] ESAPI installation not found.
    echo.
    echo Please do ONE of the following:
    echo.
    echo   1. Set the environment variable ESAPI_ROOT to your ESAPI API folder:
    echo        setx ESAPI_ROOT "C:\path\to\RTM\16.1\esapi\API"
    echo.
    echo   2. Create a file named "esapi_path.txt" next to this build.bat
    echo      containing the full path to your ESAPI API folder, e.g.:
    echo        C:\Program Files (x86)\Varian\RTM\16.1\esapi\API
    echo.
    goto :end
)

set "API=%ESAPI_DIR%\VMS.TPS.Common.Model.API.dll"
set "TYPES=%ESAPI_DIR%\VMS.TPS.Common.Model.Types.dll"

REM ----------------------------------------------------------------
REM Step 3 - Verify source file exists
REM ----------------------------------------------------------------
if not exist "%SRC%" (
    echo [ERROR] Source file not found: %SRC%
    echo         Make sure you are running build.bat from the repository root.
    goto :end
)

REM ----------------------------------------------------------------
REM Step 4 - Compile
REM ----------------------------------------------------------------
echo.
echo Compiling...
echo.

"%CSC%" ^
 /nologo ^
 /target:library ^
 /out:"%OUT%" ^
 /reference:"%API%" ^
 /reference:"%TYPES%" ^
 /reference:System.Core.dll ^
 /reference:System.Windows.Forms.dll ^
 /reference:System.Drawing.dll ^
 "%SRC%"

if errorlevel 1 goto :build_fail

echo.
echo ================================================================
echo   BUILD SUCCESSFUL
echo ================================================================
echo.
echo Output: %OUT%
echo.
echo Next steps:
echo   1. Copy %OUT% to your Eclipse PublishedScripts folder, e.g.:
echo        \\your-server\VA_DATA$\ProgramData\Vision\PublishedScripts\
echo   2. Restart Eclipse (or refresh scripts).
echo   3. Open a QA patient and launch the script from the Scripts menu.
echo.
goto :end

:build_fail
echo.
echo ================================================================
echo   BUILD FAILED
echo ================================================================
echo.
echo Copy the entire error output above and check the repository
echo Issues page or README troubleshooting section.
goto :end

:end
echo.
pause
