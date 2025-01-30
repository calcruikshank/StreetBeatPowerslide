using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SkaterBotController : MonoBehaviour
{
    [Header("Board Settings")]
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float maxSpeed = 20f;
    [SerializeField] private float turnSpeed = 100f;
    [SerializeField] private float driftTurnSpeed = 200f; // Increased turn speed during drift
    [SerializeField] private float friction = 0.5f;
    [SerializeField] private float brakeForce = 80f;

    // Ollie Settings
    [Header("Ollie Settings")]
    private float ollieForce = 4f; // Adjust as needed
    [SerializeField] private float ollieCooldown = 1f; // Time before another ollie can be performed

    private Rigidbody rb;
    private Vector2 inputMovement;
    private Vector2 rightStickInput;

    [SerializeField] Transform pivotPointDrift;

    private bool isAccelerating = false; // Track if acceleration is active
    private bool isDrifting = false; // Track if the player is drifting

    public enum State { Normal, Braking, Drifting }
    public State state = State.Normal;

    [SerializeField] Transform pivotPoint;

    [SerializeField] List<TrailRenderer> driftTrails;

    public bool preparedForOllie = false;
    private bool ollieOnCooldown = false;

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
        if (driftingPressed && !isDrifting)
        {
            StartDrifting();
        }
        if (!driftingPressed && isDrifting)
        {
            StopDrifting();
        }

        // Handle Ollie Preparation and Execution
        if (!preparedForOllie)
        {
            // Detect when the right stick is pressed down
            if (rightStickInput.y < -0.5f)
            {
                preparedForOllie = true;
                // Optional: You can add visual/audio feedback here to indicate preparation
            }
        }
        else
        {
            // Detect when the right stick is released upward
            if (rightStickInput.y > 0.5f && !ollieOnCooldown)
            {
                Ollie();
                preparedForOllie = false;
                StartCoroutine(OllieCooldown());
            }
            // Optional: Reset preparation if the stick is released without flipping up
            else if (rightStickInput.y >= -0.5f && rightStickInput.y <= 0.5f)
            {
                // You can decide whether to reset or keep the prepared state
                // For example, reset after a short delay
                // preparedForOllie = false;
            }
        }
    }

    private IEnumerator OllieCooldown()
    {
        ollieOnCooldown = true;
        yield return new WaitForSeconds(ollieCooldown);
        ollieOnCooldown = false;
    }

    private float pivotTiltZ = 0f; // Stores the current tilt value
    [SerializeField] private float tiltSpeed = 5f; // Speed of tilt transition
    [SerializeField] private float maxTiltAngle = 10f; // Maximum tilt angle on the Z-axis

    private void HandleMovement(float currentTurnSpeed)
    {
        Vector3 forwardDirection = transform.forward;
        Vector3 currentVelocity = rb.linearVelocity; // Changed from rb.linearVelocity to rb.velocity

        // Remove sideways movement (keep only forward direction)
        Vector3 forwardVelocity = Vector3.Project(currentVelocity, forwardDirection);
        rb.linearVelocity = new Vector3(forwardVelocity.x, rb.linearVelocity.y, forwardVelocity.z); // Preserve gravity

        // Apply acceleration
        if (isAccelerating)
        {
            rb.AddForce(forwardDirection * acceleration, ForceMode.Acceleration);
        }

        // Clamp speed but preserve falling speed
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        if (horizontalVelocity.magnitude > maxSpeed)
        {
            Vector3 clampedVelocity = horizontalVelocity.normalized * maxSpeed;
            rb.linearVelocity = new Vector3(clampedVelocity.x, rb.linearVelocity.y, clampedVelocity.z);
        }

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

        if (!isDrifting)
        {
            // Smoothly rotate pivot based on input
            float targetZRotation = Mathf.Lerp(10f, -10f, (inputMovement.x + 1f) / 2f);
            pivotTiltZ = Mathf.MoveTowards(pivotTiltZ, targetZRotation, tiltSpeed * Time.deltaTime);
            pivotPoint.localEulerAngles = new Vector3(pivotPoint.localEulerAngles.x, pivotPoint.localEulerAngles.y, pivotTiltZ);
            float newZRotation = Mathf.MoveTowardsAngle(pivotPointDrift.localEulerAngles.y, 0, driftTiltSpeed * Time.deltaTime);
            pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, newZRotation, pivotPointDrift.localEulerAngles.z);
        }

        // Smoothly transition **drift tilt** (drifting effect)
        if (isDrifting)
        {
            float newZRotation = Mathf.MoveTowardsAngle(pivotPointDrift.localEulerAngles.y, driftTiltZ, driftTiltSpeed * Time.deltaTime);
            pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, newZRotation, pivotPointDrift.localEulerAngles.z);
        }
    }

    private void HandleBraking()
    {
        rb.AddForce(-rb.linearVelocity.normalized * brakeForce, ForceMode.Acceleration);
        if (rb.linearVelocity.magnitude < 1f) state = State.Normal;
    }

    private float driftDirection = 0f; // -1 for left, 1 for right

    private float driftTiltZ = 0f;
    private float driftTiltSpeed = 1500f; // Speed of tilt transition

    private void StartDrifting()
    {
        // Reset normal tilt
        pivotTiltZ = 0f;

        // Apply tilt to pivot point
        Vector3 currentRotation = pivotPoint.localEulerAngles;
        pivotPoint.localEulerAngles = new Vector3(currentRotation.x, currentRotation.y, pivotTiltZ);

        // Determine drift direction
        driftDirection = Mathf.Sign(inputMovement.x);
        if (driftDirection == 0) driftDirection = 1f; // Default to right if no input

        isDrifting = true;
        state = State.Drifting;

        // Set target drift angle
        driftTiltZ = driftDirection > 0 ? 50f : -50f;

        foreach (TrailRenderer trail in driftTrails)
        {
            trail.emitting = true;
        }
    }

    private void StopDrifting()
    {
        isDrifting = false;
        state = State.Normal;

        // Reset target drift angle
        driftTiltZ = 0f;

        foreach (TrailRenderer trail in driftTrails)
        {
            trail.emitting = false;
        }
    }

    private void OnLook(InputValue value)
    {
        rightStickInput = value.Get<Vector2>();
    }

    private void OnMove(InputValue value)
    {
        inputMovement = value.Get<Vector2>();
    }

    private void OnAttack(InputValue value)
    {
        isAccelerating = value.Get<float>() > 0.1f;
    }

    private void OnAttackReleased(InputValue value)
    {
        isAccelerating = false;
    }

    public bool driftingPressed;
    private void OnJump(InputValue value)
    {
        driftingPressed = true;
    }

    private void OnJumpReleased(InputValue value)
    {
        driftingPressed = false;
    }

    public float GetTurnInput()
    {
        return inputMovement.x;
    }

    /// <summary>
    /// Executes an ollie by applying an upward force.
    /// </summary>
    private void Ollie()
    {
        // Apply upward force for ollie
        rb.AddForce(Vector3.up * ollieForce, ForceMode.Impulse);

        // Optional: Trigger animations, sounds, or particle effects here
        Debug.Log("Ollie performed!");
    }
}
