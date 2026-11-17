ARG UNITY_VERSION
FROM unityci/editor:ubuntu-${UNITY_VERSION}-linux-il2cpp-3

# Read WEBP_COMMIT from packages-lock.json so this stays identical to Unity's
# packaged library, rather than installing a different system libwebp version.
ARG WEBP_COMMIT
RUN curl --fail --show-error --location --proto '=https' --max-time 60 --retry 2 \
      "https://raw.githubusercontent.com/netpyoung/unity.webp/${WEBP_COMMIT}/unity_project/Assets/unity.webp/Plugins/x64/libsharpyuv.so" \
      -o /usr/local/lib/libsharpyuv.so.0 \
    && chmod 0644 /usr/local/lib/libsharpyuv.so.0 \
    && ldconfig
