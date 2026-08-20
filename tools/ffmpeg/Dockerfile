FROM ubuntu@sha256:33ceb71981b602c1a7443a53469e4dba065f7503eab3078a2d7a57a2ab987517

ARG DEBIAN_FRONTEND=noninteractive
ARG FFMPEG_REVISION=9d4ca21220
ARG FFMPEG_ARCHIVE_SHA256=6d9f8e49d1b6c561b6abb007fa872e844be181fb2516fd6e77741d0dd838bfa4

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        binutils-mingw-w64-x86-64=2.41.90.20240122-1ubuntu1+11.4 \
        ca-certificates=20260601~24.04.1 \
        curl=8.5.0-2ubuntu10.12 \
        gcc=4:13.2.0-7ubuntu1 \
        gcc-mingw-w64-x86-64=13.2.0-6ubuntu1+26.1 \
        libc6-dev=2.39-0ubuntu8.8 \
        make=4.3-4.1build2 \
        nasm=2.16.01-1build1 \
        pkg-config=1.8.1-2build1 \
        xz-utils=5.6.1+really5.4.5-1ubuntu0.3 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /build
RUN curl --fail --location --retry 3 \
        "https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/${FFMPEG_REVISION}" \
        --output ffmpeg.tar.gz \
    && echo "${FFMPEG_ARCHIVE_SHA256}  ffmpeg.tar.gz" | sha256sum --check --strict \
    && mkdir source \
    && tar --extract --gzip --file ffmpeg.tar.gz --directory source --strip-components=1 \
    && rm ffmpeg.tar.gz

COPY build-minimal-win64.sh /usr/local/bin/build-aura-ffmpeg
RUN chmod 0755 /usr/local/bin/build-aura-ffmpeg

ENTRYPOINT ["/usr/local/bin/build-aura-ffmpeg"]
