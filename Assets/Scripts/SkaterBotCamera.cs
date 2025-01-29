using UnityEngine;

public class SkaterBotCamera : MonoBehaviour
{
    [Header("Camera Settings")]
    [SerializeField] private Vector3 baseOffset = new Vector3(0, 5, -10); // Default position behind player
    [SerializeField] private float followSpeed = 5f;  // How fast the camera moves
    [SerializeField] private float zoomOutFactor = 0.1f; // Small zoom-out multiplier
    [SerializeField] private float maxZoomOut = 12f; // Slight zoom-out distance
    [SerializeField] private float minZoomOut = 10f; // Default zoom distance
    [SerializeField] private float smoothTime = 0.2f; // Smoothing time for camera movement

    private Transform player;
    private Rigidbody playerRb;
    private Vector3 velocity = Vector3.zero; // Used for SmoothDamp

    private void LateUpdate()
    {
        if (player == null || playerRb == null) return;

        // Get player's velocity
        Vector3 playerVelocity = playerRb.linearVelocity;
        float forwardSpeed = Vector3.Dot(playerVelocity, player.forward); // Forward velocity component

        // Only zoom out slightly if moving forward
        float zoomAmount = Mathf.Lerp(minZoomOut, maxZoomOut, Mathf.Clamp01(forwardSpeed * zoomOutFactor));

        // Calculate new offset with zoom
        Vector3 dynamicOffset = baseOffset.normalized * zoomAmount;

        // Target camera position behind the player
        Vector3 targetPosition = player.position + player.rotation * dynamicOffset;

        // Use SmoothDamp for smoother movement
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);

        // Keep camera facing the player
        transform.LookAt(player.position);
    }

    public void FindPlayer(SkaterBotController skaterBot)
    {
        player = skaterBot.transform;
        playerRb = skaterBot.GetComponent<Rigidbody>();
    }
}
