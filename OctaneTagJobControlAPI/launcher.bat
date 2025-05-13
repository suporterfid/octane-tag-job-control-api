@echo off
echo RFID Tag Writing Application Launcher
echo ====================================

if "%1"=="" (
  echo Running with default config.json
  OctaneTagWritingTest.exe --config config.json
) else if "%1"=="--help" (
  OctaneTagWritingTest.exe --help
) else if "%1"=="--interactive" (
  OctaneTagWritingTest.exe --interactive
) else (
  echo Running with custom config: %1
  OctaneTagWritingTest.exe --config %1
)