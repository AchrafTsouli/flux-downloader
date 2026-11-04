#!/usr/bin/env bash
set -euo pipefail
export PATH="/usr/bin:$PATH"
source_dir="$(cygpath -u "$1")"
toolchain="$(cygpath -u "$2")"
build_dir="$(cygpath -u "$3")"
prefix="$(cygpath -u "$4")"
export PATH="$toolchain/bin:$PATH"
# FFmpeg 8.1.2's MOV channel-layout reader drops this return value and then
# returns an uninitialized variable. Retain the actual parser status instead.
sed -i 's/^    ff_mov_read_chnl(c->fc, pb, st);$/    ret = ff_mov_read_chnl(c->fc, pb, st);/' "$source_dir/libavformat/mov.c"
mkdir -p "$build_dir" "$prefix"
cd "$build_dir"
"$source_dir/configure" \
  --prefix="$prefix" --arch=x86_64 --target-os=mingw32 --enable-cross-compile \
  --cc=x86_64-w64-mingw32-clang --cxx=x86_64-w64-mingw32-clang++ \
  --ar=llvm-ar --nm=llvm-nm --ranlib=llvm-ranlib --strip=llvm-strip \
  --disable-autodetect --disable-x86asm --disable-debug --disable-doc \
  --disable-gpl --disable-nonfree --disable-version3 \
  --enable-shared --disable-static --enable-small --extra-cflags=-Os \
  --disable-everything --enable-ffmpeg --enable-ffprobe --disable-ffplay \
  --disable-avdevice --disable-swscale --disable-swresample \
  --enable-avfilter --enable-filter=anull,null \
  --enable-network --enable-schannel \
  --enable-protocol=file,pipe,http,https,tcp,tls,crypto,data,concat,udp \
  --enable-demuxer=mov,matroska,mpegts,aac,ogg,mp3,flac,hls,concat \
  --enable-decoder=aac,aac_latm,vp9 \
  --enable-muxer=mp4,mov,ipod,matroska,webm,adts,ogg,mpegts \
  --enable-parsers --enable-bsfs
make -j4
make install
