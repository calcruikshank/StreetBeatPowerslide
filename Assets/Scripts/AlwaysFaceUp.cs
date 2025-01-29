using UnityEngine;

public class AlwaysFaceUp : MonoBehaviour
{
    private void Update()
    {
        // Preserve forward direction but keep up aligned to world up
        transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
    }
}
