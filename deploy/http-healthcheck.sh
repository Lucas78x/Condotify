#!/usr/bin/env bash
set -Eeuo pipefail

path="${1:-/health/live}"
exec 3<>/dev/tcp/127.0.0.1/8080
printf 'GET %s HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n' "$path" >&3
IFS=' ' read -r _ status _ <&3
[[ "$status" == "200" ]]
