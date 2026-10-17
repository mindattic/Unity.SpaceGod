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

        /// <summary>Raw mouse delta, unconditional (unlike <see cref="LookDelta"/>, which only reports while
        /// the right button is held for camera orbit) — piloting reads this for always-on heading control.</summary>
        public Vector2 MouseDelta;
        public float Scroll;
        public bool Boost;
        public bool Slow;

        /// <summary>Held while piloting: engages the cruise drive (Elite-style) for fast open-space
        /// travel. See <see cref="SolarSystem.Core.PilotCruiseState"/>.</summary>
        public bool Cruise;

        /// <summary>While piloting: roll, -1 (Z, anticlockwise) .. 1 (X, clockwise), seen from behind.</summary>
        public float Roll;

        /// <summary>While piloting: switches flight assist on or off (shares F with <see cref="Focus"/>,
        /// which is gated off while piloting, so the two never fire together).</summary>
        public bool ToggleFlightAssist;

        /// <summary>Held, not click-edged: while piloting, fires the ship's forward-mount weapons.
        /// Left click still selects normally outside piloting, via the click-edged <see cref="SelectClicked"/>
        /// below (gated off while piloting, so holding the trigger down and releasing it never also selects).</summary>
        public bool Fire;

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
        public bool ToggleMarket;
        public bool ToggleCargo;

        /// <summary>Dev/sandbox panel: spawn a ship of any class at the camera and pilot it.</summary>
        public bool ToggleSpawn;
        public bool WatchBattle;
        public bool ToggleLanes;
        public bool ToggleSatellites;
        public bool LevelView;
        public bool CycleShips;
        public bool CycleBackward;
        public bool Home;
        public int NumberKey = -1;

        /// <summary>Takes or releases direct control of the selected/focused ship.</summary>
        public bool Pilot;

        /// <summary>While piloting: locks (or clears) the camera's tracking onto the hovered/selected
        /// ship. Shares its key with <see cref="ResetTime"/>, which is gated off while piloting so the
        /// two never fire together.</summary>
        public bool LockTarget;

        /// <summary>While piloting, in range of one of this system's jump gates: activates it.</summary>
        public bool ActivateGate;

        Vector2 pressPosition;
        float lastClickTime = -10f;
        Vector2 lastClickPosition;
        bool rightDragged;
        Vector2 rightPressPosition;

        /// <summary>True on the frame the right button was released without dragging (a "right click").</summary>
        public bool RightClicked;

        /// <summary>Reads this frame's input; with <paramref name="suspended"/> (another screen has the
        /// keyboard and mouse, such as the model viewer) everything reads as idle instead.</summary>
        public void Poll(bool pointerOverUi, bool suspended = false)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            Reset();
            if (suspended || kb == null || mouse == null) return;

            Move = new Vector3(
                Axis(kb.dKey, kb.aKey),
                Axis(kb.eKey, kb.qKey) + Axis(kb.rightBracketKey, kb.cKey),
                Axis(kb.wKey, kb.sKey));
            Move.y = Mathf.Clamp(Move.y, -1, 1);
            Boost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            Slow = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            Cruise = kb.jKey.isPressed;
            Roll = Axis(kb.xKey, kb.zKey);

            MousePosition = mouse.position.ReadValue();
            MouseDelta = mouse.delta.ReadValue();
            Fire = mouse.leftButton.isPressed && !pointerOverUi;
            LookHeld = mouse.rightButton.isPressed;
            LookDelta = LookHeld ? MouseDelta : Vector2.zero;
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
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    // Standard mice report multiples of 120 per notch; trackpads and high-resolution wheels
                    // already report small per-notch values directly, so scale each convention on its own
                    // rather than dividing both by 120 and snapping the underflowed result to a full notch.
                    float normalized = Mathf.Abs(wheel) < 2f ? wheel : wheel / 120f;
                    Scroll = Mathf.Clamp(normalized, -3f, 3f);
                }
                else
                {
                    Scroll = 0f;
                }
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
            ToggleFlightAssist = kb.fKey.wasPressedThisFrame;
            Escape = kb.escapeKey.wasPressedThisFrame;
            ToggleScale = kb.gKey.wasPressedThisFrame || kb.backquoteKey.wasPressedThisFrame;
            TimeFaster = kb.periodKey.wasPressedThisFrame || kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame;
            TimeSlower = kb.commaKey.wasPressedThisFrame || kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame;
            TogglePause = kb.spaceKey.wasPressedThisFrame || kb.pauseKey.wasPressedThisFrame;
            ResetTime = kb.tKey.wasPressedThisFrame;
            LockTarget = kb.tKey.wasPressedThisFrame;
            ToggleOrbits = kb.oKey.wasPressedThisFrame;
            ToggleLabels = kb.lKey.wasPressedThisFrame;
            ToggleHelp = kb.hKey.wasPressedThisFrame || kb.f1Key.wasPressedThisFrame;
            ToggleHud = kb.uKey.wasPressedThisFrame;
            ToggleMarket = kb.mKey.wasPressedThisFrame;
            ToggleCargo = kb.iKey.wasPressedThisFrame;
            ToggleSpawn = kb.vKey.wasPressedThisFrame;
            WatchBattle = kb.bKey.wasPressedThisFrame;
            ToggleLanes = kb.nKey.wasPressedThisFrame;
            ToggleSatellites = kb.kKey.wasPressedThisFrame;
            LevelView = kb.rKey.wasPressedThisFrame;
            CycleShips = kb.tabKey.wasPressedThisFrame;
            CycleBackward = CycleShips && Boost;
            Home = kb.homeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
            Pilot = kb.pKey.wasPressedThisFrame;
            ActivateGate = kb.yKey.wasPressedThisFrame;

            Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < digits.Length; i++)
                if (kb[digits[i]].wasPressedThisFrame) NumberKey = i;
        }

        void Reset()
        {
            Move = Vector3.zero;
            LookDelta = Vector2.zero;
            LookHeld = false;
            MouseDelta = Vector2.zero;
            Scroll = 0;
            Roll = 0;
            ToggleFlightAssist = false;
            Boost = Slow = Fire = Cruise = false;
            SelectClicked = SelectDoubleClicked = RightClicked = false;
            Focus = Escape = ToggleScale = TimeFaster = TimeSlower = TogglePause = ResetTime = false;
            ToggleOrbits = ToggleLabels = ToggleHelp = ToggleHud = ToggleMarket = ToggleCargo = ToggleSpawn = WatchBattle = ToggleLanes = ToggleSatellites = LevelView = CycleShips = CycleBackward = Home = Pilot = LockTarget = ActivateGate = false;
            NumberKey = -1;
        }

        static float Axis(UnityEngine.InputSystem.Controls.KeyControl positive, UnityEngine.InputSystem.Controls.KeyControl negative) =>
            (positive.isPressed ? 1f : 0f) - (negative.isPressed ? 1f : 0f);
    }
}
