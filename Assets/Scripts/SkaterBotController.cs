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

    [Header("Trick Settings")]
    [SerializeField] private float ollieForce = 5f; // Adjust as needed
    [SerializeField] private float trickCooldown = 1f; // Time before another trick can be performed
    [SerializeField] private float trickPreparationTime = 1f; // Time allowed for player to flick after preparation
    [SerializeField] private float trickCancelDelay = 0.1f; // Delay before cancelling preparation after stick returns to center

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

    private bool trickOnCooldown = false;

    [Header("Coyote Time Settings")]
    [SerializeField] private float coyoteTimeThreshold = 0.2f;

    private Collider _collider;
    private float _coyoteTimer;
    public bool IsGrounded { get; private set; }

    [Header("Ground Check Settings")]
    [SerializeField] private Vector3 boxHalfExtents = new Vector3(0.5f, 0.1f, 0.5f);
    [SerializeField] private Vector3 boxOffset = new Vector3(0, -0.3f, 0); // Increased from -0.1f to -0.3f
    [SerializeField] private LayerMask groundLayerMask;

    private bool isPerformingTrick = false; // Flag to indicate if a trick is in progress

    [SerializeField] Animator animator;

    // Drift Variables
    private float driftDirection = 0f; // -1 for left, 1 for right
    private float pivotTiltZ = 0f; // Stores the current tilt value
    [SerializeField] private float tiltSpeed = 5f; // Speed of tilt transition
    [SerializeField] private float driftTiltSpeed = 1500f; // Speed of drift tilt transition
    private float driftTiltZ = 0f;

    // Trick Preparation Variables
    public bool preparedForTrick = false;
    private Coroutine trickPreparationCoroutine;
    private Coroutine trickCancelCoroutine;

    // New Variables for Ground Alignment
    [Header("Ground Alignment Settings")]
    [SerializeField] private float groundRayDistance = 2f; // Distance to cast the ray
    [SerializeField] private float alignmentSpeed = 5f; // Speed of alignment rotation
    [SerializeField] private float alignmentOffset = 0.5f; // Offset above the ground to cast the ray

    // Rotation Constraints
    [SerializeField] private Vector3 rotationConstraints = new Vector3(0f, 360f, 0f); // Limit rotation on specific axes if needed

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = 0f;
        rb.angularDamping = 1f;
        rb.useGravity = true;

        _collider = GetComponent<Collider>();
        if (_collider == null)
        {
            Debug.LogWarning("Collider component missing on SkaterBot.");
        }
    }

    private void Start()
    {
        if (GameManager.instance != null && GameManager.instance.skaterBotCamera != null)
        {
            GameManager.instance.skaterBotCamera.FindPlayer(this);
        }
        else
        {
            Debug.LogWarning("GameManager or SkaterBotCamera not found.");
        }
    }

    private void Update()
    {
        HandleBufferInput();
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

        AlignWithGround(); // Move alignment to FixedUpdate for physics consistency
    }

    /// <summary>
    /// Aligns the skater's orientation with the ground's normal using physics-based rotation.
    /// </summary>
    /// 
    [SerializeField] Transform alignTransform;
    Vector3 movementDirection; // Assume this is set based on your input/movement logic
    private void AlignWithGround()
    {
        if (!IsGrounded || _coyoteTimer > 0)
            return;

        Ray ray = new Ray(transform.position + Vector3.up * alignmentOffset, Vector3.down);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, groundRayDistance, groundLayerMask))
        {
            Vector3 groundNormal = hit.normal;

            // Check if the ground is flat (normal is approximately up)
            if (Vector3.Dot(groundNormal, Vector3.up) > 0.999f) // Adjust threshold as needed
            {
                // On flat ground, reset the local Euler angles to zero
                alignTransform.localEulerAngles = Vector3.zero;
            }
            else
            {
                // On slopes, align with the ground normal
                if (movementDirection.sqrMagnitude > 0.001f)
                {
                    // Calculate the desired forward direction based on movement
                    Vector3 desiredForward = Vector3.ProjectOnPlane(movementDirection, groundNormal).normalized;

                    // Create a rotation that looks in the desired forward direction with the up vector aligned to the ground normal
                    Quaternion targetRotation = Quaternion.LookRotation(desiredForward, groundNormal);

                    // Smoothly interpolate to the target rotation
                    Quaternion newRotation = Quaternion.Slerp(alignTransform.rotation, targetRotation, alignmentSpeed * Time.fixedDeltaTime);

                    alignTransform.rotation = newRotation;
                }
                else
                {
                    // If there's no movement, just align the up vector
                    Quaternion targetRotation = Quaternion.FromToRotation(alignTransform.up, groundNormal) * alignTransform.rotation;
                    Quaternion newRotation = Quaternion.Slerp(alignTransform.rotation, targetRotation, alignmentSpeed * Time.fixedDeltaTime);
                    alignTransform.rotation = newRotation;
                }
            }
        }
    }




    /// <summary>
    /// Handles the input buffering for initiating and executing tricks.
    /// </summary>
    private void HandleBufferInput()
    {
        // Handle drifting input
        if (driftingPressed && !isDrifting)
        {
            StartDrifting();
        }
        if (!driftingPressed && isDrifting)
        {
            StopDrifting();
        }

        // Handle Trick Preparation and Execution
        if (!preparedForTrick && !trickOnCooldown && !isPerformingTrick)
        {
            // Detect when the right stick is pressed down (e.g., y < -0.5)
            if (rightStickInput.y < -0.5f)
            {
                preparedForTrick = true;
                animator.SetTrigger("PreparedForTrick");
                Debug.Log("Prepared for Trick!");

                // Start a coroutine to handle trick preparation timeout
                if (trickPreparationCoroutine != null)
                {
                    StopCoroutine(trickPreparationCoroutine);
                }
                trickPreparationCoroutine = StartCoroutine(TrickPreparationTimer());
            }
        }
        else if (preparedForTrick && !isPerformingTrick)
        {
            // Detect if the stick has been flicked in a direction
            if (rightStickInput.magnitude >= 0.5f && rightStickInput.y >= -0.5f)
            {
                if (rightStickInput.x > 0.5f && Mathf.Abs(rightStickInput.y) < 0.5f)
                {
                    // Right Flick - Kickflip
                    if (!isPerformingTrick)
                    {
                        PerformKickflip();
                        ResetTrickPreparation();
                    }
                }
                else if (rightStickInput.x < -0.5f && Mathf.Abs(rightStickInput.y) < 0.5f)
                {
                    // Left Flick - Pop Shuv It
                    if (!isPerformingTrick)
                    {
                        PerformPopShuvIt();
                        ResetTrickPreparation();
                    }
                }
                else if (rightStickInput.y > 0.5f)
                {
                    // Up Flick - Ollie
                    if (!isPerformingTrick && IsGrounded)
                    {
                        PerformOllie();
                        ResetTrickPreparation();
                    }
                }
                else
                {
                    // Ambiguous direction; do not perform any trick
                    // Optionally, provide feedback or handle as needed
                }
            }
            else if (rightStickInput.magnitude < 0.5f)
            {
                // Stick returned to center without a flick; start cancel timer
                if (trickCancelCoroutine == null)
                {
                    trickCancelCoroutine = StartCoroutine(TrickCancelTimer());
                }
            }
        }
    }

    /// <summary>
    /// Coroutine to handle the trick preparation timer.
    /// If the player does not perform a trick within the preparation time, cancel the preparation.
    /// </summary>
    private IEnumerator TrickPreparationTimer()
    {
        yield return new WaitForSeconds(trickPreparationTime);

        if (preparedForTrick && !isPerformingTrick)
        {
            // Preparation time elapsed without a valid flick; reset preparation
            ResetTrickPreparation();
            Debug.Log("Trick preparation timed out without a valid flick.");
            // Optionally, add feedback to indicate cancellation of Trick
        }
    }

    /// <summary>
    /// Coroutine to handle the trick cancellation after the stick returns to center.
    /// </summary>
    private IEnumerator TrickCancelTimer()
    {
        yield return new WaitForSeconds(trickCancelDelay);

        // Only reset if the stick is still centered after the delay
        if (rightStickInput.magnitude < 0.5f && preparedForTrick)
        {
            ResetTrickPreparation();
            Debug.Log("Trick preparation canceled due to stick returning to center.");
        }

        trickCancelCoroutine = null;
    }

    /// <summary>
    /// Resets the trick preparation state.
    /// </summary>
    private void ResetTrickPreparation()
    {
        preparedForTrick = false;
        animator.SetBool("PreparedForTrick", false);

        // Stop the preparation timer coroutine if it's still running
        if (trickPreparationCoroutine != null)
        {
            StopCoroutine(trickPreparationCoroutine);
            trickPreparationCoroutine = null;
        }

        // Stop the cancellation coroutine if it's running
        if (trickCancelCoroutine != null)
        {
            StopCoroutine(trickCancelCoroutine);
            trickCancelCoroutine = null;
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

        if (hitGround && !isPerformingTrick)
        {
            if (!IsGrounded)
            {
                animator.SetTrigger("Landing");
            }
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

        // Visualize the ground alignment ray
        if (alignTransform != null)
        {
            Gizmos.color = Color.blue;
            Vector3 rayOrigin = alignTransform.position + Vector3.up * alignmentOffset;
            Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.down * groundRayDistance);
        }
    }


    private bool CheckColliderGround(RaycastHit col)
    {
        //Debug.Log($"{Vector3.Dot(col.normal.normalized, Vector3.up)} bool result: {Vector3.Dot(col.normal.normalized, Vector3.up) >= 0.90f}");
        return (Vector3.Dot(col.normal.normalized, Vector3.up) >= 0.65f);
    }

    /// <summary>
    /// Starts the cooldown timer for performing tricks.
    /// </summary>
    private void StartTrickCooldown()
    {
        if (trickOnCooldown)
            return;

        StartCoroutine(TrickCooldownRoutine());
    }

    private IEnumerator TrickCooldownRoutine()
    {
        trickOnCooldown = true;
        yield return new WaitForSeconds(trickCooldown);
        trickOnCooldown = false;
    }

    private void HandleMovement(float currentTurnSpeed)
    {
        Vector3 forwardDirection = transform.forward;
        Vector3 currentVelocity = rb.linearVelocity; // Changed from rb.linearVelocity to rb.velocity

        // Remove sideways movement (keep only forward direction)
        Vector3 forwardVelocity = Vector3.Project(currentVelocity, forwardDirection);
        rb.linearVelocity = new Vector3(forwardVelocity.x, rb.linearVelocity.y, forwardVelocity.z); // Preserve gravity

        if (IsGrounded)
        {
            // Apply acceleration
            if (isAccelerating)
            {
                rb.AddForce(forwardDirection * acceleration, ForceMode.Acceleration);
            }
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

        if (IsGrounded)
        {
            // Apply rotation using Rigidbody.MoveRotation for physics compatibility
            Quaternion turnRotation = Quaternion.Euler(0f, turnAmount, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }

        if (!isDrifting)
        {
            foreach (TrailRenderer trail in driftTrails)
            {
                trail.emitting = false;
            }
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
            if (IsGrounded)
            {
                foreach (TrailRenderer trail in driftTrails)
                {
                    trail.emitting = true;
                }
            }
            float newZRotation = Mathf.MoveTowardsAngle(pivotPointDrift.localEulerAngles.y, driftTiltZ, driftTiltSpeed * Time.deltaTime);
            pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, newZRotation, pivotPointDrift.localEulerAngles.z);
        }
        if (!IsGrounded)
        {
            foreach (TrailRenderer trail in driftTrails)
            {
                trail.emitting = false;
            }
        }
    }

    private void HandleBraking()
    {
        rb.AddForce(-rb.linearVelocity.normalized * brakeForce, ForceMode.Acceleration);
        if (rb.linearVelocity.magnitude < 1f) state = State.Normal;
    }

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

    // Input Handlers
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
    private void PerformOllie()
    {
        isPerformingTrick = true;
        _coyoteTimer = coyoteTimeThreshold;

        // Apply upward force for Ollie
        if (IsGrounded)
        {
            rb.AddForce(Vector3.up * ollieForce * 1.5f, ForceMode.Impulse);
        }
        animator.SetTrigger("Ollie");
        // Immediately set IsGrounded to false and indicate a trick is in progress
        IsGrounded = false;

        // Start cooldown
        StartTrickCooldown();

        // Optional: Trigger animations, sounds, or particle effects here
        Debug.Log("Ollie performed!");

        // Reset the isPerformingTrick flag after a short delay to ensure accurate ground detection
        Invoke(nameof(ResetTrickState), 0.02f); // Adjust delay as needed
    }

    /// <summary>
    /// Executes a Kickflip by applying an upward and rotational force.
    /// </summary>
    private void PerformKickflip()
    {
        isPerformingTrick = true;
        _coyoteTimer = coyoteTimeThreshold;
        // Apply upward force for Kickflip
        if (IsGrounded)
        {
            rb.AddForce(Vector3.up * ollieForce, ForceMode.Impulse);
        }
        animator.SetTrigger("Kickflip");
        // Immediately set IsGrounded to false and indicate a trick is in progress
        IsGrounded = false;



        // Start cooldown
        StartTrickCooldown();

        // Optional: Trigger animations, sounds, or particle effects here
        Debug.Log("Kickflip performed!");

        // Reset the trick state after a short delay
        Invoke(nameof(ResetTrickState), 0.02f); // Adjust delay as needed
    }

    /// <summary>
    /// Executes a Pop Shuv It by applying a rotational force.
    /// </summary>
    private void PerformPopShuvIt()
    {
        isPerformingTrick = true;
        _coyoteTimer = coyoteTimeThreshold;
        // Apply upward force for Pop Shuv It
        if (IsGrounded)
        {
            rb.AddForce(Vector3.up * ollieForce, ForceMode.Impulse);
        }
        animator.SetTrigger("PopShuvIt");
        // Immediately set IsGrounded to false and indicate a trick is in progress
        IsGrounded = false;


        // Apply rotational force for Pop Shuv It

        // Start cooldown
        StartTrickCooldown();

        // Optional: Trigger animations, sounds, or particle effects here
        Debug.Log("Pop Shuv It performed!");

        // Reset the trick state after a short delay
        Invoke(nameof(ResetTrickState), 0.02f); // Adjust delay as needed
    }

    /// <summary>
    /// Resets the trick state after a short delay.
    /// </summary>
    private void ResetTrickState()
    {
        isPerformingTrick = false;
    }
}
