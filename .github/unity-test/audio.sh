if ! pactl info >/dev/null 2>&1; then
  pulseaudio --start --exit-idle-time=-1 --disallow-exit --load="module-null-sink sink_name=unity_ci rate=48000" || exit 1
  pactl set-default-sink unity_ci || exit 1
fi
