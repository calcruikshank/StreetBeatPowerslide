using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager instance;
    [SerializeField] public SkaterBotCamera skaterBotCamera;
    private void Awake()
    {
        if (instance != null)
        {
            Destroy(this);
        }
        instance = this;
    }
    [SerializeField] public AudioSource sfxSource;      // For one-shot SFX (ollies, flips, etc.)
    [SerializeField] public AudioSource musicSource;    // For background/looping audio (optional)
}
