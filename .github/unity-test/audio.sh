export PULSE_SERVER=unix:/run/pulse/native
if ! pactl info >/dev/null 2>&1; then
  pulseaudio --system --daemonize=yes --exit-idle-time=-1 --disallow-exit --disallow-module-loading -n \
    --load="module-native-protocol-unix socket=/run/pulse/native auth-group=pulse-access" \
    --load="module-null-sink sink_name=unity_ci rate=48000" || exit 1
  pactl info >/dev/null || exit 1
fi
