@echo off

ECHO Adding to the registry the WakaTime package...

REM SQL SERVER MANAGEMENT STUDIO 2012
REG QUERY "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\11.0\Packages" > nul 2> nul
if %errorlevel% equ 1 (
    REG ADD "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\11.0\Packages\{52d9c3ff-c893-408e-95e4-d7484ec7fa47}" /v SkipLoading /t REG_DWORD /d 1
)

REM SQL SERVER MANAGEMENT STUDIO 2014
REG QUERY "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\12.0\Packages" > nul 2> nul
if %errorlevel% equ 1 (
    REG ADD "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\12.0\Packages\{52d9c3ff-c893-408e-95e4-d7484ec7fa47}" /v SkipLoading /t REG_DWORD /d 1
)

REM SQL SERVER MANAGEMENT STUDIO 2016
REG QUERY "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\13.0\Packages" > nul 2> nul
if %errorlevel% equ 1 (
    REG ADD "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\13.0\Packages\{52d9c3ff-c893-408e-95e4-d7484ec7fa47}" /v SkipLoading /t REG_DWORD /d 1
)

REM SQL SERVER MANAGEMENT STUDIO 2017
REG QUERY "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\14.0\Packages" > nul 2> nul
if %errorlevel% equ 1 (
    REG ADD "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\14.0\Packages\{52d9c3ff-c893-408e-95e4-d7484ec7fa47}" /v SkipLoading /t REG_DWORD /d 1
)

REM SQL SERVER MANAGEMENT STUDIO 2018
REG QUERY "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\15.0\Packages" > nul 2> nul
if %errorlevel% equ 1 (
    REG ADD "HKEY_CURRENT_USER\Software\Microsoft\SQL Server Management Studio\15.0\Packages\{52d9c3ff-c893-408e-95e4-d7484ec7fa47}" /v SkipLoading /t REG_DWORD /d 1
)

ECHO All packages were successfully added