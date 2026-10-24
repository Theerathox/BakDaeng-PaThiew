using UnityEngine;

// Every sound zombies can hear: the dog's bark, clattering cans (NoiseTrap)...
// Zombies and the night sound wave (Darkness) listen to this. The blind master only answers the
// dog's own bark (DogBark.OnNoise), not random noises.
public static class Noise
{
    public static event System.Action<Vector3, float> OnSound;   // position, loudness (1 = a bark)

    public static void Emit(Vector3 at, float loudness = 1f) => OnSound?.Invoke(at, loudness);
}
