// =============================================================================
//  Blizzard — 눈보라 파티클용 URP 셰이더 (텍스처 없음)
//  원본: KooHoo-Dev/3rd-study-shader-ashadeveloperr  Assets/02. MyShaders/02_Blizzard
//  Untrodden(Quest VR)용 변경점
//   - 양쪽 눈에 그려지도록 Single Pass Instanced 스테레오 매크로 추가
//     (없으면 왼쪽 눈에만 보인다)
//   - Soft Particles는 Depth Texture가 필요해 기본 꺼 둠(Quest에서 비쌈)
// -----------------------------------------------------------------------------
//  Particle System이 눈송이의 위치·속도·크기를 정하고, 이 셰이더는 각 눈송이를
//  "어떻게 보이게 할지"를 정한다. 눈송이가 스티커처럼 선명하게 보이지 않도록
//  아래 다섯 가지를 처리한다.
//
//  1) 속도 방향으로 늘이기 (모션 블러)
//     파티클의 중심(Center)과 속도(Velocity)를 Custom Vertex Streams로 받아,
//     정점 셰이더에서 사각형을 "화면에 투영한 속도 방향"으로 늘인다.
//     눈송이 머리는 제자리에 두고, 지나온 쪽으로 꼬리를 만든다.
//  2) 거리에 따른 흐림 (카메라 초점 흉내)
//     초점 거리(_FocusDistance)에서 멀어질수록 눈송이를 크게 키우고 가장자리를
//     부드럽게 한다. 넓어진 면적만큼 불투명도를 낮춰 전체 밝기는 유지한다.
//     → 카메라 바로 앞은 커다란 흐린 빛망울, 먼 곳은 작고 희미한 점.
//  3) 불규칙한 모양: 파티클마다 다른 난수로 가장자리를 사인파로 일그러뜨린다.
//  4) 빛나지 않게: 가산(Additive)이 아닌 알파 블렌딩을 쓰고, 색은 씬의 환경광과
//     주광으로 정한다. 해를 마주 볼 때만 살짝 밝아진다(전방 산란).
//  5) 자연스럽게 사라지기: URP 안개, Soft Particle(바닥·물체와 닿는 곳),
//     Near Fade(카메라 바로 앞)로 눈송이가 툭 잘리지 않게 한다.
//
//  같은 셰이더에서 _Wispiness를 올리면 노이즈로 구멍이 숭숭 난 큰 눈가루 덩어리
//  (바람에 휩쓸리는 눈보라)가 된다.
//
//  [필요한 Custom Vertex Streams — 순서 중요]
//    Position, Color, UV, StableRandom.xy, Center, Size.x, Velocity
//    → TEXCOORD0 = (uv.xy, random.xy)
//      TEXCOORD1 = (center.xyz, size)
//      TEXCOORD2 = (velocity.xyz)
//  Renderer의 Render Mode는 Billboard로 둔다 (실제 모양은 정점 셰이더가 다시 만든다).
// =============================================================================
Shader "Untrodden/Blizzard"
{
    Properties
    {
        [Header(Color)]
        _BaseColor        ("Base Color", Color) = (1, 1, 1, 1)
        _Opacity          ("Opacity", Range(0, 1)) = 0.9

        [Header(Lighting)]
        _AmbientStrength  ("Ambient Strength", Range(0, 3)) = 1
        _LightStrength    ("Main Light Strength", Range(0, 3)) = 0.6
        _ForwardScatter   ("Forward Scatter (toward sun)", Range(0, 4)) = 1.5

        [Header(Shape)]
        _EdgeSoftness     ("Edge Softness (in focus)", Range(0.05, 1)) = 0.7
        _Irregularity     ("Edge Irregularity", Range(0, 0.5)) = 0.18
        _Twinkle          ("Tumble Twinkle", Range(0, 1)) = 0.25
        _Wispiness        ("Wispiness (noise holes)", Range(0, 1)) = 0
        _NoiseScale       ("Noise Scale", Range(0.2, 8)) = 2
        _NoiseScroll      ("Noise Scroll Speed", Range(0, 3)) = 0.4

        [Header(Motion Stretch)]
        _StretchAmount    ("Stretch (exposure sec)", Range(0, 0.2)) = 0.012
        _MaxStretch       ("Max Stretch (x size)", Range(1, 12)) = 3.5

        [Header(Depth Of Field)]
        _FocusDistance    ("Focus Distance", Range(0.5, 50)) = 7
        _FocusRange       ("In-Focus Range (+-)", Range(0, 30)) = 3
        _BlurDistance     ("Far Blur Distance", Range(1, 100)) = 25
        _BokehGrow        ("Blur Size Grow", Range(0, 5)) = 2.5

        [Header(Fading)]
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticles ("Soft Particles (needs Depth Texture)", Float) = 0
        _SoftParticleDistance ("Soft Particle Distance", Range(0.01, 5)) = 0.3
        _NearFadeStart    ("Near Fade Start", Range(0, 3)) = 0.25
        _NearFadeRange    ("Near Fade Range", Range(0.01, 5)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Blizzard"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma shader_feature_local _SOFTPARTICLES_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define TWO_PI_F 6.28318530718

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Opacity;
                float _AmbientStrength;
                float _LightStrength;
                float _ForwardScatter;
                float _EdgeSoftness;
                float _Irregularity;
                float _Twinkle;
                float _Wispiness;
                float _NoiseScale;
                float _NoiseScroll;
                float _StretchAmount;
                float _MaxStretch;
                float _FocusDistance;
                float _FocusRange;
                float _BlurDistance;
                float _BokehGrow;
                float _SoftParticles;
                float _SoftParticleDistance;
                float _NearFadeStart;
                float _NearFadeRange;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float4 texcoord0  : TEXCOORD0;   // uv.xy, stableRandom.xy
                float4 texcoord1  : TEXCOORD1;   // center.xyz, size
                float3 texcoord2  : TEXCOORD2;   // velocity.xyz
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;       // rgb = 조명까지 반영한 색, a = 파티클 알파
                float4 local      : TEXCOORD0;   // xy = 눈송이 기준 좌표(반지름 1), z = 꼬리 길이, w = 흐림 정도
                float4 rnd        : TEXCOORD1;   // xy = 난수, z = 밝기 보존 계수, w = 시선 깊이
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---- 노이즈 (눈가루 덩어리용) ---------------------------------------------
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll]
                for (int o = 0; o < 3; o++)
                {
                    v += a * ValueNoise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v / 0.875;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float2 corner  = v.texcoord0.xy * 2.0 - 1.0;            // 사각형 모서리 (-1 ~ 1)
                float2 rnd     = v.texcoord0.zw;
                float3 center  = TransformObjectToWorld(v.texcoord1.xyz);
                float  size    = v.texcoord1.w;
                float3 velWS   = TransformObjectToWorldDir(v.texcoord2, false);

                float3 camPos  = GetCameraPositionWS();
                float3 toCam   = camPos - center;
                float  dist    = max(length(toCam), 1e-4);
                toCam /= dist;
                float  eyeDepth = -TransformWorldToView(center).z;

                // ---- 2. 거리에 따른 흐림 ------------------------------------------------
                // 초점 범위보다 가까우면 카메라에 붙을수록 1, 멀면 _BlurDistance에 걸쳐 1까지
                float nearEdge = max(_FocusDistance - _FocusRange, 0.01);
                float nearBlur = saturate((nearEdge - eyeDepth) / nearEdge);
                float farBlur  = saturate((eyeDepth - (_FocusDistance + _FocusRange)) / _BlurDistance);
                float blur     = max(nearBlur, farBlur);
                float grow     = 1.0 + blur * _BokehGrow;               // 흐린 만큼 커진다
                float halfW    = size * 0.5 * grow;

                // ---- 1. 속도 방향으로 늘이기 -------------------------------------------
                // 시선 방향 성분을 빼서 "화면에서 보이는 이동 방향"만 남긴다
                float3 vPlane  = velWS - dot(velWS, toCam) * toCam;
                float  speed   = length(vPlane);
                float3 viewUp  = UNITY_MATRIX_V[1].xyz;
                float3 axisY   = speed > 1e-4 ? vPlane / speed : viewUp;        // 진행 방향 (머리 쪽)
                float3 axisX   = normalize(cross(axisY, toCam));                // 진행 방향에 수직
                // 꼬리 길이(반지름 단위): 노출 시간 동안 이동한 거리, 최대 _MaxStretch배
                float  tailLen = min(speed * _StretchAmount / halfW, 2.0 * (_MaxStretch - 1.0));

                // 사각형을 머리(+1)부터 꼬리 끝(-(1 + tailLen))까지 펼친다
                float2 local   = float2(corner.x, lerp(-(1.0 + tailLen), 1.0, v.texcoord0.y));
                float3 posWS   = center + (axisX * local.x + axisY * local.y) * halfW;

                o.positionCS = TransformWorldToHClip(posWS);

                // 넓어진 면적만큼 어둡게 해서 전체 밝기 보존
                //  - 흐림: 면적이 grow^2 배
                //  - 꼬리: 원(PI) → 캡슐(PI + 2*tailLen). 완전히 나누면 너무 옅어서 제곱근으로 완화
                float energy = 1.0 / (grow * grow);
                energy *= sqrt(PI / (PI + 2.0 * tailLen));

                // ---- 4. 조명: 환경광 + 주광, 해를 마주 볼 때 밝아지는 전방 산란 --------
                Light mainLight = GetMainLight();
                float3 lookDir  = -toCam;
                float  scatter  = pow(saturate(dot(lookDir, mainLight.direction)), 6.0) * _ForwardScatter;
                half3  lighting = SampleSH(half3(0, 1, 0)) * _AmbientStrength
                                + mainLight.color * _LightStrength * (1.0 + scatter);

                // 납작한 눈송이가 굴러가며 빛을 받았다 말았다 하는 반짝임
                float twinkle = 1.0 - _Twinkle * (0.5 + 0.5 * sin(_Time.y * (3.0 + rnd.x * 7.0) + rnd.y * 50.0));

                o.color     = half4(_BaseColor.rgb * v.color.rgb * lighting * twinkle, v.color.a * _BaseColor.a);
                o.local     = float4(local, tailLen, blur);
                o.rnd       = float4(rnd, energy, -TransformWorldToView(posWS).z);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p       = i.local.xy;
                float  tailLen = i.local.z;
                float  blur    = i.local.w;
                float2 rnd     = i.rnd.xy;

                // 머리(0,0)에서 꼬리 끝(0,-tailLen)까지의 선분에 가장 가까운 점 → 캡슐 모양
                float2 q  = float2(0.0, clamp(p.y, -tailLen, 0.0));
                float2 dp = p - q;

                // 꼬리 진행도: 머리 0 → 꼬리 끝 1. 꼬리는 끝으로 갈수록 가늘어진다(혜성 모양).
                // 폭이 일정하면 늘어난 눈송이가 딱딱한 직사각형 조각처럼 보이기 때문.
                float tailT  = tailLen > 1e-4 ? saturate(-q.y / tailLen) : 0.0;
                float radius = lerp(1.0, 0.4, tailT);

                // ---- 3. 불규칙한 가장자리 ----------------------------------------------
                float ang   = atan2(dp.x, dp.y);
                float wobble = sin(ang * 3.0 + rnd.x * TWO_PI_F) * 0.6
                             + sin(ang * 5.0 + rnd.y * TWO_PI_F) * 0.4;
                float d     = length(dp) / radius * (1.0 + _Irregularity * wobble);

                // 초점이 맞으면 _EdgeSoftness, 흐릴수록 가장자리 전체가 부드러운 빛망울.
                // 단면은 가운데가 진하고 바깥으로 갈수록 옅어지는 종 모양 → 테두리 선이 생기지 않는다.
                float soft  = lerp(_EdgeSoftness, 1.0, blur);
                float shape = 1.0 - smoothstep(1.0 - soft, 1.0, d);
                shape *= lerp(1.0, exp(-2.5 * d * d), soft);

                // 꼬리는 머리에서 멀어질수록 옅어진다
                shape *= pow(1.0 - tailT, 1.5);

                // 눈가루 덩어리: 노이즈로 구멍을 뚫어 연기처럼
                if (_Wispiness > 0.001)
                {
                    float2 np = p * _NoiseScale + rnd * 37.0 + float2(0.0, _Time.y * _NoiseScroll);
                    float  n  = saturate(Fbm(np) * 1.8 - 0.45);
                    shape *= lerp(1.0, n, _Wispiness);
                }

                float alpha = shape * i.color.a * _Opacity * i.rnd.z;

                // ---- 5. 자연스럽게 사라지기 --------------------------------------------
                float eyeDepth = i.rnd.w;
                alpha *= saturate((eyeDepth - _NearFadeStart) / _NearFadeRange);   // 카메라 바로 앞

                #if defined(_SOFTPARTICLES_ON)
                    float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    float  sceneZ   = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    alpha *= saturate((sceneZ - eyeDepth) / _SoftParticleDistance); // 바닥·물체와 닿는 곳
                #endif

                half3 color = MixFog(i.color.rgb, i.fogFactor);                     // 먼 눈은 안개 속으로
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
