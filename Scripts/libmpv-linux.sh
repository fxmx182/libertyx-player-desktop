#!/usr/bin/env bash
# Compila o libmpv.so.2 do AppImage (x64 ou ARM64, na arquitetura da máquina) em LGPL.
#
#   Scripts/libmpv-linux.sh <pasta de saída>
#
# Por que não o libmpv do Ubuntu: ele sai em GPL (x264, rubberband…) e puxa dezenas de .so da
# distribuição — o AppImage precisa abrir em qualquer Linux e o Player é vendido. Aqui é o mesmo
# caminho do Scripts/libmpv-windows.sh: mpv com -Dgpl=false, FFmpeg sem --enable-gpl, tudo
# estático dentro do libmpv.so.2 (símbolos escondidos com --exclude-libs, para não brigar com as
# bibliotecas do sistema que o app também carrega). Quem quiser troca o arquivo inteiro por outro
# compilado com este script (é o que a LGPL pede).
#
# Roda dentro de um Ubuntu 22.04 (glibc 2.35 — Ubuntu 22.04+, Debian 12+, Fedora 36+, Mint 21+): o
# que é compilado numa glibc velha abre nas novas, e não o contrário. O CI chama:
#   docker run --rm -v "$PWD:/w" -w /w ubuntu:22.04 Scripts/libmpv-linux.sh saida
# Como root num Debian/Ubuntu, instala sozinho o que precisa (LIBMPV_DEPS=0 pula).
#
# Fica de fora do .so (vem do sistema, existe em qualquer desktop): glibc, libstdc++, ALSA,
# PulseAudio (o PipeWire atende pelo mesmo cliente), fontconfig e GnuTLS (https).
# Aceleração por hardware: NVDEC (o FFmpeg abre o driver da NVIDIA só se existir) e VAAPI (libva
# estático, procura os drivers nas pastas das distribuições conhecidas).
set -euo pipefail

SAIDA=$(realpath -m "${1:?pasta de saída}")
WORK=$(realpath -m "${WORK:-libmpv-build}")
JOBS=${JOBS:-$(nproc)}

ZLIB=1.3.1
FREETYPE=2.14.3
FRIBIDI=1.0.16
HARFBUZZ=14.6.0
LIBASS=0.17.5
DAV1D=1.5.3
LIBDRM=2.4.124
LIBVA=2.22.0
DISPLAYINFO=0.2.0
NVCODEC=13.0.19.0
LUAJIT=a4c064539ea931274d407e1249b4bb575424a0f4
FFMPEG=8.1.3
LIBPLACEBO=v7.360.1
MPV=v0.41.0

ARCH=$(uname -m)   # x86_64 ou aarch64
case $ARCH in x86_64) TRIPLA=x86_64-linux-gnu ;; aarch64) TRIPLA=aarch64-linux-gnu ;; *) echo "arquitetura $ARCH?" >&2; exit 2 ;; esac

# --- ferramentas (o meson do Ubuntu 22.04 é velho demais: vem do pip) -------------------------------
if [ "$(id -u)" = 0 ] && command -v apt-get >/dev/null && [ "${LIBMPV_DEPS:-1}" != 0 ]; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq --no-install-recommends build-essential nasm pkg-config python3-pip python3-setuptools \
    git curl ca-certificates xz-utils bzip2 file hwdata libasound2-dev libpulse-dev libfontconfig1-dev libgnutls28-dev >/dev/null
  pip3 install -q 'meson==1.6.1' 'ninja==1.11.1.3' 'jinja2==3.1.6' 'markupsafe==3.0.2'
fi
export PATH=/usr/local/bin:$PATH

SRC=$WORK/src
P=$WORK/prefix-$ARCH
B=$WORK/build-$ARCH
mkdir -p "$SRC" "$P" "$B" "$SAIDA"
rm -rf "${B:?}"/* "${P:?}"/*

baixar() { # url arquivo
  [ -f "$SRC/$2" ] && return
  curl -fsSL --retry 3 -o "$SRC/$2.part" "$1" || { echo "falhou o download de $1" >&2; exit 1; }
  mv -f "$SRC/$2.part" "$SRC/$2"
}
extrair() { # arquivo pasta
  rm -rf "${B:?}/$2"; mkdir -p "$B/$2"
  tar -xf "$SRC/$1" -C "$B/$2" --strip-components=1
}

# Tudo que é nosso sai só em .a; o pkg-config com --static traz junto as dependências privadas.
# As do sistema (fontconfig, gnutls…) continuam .so e sem --static (as privadas delas nem estão
# instaladas): o --static só vale quando todo pacote pedido é daqui.
mkdir -p "$P/bin"
cat > "$P/bin/pkg-config-static" <<EOF
#!/bin/sh
for a in "\$@"; do
  case "\$a" in
    -*|*[\<\>=]*|[0-9]*) ;;
    *) [ -f "$P/lib/pkgconfig/\$a.pc" ] || [ -f "$P/share/pkgconfig/\$a.pc" ] || exec pkg-config "\$@" ;;
  esac
done
exec pkg-config --static "\$@"
EOF
chmod +x "$P/bin/pkg-config-static"
export PKG_CONFIG_PATH=$P/lib/pkgconfig:$P/share/pkgconfig
export CFLAGS="-O2 -fPIC" CXXFLAGS="-O2 -fPIC"
NATIVE=$B/native.txt
cat > "$NATIVE" <<EOF
[binaries]
pkg-config = '$P/bin/pkg-config-static'
EOF

meson_lib() { # pasta [opções…]
  local d=$1; shift
  meson setup "$B/$d/_b" "$B/$d" --native-file "$NATIVE" --prefix "$P" --libdir lib \
    --buildtype release -Ddefault_library=static -Db_staticpic=true -Db_ndebug=true "$@" >"$B/$d.log" 2>&1 \
    || { tail -40 "$B/$d.log"; exit 1; }
  ninja -C "$B/$d/_b" -j "$JOBS" install >>"$B/$d.log" 2>&1 || { tail -40 "$B/$d.log"; exit 1; }
  echo "ok: $d"
}

# --- dependências --------------------------------------------------------------------------
baixar "https://github.com/madler/zlib/releases/download/v$ZLIB/zlib-$ZLIB.tar.xz" zlib.tar.xz
extrair zlib.tar.xz zlib
( cd "$B/zlib" && ./configure --prefix="$P" --static >"$B/zlib.log" 2>&1 && make -j "$JOBS" install >>"$B/zlib.log" 2>&1 ) \
  || { tail -20 "$B/zlib.log"; exit 1; }
echo "ok: zlib"

baixar "https://downloads.sourceforge.net/project/freetype/freetype2/$FREETYPE/freetype-$FREETYPE.tar.xz" freetype.tar.xz
extrair freetype.tar.xz freetype
meson_lib freetype -Dzlib=enabled -Dbzip2=disabled -Dpng=disabled -Dharfbuzz=disabled -Dbrotli=disabled -Dtests=disabled

baixar "https://github.com/fribidi/fribidi/releases/download/v$FRIBIDI/fribidi-$FRIBIDI.tar.xz" fribidi.tar.xz
extrair fribidi.tar.xz fribidi
meson_lib fribidi -Ddocs=false -Dbin=false -Dtests=false

baixar "https://github.com/harfbuzz/harfbuzz/releases/download/$HARFBUZZ/harfbuzz-$HARFBUZZ.tar.xz" harfbuzz.tar.xz
extrair harfbuzz.tar.xz harfbuzz
meson_lib harfbuzz -Dfreetype=enabled -Dglib=disabled -Dgobject=disabled -Dcairo=disabled -Dchafa=disabled \
  -Dicu=disabled -Dgraphite2=disabled -Dpng=disabled -Dzlib=disabled \
  -Dtests=disabled -Dintrospection=disabled -Ddocs=disabled -Dutilities=disabled -Dbenchmark=disabled \
  -Dsubset=disabled -Draster=disabled -Dvector=disabled -Dgpu=disabled -Dfontations=disabled -Dharfrust=disabled

# libass acha as fontes do sistema pelo fontconfig do sistema (o .so, não entra no libmpv)
baixar "https://github.com/libass/libass/releases/download/$LIBASS/libass-$LIBASS.tar.xz" libass.tar.xz
extrair libass.tar.xz libass
meson_lib libass -Dfontconfig=enabled -Ddirectwrite=disabled -Dcoretext=disabled -Dlibunibreak=disabled \
  -Dtest=disabled -Dcompare=disabled -Dprofile=disabled -Dfuzz=disabled -Dcheckasm=disabled

baixar "https://code.videolan.org/videolan/dav1d/-/archive/$DAV1D/dav1d-$DAV1D.tar.bz2" dav1d.tar.bz2
extrair dav1d.tar.bz2 dav1d
meson_lib dav1d -Denable_tools=false -Denable_tests=false -Denable_examples=false

# libdrm e libva estáticos: um libva velho do sistema não carrega driver novo, e um .so nosso
# poderia brigar com o do Mesa. Os drivers (*_drv_video.so) continuam sendo os do sistema.
baixar "https://dri.freedesktop.org/libdrm/libdrm-$LIBDRM.tar.xz" libdrm.tar.xz
extrair libdrm.tar.xz libdrm
meson_lib libdrm -Dintel=disabled -Dradeon=disabled -Damdgpu=disabled -Dnouveau=disabled -Dvmwgfx=disabled \
  -Domap=disabled -Dexynos=disabled -Dfreedreno=disabled -Dtegra=disabled -Dvc4=disabled -Detnaviv=disabled \
  -Dcairo-tests=disabled -Dman-pages=disabled -Dvalgrind=disabled -Dtests=false -Dinstall-test-programs=false

# o VAAPI do mpv vem junto com o suporte a DRM, que pede a libdisplay-info (lê o EDID)
baixar "https://gitlab.freedesktop.org/emersion/libdisplay-info/-/releases/$DISPLAYINFO/downloads/libdisplay-info-$DISPLAYINFO.tar.xz" displayinfo.tar.xz
extrair displayinfo.tar.xz libdisplay-info
meson_lib libdisplay-info

baixar "https://github.com/intel/libva/archive/refs/tags/$LIBVA.tar.gz" libva.tar.gz
extrair libva.tar.gz libva
sed -i 's/shared_library(/library(/' "$B/libva/va/meson.build"
DRIVERS=/usr/lib/$TRIPLA/dri:/usr/lib64/dri:/usr/lib/dri:/usr/local/lib/dri:/run/opengl-driver/lib/dri
meson_lib libva -Ddriverdir="$DRIVERS" -Dwith_x11=no -Dwith_glx=no -Dwith_wayland=no -Dwith_win32=no -Denable_docs=false

baixar "https://github.com/FFmpeg/nv-codec-headers/releases/download/n$NVCODEC/nv-codec-headers-$NVCODEC.tar.gz" nvcodec.tar.gz
extrair nvcodec.tar.gz nvcodec
make -C "$B/nvcodec" PREFIX="$P" install >"$B/nvcodec.log" 2>&1 || { tail -20 "$B/nvcodec.log"; exit 1; }
echo "ok: nv-codec-headers"

# LuaJIT (MIT): o ytdl_hook do mpv é em Lua — sem ele, link do YouTube não abre nem com yt-dlp
baixar "https://github.com/LuaJIT/LuaJIT/archive/$LUAJIT.tar.gz" luajit.tar.gz
extrair luajit.tar.gz luajit
make -C "$B/luajit" -j "$JOBS" PREFIX="$P" BUILDMODE=static XCFLAGS=-fPIC CFLAGS="-O2 -fPIC" amalg >"$B/luajit.log" 2>&1 \
  && make -C "$B/luajit" PREFIX="$P" install >>"$B/luajit.log" 2>&1 || { tail -30 "$B/luajit.log"; exit 1; }
rm -f "$P"/lib/libluajit*.so*
echo "ok: luajit"

# FFmpeg em LGPL 2.1: sem --enable-gpl, sem --enable-nonfree. Codificadores só os das capturas.
baixar "https://ffmpeg.org/releases/ffmpeg-$FFMPEG.tar.xz" ffmpeg.tar.xz
extrair ffmpeg.tar.xz ffmpeg
(
  cd "$B/ffmpeg"
  ./configure --prefix="$P" --pkg-config="$P/bin/pkg-config-static" \
    --enable-static --disable-shared --enable-pic --disable-programs --disable-doc --disable-debug --disable-avdevice \
    --disable-autodetect --enable-iconv --enable-zlib --enable-gnutls --enable-libdav1d \
    --enable-libdrm --enable-vaapi --enable-ffnvcodec --enable-nvdec --enable-cuvid \
    --disable-encoders --enable-encoder=png,mjpeg \
    >"$B/ffmpeg.log" 2>&1 || { tail -40 "$B/ffmpeg.log"; tail -40 ffbuild/config.log; exit 1; }
  grep -q 'License: LGPL version 2.1 or later' "$B/ffmpeg.log" || { echo "FFmpeg não saiu LGPL" >&2; exit 1; }
  make -j "$JOBS" install >>"$B/ffmpeg.log" 2>&1 || { tail -40 "$B/ffmpeg.log"; exit 1; }
)
echo "ok: ffmpeg"

[ -d "$SRC/libplacebo-$LIBPLACEBO" ] || git clone -q --depth 1 --branch $LIBPLACEBO --recurse-submodules \
  --shallow-submodules https://github.com/haasn/libplacebo.git "$SRC/libplacebo-$LIBPLACEBO"
rm -rf "$B/libplacebo"; cp -a "$SRC/libplacebo-$LIBPLACEBO" "$B/libplacebo"
meson_lib libplacebo -Dvulkan=disabled -Dopengl=enabled -Dd3d11=disabled -Dglslang=disabled -Dshaderc=disabled \
  -Dlcms=disabled -Ddovi=enabled -Dlibdovi=disabled -Ddemos=false -Dtests=false -Dbench=false -Dfuzz=false \
  -Dunwind=disabled -Dxxhash=disabled
# o libplacebo tem um pedaço em C++ (to_chars) e o .pc dele não diz: sem isto o libmpv não liga
sed -i '/^Libs.private:/ s/$/ -lstdc++/' "$P/lib/pkgconfig/libplacebo.pc"
grep -q '^Libs.private:.*-lstdc++' "$P/lib/pkgconfig/libplacebo.pc" || echo 'Libs.private: -lstdc++' >> "$P/lib/pkgconfig/libplacebo.pc"

# --- mpv -----------------------------------------------------------------------------------
# Sem janela própria (x11/wayland/egl): o app entrega o OpenGL pela API de render.
baixar "https://github.com/mpv-player/mpv/archive/refs/tags/$MPV.tar.gz" mpv.tar.gz
extrair mpv.tar.gz mpv
meson setup "$B/mpv/_b" "$B/mpv" --native-file "$NATIVE" --prefix "$P" --libdir lib --buildtype release \
  -Ddefault_library=shared -Db_ndebug=true \
  -Dc_link_args="-static-libgcc -Wl,--exclude-libs,ALL -Wl,--as-needed -Wl,-z,defs" \
  -Dgpl=false -Dlibmpv=true -Dcplayer=false -Dtests=false -Dfuzzers=false -Dbuild-date=false \
  -Dlua=luajit -Djavascript=disabled -Dlibarchive=disabled -Duchardet=disabled -Drubberband=disabled \
  -Dvapoursynth=disabled -Dzimg=disabled -Dlcms2=disabled -Djpeg=disabled -Dlibbluray=disabled \
  -Ddvdnav=disabled -Dcdda=disabled -Dlibavdevice=disabled -Dsdl2-audio=disabled -Dsdl2-video=disabled \
  -Dsdl2-gamepad=disabled -Dopenal=disabled -Dvulkan=disabled -Dshaderc=disabled -Dspirv-cross=disabled \
  -Dgl=enabled -Dx11=disabled -Dwayland=disabled -Degl=disabled -Dgbm=disabled -Ddrm=enabled \
  -Dvaapi=enabled -Dvaapi-drm=enabled -Dvaapi-x11=disabled -Dvaapi-wayland=disabled -Dvdpau=disabled \
  -Dcuda-hwaccel=enabled -Dcuda-interop=enabled -Dalsa=enabled -Dpulse=enabled -Dpipewire=disabled -Djack=disabled \
  -Doss-audio=disabled -Dsndio=disabled -Dsixel=disabled -Dcaca=disabled \
  -Dmanpage-build=disabled -Dhtml-build=disabled -Dpdf-build=disabled >"$B/mpv.log" 2>&1 \
  || { tail -60 "$B/mpv.log"; exit 1; }
ninja -C "$B/mpv/_b" -j "$JOBS" >>"$B/mpv.log" 2>&1 || { tail -60 "$B/mpv.log"; exit 1; }
echo "ok: mpv"

SO=$(find "$B/mpv/_b" -maxdepth 1 -name 'libmpv.so.2.*' -type f | head -1)
[ -n "$SO" ] || { echo "libmpv.so.2 não apareceu" >&2; exit 1; }
strip --strip-unneeded -o "$SAIDA/libmpv.so.2" "$SO"

# O .so só pode depender do que todo desktop Linux já tem, e de uma glibc de no máximo 2.35.
DEPS=$(readelf -d "$SAIDA/libmpv.so.2" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p' | sort -u)
echo "$DEPS" | sed 's/^/  precisa: /'
FORA=$(echo "$DEPS" | grep -vE '^(libc\.so\.6|libm\.so\.6|libdl\.so\.2|libpthread\.so\.0|librt\.so\.1|ld-linux.*|libstdc\+\+\.so\.6|libgcc_s\.so\.1|libasound\.so\.2|libpulse\.so\.0|libfontconfig\.so\.1|libgnutls\.so\.30)$' || true)
[ -z "$FORA" ] || { echo "o libmpv depende de bibliotecas que nem todo Linux tem: $FORA" >&2; exit 1; }
GLIBC=$(objdump -T "$SAIDA/libmpv.so.2" | grep -o 'GLIBC_[0-9.]*' | sort -uV | tail -1)
echo "  glibc mínima: $GLIBC"
[ "$(printf '%s\nGLIBC_2.35\n' "$GLIBC" | sort -V | tail -1)" = GLIBC_2.35 ] || { echo "pede $GLIBC (> 2.35)" >&2; exit 1; }
EXPORTA=$(objdump -T "$SAIDA/libmpv.so.2" | awk '$2 ~ /g/ && $4 != "*UND*" {print $NF}' | grep -vc '^mpv_' || true)
[ "$EXPORTA" -eq 0 ] || echo "aviso: $EXPORTA símbolos exportados além da API mpv_*" >&2

cat > "$SAIDA/LICENCAS-libmpv.txt" <<EOF
libmpv.so.2 — mpv $MPV (LGPL 2.1+, compilado com -Dgpl=false) com, embutidos:
  FFmpeg $FFMPEG (LGPL 2.1+), libplacebo $LIBPLACEBO (LGPL 2.1+), libass $LIBASS (ISC),
  FreeType $FREETYPE (FTL), FriBidi $FRIBIDI (LGPL 2.1+), HarfBuzz $HARFBUZZ (MIT),
  dav1d $DAV1D (BSD-2), libva $LIBVA (MIT), libdrm $LIBDRM (MIT), libdisplay-info $DISPLAYINFO (MIT), LuaJIT $LUAJIT (MIT),
  nv-codec-headers $NVCODEC (MIT), zlib $ZLIB (zlib).
Do sistema: glibc, libstdc++, ALSA, PulseAudio, fontconfig e GnuTLS.
Compilado por Scripts/libmpv-linux.sh em https://github.com/fxmx182/libertyx-player-desktop
num Ubuntu 22.04 ($ARCH). Os fontes são os das versões acima; a única mudança é o libva sair como
biblioteca estática. Para trocar a biblioteca, substitua este arquivo por outro libmpv.so.2
compatível (o AppImage se extrai com --appimage-extract).
EOF
ls -la "$SAIDA"
