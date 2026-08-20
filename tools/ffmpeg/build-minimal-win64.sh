#!/usr/bin/env bash
set -euo pipefail

source_root=/build/source
build_root=/build/output
install_root=/build/install
output_root=/out

export LC_ALL=C
export SOURCE_DATE_EPOCH=1786990956
export TZ=UTC
export ZERO_AR_DATE=1

rm -rf "${build_root}" "${install_root}"
mkdir -p "${build_root}" "${install_root}" "${output_root}"
find "${output_root}" -mindepth 1 -maxdepth 1 -delete

cd "${build_root}"
"${source_root}/configure" \
    --prefix="${install_root}" \
    --arch=x86_64 \
    --target-os=mingw32 \
    --cross-prefix=x86_64-w64-mingw32- \
    --enable-cross-compile \
    --enable-version3 \
    --disable-everything \
    --disable-autodetect \
    --disable-network \
    --disable-debug \
    --disable-doc \
    --disable-static \
    --enable-shared \
    --enable-small \
    --disable-avdevice \
    --disable-devices \
    --disable-hwaccels \
    --disable-iconv \
    --disable-zlib \
    --disable-bzlib \
    --disable-lzma \
    --disable-schannel \
    --disable-pthreads \
    --enable-w32threads \
    --enable-ffmpeg \
    --enable-ffprobe \
    --disable-ffplay \
    --enable-protocol=file \
    --enable-protocol=pipe \
    --enable-demuxer=avi \
    --enable-demuxer=matroska \
    --enable-demuxer=mov \
    --enable-demuxer=rawvideo \
    --enable-demuxer=wav \
    --enable-muxer=mov \
    --enable-muxer=mp4 \
    --enable-muxer=null \
    --enable-encoder=aac \
    --enable-encoder=mpeg4 \
    --enable-encoder=pcm_s16le \
    --enable-encoder=wrapped_avframe \
    --enable-decoder=aac \
    --enable-decoder=alac \
    --enable-decoder=av1 \
    --enable-decoder=flac \
    --enable-decoder=h264 \
    --enable-decoder=hevc \
    --enable-decoder=mjpeg \
    --enable-decoder=mp3 \
    --enable-decoder=mp3float \
    --enable-decoder=mpeg4 \
    --enable-decoder=opus \
    --enable-decoder=pcm_f32le \
    --enable-decoder=pcm_s16le \
    --enable-decoder=pcm_s24le \
    --enable-decoder=pcm_s32le \
    --enable-decoder=rawvideo \
    --enable-decoder=vorbis \
    --enable-decoder=vp8 \
    --enable-decoder=vp9 \
    --enable-parser=aac \
    --enable-parser=aac_latm \
    --enable-parser=av1 \
    --enable-parser=h264 \
    --enable-parser=hevc \
    --enable-parser=mjpeg \
    --enable-parser=mpegaudio \
    --enable-parser=mpeg4video \
    --enable-parser=opus \
    --enable-parser=vorbis \
    --enable-parser=vp8 \
    --enable-parser=vp9 \
    --enable-filter=aformat \
    --enable-filter=aresample \
    --enable-filter=format \
    --enable-filter=fps \
    --enable-filter=scale \
    --enable-filter=setparams \
    --enable-filter=vflip \
    --extra-version=aura-minimal.1 \
    --extra-ldflags=-static-libgcc

make -s -j"$(nproc)"
make -s install

find "${install_root}/bin" -maxdepth 1 -type f \
    \( -name '*.exe' -o -name '*.dll' \) -print0 \
    | xargs -0 x86_64-w64-mingw32-strip --strip-unneeded
find "${install_root}/bin" -maxdepth 1 -type f \
    \( -name 'ffmpeg.exe' -o -name 'ffprobe.exe' -o -name 'av*.dll' \
       -o -name 'swresample-*.dll' -o -name 'swscale-*.dll' \) \
    -exec cp --target-directory="${output_root}" {} +
cp "${source_root}/COPYING.LGPLv3" "${output_root}/LICENSE.txt"
