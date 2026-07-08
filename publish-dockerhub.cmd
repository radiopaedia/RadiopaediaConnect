@echo off
SETLOCAL

:: --- CONFIGURATION ---
SET "DOCKERHUB_IMAGE=radiopaediaorg/radiopaedia-connect"

:: ---------------------

echo [1/5] Checking working tree...
set "GITSHA="
for /f %%i in ('git rev-parse --short HEAD') do set "GITSHA=%%i"
if not defined GITSHA (
    echo [ERROR] Not a git repository or git is not on PATH - cannot compute version tag.
    exit /b 1
)

set "DIRTY="
for /f "delims=" %%i in ('git status --porcelain') do set "DIRTY=1"
if defined DIRTY (
    echo [WARNING] Working tree has uncommitted changes. The published image will NOT
    echo           exactly match commit %GITSHA% - commit or stash first if you want the
    echo           version tag to be trustworthy for rollback/debugging.
    choice /M "Continue anyway"
    if errorlevel 2 exit /b 1
)

for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy.MM.dd"') do set "DATESTAMP=%%i"
SET "TAG=%DATESTAMP%-%GITSHA%"
echo Version tag: %TAG%

echo [2/5] Building Frontend (Vite)...
cd ClientApp
call npm install
call npm run build
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Frontend build failed.
    exit /b %ERRORLEVEL%
)
cd ..

echo [3/5] Building Docker Image...
docker build -t %DOCKERHUB_IMAGE%:%TAG% -t %DOCKERHUB_IMAGE%:latest .
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Docker build failed.
    exit /b %ERRORLEVEL%
)

echo [4/5] Pushing to Docker Hub...
echo NOTE: Make sure you are logged in with: docker login
docker push %DOCKERHUB_IMAGE%:%TAG%
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Docker push failed for tag %TAG%. Run 'docker login' first and ensure you have push access to %DOCKERHUB_IMAGE%.
    exit /b %ERRORLEVEL%
)
docker push %DOCKERHUB_IMAGE%:latest
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Docker push failed for tag latest.
    exit /b %ERRORLEVEL%
)

echo [5/5] Done!
echo Image published:
echo   https://hub.docker.com/r/radiopaediaorg/radiopaedia-connect/tags
echo Tags pushed: %TAG%, latest
echo.
pause
