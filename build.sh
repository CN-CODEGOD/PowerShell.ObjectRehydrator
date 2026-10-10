#!/usr/bin/env bash
#
# build.sh — 构建 ObjectRehydrator.dll 并组装成可 Import 的 PowerShell 模块目录。
#
# 需要：.NET SDK 8.0+（本机当前未安装，需在有 SDK 的机器上执行）
#
# 用法：
#   ./build.sh              # Release 构建 + 组装到 ./dist/ObjectRehydrator/
#   ./build.sh Debug        # Debug 构建
#
set -euo pipefail

CONFIG="${1:-Release}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$ROOT/src/ObjectRehydrator"
OUT="$ROOT/dist/ObjectRehydrator"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet SDK 未安装。请先安装 .NET 8 SDK。" >&2
    exit 1
fi

echo "==> dotnet build ($CONFIG)"
dotnet build "$SRC/ObjectRehydrator.csproj" -c "$CONFIG"

echo "==> 组装模块目录: $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"

# 拷贝编译产物（含 Microsoft.PowerShell.SDK 依赖）
cp "$SRC/bin/$CONFIG/net8.0/"*.dll "$OUT/" 2>/dev/null || true
cp "$SRC/ObjectRehydrator.psd1" "$OUT/"

echo "==> 完成。验证："
echo "    pwsh -NoProfile -Command 'Import-Module $OUT/ObjectRehydrator.psd1; Get-Command Import-Xml'"
