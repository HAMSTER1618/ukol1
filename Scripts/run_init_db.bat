@echo off
REM -----------------------------------------------
REM run_init_db.bat - create Firebird database and apply schema
REM -----------------------------------------------

REM --- Firebird/isql parameters ---
set ISQL=C:\FB\isql.exe
set DB_USER=SYSDBA
set DB_PASS=masterkey
set DB_SERVER=localhost
set DB_PORT=3050

REM --- Paths relative to this script ---
set DB_FILE=%~dp0KNIHOVNA2.fdb
set SCHEMA_SQL=%~dp0init_db.sql

REM --- Build the full connection string (host/port:full_path) ---
set DB_CONN=%DB_SERVER%/%DB_PORT%:%DB_FILE%

REM --- If the database file does not exist, create it ---
if not exist "%DB_FILE%" (
    echo [!] Database not found - creating "%DB_FILE%"...

    REM 1) Generate a temporary SQL file with CREATE DATABASE
    (
      echo CREATE DATABASE '%DB_CONN%' USER '%DB_USER%' PASSWORD '%DB_PASS%';
      echo COMMIT;
      echo QUIT;
    ) > "%~dp0_create_db.sql"

    REM 2) Execute the creation script via isql
    "%ISQL%" -u %DB_USER% -p %DB_PASS% -i "%~dp0_create_db.sql"
    if ERRORLEVEL 1 (
        echo [!] Error creating the database. Check _create_db.sql
        exit /b 1
    )
    del "%~dp0_create_db.sql"

    REM 3) Apply your schema
    echo Applying schema from init_db.sql...
    "%ISQL%" -u %DB_USER% -p %DB_PASS% "%DB_CONN%" -i "%SCHEMA_SQL%"
    if ERRORLEVEL 1 (
        echo [!] Error applying the schema. Check init_db.sql
        exit /b 1
    )

    echo Database created and schema applied successfully.
) else (
    echo Database already exists - skipping creation.
)

exit /b 0
