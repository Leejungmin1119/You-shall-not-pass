Shader "Game/Time Skill Sprite Lit"
{
    Properties
    {
        [HideInInspector] _StageDanger("Time Skill Danger", Float) = 0
        [HideInInspector] _StageSpriteUVBounds("Sprite UV Bounds", Vector) = (0,0,1,1)
        _MainTex("Diffuse", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "bump" {}
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
        [HideInInspector] _Color("Tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex LitVertex
            #pragma fragment LitFragment

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_LIT_OUTPUTS
                float2 stagePositionWS : TEXCOORD4;
                half4 color        : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Lit2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            float _StageGrayscale;
            float4 _StageWave; // xy: world center, z: radius, w: expanding
            float4 _StageWaveStyle; // x: soft color-wave transition width
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _StageDanger;
                float4 _StageSpriteUVBounds;
            CBUFFER_END
            #include "Assets/Material/TimeSkillWaveDistortion.hlsl"

            Varyings LitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonLitVertex(input);
                o.stagePositionWS = TransformObjectToWorld(input.positionOS).xy;
                o.color = input.color * _Color * unity_SpriteColor;

                return o;
            }

            half4 LitFragment(Varyings input) : SV_Target
            {
                input.uv = StageWaveDistortedUV(input.uv, input.stagePositionWS);
                half4 color = CommonLitFragment(input, input.color);
                half gray = dot(color.rgb, half3(0.299h, 0.587h, 0.114h));
                // 검은 대포알도 식별되도록 최소 밝기를 보장하고 원래 명암은 남긴다.
                half3 danger = half3(0.35h + 0.65h * gray, 0.03h * gray, 0.03h * gray);
                half3 stageColor = lerp(gray.xxx, danger, saturate(_StageDanger));
                float distanceFromCenter = distance(input.stagePositionWS, _StageWave.xy);
                float edgeWidth = max(0.01, _StageWaveStyle.x);
                half waveMask = 1.0h - smoothstep(_StageWave.z - edgeWidth, _StageWave.z, distanceFromCenter);
                // 흰색 덧칠 없이 색상 전환에만 약한 물결을 남긴다.
                float waveDepth = max(0.0, (_StageWave.z - distanceFromCenter) / edgeWidth);
                half ripple = sin(waveDepth * 6.283185) * exp(-waveDepth * 2.0) * 0.08h;
                waveMask = saturate(waveMask + ripple);
                half coverage = lerp(1.0h, waveMask, saturate(_StageWave.w));
                half strength = saturate(_StageGrayscale);
                color.rgb = lerp(color.rgb, stageColor, strength * coverage);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "NormalsRendering"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex NormalsRenderingVertex
            #pragma fragment NormalsRenderingFragment

            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ SKINNED_SPRITE

            struct Attributes
            {
                COMMON_2D_NORMALS_INPUTS
                float4 color        : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_NORMALS_OUTPUTS
                float2 stagePositionWS : TEXCOORD4;
                half4   color           : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Normals2DCommon.hlsl"

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            float _StageGrayscale;
            float4 _StageWave; // xy: world center, z: radius, w: expanding
            float4 _StageWaveStyle; // x: soft color-wave transition width
            CBUFFER_START( UnityPerMaterial )
                half4 _Color;
                half _StageDanger;
                float4 _StageSpriteUVBounds;
            CBUFFER_END
            #include "Assets/Material/TimeSkillWaveDistortion.hlsl"

            Varyings NormalsRenderingVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonNormalsVertex(input);
                o.stagePositionWS = TransformObjectToWorld(input.positionOS).xy;
                o.color = input.color * _Color * unity_SpriteColor;

                return o;
            }

            half4 NormalsRenderingFragment(Varyings input) : SV_Target
            {
                input.uv = StageWaveDistortedUV(input.uv, input.stagePositionWS);
                return CommonNormalsFragment(input, input.color);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" "Queue"="Transparent" "RenderType"="Transparent"}

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                float2 stagePositionWS : TEXCOORD4;
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"
          
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            float _StageGrayscale;
            float4 _StageWave; // xy: world center, z: radius, w: expanding
            float4 _StageWaveStyle; // x: soft color-wave transition width
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _StageDanger;
                float4 _StageSpriteUVBounds;
            CBUFFER_END
            #include "Assets/Material/TimeSkillWaveDistortion.hlsl"

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.stagePositionWS = TransformObjectToWorld(input.positionOS).xy;
                o.color = input.color *_Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                input.uv = StageWaveDistortedUV(input.uv, input.stagePositionWS);
                half4 color = CommonUnlitFragment(input, input.color);
                half gray = dot(color.rgb, half3(0.299h, 0.587h, 0.114h));
                // 검은 대포알도 식별되도록 최소 밝기를 보장하고 원래 명암은 남긴다.
                half3 danger = half3(0.35h + 0.65h * gray, 0.03h * gray, 0.03h * gray);
                half3 stageColor = lerp(gray.xxx, danger, saturate(_StageDanger));
                float distanceFromCenter = distance(input.stagePositionWS, _StageWave.xy);
                float edgeWidth = max(0.01, _StageWaveStyle.x);
                half waveMask = 1.0h - smoothstep(_StageWave.z - edgeWidth, _StageWave.z, distanceFromCenter);
                // 흰색 덧칠 없이 색상 전환에만 약한 물결을 남긴다.
                float waveDepth = max(0.0, (_StageWave.z - distanceFromCenter) / edgeWidth);
                half ripple = sin(waveDepth * 6.283185) * exp(-waveDepth * 2.0) * 0.08h;
                waveMask = saturate(waveMask + ripple);
                half coverage = lerp(1.0h, waveMask, saturate(_StageWave.w));
                half strength = saturate(_StageGrayscale);
                color.rgb = lerp(color.rgb, stageColor, strength * coverage);
                return color;
            }
            ENDHLSL
        }
    }
}
