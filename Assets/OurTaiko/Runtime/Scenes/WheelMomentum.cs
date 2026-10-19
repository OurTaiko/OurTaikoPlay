using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // The motion of a swiped wheel, in rows and seconds: the finger drags it, a flick leaves it
    // gliding under friction, a flick during a glide adds its speed to the glide's (a reverse flick
    // brakes or turns it), and a press during a glide holds it so a tap stops it. Offset is how far
    // the wheel has moved past the focused row; the owner takes whole rows off it with Shift.
    public sealed class WheelMomentum
    {
        // Release speeds below FlickSpeed are a let go, not a flick; a glide ends below StopSpeed.
        public const double FlickSpeed = 3, StopSpeed = 1.5, MaxSpeed = 60;
        // Deceleration: Friction per second of the speed plus a constant Brake in rows/s².
        public const double Friction = 2.2, Brake = 3;
        // The release speed is the finger's over its last SampleWindow seconds.
        const double SampleWindow = 0.1, MinSpan = 1 / 60.0;

        readonly List<(double time, double travel)> samples = new List<(double, double)>();
        double travel, carry, lastTick = double.NaN;

        public double Offset { get; private set; }
        public double Velocity { get; private set; }
        public bool Dragging { get; private set; }
        // A finger is on a wheel that was gliding (it has not started dragging yet).
        public bool Holding { get; private set; }
        public bool Gliding => Velocity != 0;
        public bool Active => Dragging || Holding || Gliding;

        // A finger goes down: a glide stops under it and keeps its speed for the next flick.
        public void Press()
        {
            if (Gliding) { carry = Velocity; Velocity = 0; Holding = true; }
            else if (!Dragging) carry = 0;
        }

        // The finger came up without dragging: the wheel stays where the tap stopped it.
        public void Release()
        {
            Holding = false; carry = 0;
        }

        public void BeginDrag(double time)
        {
            if (Gliding) { carry = Velocity; Velocity = 0; }
            Dragging = true; Holding = false;
            travel = 0;
            samples.Clear();
            samples.Add((time, 0));
        }

        public void Drag(double rows, double time)
        {
            if (!Dragging) return;
            Offset += rows; travel += rows;
            samples.Add((time, travel));
            while (samples.Count > 2 && samples[1].time <= time - SampleWindow) samples.RemoveAt(0);
        }

        public void EndDrag(double time)
        {
            if (!Dragging) return;
            Dragging = false;
            double release = ReleaseSpeed(time);
            Velocity = Math.Abs(release) >= FlickSpeed ? Clamp(release + carry) : 0;
            carry = 0;
            lastTick = time;
        }

        // Advances a glide to `time`.
        public void Tick(double time)
        {
            double dt = double.IsNaN(lastTick) ? 0 : Math.Max(0, time - lastTick);
            lastTick = time;
            if (!Gliding || dt <= 0) return;
            double speed = Math.Abs(Velocity), sign = Math.Sign(Velocity);
            double slowed = speed - (Friction * speed + Brake) * dt;
            Offset += sign * (speed + Math.Max(slowed, 0)) / 2 * dt;
            Velocity = slowed < StopSpeed ? 0 : sign * slowed;
        }

        public void Shift(int rows) => Offset -= rows;

        public void Stop()
        {
            Dragging = Holding = false;
            Velocity = carry = Offset = 0;
            samples.Clear();
        }

        double ReleaseSpeed(double time)
        {
            if (samples.Count == 0) return 0;
            // The newest sample at least a window old (or the first), to now.
            var from = samples[0];
            foreach (var sample in samples) if (sample.time <= time - SampleWindow) from = sample;
            double span = Math.Max(time - from.time, MinSpan);
            return (travel - from.travel) / span;
        }

        static double Clamp(double v) => Math.Max(-MaxSpeed, Math.Min(MaxSpeed, v));
    }
}
