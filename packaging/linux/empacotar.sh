#!/bin/bash
# Empacota o LibertyX Player para Linux a partir do executável único já publicado:
#   .tar.gz (pasta solta), .deb (Debian/Ubuntu/Mint), .rpm (Fedora/openSUSE) e AppImage (qualquer um).
# Nos três primeiros o motor de vídeo é o libmpv da própria distribuição (dependência do pacote);
# o AppImage leva o próprio, LGPL, compilado por Scripts/libmpv-linux.sh (a pasta do 5º argumento).
# Uso: empacotar.sh <x64|arm64> <versão 1.0.N> <executável> <pasta de saída> [pasta do libmpv]
set -euo pipefail
ARCH="$1"; VER="$2"; EXE="$3"; OUT="$4"; LIBMPV="${5:-}"
AQUI="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
case "$ARCH" in
  x64)   DEB_ARCH=amd64; RPM_ARCH=x86_64;  AI_ARCH=x86_64;  SUF="" ;;
  arm64) DEB_ARCH=arm64; RPM_ARCH=aarch64; AI_ARCH=aarch64; SUF="-arm64" ;;
  *) echo "arquitetura desconhecida: $ARCH" >&2; exit 2 ;;
esac

# a árvore instalada, igual para .deb e .rpm
raiz() {
  local R="$1"
  install -Dm755 "$EXE" "$R/opt/libertyx-player/LibertyXPlayer"
  install -Dm755 "$AQUI/libertyx-player.sh" "$R/usr/bin/libertyx-player"
  install -Dm644 "$AQUI/libertyx-player.desktop" "$R/usr/share/applications/libertyx-player.desktop"
  install -Dm644 "$AQUI/io.github.fxmx182.libertyx-player.metainfo.xml" "$R/usr/share/metainfo/io.github.fxmx182.libertyx-player.metainfo.xml"
  for s in 16 24 32 48 64 128 256 512; do
    install -Dm644 "$AQUI/icons/$s.png" "$R/usr/share/icons/hicolor/${s}x${s}/apps/libertyx-player.png"
  done
}

# ── tar.gz ──
T="$TMP/tar/libertyx-player"
install -Dm755 "$EXE" "$T/LibertyXPlayer"
install -Dm644 "$AQUI/icons/256.png" "$T/libertyx-player.png"
cat > "$T/LEIA-ME.txt" <<TXT
LibertyX Player $VER para Linux ($ARCH)

Abrir:   ./LibertyXPlayer   (ou ./LibertyXPlayer video.mkv)
Precisa do libmpv da sua distribuição:
  sudo apt install libmpv2        # Debian, Ubuntu, Mint
  sudo dnf install mpv-libs       # Fedora
  sudo pacman -S mpv              # Arch, Manjaro
  sudo zypper install libmpv2     # openSUSE
Instalar de verdade (menu, "Abrir com" nos vídeos): prefira o .deb ou o .rpm em
https://github.com/fxmx182/libertyx-player-desktop/releases/latest
TXT
tar -C "$TMP/tar" -czf "$OUT/LibertyXPlayer-linux${SUF}.tar.gz" libertyx-player

# ── .deb ──
D="$TMP/deb"
raiz "$D"
SIZE=$(du -sk "$D" | cut -f1)
mkdir -p "$D/DEBIAN"
cat > "$D/DEBIAN/control" <<CTL
Package: libertyx-player
Version: $VER
Architecture: $DEB_ARCH
Maintainer: LibertyX <fxmx182@users.noreply.github.com>
Installed-Size: $SIZE
Section: video
Priority: optional
Homepage: https://fxmx182.github.io/#/player
Depends: libc6, libmpv2 | libmpv1, libfontconfig1, libx11-6, libice6, libsm6, libgl1
Recommends: xdg-utils, avahi-utils
Suggests: yt-dlp
Description: Player de vídeo com biblioteca e pastas da rede
 O LibertyX Player do Android e do iPhone, no desktop: biblioteca das pastas de
 vídeo com capas e "continuar assistindo", rolagem quadro a quadro arrastando o
 vídeo, legendas e faixas de áudio, e pastas da rede (SMB) tocadas direto do PC
 ou do NAS, sem copiar.
CTL
cat > "$D/DEBIAN/postinst" <<'SH'
#!/bin/sh
set -e
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
exit 0
SH
cp "$D/DEBIAN/postinst" "$D/DEBIAN/postrm"
chmod 755 "$D/DEBIAN/postinst" "$D/DEBIAN/postrm"
dpkg-deb --root-owner-group -Zxz --build "$D" "$OUT/libertyx-player_${DEB_ARCH}.deb" >/dev/null

# ── .rpm ──
if command -v rpmbuild >/dev/null; then
  R="$TMP/rpm"
  mkdir -p "$R"/{BUILD,RPMS,SOURCES,SPECS,SRPMS,BUILDROOT}
  raiz "$TMP/rpmroot"
  cat > "$R/SPECS/libertyx-player.spec" <<SPEC
Name:           libertyx-player
Version:        $VER
Release:        1
Summary:        Player de vídeo com biblioteca e pastas da rede
License:        Proprietary
URL:            https://fxmx182.github.io/#/player
BuildArch:      $RPM_ARCH
# Fedora chama o libmpv de mpv-libs; o openSUSE, de libmpv2
Requires:       (mpv-libs or libmpv2)
Requires:       fontconfig libX11 libICE libSM
Recommends:     xdg-utils avahi-tools
AutoReqProv:    no
%define __strip /bin/true
%define debug_package %{nil}
%define _build_id_links none

%description
O LibertyX Player do Android e do iPhone, no desktop: biblioteca das pastas de vídeo,
rolagem quadro a quadro, legendas e faixas de áudio, e pastas da rede (SMB) sem copiar.

%install
cp -a "$TMP/rpmroot/." %{buildroot}/

%post
update-desktop-database -q /usr/share/applications >/dev/null 2>&1 || :
gtk-update-icon-cache -q -t /usr/share/icons/hicolor >/dev/null 2>&1 || :

%files
/opt/libertyx-player
/usr/bin/libertyx-player
/usr/share/applications/libertyx-player.desktop
/usr/share/metainfo/io.github.fxmx182.libertyx-player.metainfo.xml
/usr/share/icons/hicolor/*/apps/libertyx-player.png
SPEC
  rpmbuild --quiet --define "_topdir $R" --target "$RPM_ARCH" -bb "$R/SPECS/libertyx-player.spec"
  cp "$R"/RPMS/*/libertyx-player-*.rpm "$OUT/libertyx-player.${RPM_ARCH}.rpm"
fi

# ── AppImage ──
if [ -n "$LIBMPV" ]; then
  A="$TMP/LibertyXPlayer.AppDir"
  install -Dm755 "$EXE" "$A/usr/bin/LibertyXPlayer"
  install -Dm644 "$LIBMPV/libmpv.so.2" "$A/usr/lib/libmpv.so.2"
  install -Dm644 "$LIBMPV/LICENCAS-libmpv.txt" "$A/usr/share/doc/libertyx-player/LICENCAS-libmpv.txt"
  install -Dm755 "$AQUI/AppRun" "$A/AppRun"
  sed 's|^Exec=.*|Exec=LibertyXPlayer %U|' "$AQUI/libertyx-player.desktop" > "$A/libertyx-player.desktop"
  install -Dm644 "$AQUI/icons/256.png" "$A/libertyx-player.png"
  for s in 16 24 32 48 64 128 256 512; do install -Dm644 "$AQUI/icons/$s.png" "$A/usr/share/icons/hicolor/${s}x${s}/apps/libertyx-player.png"; done
  ln -s libertyx-player.png "$A/.DirIcon"
  TOOL="$TMP/appimagetool"
  curl -fsSL --retry 3 -o "$TOOL" "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-$(uname -m).AppImage"
  chmod +x "$TOOL"
  RUNTIME=()
  if [ "$AI_ARCH" != "$(uname -m)" ]; then
    curl -fsSL --retry 3 -o "$TMP/runtime" "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$AI_ARCH"
    RUNTIME=(--runtime-file "$TMP/runtime")
  fi
  ARCH="$AI_ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "${RUNTIME[@]}" "$A" "$OUT/LibertyXPlayer-$AI_ARCH.AppImage" >/dev/null
fi
ls -la "$OUT"
