using UnityEngine;

public class SkaterBotCamera : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0, 5, -10); // Default position behind player
    [SerializeField] private float followSpeed = 5f;  // How fast the camera moves
    [SerializeField] private float zoomOutFactor = 0.2f; // Camera zoom-out multiplier
    [SerializeField] private float maxZoomOut = 15f; // Max distance camera zooms out
    [SerializeField] private float minZoomOut = 8f;  // Min zoom (when stopped)
    [SerializeField] private float lookAheadFactor = 3f; // How far the camera looks ahead when turning
    [SerializeField] private bool rotateWithPlayer = true; // Set false if you don’t want rotation

    private Transform player;
    private Rigidbody playerRb;
    [SerializeField] private SkaterBotController playerInput;

    private void Start()
    {
        FindPlayer(); // Try to find the SkaterBot at start
    }

    private void LateUpdate()
    {
        if (player == null || playerRb == null || playerInput == null) return;

        // Get player's speed
        float playerSpeed = playerRb.linearVelocity.magnitude;

        // Calculate dynamic zoom based on speed
        float zoomAmount = Mathf.Lerp(minZoomOut, maxZoomOut, playerSpeed * zoomOutFactor);

        // Calculate look-ahead based on player input (turning)
        Vector3 lookAheadOffset = player.transform.right * (playerInput.GetTurnInput() * lookAheadFactor);

        // Target camera position: behind player + dynamic zoom + look ahead
        Vector3 targetPosition = player.position + player.transform.rotation * (offset.normalized * zoomAmount) + lookAheadOffset;

        // Smoothly move camera to the target position
        transform.position = Vector3.Lerp(transform.position, targetPosition, followSpeed * Time.deltaTime);

        // Rotate with player if enabled
        if (rotateWithPlayer)
        {
            Quaternion targetRotation = Quaternion.LookRotation(player.position - transform.position);
            transform.rotation = targetRotation;
        }
    }

    // 🔥 Automatically finds and assigns the player when it spawns
    public void FindPlayer()
    {
        player = playerInput.transform;
        playerRb = playerInput.GetComponent<Rigidbody>();
    }
}
