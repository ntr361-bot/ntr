@echo off
chcp 65001 >nul
title 更新智能表格开奖记录
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0智能表格_自动更新.ps1"
