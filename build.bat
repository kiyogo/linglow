@echo off
chcp 65001 >nul
echo ============================================================
echo   BUILDING VIDEO NARRATOR AI - WINDOWS x64 RELEASE
echo ============================================================

echo [1/4] Khoi phuc dependencies - dotnet restore...
dotnet restore VideoNarrator.sln
if %ERRORLEVEL% NEQ 0 (
    echo [LOI] Restore that bai!
    pause
    exit /b %ERRORLEVEL%
)

echo [2/4] Xuat ban ung dung Release win-x64 SingleFile...
dotnet publish VideoNarrator\VideoNarrator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish
if %ERRORLEVEL% NEQ 0 (
    echo [LOI] Publish that bai!
    pause
    exit /b %ERRORLEVEL%
)

echo [3/4] Dong bo FFmpeg binaries va giao dien wwwroot vao thu muc publish...
if not exist "publish\Assets\ffmpeg" mkdir "publish\Assets\ffmpeg"
if exist "VideoNarrator\Assets\ffmpeg\ffmpeg.exe" (
    copy /y "VideoNarrator\Assets\ffmpeg\ffmpeg.exe" "publish\Assets\ffmpeg\ffmpeg.exe" >nul
)
if exist "VideoNarrator\Assets\ffmpeg\ffprobe.exe" (
    copy /y "VideoNarrator\Assets\ffmpeg\ffprobe.exe" "publish\Assets\ffmpeg\ffprobe.exe" >nul
)
if exist "VideoNarrator\wwwroot" (
    xcopy /E /I /Y "VideoNarrator\wwwroot" "publish\wwwroot" >nul
)

echo [4/4] Kiem tra ket qua...
if exist "publish\VideoNarrator.exe" (
    echo ============================================================
    echo   BUILD THANH CONG!
    echo   File thuc thi: publish\VideoNarrator.exe
    echo   FFmpeg: publish\Assets\ffmpeg\ffmpeg.exe
    echo   FFprobe: publish\Assets\ffmpeg\ffprobe.exe
    echo ============================================================
) else (
    echo [CANH BAO] Khong tim thay file VideoNarrator.exe trong publish.
)

pause
