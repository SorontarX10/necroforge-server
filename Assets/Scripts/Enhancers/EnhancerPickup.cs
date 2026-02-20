using UnityEngine;
using GrassSim.UI;

namespace GrassSim.Enhancers
{
    public class EnhancerPickup : MonoBehaviour
    {
        public EnhancerDefinition enhancer;

        private void OnEnable()
        {
            MapCollectibleRegistry.RegisterEnhancer(this);
        }

        private void OnDestroy()
        {
            MapCollectibleRegistry.UnregisterEnhancer(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            var system = other.GetComponentInChildren<WeaponEnhancerSystem>();
            if (system == null || enhancer == null)
                return;

            system.AddEnhancer(enhancer);
            EnhancerCardUI.Instance?.Show(enhancer);

            // TODO: VFX / sound
            Destroy(gameObject);
        }
    }
}
