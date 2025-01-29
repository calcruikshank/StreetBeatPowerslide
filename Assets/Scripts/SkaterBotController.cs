using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SkaterBotController : MonoBehaviour
{
    [Header("Buffer Settings")]
    [SerializeField] private float bufferTimerThreshold = 0.2f;

    [Header("Car Settings")]
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float turnSpeed = 100f;
    [SerializeField] private float friction = 0.5f;
    [SerializeField] private float brakeForce = 80f;

    private Rigidbody rb;
    private Vector2 inputMovement;
    private Queue<BufferInput> inputQueue = new Queue<BufferInput>();

    private bool isAccelerating = false; // Track if acceleration is active

    public enum State { Normal, Braking }
    public State state = State.Normal;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0f;
        rb.angularDamping = 1f;
        rb.useGravity = true;
    }

    private void Start()
    {
        GameManager.instance.skaterBotCamera.FindPlayer(this);
    }

    private void Update()
    {
        HandleBufferInput();
    }

    private void FixedUpdate()
    {
        switch (state)
        {
            case State.Normal:
                HandleMovement();
                break;
            case State.Braking:
                HandleBraking();
                break;
        }
    }

    private void HandleBufferInput()
    {
        if (inputQueue.Count == 0) return;
        BufferInput currentInput = inputQueue.Peek();
        if (Time.time - currentInput.timeOfInput < bufferTimerThreshold)
        {
            if (currentInput.actionType == PowerSlideData.InputActionType.BRAKE)
            {
                state = State.Braking;
                inputQueue.Dequeue();
            }
        }
        else
        {
            inputQueue.Dequeue();
        }
    }

    private void HandleMovement()
    {
        Vector3 forwardDirection = transform.forward;
        Vector3 currentVelocity = rb.linearVelocity;

        // Remove sideways movement (keep only forward direction)
        Vector3 forwardVelocity = Vector3.Project(currentVelocity, forwardDirection);
        rb.linearVelocity = new Vector3(forwardVelocity.x, currentVelocity.y, forwardVelocity.z); // Preserve gravity

        // Apply acceleration ONLY if right trigger (Attack) is held down
        if (isAccelerating)
        {
            rb.AddForce(forwardDirection * acceleration, ForceMode.Acceleration);
        }

        // Stop automatic deceleration when Attack is released
        if (!isAccelerating)
        {
            rb.linearVelocity = forwardVelocity; // Maintain speed instead of slowing down
        }

        // Clamp speed but preserve falling speed
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        if (horizontalVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = horizontalVelocity.normalized * maxSpeed + Vector3.up * rb.linearVelocity.y;
        }

        // Turning logic (rotate while keeping velocity)
        if (Mathf.Abs(inputMovement.x) > 0.01f)
        {
            float turnAmount = inputMovement.x * turnSpeed * Time.fixedDeltaTime;
            transform.Rotate(0, turnAmount, 0);
        }
    }

    private void HandleBraking()
    {
        rb.AddForce(-rb.linearVelocity.normalized * brakeForce, ForceMode.Acceleration);
        if (rb.linearVelocity.magnitude < 1f) state = State.Normal;
    }

    private void OnMove(InputValue value)
    {
        Debug.Log("Moving: " + value);
        inputMovement = value.Get<Vector2>();
    }

    // ??? **Right Trigger (Attack) ONLY Accelerates While Held**
    private void OnAttack(InputValue value)
    {
        isAccelerating = value.Get<float>() > 0.1f; // Only accelerates while trigger is held
        Debug.Log(isAccelerating ? "Accelerating!" : "Stopped Accelerating");
    }

    // ??? **Stops Deceleration on Attack Release**
    private void OnAttackReleased(InputValue value)
    {
        isAccelerating = false;
    }

    private void OnBrake()
    {
        inputQueue.Enqueue(new BufferInput(PowerSlideData.InputActionType.BRAKE, inputMovement, Time.time));
    }

    public float GetTurnInput()
    {
        return inputMovement.x;
    }
}
