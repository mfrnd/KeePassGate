#!/bin/sh
# Stop a running batch and its driver (not the emulator, not adb, not the server).
pkill -f batch-sy.sh; pkill -f run-sy.sh; pkill -f 'k.py run'; sleep 1
ps -eo pid,args | grep -E 'batch-|run-sy|k\.py' | grep -v grep || echo "no batch left"
