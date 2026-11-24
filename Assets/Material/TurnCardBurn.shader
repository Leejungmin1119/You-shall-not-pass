Shader "UI/TurnCardBurn"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BurnProgress ("Burn", Range(0,1)) = 0
        _BurnSeed ("Seed", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };
            sampler2D _MainTex;
            float4 _Color, _TextureSampleAdd, _ClipRect;
            float _BurnProgress, _BurnSeed;

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7)) + _BurnSeed * 47.3) * 43758.5453);
            }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1,0)), f.x),
                    lerp(hash(i + float2(0,1)), hash(i + 1), f.x), f.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 color = (tex2D(_MainTex, i.uv) + _TextureSampleAdd) * i.color;
                if (_BurnProgress > 0.001)
                {
                    // 가장자리와 여러 내부 지점이 불규칙하게 타며 구멍이 서로 이어진다.
                    float edge = min(min(i.uv.x, 1-i.uv.x), min(i.uv.y, 1-i.uv.y));
                    float field = edge * 0.45 + noise(i.uv * 5) * 0.5 + noise(i.uv * 17) * 0.12;
                    float remaining = field - (_BurnProgress * 0.95 - 0.07);
                    float charred = 1 - smoothstep(0.035, 0.12, remaining);
                    color.rgb = lerp(color.rgb, float3(0.055,0.025,0.045), charred);
                    float glow = 1 - smoothstep(0.005, 0.045, remaining);
                    float flicker = 0.8 + 0.2 * sin(_Time.y * 30 + i.uv.y * 35);
                    color.rgb = lerp(color.rgb, lerp(float3(1,0.22,0.015), float3(1,0.95,0.38), glow), glow * flicker);
                    color.a *= smoothstep(-0.008, 0.005, remaining) * step(_BurnProgress, 0.999);
                }
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
