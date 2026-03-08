using GrassSim.Enhancers;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;

namespace GrassSim.Editor.Tests
{
    public sealed class EnhancerPickupTests
    {
        [Test]
        public void TryCollect_AddsEnhancer_AndRejectsDuplicateCollect()
        {
            GameObject root = new("EnhancerPickupTestRoot");
            EnhancerDefinition def = ScriptableObject.CreateInstance<EnhancerDefinition>();
            def.enhancerId = "test-enhancer";
            def.duration = 10f;

            try
            {
                WeaponEnhancerSystem system = root.AddComponent<WeaponEnhancerSystem>();

                GameObject pickupGo = new("EnhancerPickup");
                pickupGo.transform.SetParent(root.transform, false);
                EnhancerPickup pickup = pickupGo.AddComponent<EnhancerPickup>();
                pickup.enhancer = def;
                SetDestroyOnCollectForTests(pickup, false);

                bool firstCollect = pickup.TryCollect(system);
                bool secondCollect = pickup.TryCollect(system);

                Assert.IsTrue(firstCollect);
                Assert.IsFalse(secondCollect);
                Assert.AreEqual(1, system.Active.Count);
                Assert.AreSame(def, system.Active[0].Definition);
            }
            finally
            {
                Object.DestroyImmediate(def);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryCollect_Fails_WhenEnhancerIsMissing()
        {
            GameObject root = new("EnhancerPickupMissingEnhancer");
            try
            {
                WeaponEnhancerSystem system = root.AddComponent<WeaponEnhancerSystem>();
                EnhancerPickup pickup = root.AddComponent<EnhancerPickup>();
                SetDestroyOnCollectForTests(pickup, false);
                pickup.enhancer = null;

                bool collected = pickup.TryCollect(system);

                Assert.IsFalse(collected);
                Assert.AreEqual(0, system.Active.Count);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void SetDestroyOnCollectForTests(EnhancerPickup pickup, bool enabled)
        {
            FieldInfo field = typeof(EnhancerPickup).GetField(
                "destroyOnCollect",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            field?.SetValue(pickup, enabled);
        }
    }
}
