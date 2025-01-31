using UnityEngine;

public class AlwaysFaceUp : MonoBehaviour
{
    [SerializeField] Transform transformToUp;
    private void Update()
    {
        // Preserve forward direction but keep up aligned to world up
        transform.rotation = Quaternion.LookRotation(transform.forward, transformToUp.up);
    }
}
