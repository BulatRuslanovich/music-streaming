FROM rust:1.99-trixie AS chef
RUN cargo install cargo-chef --locked
WORKDIR /src

FROM chef AS planner
COPY . .
RUN cargo chef prepare --recipe-path recipe.json

FROM chef AS build
COPY --from=planner /src/recipe.json recipe.json
RUN cargo chef cook --release --recipe-path recipe.json

COPY . .
ENV SQLX_OFFLINE=true
RUN cargo build --release --locked --bin caimack

FROM debian:trixie-slim AS ffmpeg
ARG FFMPEG_VERSION=8.1.1
ARG FFMPEG_SHA256=b6863adde98898f42602017462871b5f6333e65aec803fdd7a6308639c52edf3
RUN apt-get update \
    && apt-get install -y --no-install-recommends build-essential ca-certificates curl nasm xz-utils \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /ffmpeg
RUN curl -fsSL --retry 5 --retry-all-errors --retry-delay 3 -o ffmpeg.tar.xz \
        "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && echo "${FFMPEG_SHA256}  ffmpeg.tar.xz" | sha256sum -c - \
    && tar -xJf ffmpeg.tar.xz --strip-components=1 \
    && rm ffmpeg.tar.xz
RUN ./configure --prefix=/opt/ffmpeg \
        --disable-everything --disable-autodetect --disable-doc --disable-debug --disable-network \
        --disable-ffplay --disable-ffprobe --disable-avdevice --disable-swscale \
        --enable-protocol=file,pipe \
        --enable-demuxer=mp3,flac,mov \
        --enable-parser=mpegaudio,flac,aac \
        --enable-decoder=mp3,mp3float,flac,aac \
        --enable-encoder=aac \
        --enable-muxer=hls,mp4,flac \
        --enable-filter=abuffer,abuffersink,aformat,anull,aresample,atrim \
    && make -j"$(nproc)" \
    && make install \
    && strip /opt/ffmpeg/bin/ffmpeg \
    && mkdir -p /out/storage/music /out/storage/covers

FROM gcr.io/distroless/cc-debian13:nonroot AS runtime

COPY --from=ffmpeg --chown=nonroot:nonroot /out/storage /storage
COPY --from=ffmpeg /opt/ffmpeg/bin/ffmpeg /usr/local/bin/ffmpeg
COPY --from=build /src/target/release/caimack /usr/local/bin/caimack

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=4s --start-period=20s --retries=5 \
    CMD ["caimack", "healthcheck"]

ENTRYPOINT ["caimack"]
