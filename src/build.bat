
@echo off
setlocal

REM ================================================================
REM  build_DLG_v0_9_1.bat
REM  DLG QA Plan Generator v0.9.1
REM  Multi-energy support + null-safety + expanded documentation
REM ================================================================

set "API=C:\Program Files (x86)\Varian\RTM\16.1\esapi\API\VMS.TPS.Common.Model.API.dll"
set "TYPES=C:\Program Files (x86)\Varian\RTM\16.1\esapi\API\VMS.TPS.Common.Model.Types.dll"
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

set "SRC=DLGGenerator_v0_9_1.cs"
set "OUT=DLGGenerator_v0_9_1.esapi.dll"

echo ================================================================
echo   DLG QA PLAN GENERATOR v0.9.1
echo   6X / 10X / 15X / 6X FFF / 10X FFF
echo ================================================================
echo.
echo Fonte : %SRC%
echo Saida : %OUT%
echo.

if not exist "%CSC%" goto :no_csc
if not exist "%API%" goto :no_api
if not exist "%TYPES%" goto :no_types
if not exist "%SRC%" goto :no_cs

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
echo   OK: %OUT% GERADO
echo ================================================================
echo.
echo Copie para:
echo \\varian-fs\VA_DATA$\ProgramData\Vision\PublishedScripts\
goto :end

:no_csc
echo ERRO: csc.exe nao encontrado:
echo "%CSC%"
goto :end

:no_api
echo ERRO: API ESAPI nao encontrada:
echo "%API%"
goto :end

:no_types
echo ERRO: Types ESAPI nao encontrada:
echo "%TYPES%"
goto :end

:no_cs
echo ERRO: %SRC% nao encontrado nesta pasta.
goto :end

:build_fail
echo.
echo ================================================================
echo   FALHA NA COMPILACAO - v0.9.1
echo ================================================================
echo Copie TODO o erro acima e envie no chat.
goto :end

:end
echo.
pause
