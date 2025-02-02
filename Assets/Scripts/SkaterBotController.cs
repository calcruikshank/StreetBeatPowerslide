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
    private float brakeForce = 80f;

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
    [Header("Boost Settings")]
    [SerializeField] private float boostAmount = 10f; // Boost added per trick
    private float boostDecayRate = 5f; // How quickly the boost degrades
    [SerializeField] private float maxBoost = 60f; // Maximum boost cap
    [SerializeField] private float driftBoostMultiplier = 10f; // Boost per second of drifting

    private float currentBoost = 0f; // Current boost value
    private int trickCount = 0; // Number of tricks performed before landing
    private float driftTime = 0f; // Time spent drifting
    private bool hasLeftGround = false;
    [Header("Downforce Settings")]
    [SerializeField] private float downforceRayMultiplier = 0.5f; // Multiplier for ray distance (half the collider height)
    private float significantDownforce = 50f;   // Force applied when the ray doesn't hit ground
    [Header("Braking Turn Settings")]
    [SerializeField] private float brakingTurnSpeed = 300f; // Speed at which the board rotates during braking
    [SerializeField] private float brakingTiltAngle = 90f;    // Target tilt angle (in degrees) for a 90° turn

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

        // Track drift time
        if (isDrifting)
        {
            driftTime += Time.deltaTime;
        }
    }

    private void FixedUpdate()
    {
        // Handle state-based movement.
        switch (state)
        {
            case State.Normal:
                HandleMovement(turnSpeed);
                break;
            case State.Braking:
                // Call braking logic.
                HandleBraking();
                // Also handle a 90° braking turn.
                HandleBrakingTurn();
                break;
            case State.Drifting:
                HandleMovement(driftTurnSpeed);
                break;
        }

        AlignWithGround(); // Keep the board aligned with slopes

        // Only apply downforce when grounded and not performing a trick
        if (IsGrounded && !isPerformingTrick)
        {
            ApplySignificantDownforce();
        }
    }


    /// <summary>
    /// Casts a short ray down from the collider’s center (using boxOffset)
    /// and if it doesn’t hit the ground, applies significant downforce to keep the board stuck.
    /// </summary>
    private void ApplySignificantDownforce()
    {
        // Only do this when our collider indicates ground contact.
        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);

        // Calculate a short ray distance (half the vertical size of the collider check)
        float rayDistance = boxHalfExtents.y * downforceRayMultiplier;

        // Shoot a ray downward from the box center.
        if (Physics.Raycast(boxCenter, Vector3.down, rayDistance, groundLayerMask))
        {
            // The ray missed ground—even though the box is colliding.
            // Apply a strong downward force.
            rb.AddForce(Vector3.down * significantDownforce, ForceMode.Acceleration);
        }
    }/// <summary>
     /// Rotates the board’s pivot toward a 90° tilt (left or right) when braking,
     /// similar to the drift tilt logic.
     /// </summary>
    private void HandleBrakingTurn()
    {
        float brakeTiltZ = 90;
        if (IsGrounded)
        {
            foreach (TrailRenderer trail in driftTrails)
            {
                trail.emitting = true;
            }
        }
        float newZRotation = Mathf.MoveTowardsAngle(pivotPointDrift.localEulerAngles.y, brakeTiltZ, driftTiltSpeed * Time.deltaTime);
        pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, newZRotation, pivotPointDrift.localEulerAngles.z);
    }

    private void HandleBraking()
    {
        pivotTiltZ = 0f;
        Vector3 currentRotation = pivotPoint.localEulerAngles;
        pivotPoint.localEulerAngles = new Vector3(currentRotation.x, currentRotation.y, pivotTiltZ);
        rb.AddForce(-rb.linearVelocity.normalized * brakeForce, ForceMode.Acceleration);
        if (rb.linearVelocity.magnitude < 1f)
            state = State.Normal;
    }

    /// <summary>
    /// Aligns the skater's orientation with the ground's normal using physics-based rotation.
    /// </summary>
    /// 
    [SerializeField] public Transform alignTransform;
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
        if (brakingPressed)
        {
            float newZRotation = Mathf.MoveTowardsAngle(pivotPointDrift.localEulerAngles.y, 90, driftTiltSpeed * Time.deltaTime);
            pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, newZRotation, pivotPointDrift.localEulerAngles.z);
        }
        // If braking is pressed (and not drifting), set state to braking.
        if (brakingPressed && !isDrifting && IsGrounded)
        {
            state = State.Braking;
        }
        if (state == State.Braking && !IsGrounded || state == State.Braking && _coyoteTimer > 0)
        {
            state = State.Normal;
        }
        if (!brakingPressed)
        {
            state = State.Normal;
        }
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
        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);
        bool hitGround = Physics.CheckBox(boxCenter, boxHalfExtents, transform.rotation, groundLayerMask, QueryTriggerInteraction.Ignore);

        DrawBox(boxCenter, boxHalfExtents * 2, hitGround ? Color.green : Color.red);

        if (hitGround && !isPerformingTrick)
        {
            // Only trigger landing if we were airborne (hasLeftGround is true)
            if (!IsGrounded && hasLeftGround)
            {
                animator.SetTrigger("Landing");

                // Apply boost when landing after performing tricks
                if (trickCount > 0)
                {
                    Vector3 boostDirection = rb.linearVelocity.normalized;
                    rb.linearVelocity += boostDirection * (boostAmount * trickCount);
                    currentBoost += boostAmount * trickCount;
                    currentBoost = Mathf.Clamp(currentBoost, 0f, maxBoost);
                    trickCount = 0; // Reset trick count after applying boost
                }
            }
            IsGrounded = true;
            _coyoteTimer = 0f;
            hasLeftGround = false; // Reset the flag since we are on the ground
        }
        else
        {
            // Increase the coyote timer (and eventually mark as not grounded)
            if (_coyoteTimer < coyoteTimeThreshold)
            {
                _coyoteTimer += Time.deltaTime;
                if (_coyoteTimer >= coyoteTimeThreshold)
                {
                    IsGrounded = false;
                }
            }

            // Mark that we have left the ground (airborne)
            if (!hitGround)
            {
                hasLeftGround = true;
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
    private void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 20;
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(10, 10, 200, 50), $"Boost: {rb.linearVelocity.magnitude:F1}", style);
    }
    private void HandleMovement(float currentTurnSpeed)
    {
        Vector3 forwardDirection = alignTransform.forward;
        Vector3 currentVelocity = rb.linearVelocity;

        if (IsGrounded)
        {
            Vector3 forwardVelocity = Vector3.Project(currentVelocity, forwardDirection);
            rb.linearVelocity = new Vector3(forwardVelocity.x, rb.linearVelocity.y, forwardVelocity.z);
            // Apply acceleration
            if (isAccelerating)
            {
                rb.AddForce(forwardDirection * acceleration, ForceMode.Acceleration);
            }
        }

        // Clamp speed but preserve falling speed
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        // Allow speed up to maxSpeed + currentBoost
        if (horizontalVelocity.magnitude > maxSpeed + currentBoost)
        {
            Vector3 clampedVelocity = horizontalVelocity.normalized * (maxSpeed + currentBoost);
            rb.linearVelocity = new Vector3(clampedVelocity.x, rb.linearVelocity.y, clampedVelocity.z);
        }

        // Decay boost over time
        if (currentBoost > 0f)
        {
            currentBoost -= boostDecayRate * Time.fixedDeltaTime;
            currentBoost = Mathf.Max(currentBoost, 0f);
        }

        // REMOVED: The second clamp that resets speed to maxSpeed only.
        // if (horizontalVelocity.magnitude > maxSpeed)
        // {
        //     Vector3 clampedVelocity = horizontalVelocity.normalized * maxSpeed;
        //     rb.linearVelocity = new Vector3(clampedVelocity.x, rb.linearVelocity.y, clampedVelocity.z);
        // }

        float turnAmount = inputMovement.x * currentTurnSpeed * Time.fixedDeltaTime;

        if (isDrifting)
        {
            // Only invert turning when drifting to the right
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

            if (!brakingPressed)
            {
                float newZRotation = Mathf.MoveTowardsAngle(pivotPointDrift.localEulerAngles.y, 0, driftTiltSpeed * Time.deltaTime);
                pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, newZRotation, pivotPointDrift.localEulerAngles.z);
            }
        }

        // Smoothly transition drift tilt (drifting effect)
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


    private void StartDrifting()
    {
        pivotTiltZ = 0f;
        Vector3 currentRotation = pivotPoint.localEulerAngles;
        pivotPoint.localEulerAngles = new Vector3(currentRotation.x, currentRotation.y, pivotTiltZ);

        driftDirection = Mathf.Sign(inputMovement.x);
        if (driftDirection == 0) driftDirection = 1f;

        isDrifting = true;
        state = State.Drifting;
        driftTiltZ = driftDirection > 0 ? 50f : -50f;

        driftTime = 0f; // Reset drift time when starting a drift
    }

    private void StopDrifting()
    {
        isDrifting = false;
        state = State.Normal;
        driftTiltZ = 0f;

        // Apply boost based on drift time
        if (driftTime > 0f)
        {
            // Add boost to current velocity
            Vector3 boostDirection = rb.linearVelocity.normalized;
            rb.linearVelocity += boostDirection * (driftTime * driftBoostMultiplier);
            currentBoost += driftTime * driftBoostMultiplier;
            currentBoost = Mathf.Clamp(currentBoost, 0f, maxBoost);
        }

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
    public bool brakingPressed;
    private void OnB(InputValue value)
    {
        brakingPressed = true;
    }

    private void OnBReleased(InputValue value)
    {
        brakingPressed = false;
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

        if (IsGrounded)
        {
            rb.AddForce(Vector3.up * ollieForce * 1.5f, ForceMode.Impulse);
        }
        animator.SetTrigger("Ollie");
        IsGrounded = false;

        StartTrickCooldown();

        Debug.Log("Ollie performed!");
        Invoke(nameof(ResetTrickState), 0.02f);
    }

    private void PerformKickflip()
    {
        isPerformingTrick = true;
        _coyoteTimer = coyoteTimeThreshold;

        if (IsGrounded)
        {
            rb.AddForce(Vector3.up * ollieForce, ForceMode.Impulse);
        }
        animator.SetTrigger("Kickflip");
        IsGrounded = false;

        trickCount++; // Increment trick count
        StartTrickCooldown();

        Debug.Log("Kickflip performed!");
        Invoke(nameof(ResetTrickState), 0.02f);
    }

    private void PerformPopShuvIt()
    {
        isPerformingTrick = true;
        _coyoteTimer = coyoteTimeThreshold;

        if (IsGrounded)
        {
            rb.AddForce(Vector3.up * ollieForce, ForceMode.Impulse);
        }
        animator.SetTrigger("PopShuvIt");
        IsGrounded = false;

        trickCount++; // Increment trick count
        StartTrickCooldown();

        Debug.Log("Pop Shuv It performed!");
        Invoke(nameof(ResetTrickState), 0.02f);
    }
    /// <summary>
    /// Resets the trick state after a short delay.
    /// </summary>
    private void ResetTrickState()
    {
        isPerformingTrick = false;
    }
}
