@echo off
rem Antigravity runs hook commands through cmd /c. This sets the isolated test port and runs the frozen build.
set PIGEON_PORT=49473
set PIGEON_DATABASE=C:\Projects\Pigeon\artifacts\host-test\data\messages.db
C:\Projects\Pigeon\artifacts\host-test\bin\pigeon.exe hook %1 antigravity
