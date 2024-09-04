@echo off
setlocal enabledelayedexpansion

:: 遍历当前目录的所有子目录
for /d %%d in (*) do (
    if exist "%%d\.git" (
        echo 进入目录 %%d
        pushd %%d
        :: 执行 git pull 更新代码库
        git pull -v --all
        popd
    )
)

echo 所有 Git 仓库已更新。
pause
