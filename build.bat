@echo off
chcp 65001 >nul
title ULTRA-CRAFT Build Script

echo ========================================================
echo          Сборка ULTRA-CRAFT with Destruction
echo ========================================================
echo.

set "GAMEDIR=U:\games\ULTRAKILL"

if exist "%GAMEDIR%" goto :FOUND_GAME
if exist "D:\Games\ULTRAKILL Killcraft" (set "GAMEDIR=D:\Games\ULTRAKILL Killcraft" & goto :FOUND_GAME)
if exist "D:\SteamLibrary\steamapps\common\ULTRAKILL" (set "GAMEDIR=D:\SteamLibrary\steamapps\common\ULTRAKILL" & goto :FOUND_GAME)
if exist "C:\Program Files (x86)\Steam\steamapps\common\ULTRAKILL" (set "GAMEDIR=C:\Program Files (x86)\Steam\steamapps\common\ULTRAKILL" & goto :FOUND_GAME)
if exist "C:\Program Files\Steam\steamapps\common\ULTRAKILL" (set "GAMEDIR=C:\Program Files\Steam\steamapps\common\ULTRAKILL" & goto :FOUND_GAME)
if exist "E:\SteamLibrary\steamapps\common\ULTRAKILL" (set "GAMEDIR=E:\SteamLibrary\steamapps\common\ULTRAKILL" & goto :FOUND_GAME)

:FOUND_GAME
echo [ИНФО] Папка ULTRAKILL: %GAMEDIR%
echo.

echo [ИНФО] Проверка .NET SDK...
dotnet --version
if errorlevel 1 goto :NO_DOTNET

echo.
echo [ИНФО] Запуск компиляции...
dotnet build -c Release -p:GameDir="%GAMEDIR%"
if errorlevel 1 goto :BUILD_FAIL

echo.
echo ========================================================
echo [УСПЕХ] Сборка успешно завершена!
echo Файл UltraCraft.dll собран и скопирован в:
echo %GAMEDIR%\BepInEx\plugins\UltraCraft\
echo ========================================================
echo.
pause
exit /b 0

:NO_DOTNET
echo.
echo ========================================================
echo [ОШИБКА] .NET SDK не установлен или не найден в PATH!
echo Скачайте .NET SDK: https://dotnet.microsoft.com/download
echo ========================================================
echo.
pause
exit /b 1

:BUILD_FAIL
echo.
echo ========================================================
echo [ОШИБКА] Ошибка при сборке!
echo ========================================================
echo.
pause
exit /b 1
