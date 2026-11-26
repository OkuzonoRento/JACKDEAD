// 落ちているアイテムを見つけやすくする光。元の見た目の上に、足し算で重ねて描く（周りは照らさない）。
// ふちが光り、ときどき斜めの光の帯が流れる。遠くでは薄くなる。壁の向こうのものは見えない。
Shader "JackDead/ItemGlow"
{
    Properties
    {
        _GlowColor ("光の色", Color) = (1, 0.82, 0.42, 1)
        _Surface ("面全体の明るさ", Range(0, 1)) = 0.1
        _Rim ("ふちの明るさ", Range(0, 4)) = 0.5
        _RimPower ("ふちの細さ", Range(0.5, 8)) = 2
        _Glint ("流れる光の明るさ", Range(0, 4)) = 0.6
        _GlintInterval ("流れる光の間隔（秒）", Float) = 2.5
        _FadeStart ("薄くなり始める距離", Float) = 10
        _FadeEnd ("見えなくなる距離", Float) = 18
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                float _Surface;
                float _Rim;
                float _RimPower;
                float _Glint;
                float _GlintInterval;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim = pow(saturate(1.0 - dot(normalize(input.normalWS), viewDir)), _RimPower);
                float glow = _Surface + _Rim * rim;

                // 斜めの光の帯が、ときどき表面を流れる
                float band = frac(_Time.y / max(_GlintInterval, 0.1) - dot(input.positionWS, float3(0.35, 0.2, 0.35)));
                glow += _Glint * smoothstep(0.86, 0.93, band) * (1.0 - smoothstep(0.93, 1.0, band));

                float dist = distance(input.positionWS, GetCameraPositionWS());
                glow *= saturate((_FadeEnd - dist) / max(_FadeEnd - _FadeStart, 0.01));

                return half4(_GlowColor.rgb * glow, 0);
            }
            ENDHLSL
        }
    }
}
