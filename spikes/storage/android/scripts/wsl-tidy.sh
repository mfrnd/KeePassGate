#!/bin/sh
# Remove the empty folder rclone made for its configuration (rclone v1.68.2 creates it on first run;
# it holds nothing), and its parent if that is then empty.
c="$HOME/.config/rclone"
ls -la --time-style=full-iso "$c"
rmdir "$c" && rmdir "$HOME/.config" 2>/dev/null
ls -la "$HOME" | grep -E 'config|cache' || echo "no .config or .cache left"
