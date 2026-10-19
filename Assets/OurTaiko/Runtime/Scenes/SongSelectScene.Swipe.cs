using System;
using UnityEngine;
using UnityEngine.UI;
using State = OurTaiko.SongSelectManager.State;

namespace OurTaiko
{
    // Song select: the wheel follows a swipe row by row (WheelSwipe). While it moves the boards sit
    // evenly, without the gap around the open board, and the focus walks to the board nearest the
    // centre; once it comes to rest it slides onto that board and opens it like a ka.
    public sealed partial class SongSelectScene
    {
        WheelSwipe swipe;
        // The boards are laid out by the swipe (set when it starts, cleared when it settles).
        bool swipeLayout, swipeStepping;
        double swipeStartedAt;

        public bool IsSwiping => swipeLayout;

        void BindSwipe()
        {
            // Swipes that start between boards land on the wheel itself.
            if (wheel.GetComponent<Graphic>() == null)
            {
                var catcher = wheel.gameObject.AddComponent<Image>();
                catcher.color = Color.clear;
            }
            swipe = wheel.GetComponent<WheelSwipe>();
            if (swipe == null) swipe = wheel.gameObject.AddComponent<WheelSwipe>();
            swipe.pitch = view.rowPitch;
            swipe.CanSwipe = () => Manager.Phase == State.Browsing && Manager.AcceptsInput()
                && (view.search == null || !view.search.BlocksInput);
        }

        void StepSwipe(double now)
        {
            swipe.Tick();
            var momentum = swipe.Momentum;
            if (!momentum.Active && !swipeLayout) return;
            if (!swipe.CanSwipe()) { StopSwipe(); return; }
            if (!swipeLayout)
            {
                // The boards blend from wherever they are into the swipe's layout.
                swipeLayout = true; swipeStartedAt = now;
                foreach (var board in visuals.Values)
                {
                    board.MoveFrom = board.Position; board.CrossFrom = board.Cross; board.MoveStart = -1;
                }
                var focused = FocusedBoard;
                if (focused.OpenStart >= 0) { focused.CloseStart = now; focused.OpenStart = -1; }
                StopPreview();
            }
            int rows = (int)Math.Round(momentum.Offset);
            if (rows != 0)
            {
                momentum.Shift(rows);
                swipeStepping = true;
                try { Manager.Scroll(rows); }
                finally { swipeStepping = false; }
            }
            LayOutSwipe(now, momentum.Offset);
            if (!momentum.Active) SettleSwipe();
        }

        // Navigator::set_positions without the open board's gap, shifted by the part-row offset.
        void LayOutSwipe(double now, double offset)
        {
            var items = Manager.Items;
            int count = items.Count, focused = Manager.Focused;
            double blend = SkinUi.CubicOut((now - swipeStartedAt) / MoveMs);
            for (int i = 0; i < count; i++)
            {
                double row = i - focused;
                if (row > count / 2.0) row -= count;
                else if (row < -count / 2.0) row += count;
                row -= offset;
                double position = view.wheelCentre.y + row * view.rowPitch;
                double cross = view.wheelCentre.x + row * view.rowCurve;
                var board = BoardOf(items[i]);
                board.Position = board.MoveFrom + (position - board.MoveFrom) * blend;
                board.Cross = board.CrossFrom + (cross - board.CrossFrom) * blend;
            }
        }

        void SettleSwipe()
        {
            swipeLayout = false;
            SetPositions(false, MoveMs);
            OpenFocused(OpenHoldMs);
        }

        // The swipe stops where it is and the wheel settles on the focused board.
        void StopSwipe()
        {
            if (swipe == null) return;
            swipe.Stop();
            if (swipeLayout) SettleSwipe();
        }

        // Keys, taps, folders and the course panel take the wheel from the swipe; they lay it out themselves.
        void CancelSwipe()
        {
            if (swipe != null) swipe.Stop();
            swipeLayout = false;
        }
    }
}
