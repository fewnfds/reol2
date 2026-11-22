Shader "Reol2/World Sky"
{
    Properties
    {
        _ReolSkyHorizon("Horizon", Color) = (0.69, 0.78, 0.84, 1)
        _Zenith("Zenith", Color) = (0.24, 0.51, 0.72, 1)
        _SunDirection("Sun direction and daylight", Vector) = (0, 1, 0, 1)
        _Cloud("Cloud cover", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SurfaceNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ReolSkyHorizon;
                half4 _Zenith;
                float4 _SunDirection;
                half _Cloud;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                half vertical = sqrt(saturate(direction.y));
                half3 color = lerp(_ReolSkyHorizon.rgb, _Zenith.rgb, vertical);
                float sun = dot(direction, _SunDirection.xyz);
                float sunDisk = smoothstep(0.9996, 0.99984, sun);
                float moonDisk = smoothstep(0.9993, 0.99962, -sun);
                half clearSky = 1.0 - _Cloud * 0.88;
                color += half3(1.0, 0.69, 0.38) * (pow(saturate(sun), 28.0) * 0.20 + sunDisk * 0.62)
                    * clearSky * step(-0.02, _SunDirection.y);
                color += half3(0.55, 0.70, 0.88) * moonDisk * clearSky * step(0.01, -_SunDirection.y);
                if (_SunDirection.w < 0.45)
                {
                    float2 sky = direction.xz / (1.0 + max(0.0, direction.y)) * 230.0;
                    int2 cell = (int2)floor(sky);
                    float rating = ReolSurfaceHash(cell);
                    float2 center = 0.24 + 0.52 * float2(
                        ReolSurfaceHash(cell + int2(2317, -571)), ReolSurfaceHash(cell + int2(-197, 1871)));
                    float pixel = min(0.14, max(length(ddx(sky)), length(ddy(sky))) * 0.65);
                    float star = (1.0 - smoothstep(0.06, 0.12 + pixel, length(frac(sky) - center))) * step(0.978, rating);
                    color += half3(0.53, 0.67, 0.91) * star * clearSky * smoothstep(0.06, 0.18, direction.y)
                        * saturate((0.45 - _SunDirection.w) * 2.3);
                }
                if (_Cloud > 0.05 && direction.y > 0.025)
                {
                    float2 sky = direction.xz / (0.35 + direction.y);
                    float layer = ReolSurfaceNoise(sky * 4.0 + float2(_Time.y * 0.016, 0.0), int2(0, 0)) * 0.72
                        + ReolSurfaceNoise(sky * 9.0 - float2(0.0, _Time.y * 0.024), int2(317, 28)) * 0.28;
                    float mask = smoothstep(0.82 - _Cloud * 0.58, 0.95 - _Cloud * 0.5, layer)
                        * smoothstep(0.025, 0.23, direction.y) * _Cloud;
                    color = lerp(color, lerp(_ReolSkyHorizon.rgb * 0.75, half3(0.70, 0.73, 0.75), _SunDirection.w), mask);
                }
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
