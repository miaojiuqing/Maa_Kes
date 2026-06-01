#!/bin/bash
set -e

APP_DIR="/usr/lib/maa-kes"
RUN_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/maa-kes"

find_dotnet() {
    local dotnet="${DOTNET_ROOT:-/usr/share/dotnet}/dotnet"
    if [ -x "$dotnet" ]; then
        echo "$dotnet"
        return 0
    fi
    dotnet="$(command -v dotnet 2>/dev/null || true)"
    if [ -n "$dotnet" ] && [ -x "$dotnet" ]; then
        echo "$dotnet"
        return 0
    fi
    return 1
}

DOTNET="$(find_dotnet)"
if [ -z "$DOTNET" ]; then
    echo "错误: 找不到 .NET 运行时。请安装 dotnet-runtime (>= 10.0)。" >&2
    exit 1
fi

setup_runtime() {
    mkdir -p "${RUN_DIR}"
    mkdir -p "${RUN_DIR}/config/instances"
    mkdir -p "${RUN_DIR}/logs" "${RUN_DIR}/temp" "${RUN_DIR}/backup"

    for f in MFAAvalonia MFAAvalonia.dll MFAAvalonia.deps.json \
             MFAAvalonia.runtimeconfig.json libloader.dll; do
        if [ ! -f "${RUN_DIR}/${f}" ]; then
            cp "${APP_DIR}/${f}" "${RUN_DIR}/${f}"
        fi
    done

    for item in "${APP_DIR}"/*; do
        name="$(basename "${item}")"
        case "${name}" in
            MFAAvalonia|MFAAvalonia.dll|MFAAvalonia.deps.json|MFAAvalonia.runtimeconfig.json|libloader.dll)
                ;;
            config|logs|temp|backup)
                ;;
            *)
                if [ ! -e "${RUN_DIR}/${name}" ]; then
                    ln -s "${item}" "${RUN_DIR}/${name}"
                fi
                ;;
        esac
    done
}

setup_runtime

cd "${RUN_DIR}"
exec "${DOTNET}" exec "${RUN_DIR}/MFAAvalonia.dll" "$@"
