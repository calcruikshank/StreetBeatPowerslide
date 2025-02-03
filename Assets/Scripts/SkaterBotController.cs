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
    private float brakeForce = 40f;

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
    public float _coyoteTimer;
    public bool IsGrounded { get; private set; }

    [Header("Ground Check Settings")]
    [SerializeField] private Vector3 boxHalfExtents = new Vector3(0.5f, 0.1f, 0.5f);
    [SerializeField] private Vector3 boxOffset = new Vector3(0, -0.3f, 0);
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

    // Ground Alignment
    [Header("Ground Alignment Settings")]
    [SerializeField] private float groundRayDistance = 2f;
    [SerializeField] private float alignmentSpeed = 5f;
    [SerializeField] private float alignmentOffset = 0.5f;

    [SerializeField] private Vector3 rotationConstraints = new Vector3(0f, 360f, 0f);

    // Boost Settings
    [Header("Boost Settings")]
    [SerializeField] private float boostAmount = 10f;
    private float boostDecayRate = 5f;
    [SerializeField] private float maxBoost = 60f;
    [SerializeField] private float driftBoostMultiplier = 10f;

    private float currentBoost = 0f;
    private int trickCount = 0;
    private float driftTime = 0f;
    private bool hasLeftGround = false;

    [Header("Downforce Settings")]
    [SerializeField] private float downforceRayMultiplier = 0.5f;
    private float significantDownforce = 50f;

    [Header("Braking Turn Settings")]
    [SerializeField] private float brakingTurnSpeed = 300f;
    [SerializeField] private float brakingTiltAngle = 90f;

    // AUDIO
    [Header("Audio References")]
    AudioSource sfxSource;      // For one-shot SFX
    AudioSource musicSource;    // For background/looping audio

    // Drag your clips in from the Inspector
    [SerializeField] private AudioClip mainThemeClip;

    [SerializeField] private AudioClip ollieClip;
    [SerializeField] private AudioClip tailSlapClip;

    // Existing "combo" clips
    [SerializeField] private AudioClip combo1Clip; // Possibly your "lowest boost" combo
    [SerializeField] private AudioClip combo2Clip; // ...
    // NEW: Add references for combo3 and combo4
    [SerializeField] private AudioClip combo3Clip;
    [SerializeField] private AudioClip combo4Clip;

    [SerializeField] private AudioClip grindStartClip;
    [SerializeField] private AudioClip grindLoopClip;
    [SerializeField] private AudioClip turn1Clip;
    [SerializeField] private AudioClip turn2Clip;
    [SerializeField] private AudioClip turn3Clip;

    [SerializeField] public Transform alignTransform;
    Vector3 movementDirection;

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

        // Get references to the audio sources from the GameManager (if that’s how your project is set up):
        sfxSource = GameManager.instance.sfxSource;
        musicSource = GameManager.instance.musicSource;
    }

    private void Start()
    {
        // Optionally start the main theme music
        if (musicSource != null && mainThemeClip != null)
        {
            musicSource.clip = mainThemeClip;
            musicSource.loop = true;
            musicSource.Play();
        }

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
        // If a gamepad is connected, treat it as if the RMB is held down.
        if (Gamepad.current != null)
        {
            rmbPresssed = true;
        }

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
        // Handle state-based movement
        switch (state)
        {
            case State.Normal:
                HandleMovement(turnSpeed);
                break;
            case State.Braking:
                HandleBraking();
                HandleBrakingTurn();
                break;
            case State.Drifting:
                HandleMovement(driftTurnSpeed);
                break;
        }

        AlignWithGround();

        // Only apply downforce when grounded and not performing a trick
        if (IsGrounded && !isPerformingTrick)
        {
            ApplySignificantDownforce();
        }
    }

    // ------------------------------------------------
    //  HELPER: Plays a "combo" clip based on currentBoost
    // ------------------------------------------------
    private void PlayComboClipBasedOnBoost()
    {
        if (sfxSource == null) return; // Safety check

        // Adjust thresholds or ranges as you like:
        if (currentBoost < 10f)
        {
            // Lowest-level combo
            if (combo1Clip) sfxSource.PlayOneShot(combo1Clip);
        }
        else if (currentBoost < 20f)
        {
            if (combo2Clip) sfxSource.PlayOneShot(combo2Clip);
        }
        else if (currentBoost < 30f)
        {
            if (combo3Clip) sfxSource.PlayOneShot(combo3Clip);
        }
        else
        {
            // Highest-level combo
            if (combo4Clip) sfxSource.PlayOneShot(combo4Clip);
        }
    }

    /// <summary>
    /// Casts a short ray down from the collider’s center and if it doesn’t hit ground, applies significant downforce.
    /// </summary>
    private void ApplySignificantDownforce()
    {
        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);
        float rayDistance = boxHalfExtents.y * downforceRayMultiplier;

        if (Physics.Raycast(boxCenter, Vector3.down, out RaycastHit hit, rayDistance, groundLayerMask))
        {
            Vector3 downforceDirection = -hit.normal;
            rb.AddForce(downforceDirection * significantDownforce, ForceMode.Acceleration);
        }
    }

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
        float newZRotation = Mathf.MoveTowardsAngle(
            pivotPointDrift.localEulerAngles.y,
            brakeTiltZ,
            driftTiltSpeed * Time.deltaTime
        );
        pivotPointDrift.localEulerAngles = new Vector3(
            pivotPointDrift.localEulerAngles.x,
            newZRotation,
            pivotPointDrift.localEulerAngles.z
        );

        pivotTiltZ = 0f;
        Vector3 currentRotation = pivotPoint.localEulerAngles;
        pivotPoint.localEulerAngles = new Vector3(
            currentRotation.x,
            currentRotation.y,
            pivotTiltZ
        );
    }

    private void HandleBraking()
    {
        rb.AddForce(-rb.linearVelocity.normalized * brakeForce, ForceMode.Acceleration);
        if (rb.linearVelocity.magnitude < 1f)
            state = State.Normal;
    }

    private void AlignWithGround()
    {
        if (!IsGrounded || _coyoteTimer > 0) return;

        Ray ray = new Ray(transform.position + Vector3.up * alignmentOffset, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, groundRayDistance, groundLayerMask))
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
    /// Handles input for tricks, drifting, braking, etc.
    /// </summary>
    private void HandleBufferInput()
    {
        // Braking
        if (brakingPressed)
        {
            if (!isDrifting)
            {
                float newZRotation = Mathf.MoveTowardsAngle(
                    pivotPointDrift.localEulerAngles.y,
                    90,
                    driftTiltSpeed * Time.deltaTime
                );
                pivotPointDrift.localEulerAngles = new Vector3(
                    pivotPointDrift.localEulerAngles.x,
                    newZRotation,
                    pivotPointDrift.localEulerAngles.z
                );
            }
        }
        if (brakingPressed && !isDrifting && IsGrounded && state != State.Drifting)
        {
            state = State.Braking;
        }
        if (state == State.Braking && (!IsGrounded || _coyoteTimer > 0))
        {
            state = State.Normal;
        }
        if (!brakingPressed)
        {
            state = State.Normal;
        }

        // Drifting input
        if (driftingPressed && !isDrifting)
        {
            StartDrifting();
        }
        if (!driftingPressed && isDrifting)
        {
            StopDrifting();
        }

        // Right Mouse Button -> Trick logic
        if (rmbPresssed)
        {
            if (!preparedForTrick && !trickOnCooldown && !isPerformingTrick)
            {
                // Check for "push right stick down" as a sign of preparing a trick
                if (rightStickInput.y < -0.5f)
                {
                    preparedForTrick = true;
                    animator.SetTrigger("PreparedForTrick");
                    Debug.Log("Prepared for Trick!");

                    if (trickPreparationCoroutine != null)
                        StopCoroutine(trickPreparationCoroutine);

                    trickPreparationCoroutine = StartCoroutine(TrickPreparationTimer());
                }
            }
            else if (preparedForTrick && !isPerformingTrick)
            {
                // Check if the stick has been flicked in a direction
                if (rightStickInput.magnitude >= 0.5f && rightStickInput.y >= -0.5f)
                {
                    if (rightStickInput.x > 0.5f && Mathf.Abs(rightStickInput.y) < 0.5f)
                    {
                        // Right Flick = Kickflip
                        if (!isPerformingTrick)
                        {
                            PerformKickflip();
                            ResetTrickPreparation();
                        }
                    }
                    else if (rightStickInput.x < -0.5f && Mathf.Abs(rightStickInput.y) < 0.5f)
                    {
                        // Left Flick = Pop Shuv It
                        if (!isPerformingTrick)
                        {
                            PerformPopShuvIt();
                            ResetTrickPreparation();
                        }
                    }
                    else if (rightStickInput.y > 0.5f)
                    {
                        // Up Flick = Ollie
                        if (!isPerformingTrick && IsGrounded)
                        {
                            PerformOllie();
                            ResetTrickPreparation();
                        }
                    }
                    // else ambiguous
                }
                else if (rightStickInput.magnitude < 0.5f)
                {
                    // Stick returned to center: start cancel timer
                    if (trickCancelCoroutine == null)
                    {
                        trickCancelCoroutine = StartCoroutine(TrickCancelTimer());
                    }
                }
            }
        }
        else
        {
            // If RMB not held, cancel any trick preparation
            if (preparedForTrick)
            {
                ResetTrickPreparation();
                Debug.Log("RMB not held, trick preparation cancelled.");
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

    private void CheckForGround()
    {
        Vector3 boxCenter = transform.position + transform.TransformDirection(boxOffset);
        bool hitGround = Physics.CheckBox(
            boxCenter,
            boxHalfExtents,
            transform.rotation,
            groundLayerMask,
            QueryTriggerInteraction.Ignore
        );

        DrawBox(boxCenter, boxHalfExtents * 2, hitGround ? Color.green : Color.red);

        if (hitGround && !isPerformingTrick)
        {
            // Only trigger landing if we WERE airborne
            if (!IsGrounded && hasLeftGround)
            {
                animator.SetTrigger("Landing");

                // Optionally play a tail-slap or landing sound
                if (sfxSource != null && tailSlapClip != null)
                {
                    sfxSource.PlayOneShot(tailSlapClip);
                }

                // Apply boost after performing tricks
                if (trickCount > 0)
                {
                    Vector3 boostDirection = rb.linearVelocity.normalized;
                    rb.linearVelocity += boostDirection * (boostAmount * trickCount);
                    currentBoost += boostAmount * trickCount;
                    currentBoost = Mathf.Clamp(currentBoost, 0f, maxBoost);
                    trickCount = 0;
                }
            }
            IsGrounded = true;
            _coyoteTimer = 0f;
            hasLeftGround = false;
        }
        else
        {
            if (_coyoteTimer < coyoteTimeThreshold)
            {
                _coyoteTimer += Time.deltaTime;
                if (_coyoteTimer >= coyoteTimeThreshold)
                    IsGrounded = false;
            }
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

    private bool CheckColliderGround(RaycastHit col)
    {
        return (Vector3.Dot(col.normal.normalized, Vector3.up) >= 0.65f);
    }

    /// <summary>
    /// Starts the cooldown timer for performing tricks.
    /// </summary>
    private void StartTrickCooldown()
    {
        if (trickOnCooldown) return;
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
            if (isAccelerating)
            {
                rb.AddForce(forwardDirection * acceleration, ForceMode.Acceleration);
            }
        }

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        if (horizontalVelocity.magnitude > maxSpeed + currentBoost)
        {
            Vector3 clampedVelocity = horizontalVelocity.normalized * (maxSpeed + currentBoost);
            rb.linearVelocity = new Vector3(clampedVelocity.x, rb.linearVelocity.y, clampedVelocity.z);
        }

        // Decay boost
        if (currentBoost > 0f)
        {
            currentBoost -= boostDecayRate * Time.fixedDeltaTime;
            currentBoost = Mathf.Max(currentBoost, 0f);
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

            // Optionally: Quick turn SFX if you'd like
            // if (Mathf.Abs(turnAmount) > 1f && turn1Clip != null)
            // {
            //     sfxSource.PlayOneShot(turn1Clip);
            // }
        }

        // Non-drifting tilt
        if (!isDrifting)
        {
            foreach (TrailRenderer trail in driftTrails)
            {
                trail.emitting = false;
            }
            float targetZRotation = Mathf.Lerp(10f, -10f, (inputMovement.x + 1f) / 2f);
            pivotTiltZ = Mathf.MoveTowards(pivotTiltZ, targetZRotation, tiltSpeed * Time.deltaTime);
            pivotPoint.localEulerAngles = new Vector3(
                pivotPoint.localEulerAngles.x,
                pivotPoint.localEulerAngles.y,
                pivotTiltZ
            );

            if (!brakingPressed)
            {
                float newZRotation = Mathf.MoveTowardsAngle(
                    pivotPointDrift.localEulerAngles.y,
                    0,
                    driftTiltSpeed * Time.deltaTime
                );
                pivotPointDrift.localEulerAngles = new Vector3(
                    pivotPointDrift.localEulerAngles.x,
                    newZRotation,
                    pivotPointDrift.localEulerAngles.z
                );
            }
        }

        // Drifting tilt
        if (isDrifting)
        {
            if (IsGrounded)
            {
                if (brakingPressed)
                {
                    HandleBraking();
                }
                foreach (TrailRenderer trail in driftTrails)
                {
                    trail.emitting = true;
                }
            }

            float newZRotation = Mathf.MoveTowardsAngle(
                pivotPointDrift.localEulerAngles.y,
                driftTiltZ,
                driftTiltSpeed * Time.deltaTime
            );
            pivotPointDrift.localEulerAngles = new Vector3(
                pivotPointDrift.localEulerAngles.x,
                newZRotation,
                pivotPointDrift.localEulerAngles.z
            );
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

        driftTime = 0f;
    }

    private void StopDrifting()
    {
        isDrifting = false;
        state = State.Normal;
        driftTiltZ = 0f;

        if (driftTime > 0f)
        {
            Vector3 boostDirection = rb.linearVelocity.normalized;
            rb.linearVelocity += boostDirection * (driftTime * driftBoostMultiplier);
            currentBoost += driftTime * driftBoostMultiplier;
            currentBoost = Mathf.Clamp(currentBoost, 0f, maxBoost);
        }

        foreach (TrailRenderer trail in driftTrails)
        {
            trail.emitting = false;
        }

        // If you want a "drift stop" or "grind start" SFX:
        if (sfxSource != null && grindStartClip != null)
        {
            sfxSource.PlayOneShot(grindStartClip);
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

    public bool rmbPresssed;
    private void OnRMB(InputValue value)
    {
        rmbPresssed = true;
    }
    private void OnRMBReleased(InputValue value)
    {
        rmbPresssed = false;
    }

    public float GetTurnInput()
    {
        return inputMovement.x;
    }

    // ------------------------------------------------
    //  TRICK METHODS
    // ------------------------------------------------

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

        // If you want the Ollie also to use the combo system, call PlayComboClipBasedOnBoost();
        // otherwise, just play a dedicated Ollie SFX:
        if (sfxSource != null && ollieClip != null)
        {
            sfxSource.PlayOneShot(ollieClip);
        }
        // Or you could do: PlayComboClipBasedOnBoost();

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

        // Instead of always playing tailSlapClip, let's do combos based on boost:
        PlayComboClipBasedOnBoost();

        trickCount++;
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

        // Again, use the combos based on boost:
        PlayComboClipBasedOnBoost();

        trickCount++;
        StartTrickCooldown();

        Debug.Log("Pop Shuv It performed!");
        Invoke(nameof(ResetTrickState), 0.02f);
    }

    private void ResetTrickState()
    {
        isPerformingTrick = false;
    }
}
