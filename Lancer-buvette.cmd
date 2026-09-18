@echo off
REM Demarre la buvette, puis ouvre le navigateur des que le serveur repond.
cd /d "%~dp0"

start "" /min powershell -NoProfile -WindowStyle Hidden -Command ^
 "for($i=0;$i -lt 120;$i++){try{Invoke-WebRequest 'http://localhost:5000' -UseBasicParsing -TimeoutSec 2 ^| Out-Null; Start-Process 'http://localhost:5000'; break}catch{Start-Sleep -Milliseconds 500}}"

echo Demarrage de la buvette... (fermez cette fenetre pour arreter)
dotnet run --no-launch-profile --project src\Buvette
pause
