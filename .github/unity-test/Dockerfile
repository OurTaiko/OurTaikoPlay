ARG editor
FROM ${editor}
RUN apt-get update && apt-get install -y --no-install-recommends libsharpyuv0 pulseaudio pulseaudio-utils libasound2-plugins && rm -rf /var/lib/apt/lists/*
COPY asound.conf /etc/asound.conf
COPY audio.sh /usr/bin/unity-editor.d/test-audio.sh
