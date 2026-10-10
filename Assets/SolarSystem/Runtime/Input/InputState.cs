using UnityEngine;
using UnityEngine.InputSystem;

namespace SolarSystem.Input
{
    /// <summary>
    /// One frame of player input, polled from the Input System devices. Keeping all key reads in
    /// one place makes the bindings easy to find and keeps the camera and app logic device-free.
    /// </summary>
    public sealed class InputState
    {
        /// <summary>x = strafe, y = rise, z = forward; each in [-1, 1].</summary>
        public Vector3 Move;
        public Vector2 LookDelta;
        public bool LookHeld;
        public float Scroll;
        public bool Boost;
        public bool Slow;

        public bool SelectClicked;
        public bool SelectDoubleClicked;
        public Vector2 MousePosition;

        public bool Focus;
        public bool Escape;
        public bool ToggleScale;
        public bool TimeFaster;
        public bool TimeSlower;
        public bool TogglePause;
        public bool ResetTime;
        public bool ToggleOrbits;
        public bool ToggleLabels;
        public bool ToggleHelp;
        public bool ToggleHud;
        public bool CycleShips;
        public bool CycleBackward;
        public bool Home;
        public int NumberKey = -1;

        Vector2 pressPosition;
        float lastClickTime = -10f;
        Vector2 lastClickPosition;
        bool rightDragged;
        Vector2 rightPressPosition;

        /// <summary>True on the frame the right button was released without dragging (a "right click").</summary>
        public bool RightClicked;

        public void Poll(bool pointerOverUi)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            Reset();
            if (kb == null || mouse == null) return;

            Move = new Vector3(
                Axis(kb.dKey, kb.aKey),
                Axis(kb.eKey, kb.qKey) + Axis(kb.spaceKey, kb.cKey),
                Axis(kb.wKey, kb.sKey));
            Move.y = Mathf.Clamp(Move.y, -1, 1);
            Boost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            Slow = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;

            MousePosition = mouse.position.ReadValue();
            LookHeld = mouse.rightButton.isPressed;
            LookDelta = LookHeld ? mouse.delta.ReadValue() : Vector2.zero;
            if (mouse.rightButton.wasPressedThisFrame)
            {
                rightPressPosition = MousePosition;
                rightDragged = false;
            }
            if (LookHeld && (MousePosition - rightPressPosition).sqrMagnitude > 16) rightDragged = true;
            if (mouse.rightButton.isPressed && mouse.delta.ReadValue().sqrMagnitude > 4) rightDragged = true;
            RightClicked = mouse.rightButton.wasReleasedThisFrame && !rightDragged;

            if (!pointerOverUi)
            {
                float wheel = mouse.scroll.ReadValue().y;
                Scroll = Mathf.Abs(wheel) > 0.01f ? Mathf.Sign(wheel) * Mathf.Min(Mathf.Abs(wheel) / 120f, 3f) : 0f;
                if (Mathf.Abs(wheel) > 0.01f && Mathf.Abs(Scroll) < 0.01f) Scroll = Mathf.Sign(wheel);
            }

            if (mouse.leftButton.wasPressedThisFrame) pressPosition = MousePosition;
            if (mouse.leftButton.wasReleasedThisFrame && !pointerOverUi && (MousePosition - pressPosition).sqrMagnitude < 36)
            {
                SelectClicked = true;
                float now = Time.unscaledTime;
                SelectDoubleClicked = now - lastClickTime < 0.35f && (MousePosition - lastClickPosition).sqrMagnitude < 100;
                lastClickTime = SelectDoubleClicked ? -10f : now;
                lastClickPosition = MousePosition;
            }

            Focus = kb.fKey.wasPressedThisFrame;
            Escape = kb.escapeKey.wasPressedThisFrame;
            ToggleScale = kb.gKey.wasPressedThisFrame || kb.backquoteKey.wasPressedThisFrame;
            TimeFaster = kb.periodKey.wasPressedThisFrame || kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame;
            TimeSlower = kb.commaKey.wasPressedThisFrame || kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame;
            TogglePause = kb.pKey.wasPressedThisFrame || kb.pauseKey.wasPressedThisFrame;
            ResetTime = kb.tKey.wasPressedThisFrame;
            ToggleOrbits = kb.oKey.wasPressedThisFrame;
            ToggleLabels = kb.lKey.wasPressedThisFrame;
            ToggleHelp = kb.hKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame;
            ToggleHud = kb.uKey.wasPressedThisFrame;
            CycleShips = kb.tabKey.wasPressedThisFrame;
            CycleBackward = CycleShips && Boost;
            Home = kb.homeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;

            Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digits.Length; i++)
                if (kb[digits[i]].wasPressedThisFrame) NumberKey = i;
        }

        void Reset()
        {
            Move = Vector3.zero;
            LookDelta = Vector2.zero;
            LookHeld = false;
            Scroll = 0;
            Boost = Slow = false;
            SelectClicked = SelectDoubleClicked = RightClicked = false;
            Focus = Escape = ToggleScale = TimeFaster = TimeSlower = TogglePause = ResetTime = false;
            ToggleOrbits = ToggleLabels = ToggleHelp = ToggleHud = CycleShips = CycleBackward = Home = false;
            NumberKey = -1;
        }

        static float Axis(UnityEngine.InputSystem.Controls.KeyControl positive, UnityEngine.InputSystem.Controls.KeyControl negative) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }
}
