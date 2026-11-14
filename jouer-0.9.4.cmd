@echo off
rem Remet la version publiee 0.9.4 du mod dans le jeu (pour jouer avec les amis). Le jeu doit etre ferme.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-release.ps1 0.9.4
pause
