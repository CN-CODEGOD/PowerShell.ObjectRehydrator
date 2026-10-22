#!/usr/bin/env bash
#
# build.docker.sh — 在容器里构建，无需本机安装 .NET SDK。
#
# 用法：
#   ./build.docker.sh          # 构建镜像并抽出 dist/ObjectRehydrator/
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGE="objectrehydrator-build:local"
CONTAINER="objectrehydrator-build-tmp"

if ! command -v docker >/dev/null 2>&1; then
    echo "ERROR: 未找到 docker。" >&2
    exit 1
fi

echo "==> 构建镜像 ($IMAGE)"
docker build -f "$ROOT/Dockerfile.build" -t "$IMAGE" "$ROOT"

echo "==> 抽出构建产物"
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker create --name "$CONTAINER" "$IMAGE" >/dev/null
rm -rf "$ROOT/dist"
docker cp "$CONTAINER:/src/dist" "$ROOT/dist"
docker rm -f "$CONTAINER" >/dev/null

echo "==> 完成：$ROOT/dist/ObjectRehydrator/"
echo "    验证：pwsh -NoProfile -Command 'Import-Module $ROOT/dist/ObjectRehydrator/ObjectRehydrator.psd1; Get-Command Import-Xml'"
