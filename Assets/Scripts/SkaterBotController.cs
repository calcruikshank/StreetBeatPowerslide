using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SkaterBotController : MonoBehaviour
{
    [Header("Board Settings")]
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float maxSpeed = 30f;
    [SerializeField] private float turnSpeed = 100f;
    [SerializeField] private float driftTurnSpeed = 200f;
    [SerializeField] private float friction = 0.5f;
    [SerializeField] private float brakeForce = 80f;

    [Header("Trick Settings")]
    [SerializeField] private float ollieForce = 5f;
    [SerializeField] private float trickCooldown = 1f;
    [SerializeField] private float trickPreparationTime = 1f;
    [SerializeField] private float trickCancelDelay = 0.1f;

    private Rigidbody rb;
    private Vector2 inputMovement;
    private Vector2 rightStickInput;

    [SerializeField] Transform pivotPointDrift;

    private bool isAccelerating = false;
    private bool isDrifting = false;

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
    [SerializeField] private Vector3 boxOffset = new Vector3(0, -0.3f, 0);
    [SerializeField] private LayerMask groundLayerMask;

    private bool isPerformingTrick = false;

    [SerializeField] Animator animator;

    // Drift Variables
    private float driftDirection = 0f;
    private float pivotTiltZ = 0f;
    [SerializeField] private float tiltSpeed = 5f;
    [SerializeField] private float driftTiltSpeed = 1500f;
    private float driftTiltZ = 0f;

    // Trick Preparation Variables
    public bool preparedForTrick = false;
    private Coroutine trickPreparationCoroutine;
    private Coroutine trickCancelCoroutine;

    // Ground Alignment Variables
    [Header("Ground Alignment Settings")]
    [SerializeField] private float groundRayDistance = 2f;
    [SerializeField] private float alignmentSpeed = 5f;
    [SerializeField] private float alignmentOffset = 0.5f;
    [SerializeField] private Vector3 rotationConstraints = new Vector3(0f, 360f, 0f);

    [SerializeField] Transform alignTransform;
    Vector3 movementDirection; // Used for aligning movement on slopes

    // Landing Boost Variables
    [Header("Landing Boost Settings")]
    // Extra speed per boost level (each landing adds +20 units per level)
    [SerializeField] private float boostBonusPerLevel = 20f;
    // Duration (in seconds) the bonus remains at full strength before decaying
    [SerializeField] private float boostFullDuration = 1f;
    // Rate at which the bonus decays (units per second)
    [SerializeField] private float boostDecayRate = 10f;
    // Current boost level (max 3)
    private int boostLevel = 0;
    // Active bonus added to maxSpeed
    private float currentBoostBonus = 0f;
    // Timer for the full bonus duration
    private float boostTimer = 0f;

    // Prevent landing boost immediately after a trick
    private bool trickJustHappened = false;
    [SerializeField] private float trickLandingDelay = 0.2f; // Delay before allowing landing boost

    // Used to detect landing transitions
    private bool wasGrounded = true;

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

        AlignWithGround();
        UpdateBoost();

        // Save the grounded state for the next frame
        wasGrounded = IsGrounded;
    }

    /// <summary>
    /// Aligns the skater's orientation with the ground normal.
    /// </summary>
    private void AlignWithGround()
    {
        if (!IsGrounded || _coyoteTimer > 0)
            return;

        Ray ray = new Ray(transform.position + Vector3.up * alignmentOffset, Vector3.down);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, groundRayDistance, groundLayerMask))
        {
            Vector3 groundNormal = hit.normal;
            if (Vector3.Dot(groundNormal, Vector3.up) > 0.999f)
            {
                alignTransform.localEulerAngles = Vector3.zero;
            }
            else
            {
                if (movementDirection.sqrMagnitude > 0.001f)
                {
                    Vector3 desiredForward = Vector3.ProjectOnPlane(movementDirection, groundNormal).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(desiredForward, groundNormal);
                    Quaternion newRotation = Quaternion.Slerp(alignTransform.rotation, targetRotation, alignmentSpeed * Time.fixedDeltaTime);
                    alignTransform.rotation = newRotation;
                }
                else
                {
                    Quaternion targetRotation = Quaternion.FromToRotation(alignTransform.up, groundNormal) * alignTransform.rotation;
                    Quaternion newRotation = Quaternion.Slerp(alignTransform.rotation, targetRotation, alignmentSpeed * Time.fixedDeltaTime);
                    alignTransform.rotation = newRotation;
                }
            }
        }
    }

    /// <summary>
    /// Handles input buffering for tricks and drifting.
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

        // Trick preparation and execution
        if (!preparedForTrick && !trickOnCooldown && !isPerformingTrick)
        {
            if (rightStickInput.y < -0.5f)
            {
                preparedForTrick = true;
                animator.SetTrigger("PreparedForTrick");
                Debug.Log("Prepared for Trick!");

                if (trickPreparationCoroutine != null)
                {
                    StopCoroutine(trickPreparationCoroutine);
                }
                trickPreparationCoroutine = StartCoroutine(TrickPreparationTimer());
            }
        }
        else if (preparedForTrick && !isPerformingTrick)
        {
            if (rightStickInput.magnitude >= 0.5f && rightStickInput.y >= -0.5f)
            {
                if (rightStickInput.x > 0.5f && Mathf.Abs(rightStickInput.y) < 0.5f)
                {
                    if (!isPerformingTrick)
                    {
                        PerformKickflip();
                        ResetTrickPreparation();
                    }
                }
                else if (rightStickInput.x < -0.5f && Mathf.Abs(rightStickInput.y) < 0.5f)
                {
                    if (!isPerformingTrick)
                    {
                        PerformPopShuvIt();
                        ResetTrickPreparation();
                    }
                }
                else if (rightStickInput.y > 0.5f)
                {
                    if (!isPerformingTrick && IsGrounded)
                    {
                        PerformOllie();
                        ResetTrickPreparation();
                    }
                }
            }
            else if (rightStickInput.magnitude < 0.5f)
            {
                if (trickCancelCoroutine == null)
                {
                    trickCancelCoroutine = StartCoroutine(TrickCancelTimer());
                }
            }
        }
    }

    private IEnumerator TrickPreparationTimer()
    {
        yield return new WaitForSeconds(trickPreparationTime);
        if (preparedForTrick && !isPerformingTrick)
        {
            ResetTrickPreparation();
            Debug.Log("Trick preparation timed out without a valid flick.");
        }
    }

    private IEnumerator TrickCancelTimer()
    {
        yield return new WaitForSeconds(trickCancelDelay);
        if (rightStickInput.magnitude < 0.5f && preparedForTrick)
        {
            ResetTrickPreparation();
            Debug.Log("Trick preparation canceled due to stick returning to center.");
        }
        trickCancelCoroutine = null;
    }

    private void ResetTrickPreparation()
    {
        preparedForTrick = false;
        animator.SetBool("PreparedForTrick", false);

        if (trickPreparationCoroutine != null)
        {
            StopCoroutine(trickPreparationCoroutine);
            trickPreparationCoroutine = null;
        }
        if (trickCancelCoroutine != null)
        {
            StopCoroutine(trickCancelCoroutine);
            trickCancelCoroutine = null;
        }
    }

    /// <summary>
    /// Checks for ground contact using a box overlap.
    /// </summary>
    private void CheckForGround()
    {
        bool previouslyGrounded = IsGrounded;

        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);
        bool hitGround = Physics.CheckBox(boxCenter, boxHalfExtents, transform.rotation, groundLayerMask, QueryTriggerInteraction.Ignore);
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

        // Only trigger landing boost if we just landed and a trick wasn�t just performed.
        if (!previouslyGrounded && IsGrounded && !trickJustHappened)
        {
            OnLanding();
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
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);
        Gizmos.DrawWireCube(boxCenter, boxHalfExtents * 2);

        if (alignTransform != null)
        {
            Gizmos.color = Color.blue;
            Vector3 rayOrigin = alignTransform.position + Vector3.up * alignmentOffset;
            Gizmos.DrawLine(rayOrigin, rayOrigin + Vector3.down * groundRayDistance);
        }
    }

    private bool CheckColliderGround(RaycastHit col)
    {
        return (Vector3.Dot(col.normal.normalized, Vector3.up) >= 0.65f);
    }

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

    /// <summary>
    /// Handles movement including acceleration, turning, and clamping the speed.
    /// The effective max speed is the base maxSpeed plus any active boost bonus.
    /// </summary>
    private void HandleMovement(float currentTurnSpeed)
    {
        Vector3 forwardDirection = transform.forward;
        Vector3 currentVelocity = rb.linearVelocity;
        Vector3 forwardVelocity = Vector3.Project(currentVelocity, forwardDirection);
        rb.linearVelocity = new Vector3(forwardVelocity.x, rb.linearVelocity.y, forwardVelocity.z);

        if (IsGrounded)
        {
            if (isAccelerating)
            {
                rb.AddForce(forwardDirection * acceleration, ForceMode.Acceleration);
            }
        }

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float effectiveMaxSpeed = maxSpeed + currentBoostBonus;
        if (horizontalVelocity.magnitude > effectiveMaxSpeed)
        {
            Vector3 clampedVelocity = horizontalVelocity.normalized * effectiveMaxSpeed;
            rb.linearVelocity = new Vector3(clampedVelocity.x, rb.linearVelocity.y, clampedVelocity.z);
        }

        float turnAmount = inputMovement.x * currentTurnSpeed * Time.fixedDeltaTime;
        if (isDrifting)
        {
            float driftTurnInput = (driftDirection <= 0) ? -inputMovement.x : inputMovement.x;
            float clampedTurn = Mathf.Clamp(driftTurnInput, driftDirection * 0.3f, driftDirection * 1.0f);
            turnAmount = clampedTurn * currentTurnSpeed * Time.fixedDeltaTime;
        }

        if (IsGrounded)
        {
            Quaternion turnRotation = Quaternion.Euler(0f, turnAmount, 0f);
            rb.MoveRotation(rb.rotation * turnRotation);
        }

        if (!isDrifting)
        {
            foreach (TrailRenderer trail in driftTrails)
            {
                trail.emitting = false;
            }
            float targetZRotation = Mathf.Lerp(10f, -10f, (inputMovement.x + 1f) / 2f);
            pivotTiltZ = Mathf.MoveTowards(pivotTiltZ, targetZRotation, tiltSpeed * Time.deltaTime);
            pivotPoint.localEulerAngles = new Vector3(pivotPoint.localEulerAngles.x, pivotPoint.localEulerAngles.y, pivotTiltZ);
            float newZRotation = Mathf.MoveTowardsAngle(pivotPointDrift.localEulerAngles.y, 0, driftTiltSpeed * Time.deltaTime);
            pivotPointDrift.localEulerAngles = new Vector3(pivotPointDrift.localEulerAngles.x, newZRotation, pivotPointDrift.localEulerAngles.z);
        }

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
        if (rb.linearVelocity.magnitude < 1f)
            state = State.Normal;
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
    }

    private void StopDrifting()
    {
        isDrifting = false;
        state = State.Normal;
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
        movementDirection = new Vector3(inputMovement.x, 0f, inputMovement.y);
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

    // Trick Methods
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
        StartTrickCooldown();
        Debug.Log("Pop Shuv It performed!");
        Invoke(nameof(ResetTrickState), 0.02f);
    }

    /// <summary>
    /// Resets the trick state and sets a flag to prevent an immediate landing boost.
    /// </summary>
    private void ResetTrickState()
    {
        isPerformingTrick = false;
        trickJustHappened = true;
        StartCoroutine(ResetTrickJustHappened());
    }

    private IEnumerator ResetTrickJustHappened()
    {
        yield return new WaitForSeconds(trickLandingDelay);
        trickJustHappened = false;
    }

    /// <summary>
    /// Updates the landing boost bonus. Once the full bonus duration elapses, the bonus decays gradually.
    /// </summary>
    private void UpdateBoost()
    {
        if (boostTimer > 0)
        {
            boostTimer -= Time.fixedDeltaTime;
            if (boostTimer < 0)
                boostTimer = 0;
        }
        else
        {
            if (currentBoostBonus > 0)
            {
                currentBoostBonus -= boostDecayRate * Time.fixedDeltaTime;
                if (currentBoostBonus < 0)
                    currentBoostBonus = 0;
                if (currentBoostBonus == 0)
                {
                    boostLevel = 0;
                }
            }
        }
    }

    /// <summary>
    /// Called when landing occurs (transition from airborne to grounded).
    /// Increments boost level (up to 3), sets the bonus, and applies an immediate forward impulse.
    /// </summary>
    private void OnLanding()
    {
        boostLevel = Mathf.Clamp(boostLevel + 1, 0, 3);
        currentBoostBonus = boostLevel * boostBonusPerLevel;
        boostTimer = boostFullDuration;

        // Optionally, add an immediate forward impulse.
        rb.linearVelocity += transform.forward * (boostBonusPerLevel * boostLevel);
        Debug.Log("Landing boost applied: Level " + boostLevel + ", Bonus = " + currentBoostBonus);
    }
}
