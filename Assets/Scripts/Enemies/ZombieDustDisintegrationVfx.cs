using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GrassSim.Core;

namespace GrassSim.Enemies
{
    /// <summary>
    /// Runtime-only death effect used for zombie-like enemies.
    /// It snapshots meshes, emits dust particles and quickly shrinks the mesh pieces.
    /// </summary>
    public sealed class ZombieDustDisintegrationVfx : MonoBehaviour
    {
        private struct Piece
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3 startScale;
            public Vector3 velocity;
            public Vector3 angularVelocity;
            public float hideAt;
        }

        private const float MeshLifetime = 0.62f;
        private const float TotalLifetime = 1.6f;
        private const float LateralSpeedMin = 0.45f;
        private const float LateralSpeedMax = 1.9f;
        private const float UpwardSpeedMin = 0.55f;
        private const float UpwardSpeedMax = 1.65f;
        private const float ShrinkExponent = 1.55f;
        private const int MaxParticlesPerMesh = 130;
        private const int MaxConcurrentEffects = 6;
        private const float MinSpawnInterval = 0.03f;
        private const float MaxVisibleDistance = 34f;

        private static Material sharedParticleMaterial;
        private static int liveEffects;
        private static float nextSpawnAt;
        private static bool missingDustShaderWarningLogged;

        private readonly List<Piece> pieces = new();
        private readonly List<Mesh> runtimeMeshes = new();

        private float elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            sharedParticleMaterial = null;
            liveEffects = 0;
            nextSpawnAt = 0f;
            missingDustShaderWarningLogged = false;
        }

        public static void SpawnFrom(GameObject source)
        {
            if (source == null)
                return;

            if (Time.time < nextSpawnAt || liveEffects >= MaxConcurrentEffects)
                return;

            Transform player = PlayerLocator.GetTransform();
            if (player != null)
            {
                Vector3 delta = source.transform.position - player.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > MaxVisibleDistance * MaxVisibleDistance)
                    return;
            }

            nextSpawnAt = Time.time + MinSpawnInterval;

            var root = new GameObject("ZombieDustDisintegrationVfx");
            root.transform.position = source.transform.position;
            root.transform.rotation = source.transform.rotation;

            var vfx = root.AddComponent<ZombieDustDisintegrationVfx>();
            vfx.InitializeFrom(source);
        }

        private void OnEnable()
        {
            liveEffects++;
        }

        private void InitializeFrom(GameObject source)
        {
            Vector3 inheritedVelocity = Vector3.zero;
            var rb = source.GetComponent<Rigidbody>();
            if (rb != null)
                inheritedVelocity = rb.linearVelocity * 0.15f;

            CollectSkinnedMeshes(source, inheritedVelocity);
            CollectStaticMeshes(source, inheritedVelocity);

            if (pieces.Count == 0)
                EmitFallbackDust(source.transform.position, source.transform.rotation);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / MeshLifetime);
            float shrinkT = Mathf.Pow(t, ShrinkExponent);

            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece.transform == null)
                    continue;

                piece.transform.position += piece.velocity * Time.deltaTime;
                piece.transform.Rotate(piece.angularVelocity * Time.deltaTime, Space.Self);
                piece.transform.localScale = Vector3.LerpUnclamped(piece.startScale, Vector3.zero, shrinkT);

                if (piece.renderer != null && t >= piece.hideAt)
                    piece.renderer.enabled = false;
            }

            if (elapsed >= TotalLifetime)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            liveEffects = Mathf.Max(0, liveEffects - 1);
            for (int i = 0; i < runtimeMeshes.Count; i++)
            {
                if (runtimeMeshes[i] != null)
                    Destroy(runtimeMeshes[i]);
            }
        }

        private void CollectSkinnedMeshes(GameObject source, Vector3 inheritedVelocity)
        {
            var skinnedMeshes = source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedMeshes.Length; i++)
            {
                var smr = skinnedMeshes[i];
                if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null)
                    continue;

                var bakedMesh = new Mesh();
                // Bake with scale for skinned meshes and keep spawned piece transform at unit scale.
                // This keeps dog and zombie rigs consistent regardless of armature/root scaling.
                smr.BakeMesh(bakedMesh, true);
                if (bakedMesh.vertexCount <= 0)
                {
                    Destroy(bakedMesh);
                    continue;
                }

                runtimeMeshes.Add(bakedMesh);
                SpawnPiece(smr.transform, bakedMesh, smr.sharedMaterials, inheritedVelocity, applyTransformScale: false);
                EmitDustParticles(smr.transform, bakedMesh, Mathf.Max(22, bakedMesh.vertexCount / 18), applyTransformScale: false);
            }
        }

        private void CollectStaticMeshes(GameObject source, Vector3 inheritedVelocity)
        {
            var meshFilters = source.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null)
                    continue;

                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || !mr.gameObject.activeInHierarchy)
                    continue;

                SpawnPiece(mf.transform, mf.sharedMesh, mr.sharedMaterials, inheritedVelocity, applyTransformScale: true);
                EmitDustParticles(mf.transform, mf.sharedMesh, Mathf.Max(16, mf.sharedMesh.vertexCount / 22), applyTransformScale: true);
            }
        }

        private void SpawnPiece(
            Transform sourceTransform,
            Mesh mesh,
            Material[] materials,
            Vector3 inheritedVelocity,
            bool applyTransformScale
        )
        {
            if (sourceTransform == null || mesh == null)
                return;

            var go = new GameObject("DustPiece");
            go.transform.SetParent(transform, false);
            go.transform.position = sourceTransform.position;
            go.transform.rotation = sourceTransform.rotation;
            go.transform.localScale = applyTransformScale ? sourceTransform.lossyScale : Vector3.one;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            Vector3 dir = Random.insideUnitSphere;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.right;
            dir.Normalize();

            pieces.Add(new Piece
            {
                transform = go.transform,
                renderer = mr,
                startScale = go.transform.localScale,
                velocity = inheritedVelocity
                    + dir * Random.Range(LateralSpeedMin, LateralSpeedMax)
                    + Vector3.up * Random.Range(UpwardSpeedMin, UpwardSpeedMax),
                angularVelocity = new Vector3(
                    Random.Range(-260f, 260f),
                    Random.Range(-260f, 260f),
                    Random.Range(-260f, 260f)
                ),
                hideAt = Random.Range(0.38f, 0.86f)
            });
        }

        private void EmitFallbackDust(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("FallbackDust");
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            go.transform.rotation = rotation;

            var ps = go.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(ps, 40, null, useSphereShape: true);
            ps.Play();
        }

        private void EmitDustParticles(Transform sourceTransform, Mesh mesh, int count, bool applyTransformScale)
        {
            if (sourceTransform == null || mesh == null)
                return;

            var go = new GameObject("DustParticles");
            go.transform.SetParent(transform, false);
            go.transform.position = sourceTransform.position;
            go.transform.rotation = sourceTransform.rotation;
            go.transform.localScale = applyTransformScale ? sourceTransform.lossyScale : Vector3.one;

            var ps = go.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(ps, count, mesh, useSphereShape: false);
            ps.Play();
        }

        private static void ConfigureParticleSystem(
            ParticleSystem ps,
            int particleCount,
            Mesh mesh,
            bool useSphereShape
        )
        {
            if (ps == null)
                return;

            // Freshly added particle systems can already be in a playing state (Play On Awake).
            // Ensure a clean stopped state before mutating duration and other main-module fields.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            particleCount = Mathf.Clamp(particleCount, 12, MaxParticlesPerMesh);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.18f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = particleCount;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
            main.startColor = new Color(0.72f, 0.66f, 0.57f, 1f);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var burst = new ParticleSystem.Burst(0f, (short)particleCount);
            emission.SetBursts(new[] { burst });

            var shape = ps.shape;
            shape.enabled = true;
            if (useSphereShape || mesh == null)
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.35f;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Mesh;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                shape.mesh = mesh;
                shape.randomDirectionAmount = 0.45f;
                shape.sphericalDirectionAmount = 0.95f;
            }

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.42f;
            noise.frequency = 0.72f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.78f, 0.72f, 0.62f), 0f),
                    new GradientColorKey(new Color(0.52f, 0.48f, 0.42f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.8f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = gradient;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.9f),
                    new Keyframe(0.7f, 1f),
                    new Keyframe(1f, 0f)
                )
            );

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            Material particleMaterial = GetParticleMaterial();
            if (particleMaterial != null)
            {
                renderer.sharedMaterial = particleMaterial;
                renderer.enabled = true;
            }
            else
            {
                renderer.enabled = false;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Material GetParticleMaterial()
        {
            if (sharedParticleMaterial != null)
                return sharedParticleMaterial;

            RenderPipelineAsset pipeline = ResolveActiveRenderPipeline();
            Shader shader = null;

            if (pipeline != null)
            {
                shader = FindSupportedShader(
                    "Universal Render Pipeline/Particles/Unlit",
                    "Universal Render Pipeline/Particles/Lit"
                );

                if (shader == null)
                {
                    Material defaultParticle = pipeline.defaultParticleMaterial;
                    if (defaultParticle != null && defaultParticle.shader != null && defaultParticle.shader.isSupported)
                    {
                        string shaderName = defaultParticle.shader.name;
                        if (!string.IsNullOrWhiteSpace(shaderName) && shaderName.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal))
                        {
                            sharedParticleMaterial = defaultParticle;
                            return sharedParticleMaterial;
                        }
                    }
                }

                if (shader == null)
                {
                    if (!missingDustShaderWarningLogged)
                    {
                        missingDustShaderWarningLogged = true;
                        Debug.LogWarning("Zombie dust VFX disabled: no URP-compatible particle shader/material found.");
                    }

                    return null;
                }
            }
            else
            {
                shader = FindSupportedShader(
                    "Particles/Standard Unlit",
                    "Particles/Additive"
                );
            }
            if (shader == null)
                return null;

            sharedParticleMaterial = new Material(shader)
            {
                name = "ZombieDustRuntimeMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };

            if (sharedParticleMaterial.HasProperty("_BaseColor"))
                sharedParticleMaterial.SetColor("_BaseColor", Color.white);
            if (sharedParticleMaterial.HasProperty("_Color"))
                sharedParticleMaterial.SetColor("_Color", Color.white);

            return sharedParticleMaterial;
        }

        private static RenderPipelineAsset ResolveActiveRenderPipeline()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
                return pipeline;

            return GraphicsSettings.defaultRenderPipeline;
        }

        private static Shader FindSupportedShader(params string[] shaderNames)
        {
            if (shaderNames == null)
                return null;

            for (int i = 0; i < shaderNames.Length; i++)
            {
                string shaderName = shaderNames[i];
                if (string.IsNullOrWhiteSpace(shaderName))
                    continue;

                Shader shader = Shader.Find(shaderName);
                if (shader != null && shader.isSupported)
                    return shader;
            }

            return null;
        }
    }
}
