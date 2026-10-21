@echo off
setlocal

rem A window you can open, talk into, and watch.
rem
rem The meter has to be looked at while somebody is speaking, and "dotnet run --project
rem src/Kvazr.Cli -- --meter 20 --name Maxwell" is not a thing anybody types twice. So:
rem double-click this, or run it with a name fragment and a number of seconds.
rem
rem   tools\mic.cmd                  every microphone, twenty seconds each
rem   tools\mic.cmd Maxwell          only the one whose name carries that word
rem   tools\mic.cmd Maxwell 60       and for a minute

set "ROOT=%~dp0.."
set "EXE=%ROOT%\src\Kvazr.Cli\bin\Debug\net10.0-windows10.0.17763.0\kvazr.exe"

set "WHO=%~1"
set "HOWLONG=%~2"
if "%HOWLONG%"=="" set "HOWLONG=20"

rem Built if it is not there yet, quietly. A first run should not need a second command.
if not exist "%EXE%" (
  echo Собираю консоль...
  dotnet build "%ROOT%\src\Kvazr.Cli" -v q --nologo || goto :fail
)

if "%WHO%"=="" (
  "%EXE%" --meter %HOWLONG%
) else (
  "%EXE%" --meter %HOWLONG% --name "%WHO%"
)

echo.
echo Нажмите любую клавишу, чтобы закрыть.
pause > nul
exit /b 0

:fail
echo.
echo Собрать не вышло. Запустите dotnet build и посмотрите, на чём.
pause > nul
exit /b 1
