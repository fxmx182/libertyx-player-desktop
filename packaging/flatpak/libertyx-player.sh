#!/bin/sh
# O .NET de arquivo único extrai as bibliotecas nativas na primeira vez que abre. Na caixinha do
# Flatpak a pasta pessoal é só leitura: o lugar com escrita é o cache do próprio app.
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/dotnet-bundle"
exec /app/libertyx/LibertyXPlayer "$@"
