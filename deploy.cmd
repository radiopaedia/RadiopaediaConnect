@echo off
SETLOCAL

:: --- CONFIGURATION ---
SET "IMAGE_NAME=radiopaedia-connect"
SET "CONTAINER_NAME=radiopaedia-connect"
SET "TAR_NAME=radiopaedia-connect.tar"

:: Remote Server Details
SET "REMOTE_USER=rispacs"
SET "REMOTE_HOST=172.28.43.59"
SET "REMOTE_DIR=/home/rispacs"

:: Docker Run Arguments (Ports and Volume)
:: Mapping Host 80 -> Container 5000 (HTTP)
:: Mapping Host 104 -> Container 104 (DICOM)
:: Mapping Volume for DB and Keys persistence
SET "DOCKER_RUN_ARGS=-p 80:5000 -p 104:104 -v /data:/data --restart unless-stopped"

:: ---------------------

echo [1/6] Building Frontend (Vite)...
cd ClientApp
call npm install
call npm run build
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Frontend build failed.
    exit /b %ERRORLEVEL%
)
cd ..

echo [2/6] Building Docker Image...
docker build -t %IMAGE_NAME% .
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Docker build failed.
    exit /b %ERRORLEVEL%
)

echo [3/6] Saving Image to Tarball...
docker save -o %TAR_NAME% %IMAGE_NAME%
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Docker save failed.
    exit /b %ERRORLEVEL%
)

echo [4/6] Uploading Tarball to %REMOTE_HOST%...
scp %TAR_NAME% %REMOTE_USER%@%REMOTE_HOST%:%REMOTE_DIR%/%TAR_NAME%
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] SCP upload failed.
    exit /b %ERRORLEVEL%
)

echo [5/6] Executing Remote Deployment...
:: 1. Stop existing container (ignore error if not running)
:: 2. Remove existing container (ignore error if missing)
:: 3. Remove old image (to free space)
:: 4. Load new image
:: 5. Run new container
:: 6. Delete remote tarball
ssh %REMOTE_USER%@%REMOTE_HOST% "docker stop %CONTAINER_NAME% || true && docker rm %CONTAINER_NAME% || true && docker rmi %IMAGE_NAME% || true && docker load -i %REMOTE_DIR%/%TAR_NAME% && docker run -d --name %CONTAINER_NAME% %DOCKER_RUN_ARGS% %IMAGE_NAME% && rm %REMOTE_DIR%/%TAR_NAME%"

echo [6/6] Cleaning up local artifacts...
del %TAR_NAME%

echo [SUCCESS] Deployment Complete!
pause