@echo off
SETLOCAL

:: --- CONFIGURATION ---
SET "DOCKERHUB_IMAGE=radiopaediaorg/radiopaedia-connect"
SET "TAG=latest"

:: ---------------------

echo [1/4] Building Frontend (Vite)...
cd ClientApp
call npm install
call npm run build
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Frontend build failed.
    exit /b %ERRORLEVEL%
)
cd ..

echo [2/4] Building Docker Image...
docker build -t %DOCKERHUB_IMAGE%:%TAG% .
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Docker build failed.
    exit /b %ERRORLEVEL%
)

echo [3/4] Pushing to Docker Hub...
echo NOTE: Make sure you are logged in with: docker login
docker push %DOCKERHUB_IMAGE%:%TAG%
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Docker push failed. Run 'docker login' first and ensure you have push access to %DOCKERHUB_IMAGE%.
    exit /b %ERRORLEVEL%
)

echo [4/4] Done!
echo Image published: https://hub.docker.com/r/radiopaediaorg/radiopaedia-connect
echo.
pause
