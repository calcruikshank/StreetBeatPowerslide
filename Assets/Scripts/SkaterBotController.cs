using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SkaterBotController : MonoBehaviour
{
    [Header("Buffer Settings")]
    [SerializeField] private float bufferTimerThreshold = 0.2f;

    [Header("Movement Settings")]
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float maxSpeed = 15f;
    [SerializeField] private float turnSpeed = 60f;
    [SerializeField] private float driftTurnMultiplier = 1.5f;
    [SerializeField] private float jumpForce = 5f;

    [Header("Board / Animation")]
    [SerializeField] private Animator boardAnimator; // Reference to the board's animator

    public bool grounded = true; // For example, maybe controlled by a raycast in real use
    public State state = State.Normal;

    // Buffer for inputs
    public Queue<BufferInput> inputQueue = new Queue<BufferInput>();

    // Movement input
    private Vector2 inputMovement;  // (x=turn, y=forward)
    private Vector2 rightStickValue;
    private Rigidbody rb;

    // Additional flags
    public bool shiftPressed = false;
    public bool spacePressed = false;

    // For storing forward & turn amounts
    private float currentSpeed;
    private float forwardInput;
    private float turnInput;

    public enum State
    {
        Normal,
        Drifting,
        InAir,
    }

    private void Awake()
    {
        // Get reference to Rigidbody
        rb = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        state = State.Normal;
        // Lock rotation around X and Z if you want more �car-like� control
        // (optional�depends on your design)
        rb.freezeRotation = true;
    }

    private void Update()
    {
        // Check buffer inputs
        HandleBufferInput();

        // Convert the 2D inputMovement into forward/turn inputs:
        forwardInput = inputMovement.y;  // "vertical" input => forward/back
        turnInput = inputMovement.x;     // "horizontal" input => turning

        switch (state)
        {
            case State.Normal:
                // You might do normal ground logic
                break;
            case State.Drifting:
                // Drifting logic (special visuals, friction changes, etc.)
                break;
            case State.InAir:
                // In-air logic if needed
                break;
        }
    }

    private void FixedUpdate()
    {
        switch (state)
        {
            case State.Normal:
                HandleNormalMovement();
                break;
            case State.Drifting:
                HandleDriftMovement();
                break;
            case State.InAir:
                HandleAirMovement();
                break;
        }
    }

    // ���������������������������������������������������������������������������
    // Input Buffer Handling
    // ���������������������������������������������������������������������������
    private void HandleBufferInput()
    {
        if (inputQueue.Count <= 0) return;

        BufferInput currentBufferedInput = inputQueue.Peek();

        float timeSinceInput = Time.time - currentBufferedInput.timeOfInput;
        if (timeSinceInput < bufferTimerThreshold)
        {
            switch (currentBufferedInput.actionType)
            {
                case PowerSlideData.InputActionType.DRIFT:
                    if (grounded && state == State.Normal)
                    {
                        StartDrift();
                        inputQueue.Dequeue();
                    }
                    break;

                case PowerSlideData.InputActionType.BOOST:
                    if (grounded && state == State.Normal)
                    {
                        Boost();
                        inputQueue.Dequeue();
                    }
                    break;

                case PowerSlideData.InputActionType.DASH:
                    if (grounded && state == State.Normal)
                    {
                        Dash(currentBufferedInput.directionOfAction);
                        inputQueue.Dequeue();
                    }
                    break;

                case PowerSlideData.InputActionType.TRICK:
                    // NEW LOGIC: You can trick from the ground (with an ollie) or in midair
                    if (grounded && state == State.Normal)
                    {
                        // 1. Ollie you into the air
                        //Jump();
                        // 2. Perform the trick
                        //DoTrick(currentBufferedInput.directionOfAction);
                        inputQueue.Dequeue();
                    }
                    else if (state == State.InAir)
                    {
                        // Already in the air => just do the trick
                        DoTrick(currentBufferedInput.directionOfAction);
                        inputQueue.Dequeue();
                    }
                    break;
            }
        }
        else
        {
            // Input is older than bufferTimerThreshold => discard
            inputQueue.Dequeue();
        }
    }


    // ���������������������������������������������������������������������������
    // Movement States
    // ���������������������������������������������������������������������������
    private void HandleNormalMovement()
    {
        Debug.Log(forwardInput);
        // Apply forward acceleration
        float targetSpeed = forwardInput * maxSpeed;
        float speedDiff = targetSpeed - rb.linearVelocity.magnitude;
        // If you only want to accelerate forward (not in any direction of velocity),
        // you can do your own vector math. 
        // For simplicity, we'll approximate with forward direction:

        if (Mathf.Abs(speedDiff) > 0.1f)
        {
            // Calculate acceleration with direction
            Vector3 force = transform.forward * (acceleration * forwardInput);
            rb.AddForce(force, ForceMode.Acceleration);
        }

        // Turning
        if (Mathf.Abs(turnInput) > 0.01f)
        {
            // Rotate around Y axis
            float turn = turnInput * turnSpeed * Time.fixedDeltaTime;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, turn, 0f));
        }
    }

    private void HandleDriftMovement()
    {
        // Drifting might let you turn sharper, or reduce friction
        // Example: turn sharper
        float driftTurn = turnInput * turnSpeed * driftTurnMultiplier * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, driftTurn, 0f));

        // Possibly reduce forward speed or handle friction differently
        // ...
    }

    private void HandleAirMovement()
    {
        // While in the air, you might have limited control
        // For instance, slight mid-air rotation but no ground friction
    }

    // ���������������������������������������������������������������������������
    // Actions (Drift, Boost, Dash, Trick, Jump)
    // ���������������������������������������������������������������������������

    private void StartDrift()
    {
        Debug.Log("Started Drifting!");
        // Switch to drifting state
        //state = State.Drifting;
        // Possibly play a drift animation or effect
        // e.g. boardAnimator.SetTrigger("DriftStart");
    }

    private void StopDrift()
    {
        Debug.Log("Stopped Drifting!");
        //state = State.Normal;
        // e.g. boardAnimator.SetTrigger("DriftEnd");
    }

    private void Boost()
    {
        Debug.Log("Boost triggered");
        // Add an impulse or temporarily set higher speed
        rb.AddForce(transform.forward * (acceleration * 2f), ForceMode.Impulse);
        // Possibly set an animation trigger or effect
        // boardAnimator.SetTrigger("Boost");
    }

    private void Dash(Vector3 dashDirection)
    {
        Debug.Log("Dash triggered. Direction: " + dashDirection);
        // Quick dash impulse
        rb.AddForce(dashDirection.normalized * (acceleration * 3f), ForceMode.Impulse);
        // Possibly do dash animation
        // boardAnimator.SetTrigger("Dash");
    }

    private void Jump()
    {
        Debug.Log("Jump triggered");
        // If using a jump, apply upward force
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        //state = State.InAir;
    }

    private void DoTrick(Vector2 flickDirection)
    {
        Debug.Log("Performing TRICK with flick direction: " + flickDirection);
        // Trigger your board�s trick animation
        // For instance, choose animation based on flick direction
        if (flickDirection.x > 0)
        {
            //boardAnimator.SetTrigger("Kickflip");
        }
        else
        {
            //boardAnimator.SetTrigger("Heelflip");
        }
    }

    // ���������������������������������������������������������������������������
    // Input Callbacks (from the PlayerInput component)
    // ���������������������������������������������������������������������������

    // Maps to "OnMove": x = turn, y = forward
    private void OnMove(InputValue inputValue)
    {
        inputMovement = inputValue.Get<Vector2>();
    }

    // Right Stick input for TRICK detection
    private void OnLook(InputValue inputValue)
    {
        /*Vector2 newRightStickValue = inputValue.Get<Vector2>();
        float oldMag = rightStickValue.magnitude;
        float newMag = newRightStickValue.magnitude;
        rightStickValue = newRightStickValue;

        // Simple flick detection
        if (oldMag < 0.1f && newMag > 0.5f)
        {
            BufferInput trickBuffer = new BufferInput(
                PowerSlideData.InputActionType.TRICK,
                newRightStickValue,
                Time.time
            );
            inputQueue.Enqueue(trickBuffer);
            Debug.Log("Trick flick detected!");
        }*/
    }

    // "Drift" Input (mapped from your "OnJump" or any other button)
    private void OnJump()
    {
        // If you want jump to actually jump or cause a drift, up to you
        if (grounded && state == State.Normal)
        {
            // Example: do an actual jump
            Jump();
        }

        // Alternatively, if you want a separate �drift� button, just enqueue that:
        // BufferInput driftBuffer = new BufferInput(PowerSlideData.InputActionType.DRIFT, inputMovement, Time.time);
        // inputQueue.Enqueue(driftBuffer);
    }
    private void OnJumpReleased()
    {
        // If your drift ends on release:
        if (state == State.Drifting)
            StopDrift();
    }

    // "Attack" => Boost
    private void OnAttack()
    {
        Debug.Log("Boost Pressed");
        BufferInput boostBuffer =
            new BufferInput(PowerSlideData.InputActionType.BOOST, inputMovement, Time.time);
        inputQueue.Enqueue(boostBuffer);
    }

    private void OnDash()
    {
        Debug.Log("Dash Pressed");
        // Use last forward direction or inputMovement
        Vector3 dashDir = transform.forward;
        BufferInput dashBuffer =
            new BufferInput(PowerSlideData.InputActionType.DASH, dashDir, Time.time);
        inputQueue.Enqueue(dashBuffer);
    }

    // Example for releasing dash if needed
    private void OnDashReleased()
    {
        shiftPressed = false;
    }
}
