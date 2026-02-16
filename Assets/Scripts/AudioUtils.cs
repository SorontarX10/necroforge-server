using UnityEngine;

public static class AudioUtils
{
    private const int DefaultEmitterCount = 24;
    private const string HostName = "AudioUtils_OneShotPool";

    private static GameObject host;
    private static AudioSource[] emitters;
    private static int emitterIndex;
    private static bool initialized;

    public static void PlayClipAtPoint(AudioClip clip, Vector3 pos, float volume = 1f)
    {
        if (clip == null)
            return;

        EnsurePool();
        if (emitters == null || emitters.Length == 0)
            return;

        AudioSource src = RentEmitter();
        if (src == null)
            return;

        src.transform.position = pos;
        src.pitch = 1f;
        src.volume = 1f;
        src.spatialBlend = 1f;
        src.minDistance = 1f;
        src.maxDistance = 45f;
        src.dopplerLevel = 0f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    private static AudioSource RentEmitter()
    {
        int start = emitterIndex;
        for (int i = 0; i < emitters.Length; i++)
        {
            int idx = (start + i) % emitters.Length;
            AudioSource candidate = emitters[idx];
            if (candidate == null)
                continue;

            if (!candidate.isPlaying)
            {
                emitterIndex = (idx + 1) % emitters.Length;
                return candidate;
            }
        }

        AudioSource fallback = emitters[emitterIndex];
        emitterIndex = (emitterIndex + 1) % emitters.Length;
        return fallback;
    }

    private static void EnsurePool()
    {
        if (initialized && host != null && emitters != null && emitters.Length > 0)
            return;

        initialized = true;
        if (host == null)
            host = GameObject.Find(HostName);

        if (host == null)
        {
            host = new GameObject(HostName);
            Object.DontDestroyOnLoad(host);
        }

        AudioSource[] existingEmitters = host.GetComponents<AudioSource>();
        if (existingEmitters != null && existingEmitters.Length >= DefaultEmitterCount)
        {
            emitters = new AudioSource[DefaultEmitterCount];
            for (int i = 0; i < DefaultEmitterCount; i++)
                emitters[i] = existingEmitters[i];
            return;
        }

        int existingCount = existingEmitters != null ? existingEmitters.Length : 0;
        emitters = new AudioSource[DefaultEmitterCount];
        for (int i = 0; i < existingCount && i < emitters.Length; i++)
            emitters[i] = existingEmitters[i];

        for (int i = existingCount; i < emitters.Length; i++)
        {
            var src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.dopplerLevel = 0f;
            emitters[i] = src;
        }
    }
}
