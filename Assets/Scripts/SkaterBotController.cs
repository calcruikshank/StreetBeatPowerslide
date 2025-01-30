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
    [SerializeField] private float ollieForce = 5f; // Adjust as needed
    [SerializeField] private float ollieCooldown = 1f; // Time before another Ollie can be performed
    [SerializeField] private float ollieResetDelay = 0.2f; // Delay before resetting Ollie preparation

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

    // Timer for resetting Ollie preparation
    private float ollieResetTimer = 0f;

    [Header("Coyote Time Settings")]
    [SerializeField] private float coyoteTimeThreshold = 0.2f;

    private Collider _collider;
    private float _coyoteTimer;
    public bool IsGrounded { get; private set; }

    [Header("Ground Check Settings")]
    [SerializeField] private Vector3 boxHalfExtents = new Vector3(0.5f, 0.1f, 0.5f);
    [SerializeField] private Vector3 boxOffset = new Vector3(0, -0.3f, 0); // Increased from -0.1f to -0.3f
    [SerializeField] private LayerMask groundLayerMask;

    private bool isPerformingOllie = false; // Flag to indicate if an Ollie is in progress

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0f;
        rb.angularDamping = 1f;
        rb.useGravity = true;

        _collider = GetComponent<Collider>();
        if (_collider == null)
        {
            Debug.LogError("GroundChecker requires a Collider component.");
        }
    }

    private void Start()
    {
        GameManager.instance.skaterBotCamera.FindPlayer(this);
    }

    private void Update()
    {
        HandleBufferInput();
        HandleOllieResetTimer();
        CheckForGround();
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
        if (!preparedForOllie && !ollieOnCooldown)
        {
            // Detect when the right stick is pressed down
            if (rightStickInput.y < -0.5f)
            {
                preparedForOllie = true;
                Debug.Log("Prepared for Ollie!");
                // Optional: Add visual/audio feedback here to indicate preparation
            }
        }
        else if (preparedForOllie)
        {
            // Detect when the right stick is released upward
            if (rightStickInput.y > 0.5f && !ollieOnCooldown)
            {
                if (IsGrounded)
                {
                    Ollie();
                    preparedForOllie = false;

                    // Reset the Ollie reset timer if it's running
                    ollieResetTimer = 0f;
                }
            }
            // Reset preparation if the stick is released without flipping up
            else if (rightStickInput.y >= -0.5f && rightStickInput.y <= 0.5f)
            {
                if (ollieResetTimer <= 0f)
                {
                    ollieResetTimer = ollieResetDelay;
                    Debug.Log("Ollie preparation will reset in " + ollieResetDelay + " seconds.");
                }
            }
        }
    }

    /// <summary>
    /// Handles the timer for resetting the Ollie preparation.
    /// </summary>
    private void HandleOllieResetTimer()
    {
        if (preparedForOllie && ollieResetTimer > 0f)
        {
            ollieResetTimer -= Time.deltaTime;
            if (ollieResetTimer <= 0f)
            {
                preparedForOllie = false;
                ollieResetTimer = 0f;
                Debug.Log("Ollie preparation reset.");
                // Optional: Add feedback to indicate cancellation of Ollie
            }
        }
    }

    private void CheckForGround()
    {
        // Define the center of the box for the ground check
        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);

        // Perform the box check
        bool hitGround = Physics.CheckBox(boxCenter, boxHalfExtents, transform.rotation, groundLayerMask, QueryTriggerInteraction.Ignore);

        // Debug visualization
        DrawBox(boxCenter, boxHalfExtents * 2, hitGround ? Color.green : Color.red);

        if (hitGround && !isPerformingOllie)
        {
            IsGrounded = true;
            _coyoteTimer = 0f;
        }
        else
        {
            if (_coyoteTimer < coyoteTimeThreshold)
            {
                _coyoteTimer += Time.deltaTime;
                if (_coyoteTimer >= coyoteTimeThreshold)
                {
                    IsGrounded = false;
                }
            }
        }
    }

    private void DrawBox(Vector3 center, Vector3 size, Color color)
    {
        Vector3 halfSize = size / 2;
        Vector3 p0 = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
        Vector3 p1 = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
        Vector3 p2 = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
        Vector3 p3 = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);
        Vector3 p4 = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
        Vector3 p5 = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
        Vector3 p6 = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
        Vector3 p7 = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);

        // Bottom
        Debug.DrawLine(p0, p1, color);
        Debug.DrawLine(p1, p2, color);
        Debug.DrawLine(p2, p3, color);
        Debug.DrawLine(p3, p0, color);

        // Top
        Debug.DrawLine(p4, p5, color);
        Debug.DrawLine(p5, p6, color);
        Debug.DrawLine(p6, p7, color);
        Debug.DrawLine(p7, p4, color);

        // Sides
        Debug.DrawLine(p0, p4, color);
        Debug.DrawLine(p1, p5, color);
        Debug.DrawLine(p2, p6, color);
        Debug.DrawLine(p3, p7, color);
    }

    private void OnDrawGizmosSelected()
    {
        // Visualize the ground check box in the editor
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);
        Gizmos.DrawWireCube(boxCenter, boxHalfExtents * 2);
    }

    private bool CheckColliderGround(RaycastHit col)
    {
        //Debug.Log($"{Vector3.Dot(col.normal.normalized, Vector3.up)} bool result: {Vector3.Dot(col.normal.normalized, Vector3.up) >= 0.90f}");
        return (Vector3.Dot(col.normal.normalized, Vector3.up) >= 0.65f);
    }

    /// <summary>
    /// Starts the Ollie cooldown timer using a coroutine.
    /// </summary>
    private void StartOllieCooldown()
    {
        if (ollieOnCooldown)
            return;

        StartCoroutine(OllieCooldownRoutine());
    }

    private IEnumerator OllieCooldownRoutine()
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
    [SerializeField] private float driftTiltSpeed = 1500f; // Speed of tilt transition

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
    /// Executes an Ollie by applying an upward force.
    /// </summary>
    private void Ollie()
    {
        // Immediately set IsGrounded to false and indicate an Ollie is in progress
        IsGrounded = false;
        isPerformingOllie = true;
        _coyoteTimer = coyoteTimeThreshold;

        // Apply upward force for Ollie
        rb.AddForce(Vector3.up * ollieForce, ForceMode.Impulse);

        // Start cooldown
        StartOllieCooldown();

        // Optional: Trigger animations, sounds, or particle effects here
        Debug.Log("Ollie performed!");

        // Reset the isPerformingOllie flag after a short delay to ensure accurate ground detection
        Invoke(nameof(ResetOllieState), 0.1f); // Adjust delay as needed
    }

    private void ResetOllieState()
    {
        isPerformingOllie = false;
    }
}
