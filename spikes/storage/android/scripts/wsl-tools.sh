#!/bin/sh
# What WSL offers for serving a file: python, venv, pip, uv, rclone, network.
python3 --version
python3 -c "import venv, ensurepip; print('venv and ensurepip present')" 2>&1
which uv rclone pip3 2>&1
ls ~/.local/bin 2>/dev/null
curl -sS -o /dev/null -w "pypi http %{http_code}\n" https://pypi.org/simple/wsgidav/ 2>&1
