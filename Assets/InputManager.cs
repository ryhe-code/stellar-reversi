using System;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    // クリック時に発火するイベント
    public event Action OnClickEvent;

    private GameInput _gameInput;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 生成されたInput Actionsクラスのインスタンス化
        _gameInput = new GameInput();
    }

    private void OnEnable()
    {
        _gameInput.Gameplay.Enable();
        // クリック入力の検知をコールバックに登録
        _gameInput.Gameplay.Click.performed += HandleClick;
    }

    private void OnDisable()
    {
        _gameInput.Gameplay.Click.performed -= HandleClick;
        _gameInput.Gameplay.Disable();
    }

    private void HandleClick(UnityEngine.InputSystem.InputAction.CallbackContext context)
    {
        OnClickEvent?.Invoke();
    }

    /// <summary>
    /// 現在のマウススクリーン座標を取得
    /// </summary>
    public Vector2 GetMouseScreenPosition()
    {
        return _gameInput.Gameplay.Point.ReadValue<Vector2>();
    }
}