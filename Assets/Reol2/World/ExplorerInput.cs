using Reol2.Demo;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Reol2.World
{
    internal struct ExplorerInput
    {
        internal Vector2 Look, Pointer;
        internal float Forward, Turn, Strafe, Zoom;
        internal bool HasPointer, PointerPressed, Orbit, Steer;
        internal bool Rise, Descend, Jump, Crouch, Run, Walk;
        internal bool Dig, Fill, Grade, Prospect, Travel, Cancel, ToggleFlight, AdvanceTime;
        internal Key Destination;
        internal int SpeedStep;

        internal static ExplorerInput Read(Keyboard keyboard, Mouse mouse)
        {
            var input = new ExplorerInput();
            if (mouse != null)
            {
                input.HasPointer = true;
                input.Look = mouse.delta.ReadValue();
                input.Pointer = mouse.position.ReadValue();
                input.Zoom = mouse.scroll.ReadValue().y;
                input.PointerPressed = mouse.leftButton.wasPressedThisFrame;
                input.Orbit = mouse.leftButton.isPressed;
                input.Steer = mouse.rightButton.isPressed;
            }
            if (keyboard == null)
                return input;

            input.Forward = Axis(Key.W, Key.S);
            input.Turn = Axis(Key.D, Key.A);
            input.Strafe = Axis(Key.E, Key.Q);
            input.Rise = Held(Key.Space);
            input.Jump = Pressed(Key.Space);
            input.Crouch = Held(Key.C);
            input.Descend = input.Crouch || Held(Key.X);
            input.Run = Held(Key.LeftShift) || Held(Key.RightShift);
            input.Walk = Held(Key.LeftAlt) || Held(Key.RightAlt);
            input.Dig = Pressed(Key.Digit1);
            input.Fill = Pressed(Key.Digit2);
            input.Grade = Pressed(Key.Digit3);
            input.Prospect = Pressed(Key.G);
            input.Travel = Pressed(Key.T);
            input.Cancel = Pressed(Key.Escape);
            input.ToggleFlight = Pressed(Key.F);
            input.AdvanceTime = Pressed(Key.RightBracket);
            input.SpeedStep = TestMovementSpeed.ReadStep(keyboard);
            if (Pressed(Key.Home))
                input.Destination = Key.Home;
            else
            {
                for (Key key = Key.F1; key <= Key.F7; key++)
                    if (Pressed(key))
                    {
                        input.Destination = key;
                        break;
                    }
            }
            return input;

            bool Held(Key key) => keyboard[key].isPressed;
            bool Pressed(Key key) => keyboard[key].wasPressedThisFrame;
            float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);
        }
    }

    internal enum ExplorerMouseGesture { None, Orbit, Steer, Advance }

    internal sealed class ExplorerMouse
    {
        private Vector2 pendingLook, restorePointer;
        private bool buttonHeld, waitForRelease, discardLook;

        internal ExplorerMouseGesture Gesture { get; private set; }

        internal Vector2 Update(ExplorerInput input)
        {
            if (!input.Orbit && !input.Steer)
            {
                Release(wait: false);
                return Vector2.zero;
            }
            if (waitForRelease)
                return Vector2.zero;

            bool advance = input.Steer && input.Orbit;
            bool captured = Gesture != ExplorerMouseGesture.None;
            Vector2 look = input.Look;
            if (!captured)
            {
                if (!buttonHeld)
                {
                    buttonHeld = true;
                    restorePointer = input.Pointer;
                }
                pendingLook += look;
                bool steerWithKeys = input.Steer && (input.Forward != 0f || input.Turn != 0f || input.Strafe != 0f);
                if (!advance && !steerWithKeys && pendingLook.sqrMagnitude < 9f)
                    return Vector2.zero;
                look = pendingLook;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else if (discardLook)
                look = Vector2.zero;

            discardLook = !captured;
            Gesture = advance ? ExplorerMouseGesture.Advance : input.Steer ? ExplorerMouseGesture.Steer : ExplorerMouseGesture.Orbit;
            return look;
        }

        internal void Release(bool wait, bool restore = true)
        {
            if (Gesture != ExplorerMouseGesture.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (restore)
                    Mouse.current?.WarpCursorPosition(restorePointer);
            }
            Gesture = ExplorerMouseGesture.None;
            pendingLook = Vector2.zero;
            discardLook = false;
            buttonHeld = false;
            waitForRelease = wait;
        }
    }
}
