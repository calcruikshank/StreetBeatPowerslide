using UnityEngine;

public class AlwaysFaceUp : MonoBehaviour
{
    [SerializeField] Transform transformToUp;
    [SerializeField] Transform boardUpToUp;
    [SerializeField] SkaterBotController skaterToUp;
    private void Update()
    {
        // Preserve forward direction but keep up aligned to world up
        transform.rotation = Quaternion.LookRotation(transform.forward, transformToUp.up);
    }
}
