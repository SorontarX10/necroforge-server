Shader "Necroforge/Relic/AreaOverlay"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _InnerAlpha ("Inner Alpha", Range(0,1)) = 0.03
        _EdgeAlpha ("Edge Alpha", Range(0,1)) = 0.9
        _EdgeWidth ("Edge Width", Range(0.001,0.4)) = 0.08
        _EdgeSoftness ("Edge Softness", Range(0.001,0.25)) = 0.04
        _NoiseScale ("Noise Scale", Range(0.1,12)) = 4.0
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.15
        _FlowSpeed ("Flow Speed", Range(0,2)) = 0.2
        _Emission ("Emission", Range(0,2.5)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalRenderPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "AreaOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _InnerAlpha;
                float _EdgeAlpha;
                float _EdgeWidth;
                float _EdgeSoftness;
                float _NoiseScale;
                float _NoiseStrength;
                float _FlowSpeed;
                float _Emission;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float Noise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                float2 u = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * 2.0 - 1.0;
                float radius = length(uv);

                float soft = max(0.001, _EdgeSoftness);
                float edgeStart = saturate(1.0 - _EdgeWidth);

                float fillMask = 1.0 - smoothstep(edgeStart - soft, edgeStart + soft, radius);
                float ringMask = saturate(
                    smoothstep(edgeStart - soft, edgeStart + soft, radius)
                    - smoothstep(1.0 - soft, 1.0, radius)
                );

                float clipMask = 1.0 - smoothstep(1.0 - 0.005, 1.0 + 0.005, radius);

                float t = _Time.y * _FlowSpeed;
                float n = Noise2D(uv * _NoiseScale + float2(t, -t * 0.83));
                float noiseSigned = (n - 0.5) * 2.0;

                float fillAlpha = _InnerAlpha * (1.0 + noiseSigned * _NoiseStrength);
                float edgeAlpha = _EdgeAlpha * (1.0 + noiseSigned * (_NoiseStrength * 0.35));
                float alpha = saturate(fillMask * fillAlpha + ringMask * edgeAlpha);
                alpha *= clipMask * saturate(_BaseColor.a);

                float3 rgb = _BaseColor.rgb * (1.0 + ringMask * _Emission);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
