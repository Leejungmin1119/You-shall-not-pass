using UnityEngine;

/// <summary>
/// Update에서 받은 이동 및 점프 입력을 다음 FixedUpdate까지 보관한다.
/// </summary>
public sealed class PlayerInputAdapter
{
    public Vector2 Move { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool JumpReleased { get; private set; }

    public void Read()
    {
        if (GameMenuUI.BlocksGameplayInput)
        {
            Move = Vector2.zero;
            Clear();
            return;
        }

        Move = InputManager.Movement;
        JumpPressed |= InputManager.JumpWasPressed;
        JumpReleased |= InputManager.JumpWasReleased;
    }

    public void ConsumeJump()
    {
        JumpPressed = false;
    }

    public void Clear()
    {
        JumpPressed = false;
        JumpReleased = false;
    }
}
