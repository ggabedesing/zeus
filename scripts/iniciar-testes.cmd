@echo off
setlocal
chcp 65001 >nul
echo ZEUS - testes completos neste computador
echo.
echo O Windows solicitará administrador para os testes de permissões.
echo Mantenha esta janela aberta enquanto a suíte executa.
echo.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0test-on-windows.ps1"
set "ZEUS_TEST_EXIT=%ERRORLEVEL%"
echo.
if "%ZEUS_TEST_EXIT%"=="0" (
  echo Testes concluídos com aprovação. O relatório foi indicado acima.
) else (
  echo A suíte retornou o código %ZEUS_TEST_EXIT%. Consulte o relatório e os logs indicados.
)
echo.
pause
exit /b %ZEUS_TEST_EXIT%
