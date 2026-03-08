using UnityEngine;
using GrassSim.UI;
using System.Collections;

namespace GrassSim.Enhancers
{
    public class EnhancerPickup : MonoBehaviour
    {
        public EnhancerDefinition enhancer;

        [Header("Feedback")]
        [SerializeField] private ParticleSystem pickupVfxPrefab;
        [SerializeField] private AudioClip pickupSfx;
        [SerializeField, Range(0f, 1f)] private float pickupSfxVolume = 1f;
        [SerializeField, Min(0f)] private float destroyDelaySeconds = 0f;
        [SerializeField] private bool destroyOnCollect = true;

        private bool collected;

        private void OnEnable()
        {
            collected = false;
            MapCollectibleRegistry.RegisterEnhancer(this);
        }

        private void OnDestroy()
        {
            MapCollectibleRegistry.UnregisterEnhancer(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            var system = other.GetComponentInChildren<WeaponEnhancerSystem>();
            TryCollect(system);
        }

        public bool TryCollect(WeaponEnhancerSystem system)
        {
            if (collected || system == null || enhancer == null)
                return false;

            collected = true;
            system.AddEnhancer(enhancer);
            EnhancerCardUI.Instance?.Show(enhancer);
            PlayFeedback();

            if (destroyOnCollect)
            {
                if (destroyDelaySeconds <= 0f)
                    Destroy(gameObject);
                else
                    StartCoroutine(DestroyAfterDelay());
            }

            return true;
        }

        private void PlayFeedback()
        {
            if (pickupVfxPrefab != null)
            {
                ParticleSystem instance = Instantiate(pickupVfxPrefab, transform.position, Quaternion.identity);
                float lifetime = GetParticleLifetime(instance);
                if (lifetime > 0f)
                    Destroy(instance.gameObject, lifetime);
            }

            if (pickupSfx != null)
                AudioSource.PlayClipAtPoint(pickupSfx, transform.position, Mathf.Clamp01(pickupSfxVolume));
        }

        private IEnumerator DestroyAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0f, destroyDelaySeconds));
            Destroy(gameObject);
        }

        private static float GetParticleLifetime(ParticleSystem ps)
        {
            if (ps == null)
                return 0f;

            ParticleSystem.MainModule main = ps.main;
            float duration = main.duration;
            float startLifetime = 0f;

            if (main.startLifetime.mode == ParticleSystemCurveMode.Constant)
                startLifetime = main.startLifetime.constant;
            else if (main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants)
                startLifetime = main.startLifetime.constantMax;
            else
                startLifetime = 2f;

            return Mathf.Max(0.5f, duration + startLifetime);
        }

    }
}
