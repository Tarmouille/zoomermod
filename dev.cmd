@echo off
rem Recompile la version de developpement et l'installe dans le jeu. Le jeu doit etre ferme.
cd /d "%~dp0"
dotnet build DaleMod -c Debug --nologo -v q
pause
