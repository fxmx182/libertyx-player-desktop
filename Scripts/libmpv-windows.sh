#!/usr/bin/env bash
# Compila o libmpv-2.dll do Windows (x64 ou ARM64) em LGPL, a partir do Linux, com o llvm-mingw.
#
#   Scripts/libmpv-windows.sh <x64|arm64> <pasta de saída>
#
# Por que não uma build pronta: as do mpv para Windows saem em GPL (x264, rubberband, …), e o
# Player é vendido. Aqui tudo é LGPL ou mais livre — mpv com -Dgpl=false, FFmpeg sem
# --enable-gpl — e o FFmpeg entra estático dentro do libmpv-2.dll: quem quiser troca a DLL
# inteira por outra compilada com este mesmo script (é o que a LGPL pede).
#
# Precisa: meson (>= 1.3), ninja, nasm, pkg-config, python3 com jinja2, git, curl, xz, make.
# WORK (padrão ./libmpv-build) guarda fontes e o llvm-mingw entre uma arquitetura e outra.
set -euo pipefail

ARCH=${1:?arquitetura: x64 ou arm64}
SAIDA=$(realpath -m "${2:?pasta de saída}")
WORK=$(realpath -m "${WORK:-libmpv-build}")
JOBS=${JOBS:-$(nproc)}

LLVM_MINGW=20261006
ZLIB=1.3.1
FREETYPE=2.14.3
FRIBIDI=1.0.16
HARFBUZZ=14.6.0
LIBASS=0.17.5
DAV1D=1.5.3
FFMPEG=8.1.3
LIBPLACEBO=v7.360.1
MPV=v0.41.0

case $ARCH in
  x64) T=x86_64-w64-mingw32; CPU=x86_64 ;;
  arm64) T=aarch64-w64-mingw32; CPU=aarch64 ;;
  *) echo "arquitetura desconhecida: $ARCH" >&2; exit 2 ;;
esac

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

# --- toolchain -----------------------------------------------------------------------------
TC=$WORK/llvm-mingw-$LLVM_MINGW
if [ ! -x "$TC/bin/$T-clang" ]; then
  baixar "https://github.com/mstorsjo/llvm-mingw/releases/download/$LLVM_MINGW/llvm-mingw-$LLVM_MINGW-ucrt-ubuntu-22.04-x86_64.tar.xz" llvm-mingw.tar.xz
  rm -rf "$TC"; mkdir -p "$TC"
  tar -xf "$SRC/llvm-mingw.tar.xz" -C "$TC" --strip-components=1
fi
export PATH=$TC/bin:$PATH
export PKG_CONFIG_LIBDIR=$P/lib/pkgconfig PKG_CONFIG_PATH=
CROSS=$B/cross.txt
cat > "$CROSS" <<EOF
[binaries]
c = '$T-clang'
cpp = '$T-clang++'
ar = '$T-ar'
strip = '$T-strip'
windres = '$T-windres'
pkg-config = 'pkg-config'
nasm = 'nasm'

[host_machine]
system = 'windows'
cpu_family = '$CPU'
cpu = '$CPU'
endian = 'little'

[properties]
pkg_config_libdir = '$P/lib/pkgconfig'
EOF

meson_lib() { # pasta [opções…]
  local d=$1; shift
  meson setup "$B/$d/_b" "$B/$d" --cross-file "$CROSS" --prefix "$P" --libdir lib \
    --buildtype release -Ddefault_library=static -Db_ndebug=true "$@" >"$B/$d.log" 2>&1 \
    || { tail -40 "$B/$d.log"; exit 1; }
  ninja -C "$B/$d/_b" -j "$JOBS" install >>"$B/$d.log" 2>&1 || { tail -40 "$B/$d.log"; exit 1; }
  echo "ok: $d"
}

# --- dependências --------------------------------------------------------------------------
baixar "https://github.com/madler/zlib/releases/download/v$ZLIB/zlib-$ZLIB.tar.xz" zlib.tar.xz
extrair zlib.tar.xz zlib
make -C "$B/zlib" -f win32/Makefile.gcc -j "$JOBS" CC=$T-clang AR=$T-ar RC=$T-windres STRIP=$T-strip \
  libz.a >"$B/zlib.log" 2>&1 || { tail -20 "$B/zlib.log"; exit 1; }
mkdir -p "$P/include" "$P/lib/pkgconfig"
cp "$B/zlib/zlib.h" "$B/zlib/zconf.h" "$P/include/" && cp "$B/zlib/libz.a" "$P/lib/"
printf 'prefix=%s\nName: zlib\nDescription: zlib\nVersion: %s\nLibs: -L${prefix}/lib -lz\nCflags: -I${prefix}/include\n' \
  "$P" "$ZLIB" > "$P/lib/pkgconfig/zlib.pc"
echo "ok: zlib"

baixar "https://download.savannah.gnu.org/releases/freetype/freetype-$FREETYPE.tar.xz" freetype.tar.xz
extrair freetype.tar.xz freetype
meson_lib freetype -Dzlib=disabled -Dbzip2=disabled -Dpng=disabled -Dharfbuzz=disabled -Dbrotli=disabled -Dtests=disabled

baixar "https://github.com/fribidi/fribidi/releases/download/v$FRIBIDI/fribidi-$FRIBIDI.tar.xz" fribidi.tar.xz
extrair fribidi.tar.xz fribidi
meson_lib fribidi -Ddocs=false -Dbin=false -Dtests=false

baixar "https://github.com/harfbuzz/harfbuzz/releases/download/$HARFBUZZ/harfbuzz-$HARFBUZZ.tar.xz" harfbuzz.tar.xz
extrair harfbuzz.tar.xz harfbuzz
meson_lib harfbuzz -Dfreetype=enabled -Dglib=disabled -Dgobject=disabled -Dcairo=disabled -Dchafa=disabled \
  -Dicu=disabled -Dgraphite2=disabled -Dpng=disabled -Dzlib=disabled -Dgdi=disabled -Ddirectwrite=disabled \
  -Dtests=disabled -Dintrospection=disabled -Ddocs=disabled -Dutilities=disabled -Dbenchmark=disabled \
  -Dsubset=disabled -Draster=disabled -Dvector=disabled -Dgpu=disabled -Dfontations=disabled -Dharfrust=disabled

baixar "https://github.com/libass/libass/releases/download/$LIBASS/libass-$LIBASS.tar.xz" libass.tar.xz
extrair libass.tar.xz libass
meson_lib libass -Dfontconfig=disabled -Ddirectwrite=enabled -Dcoretext=disabled -Dlibunibreak=disabled \
  -Dtest=disabled -Dcompare=disabled -Dprofile=disabled -Dfuzz=disabled -Dcheckasm=disabled

baixar "https://code.videolan.org/videolan/dav1d/-/archive/$DAV1D/dav1d-$DAV1D.tar.bz2" dav1d.tar.bz2
extrair dav1d.tar.bz2 dav1d
meson_lib dav1d -Denable_tools=false -Denable_tests=false -Denable_examples=false

# FFmpeg em LGPL 2.1: sem --enable-gpl, sem --enable-nonfree. Codificadores só os das capturas.
baixar "https://ffmpeg.org/releases/ffmpeg-$FFMPEG.tar.xz" ffmpeg.tar.xz
extrair ffmpeg.tar.xz ffmpeg
(
  cd "$B/ffmpeg"
  ./configure --prefix="$P" --enable-cross-compile --target-os=mingw32 --arch=$CPU \
    --cross-prefix=$T- --cc=$T-clang --cxx=$T-clang++ --pkg-config=pkg-config --pkg-config-flags=--static \
    --enable-static --disable-shared --disable-programs --disable-doc --disable-debug --disable-avdevice \
    --disable-encoders --enable-encoder=png,mjpeg --enable-libdav1d --enable-zlib --enable-schannel \
    --enable-d3d11va --enable-dxva2 --enable-mediafoundation --disable-vulkan \
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

# --- mpv -----------------------------------------------------------------------------------
baixar "https://github.com/mpv-player/mpv/archive/refs/tags/$MPV.tar.gz" mpv.tar.gz
extrair mpv.tar.gz mpv
meson setup "$B/mpv/_b" "$B/mpv" --cross-file "$CROSS" --prefix "$P" --libdir lib --buildtype release \
  -Ddefault_library=shared -Dprefer_static=true -Db_ndebug=true -Dc_link_args=-static \
  -Dgpl=false -Dlibmpv=true -Dcplayer=false -Dtests=false -Dfuzzers=false -Dbuild-date=false \
  -Dlua=disabled -Djavascript=disabled -Dlibarchive=disabled -Duchardet=disabled -Drubberband=disabled \
  -Dvapoursynth=disabled -Dzimg=disabled -Dlcms2=disabled -Djpeg=disabled -Dlibbluray=disabled \
  -Ddvdnav=disabled -Dcdda=disabled -Dlibavdevice=disabled -Dsdl2-audio=disabled -Dsdl2-video=disabled \
  -Dsdl2-gamepad=disabled -Dopenal=disabled -Dvulkan=disabled -Dshaderc=disabled -Dspirv-cross=disabled \
  -Dd3d11=disabled -Dgl=enabled -Dgl-win32=enabled -Dwasapi=enabled -Dwin32-smtc=disabled \
  -Dmanpage-build=disabled -Dhtml-build=disabled -Dpdf-build=disabled >"$B/mpv.log" 2>&1 \
  || { tail -60 "$B/mpv.log"; exit 1; }
ninja -C "$B/mpv/_b" -j "$JOBS" >>"$B/mpv.log" 2>&1 || { tail -60 "$B/mpv.log"; exit 1; }
echo "ok: mpv"

DLL=$(find "$B/mpv/_b" -maxdepth 1 -name 'libmpv-2.dll' | head -1)
[ -n "$DLL" ] || { echo "libmpv-2.dll não apareceu" >&2; exit 1; }
$T-strip -o "$SAIDA/libmpv-2.dll" "$DLL"

# A DLL só pode depender do que o Windows já tem (nada de libc++.dll, libwinpthread-1.dll…).
DEPS=$($T-objdump -p "$SAIDA/libmpv-2.dll" | awk '/DLL Name:/ {print tolower($3)}' | sort -u)
echo "$DEPS" | sed 's/^/  importa: /'
FORA=$(echo "$DEPS" | grep -vE '^(api-ms-win-.*|kernel32|user32|gdi32|advapi32|shell32|ole32|oleaut32|ws2_32|bcrypt|crypt32|secur32|ncrypt|winmm|avrt|mfplat|mfuuid|mf|strmiids|dwrite|d3d11|dxgi|d3d9|dxva2|opengl32|comdlg32|uuid|version|shlwapi|imm32|ntdll|psapi|propsys|pathcch|mfreadwrite|evr|iphlpapi|dwmapi|uxtheme|shcore|setupapi|cfgmgr32|hid|winhttp|wininet|normaliz|rpcrt4|powrprof|ucrtbase)\.dll$' || true)
[ -z "$FORA" ] || { echo "a DLL depende de bibliotecas que não vêm no Windows: $FORA" >&2; exit 1; }

cat > "$SAIDA/LICENCAS-libmpv.txt" <<EOF
libmpv-2.dll — mpv $MPV (LGPL 2.1+, compilado com -Dgpl=false) com, embutidos:
  FFmpeg $FFMPEG (LGPL 2.1+), libplacebo $LIBPLACEBO (LGPL 2.1+), libass $LIBASS (ISC),
  FreeType $FREETYPE (FTL), FriBidi $FRIBIDI (LGPL 2.1+), HarfBuzz $HARFBUZZ (MIT),
  dav1d $DAV1D (BSD-2), zlib $ZLIB (zlib).
Compilado por Scripts/libmpv-windows.sh em https://github.com/fxmx182/libertyx-player-desktop
com o llvm-mingw $LLVM_MINGW. Os fontes são os das versões acima, sem modificação; para trocar a
biblioteca, substitua este arquivo por outro libmpv-2.dll compatível.
EOF
ls -la "$SAIDA"
