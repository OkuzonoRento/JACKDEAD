// ドーパミンが多いとき（ハイな状態）の、画面の端のオーラ。UI の画像に付けて、足し算で重ねる。
// _Intensity（0〜1）で、濃さと内側への広がりが変わる。時間は _Phase で受け取る（止まっていても確認できるように）
Shader "JackDead/HighAura"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("強さ", Range(0, 1)) = 0
        _Phase ("時間", Float) = 0
        _Aspect ("画面の横÷縦", Float) = 1.7778
        _ColorA ("色1", Color) = (1, 0.12, 0.72, 1)
        _ColorB ("色2", Color) = (1, 0.68, 0.1, 1)
        _ColorC ("色3", Color) = (0.15, 0.85, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            float _Intensity, _Phase, _Aspect;
            fixed4 _ColorA, _ColorB, _ColorC;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;                       // -1〜1（画面の端が ±1）
                float2 q = float2(p.x * _Aspect, p.y);          // ゆらぎは、縦横で同じ大きさにする

                // 角の丸い四角に沿った「端からの近さ」
                float2 a = abs(p);
                float edge = pow(pow(a.x, 5) + pow(a.y, 5), 0.2);

                // ゆらぎ（内側へ、ゆっくり立ちのぼる）
                float n = noise(q * 3.1 + float2(_Phase * 0.35, -_Phase * 0.22)) * 0.6
                        + noise(q * 7.3 - float2(_Phase * 0.5, _Phase * 0.31)) * 0.4;
                float inner = lerp(0.94, 0.8, _Intensity);      // 遊ぶ邪魔にならないよう、端の近くだけ
                float mask = smoothstep(inner, 1.18, edge + (n - 0.5) * 0.26);

                // 色は、場所と時間でゆっくり移り変わる
                float h = frac(noise(q * 1.2 + _Phase * 0.12) * 1.4 + _Phase * 0.07) * 3;
                fixed3 color = h < 1 ? lerp(_ColorA.rgb, _ColorB.rgb, h)
                             : h < 2 ? lerp(_ColorB.rgb, _ColorC.rgb, h - 1)
                                     : lerp(_ColorC.rgb, _ColorA.rgb, h - 2);

                // 端のあたりの、小さなきらめき
                float2 cell = floor(q * 22);
                float2 local = frac(q * 22) - 0.5;
                float seed = hash(cell);
                float twinkle = step(0.93, seed) * pow(saturate(sin(_Phase * (1.5 + seed * 2) + seed * 40)), 6);
                float spark = twinkle * saturate(1 - length(local) * 3.2) * smoothstep(0.72, 1.0, edge);

                float alpha = mask * (0.5 + 0.7 * n) * lerp(0.28, 0.55, _Intensity) + spark * 0.7;
                alpha *= saturate(_Intensity * 4) * i.color.a;
                return fixed4(color + spark * 0.5, saturate(alpha));
            }
            ENDCG
        }
    }
}
