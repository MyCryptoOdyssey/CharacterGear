@echo off
rem Build CharacterGear.dll (read-only gear collector) against local Decal + Virindi View Service
cd /d "%~dp0src"
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set DECAL=C:\Program Files (x86)\Decal 3.0
set VVS=C:\Games\VirindiPlugins\VirindiViewService
if not exist "..\bin" mkdir "..\bin"

rem READ-ONLY GUARD: the plugin may only ask the server to ID items (RequestId) and print to its own chat window.
rem Any other game action, chat-command dispatch or input injection in the source stops the build.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\readonly_guard.ps1" "%~dp0src"
if errorlevel 1 ( echo BUILD STOPPED: read-only guard failed & exit /b 1 )

"%CSC%" /nologo /target:library /platform:x86 /out:..\bin\CharacterGear.dll /res:view.xml,CharacterGear.ViewXML.xml /res:rec.xml,CharacterGear.RecXML.xml ^
  /r:"%DECAL%\Decal.Adapter.dll" /r:"%DECAL%\.NET 4.0 PIA\Decal.Interop.Inject.dll" ^
  /r:"%VVS%\VirindiViewService.dll" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll *.cs
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 ) else ( echo BUILD OK: bin\CharacterGear.dll )

rem Planner (runs outside the game): reads data\<server>\*.json, writes CharacterGear_Report.html
cd /d "%~dp0planner"
"%CSC%" /nologo /target:exe /out:..\bin\CharacterGearPlanner.exe /res:..\tables\spells.tsv,CharacterGear.spells.tsv /res:..\tables\skills.tsv,CharacterGear.skills.tsv /res:..\tables\set_names.tsv,CharacterGear.set_names.tsv /res:..\tables\set_bonuses.tsv,CharacterGear.set_bonuses.tsv /res:..\tables\weights.tsv,CharacterGear.weights.tsv /res:..\tables\materials.tsv,CharacterGear.materials.tsv /res:..\tables\templates.tsv,CharacterGear.templates.tsv /res:..\tables\pets.tsv,CharacterGear.pets.tsv ^
  /r:System.Web.Extensions.dll /r:System.Core.dll *.cs
if errorlevel 1 ( echo PLANNER BUILD FAILED & exit /b 1 ) else ( echo BUILD OK: bin\CharacterGearPlanner.exe )
