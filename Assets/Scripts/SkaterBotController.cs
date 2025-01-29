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
    [SerializeField] private float driftTurnSpeed = 200f; // Increased turn speed during drift
    [SerializeField] private float friction = 0.5f;
    [SerializeField] private float brakeForce = 80f;

    private Rigidbody rb;
    private Vector2 inputMovement;
    private Queue<BufferInput> inputQueue = new Queue<BufferInput>();

    [SerializeField] Transform pivotPointDrift;

    private bool isAccelerating = false; // Track if acceleration is active
    private bool isDrifting = false; // Track if the player is drifting

    public enum State { Normal, Braking, Drifting }
    public State state = State.Normal;

    [SerializeField] Transform pivotPoint;

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
                HandleMovement(turnSpeed);
                break;
            case State.Braking:
                HandleBraking();
                break;
            case State.Drifting:
                HandleMovement(driftTurnSpeed);
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
            else if (currentInput.actionType == PowerSlideData.InputActionType.DRIFT)
            {
                StartDrifting();
                inputQueue.Dequeue();
            }
        }
        else
        {
            inputQueue.Dequeue();
        }
    }

    private float pivotTiltZ = 0f; // Stores the current tilt value
    [SerializeField] private float tiltSpeed = 5f; // Speed of tilt transition
    [SerializeField] private float maxTiltAngle = 10f; // Maximum tilt angle on the Z-axis

    private void HandleMovement(float currentTurnSpeed)
    {
        Vector3 forwardDirection = transform.forward;
        Vector3 currentVelocity = rb.linearVelocity;

        // Remove sideways movement (keep only forward direction)
        Vector3 forwardVelocity = Vector3.Project(currentVelocity, forwardDirection);
        rb.linearVelocity = new Vector3(forwardVelocity.x, rb.linearVelocity.y, forwardVelocity.z); // Preserve gravity

        // Apply acceleration
        if (isAccelerating)
        {
            rb.AddForce(forwardDirection * acceleration, ForceMode.Acceleration);
        }

        // Clamp speed but preserve falling speed
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, rb.linearVelocity.y, rb.linearVelocity.z);
        if (horizontalVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = horizontalVelocity.normalized * maxSpeed + Vector3.up * rb.linearVelocity.y;
        }

        // Turning logic
        if (Mathf.Abs(inputMovement.x) > 0.01f)
        {
            float turnAmount = inputMovement.x * currentTurnSpeed * Time.fixedDeltaTime;

            if (isDrifting)
            {
                // **Fix: Only invert turning when drifting to the right**
                float driftTurnInput = (driftDirection <= 0) ? -inputMovement.x : inputMovement.x;

                // Clamp turn input to only allow turning in the drift's original direction
                float clampedTurn = Mathf.Clamp(driftTurnInput, driftDirection * 0.3f, driftDirection * 1.0f);
                turnAmount = clampedTurn * currentTurnSpeed * Time.fixedDeltaTime;
            }

            transform.Rotate(0, turnAmount, 0);
        }

        if (!isDrifting)
        {
            // Smoothly rotate pivot based on input
            float targetZRotation = Mathf.Lerp(10f, -10f, (inputMovement.x + 1f) / 2f);
            pivotTiltZ = Mathf.MoveTowards(pivotTiltZ, targetZRotation, tiltSpeed * Time.deltaTime);
            pivotPoint.localEulerAngles = new Vector3(pivotPoint.localEulerAngles.x, pivotPoint.localEulerAngles.y, pivotTiltZ);
        }
    }





    private void HandleBraking()
    {
        rb.AddForce(-rb.linearVelocity.normalized * brakeForce, ForceMode.Acceleration);
        if (rb.linearVelocity.magnitude < 1f) state = State.Normal;
    }
    private float driftDirection = 0f; // -1 for left, 1 for right

    private void StartDrifting()
    {
        float targetZRotation = 0; // Converts range [-1,1] to [-10,10]
        pivotTiltZ = 0;
        // Apply tilt to pivot point
        Vector3 currentRotation = pivotPoint.localEulerAngles;
        pivotPoint.localEulerAngles = new Vector3(currentRotation.x, currentRotation.y, pivotTiltZ);
        // Determine initial drift direction
        driftDirection = Mathf.Sign(inputMovement.x);
        if (driftDirection == 0) driftDirection = 1f; // Default to right if no input

        isDrifting = true;
        state = State.Drifting;

        // Rotate pivot for visual effect
        float driftAngle = driftDirection > 0 ? 50f : -50f;
        pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, driftAngle, pivotPointDrift.localEulerAngles.z);

        Debug.Log("Drifting!");
    }


    private void StopDrifting()
    {
        isDrifting = false;
        state = State.Normal;

        // Reset pivot rotation
        pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, 0, pivotPointDrift.localEulerAngles.z);

        Debug.Log("Stopped Drifting!");
    }


    private void OnMove(InputValue value)
    {
        Debug.Log("Moving: " + value);
        inputMovement = value.Get<Vector2>();
    }

    private void OnAttack(InputValue value)
    {
        isAccelerating = value.Get<float>() > 0.1f;
        Debug.Log(isAccelerating ? "Accelerating!" : "Stopped Accelerating");
    }

    private void OnAttackReleased(InputValue value)
    {
        isAccelerating = false;
    }

    private void OnJump(InputValue value)
    {
        inputQueue.Enqueue(new BufferInput(PowerSlideData.InputActionType.DRIFT, inputMovement, Time.time));
    }

    private void OnJumpReleased(InputValue value)
    {
        StopDrifting();
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
