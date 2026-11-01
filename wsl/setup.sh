#!/usr/bin/env bash
# What the spike runs on, from Ubuntu's own archive, and a record of what arrived.
#
#     wsl -d Ubuntu-24.04 -u root -- bash /mnt/c/dev/wt/spike-mono/wsl/setup.sh
#     wsl --terminate Ubuntu-24.04
#
# The second line is what makes `/etc/wsl.conf` take effect.
#
# Mono is the distribution's, which is the one a `keepass2` package on Ubuntu runs on. Xvfb is the
# only display anything here is given: WSLg is turned off in `.wslconfig` (see README.md), so
# nothing the probes or KeePass draw can reach the Windows desktop.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive

apt-get update
apt-get install -y --no-install-recommends \
    mono-complete xvfb xauth x11-apps x11-utils xdotool imagemagick \
    fonts-dejavu-core openssl iproute2 ca-certificates

# `--install --no-launch` skips the first start, which is where a user would have been made, so
# everything would otherwise run as root. The probes and KeePass run as `probe`, a user with no
# password and no sudo. Windows' PATH is kept out, so nothing here finds a Windows program by name.
id probe > /dev/null 2>&1 || useradd --create-home --shell /bin/bash probe
cat > /etc/wsl.conf <<'EOF'
[user]
default=probe

[interop]
appendWindowsPath=false
EOF

echo "--- versions"
mono --version | head -1
dpkg-query -W -f='${Package} ${Version}\n' mono-complete mono-runtime libgdiplus xvfb xdotool
. /etc/os-release && echo "os ${PRETTY_NAME}"
