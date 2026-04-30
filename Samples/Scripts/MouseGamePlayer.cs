using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MouseGamePlayer : MonoBehaviour
{
    private PlayerInputActions inputActions;
    private Vector2 moveInput;

    [LytixTrackable.PlayerPosition]
    private Vector3 position => transform.position;


    [LytixTrackable]
    public float speed = 5f;

    [LytixTrackable]
    public int jumps;


    private void Awake()
    {
        inputActions = new PlayerInputActions();
    }

    private void OnEnable()
    {
        inputActions.Player.Enable();
        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;
        inputActions.Player.Jump.performed += OnJump;
    }

    private void OnDisable()
    {
        inputActions.Player.Disable();
    }

    private void Update()
    {
        // Convert Vector2 (WASD) → Vector3 (XZ plane)
        Vector3 move = new Vector3(moveInput.x, 0f, moveInput.y);

        transform.Translate(move * speed * Time.deltaTime, Space.World);
    }
    private void OnJump(InputAction.CallbackContext context)
    {
        
        // Example of sending a custom event with additional data
        Lytix.Event("player jump");
        jumps++;
    }
}