@echo off
setlocal enabledelayedexpansion

:: ������ǰĿ¼��������Ŀ¼
for /d %%d in (*) do (
    if exist "%%d\.git" (
        echo ����Ŀ¼ %%d
        pushd %%d
        :: ִ�� git pull ���´����
        git pull -v --all
        popd
    )
)

echo ���� Git �ֿ��Ѹ��¡�
pause
