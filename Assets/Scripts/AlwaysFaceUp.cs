using UnityEngine;

public class AlwaysFaceUp : MonoBehaviour
{
    [SerializeField] Transform transformToUp;
    [SerializeField] Transform boardUpToUp;
    [SerializeField] SkaterBotController skaterToUp;
    private void Update()
    {

        if (skaterToUp.IsGrounded && skaterToUp._coyoteTimer <= 0)
        {
            // Preserve forward direction but keep up aligned to world up
            transform.rotation = Quaternion.LookRotation(transform.forward, transformToUp.up);
        }
        if (!skaterToUp.IsGrounded || skaterToUp._coyoteTimer >= 0)
        {
            transform.rotation = Quaternion.LookRotation(transform.forward, boardUpToUp.up);
        }
    }
}
