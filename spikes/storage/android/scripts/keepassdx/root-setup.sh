#!/bin/sh
# As root: load KVM, which does not persist across a WSL restart, give the user who will measure
# access to it, and install the JDK and the libraries the SDK and its emulator need. Idempotent.
#   sh root-setup.sh <user> [install]
U=$1
[ -n "$U" ] || { echo "usage: root-setup.sh <user> [install]"; exit 1; }
echo "### $(date -u +%Y-%m-%dT%H:%M:%SZ) root-setup"
modprobe kvm_intel 2>&1; echo "modprobe rc=$?"
ls -l /dev/kvm
chmod 666 /dev/kvm && ls -l /dev/kvm
getent group kvm && usermod -aG kvm "$U"
if [ "$2" = install ]; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -q 2>&1 | tail -2
  apt-get install -y -q openjdk-17-jdk-headless unzip curl libpulse0 libnss3 libxcomposite1 libxcursor1 libxi6 libxtst6 libgl1 libegl1 libxkbfile1 libxdamage1 libxrandr2 libasound2t64 2>&1 | tail -3
fi
java -version 2>&1 | head -1
free -g | head -2
nproc
df -h /home | tail -1
