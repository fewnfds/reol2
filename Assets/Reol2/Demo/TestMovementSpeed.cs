using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Reol2.Demo
{
    internal sealed class TestMovementSpeed
    {
        private const float Minimum = 0.25f;
        private const float Maximum = 64f;
        private static readonly GUIContent Slower = new("−");
        private static readonly GUIContent Faster = new("+");

        private string label = "Speed 1×";

        internal float Multiplier { get; private set; } = 1f;

        private static bool IsAvailable =>
#if UNITY_EDITOR || DEVELOPMENT_BUILD || REOL_TEST_BUILD
            true;
#else
            false;
#endif

        internal static int ReadStep(Keyboard keyboard)
        {
            if (!IsAvailable || keyboard == null)
                return 0;

            bool faster = keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame;
            bool slower = keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame;
            return (faster ? 1 : 0) - (slower ? 1 : 0);
        }

        internal void ApplyStep(int step)
        {
            if (step == 0)
                return;

            float next = Mathf.Clamp(Multiplier * (step > 0 ? 2f : 0.5f), Minimum, Maximum);
            if (next == Multiplier)
                return;

            Multiplier = next;
            label = $"Speed {next.ToString("0.##", CultureInfo.InvariantCulture)}×";
        }

        private static Rect HudPanel => new(Screen.width - 180f, Screen.height - 66f, 168f, 54f);
        private static Rect SlowerButton(Rect panel) => new(panel.x + 8f, panel.y + 8f, 30f, 31f);
        private static Rect FasterButton(Rect panel) => new(panel.x + 130f, panel.y + 8f, 30f, 31f);
        private static Vector2 ToGuiPoint(Vector2 pointer) => new(pointer.x, Screen.height - pointer.y);

        // Runtime IMGUI renders the controls. Pointer events come from the Input System.
        internal bool HandlePointer(Vector2 pointer, bool pressed)
        {
            if (!IsAvailable)
                return false;

            Rect panel = HudPanel;
            Vector2 point = ToGuiPoint(pointer);
            if (!panel.Contains(point))
                return false;

            if (pressed)
            {
                if (SlowerButton(panel).Contains(point))
                    ApplyStep(-1);
                else if (FasterButton(panel).Contains(point))
                    ApplyStep(1);
            }
            return true;
        }

        internal void DrawHud(bool canClick)
        {
            if (!IsAvailable)
                return;

            Rect panel = HudPanel;
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x + 44f, panel.y + 7f, 86f, 22f), label);
            GUI.Label(new Rect(panel.x + 44f, panel.y + 28f, 92f, 20f), "Keys  − / =");

            if (Event.current.type != EventType.Repaint)
                return;

            Mouse mouse = Mouse.current;
            Vector2 pointer = mouse != null ? ToGuiPoint(mouse.position.ReadValue()) : new Vector2(-1f, -1f);
            bool pressed = mouse?.leftButton.isPressed == true;
            bool wasEnabled = GUI.enabled;
            DrawButton(SlowerButton(panel), Slower, wasEnabled && canClick && Multiplier > Minimum, pointer, pressed);
            DrawButton(FasterButton(panel), Faster, wasEnabled && canClick && Multiplier < Maximum, pointer, pressed);
            GUI.enabled = wasEnabled;
        }

        private static void DrawButton(Rect bounds, GUIContent content, bool canClick, Vector2 pointer, bool pressed)
        {
            GUI.enabled = canClick;
            bool hover = canClick && bounds.Contains(pointer);
            GUI.skin.button.Draw(bounds, content, hover, hover && pressed, false, false);
        }
    }
}
