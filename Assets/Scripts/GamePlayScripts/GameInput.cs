using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static class GameInput
{
    public static Gamepad P1Pad { get; private set; }
    public static Gamepad P2Pad { get; private set; }
    public static bool HasPrimaryInput { get; private set; }
    public static bool IsSoloMode { get; private set; } = true;

    public static void RefreshGamepads()
    {
        P1Pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
        P2Pad = Gamepad.all.Count > 1 ? Gamepad.all[1] : null;
        HasPrimaryInput = P1Pad != null || Keyboard.current != null;
        IsSoloMode = P2Pad == null;
    }

    public static InputDevice GetDevice(int player)
    {
        RefreshGamepads();

        if (player == 1)
        {
            return P1Pad != null ? P1Pad : Keyboard.current;
        }

        return P2Pad != null ? P2Pad : Keyboard.current;
    }

    public static Vector2 Move(int player)
    {
        RefreshGamepads();

        Vector2 move = Vector2.zero;
        Gamepad pad = GetGamepad(player);
        if (pad != null)
        {
            move += pad.leftStick.ReadValue();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            move += player == 1 ? Wasd(keyboard) : Arrows(keyboard);
        }

        return Vector2.ClampMagnitude(move, 1f);
    }

    public static bool MenuLeft(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.dpad.left : null, pad != null ? pad.leftStick.left : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.aKey : keyboard.leftArrowKey));
    }

    public static bool MenuRight(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.dpad.right : null, pad != null ? pad.leftStick.right : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.dKey : keyboard.rightArrowKey));
    }

    public static bool Confirm(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        ButtonControl keyboardConfirm = player == 1 && keyboard != null ? keyboard.spaceKey : keyboard != null ? keyboard.enterKey : null;
        return Pressed(pad != null ? pad.buttonSouth : null, pad != null ? pad.startButton : null)
            || Pressed(keyboardConfirm);
    }

    public static bool Cancel(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.buttonEast : null, pad != null ? pad.selectButton : null)
            || (keyboard != null && Pressed(keyboard.escapeKey, keyboard.backspaceKey));
    }

    public static bool LeftBumper(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.leftShoulder : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.qKey : keyboard.pageDownKey));
    }

    public static bool RightBumper(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.rightShoulder : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.eKey : keyboard.pageUpKey));
    }

    public static bool FastAttack(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.buttonWest : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.jKey : keyboard.numpad1Key));
    }

    public static bool HeavyAttack(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.buttonNorth : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.kKey : keyboard.numpad2Key));
    }

    public static bool StabAttack(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.buttonEast : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.lKey : keyboard.numpad3Key));
    }

    // Quick step: gamepad A / south button, Space (player 1) or Numpad 0 (player 2).
    public static bool Dash(int player)
    {
        RefreshGamepads();

        Gamepad pad = GetGamepad(player);
        Keyboard keyboard = Keyboard.current;
        return Pressed(pad != null ? pad.buttonSouth : null)
            || (keyboard != null && Pressed(player == 1 ? keyboard.spaceKey : keyboard.numpad0Key));
    }

    private static Gamepad GetGamepad(int player)
    {
        return player == 1 ? P1Pad : P2Pad;
    }

    private static Vector2 Wasd(Keyboard keyboard)
    {
        Vector2 move = Vector2.zero;
        if (keyboard.aKey.isPressed) move.x -= 1f;
        if (keyboard.dKey.isPressed) move.x += 1f;
        if (keyboard.sKey.isPressed) move.y -= 1f;
        if (keyboard.wKey.isPressed) move.y += 1f;
        return move;
    }

    private static Vector2 Arrows(Keyboard keyboard)
    {
        Vector2 move = Vector2.zero;
        if (keyboard.leftArrowKey.isPressed) move.x -= 1f;
        if (keyboard.rightArrowKey.isPressed) move.x += 1f;
        if (keyboard.downArrowKey.isPressed) move.y -= 1f;
        if (keyboard.upArrowKey.isPressed) move.y += 1f;
        return move;
    }

    private static bool Pressed(params ButtonControl[] controls)
    {
        foreach (ButtonControl control in controls)
        {
            if (control != null && control.wasPressedThisFrame)
            {
                return true;
            }
        }

        return false;
    }
}
