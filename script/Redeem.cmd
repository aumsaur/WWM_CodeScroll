@echo off
rem Double-click to start. Extra arguments are passed through, e.g.  Redeem.cmd -Start 40
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0wwm-redeem-helper.ps1" %*
