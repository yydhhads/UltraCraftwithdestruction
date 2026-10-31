@echo off
chcp 65001 >nul
title ULTRA-CRAFT Build Script
color 0b

echo ========================================================
echo          Сборка ULTRA-CRAFT with Destruction
echo ========================================================
echo.

:: 1. Проверка наличия dotnet SDK
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    color 0c
    echo [ОШИБКА] .NET SDK не найден!
    echo.
    echo Пожалуйста, установите .NET SDK:
    echo https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

:: 2. Поиск папки ULTRAKILL
set GAMEDIR=

if exist "U:\games\ULTRAKILL\BepInEx\" (
    set "GAMEDIR=U:\games\ULTRAKILL"
) else if exist "U:\games\ULTRAKILL\" (
    set "GAMEDIR=U:\games\ULTRAKILL"
) else if exist "D:\Games\ULTRAKILL Killcraft\BepInEx\" (
    set "GAMEDIR=D:\Games\ULTRAKILL Killcraft"
) else if exist "D:\SteamLibrary\steamapps\common\ULTRAKILL\BepInEx\" (
    set "GAMEDIR=D:\SteamLibrary\steamapps\common\ULTRAKILL"
) else if exist "C:\Program Files (x86)\Steam\steamapps\common\ULTRAKILL\BepInEx\" (
    set "GAMEDIR=C:\Program Files (x86)\Steam\steamapps\common\ULTRAKILL"
) else if exist "C:\Program Files\Steam\steamapps\common\ULTRAKILL\BepInEx\" (
    set "GAMEDIR=C:\Program Files\Steam\steamapps\common\ULTRAKILL"
) else if exist "E:\SteamLibrary\steamapps\common\ULTRAKILL\BepInEx\" (
    set "GAMEDIR=E:\SteamLibrary\steamapps\common\ULTRAKILL"
)

if not defined GAMEDIR (
    echo [ВНИМАНИЕ] Автоматически папка игры не найдена.
    echo Введите путь к папке ULTRAKILL (например: U:\games\ULTRAKILL) или нажмите Enter:
    set /p USER_GAMEDIR="Путь к ULTRAKILL: "
    if defined USER_GAMEDIR (
        set "GAMEDIR=%USER_GAMEDIR%"
    )
)

if defined GAMEDIR (
    echo [ИНФО] Используется папка ULTRAKILL: %GAMEDIR%
    echo.
    echo Запуск сборки dotnet build...
    dotnet build -c Release -p:GameDir="%GAMEDIR%"
) else (
    echo [ИНФО] Используем пути по умолчанию...
    echo.
    echo Запуск сборки dotnet build...
    dotnet build -c Release
)

if %errorlevel% neq 0 (
    echo.
    color 0c
    echo ========================================================
    echo [ОШИБКА] Сборка завершилась с ошибкой!
    echo Проверьте, что в "%GAMEDIR%" установлены ULTRAKILL и BepInEx.
    echo ========================================================
    echo.
    pause
    exit /b %errorlevel%
)

echo.
color 0a
echo ========================================================
echo [УСПЕХ] Сборка успешно завершена!
echo Файл UltraCraft.dll собран в:
echo bin\Release\netstandard2.1\UltraCraft.dll
if defined GAMEDIR (
    echo И автоматически скопирован в:
    echo %GAMEDIR%\BepInEx\plugins\UltraCraft\
)
echo ========================================================
echo.
pause
