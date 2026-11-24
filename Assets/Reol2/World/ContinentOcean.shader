Shader "Reol2/Continent Ocean"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Ocean"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Visuals/SurfaceNoise.hlsl"
            float4 _WaterOrigin;
            half4 _ReolSkyHorizon;
            half _ReolSkyDaylight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 waterDepth : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 world : TEXCOORD0;
                half fog : TEXCOORD1;
                float2 waterDepth : TEXCOORD2;
            };

            Varyings Vertex(Attributes input)
            {
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                Varyings output;
                output.positionCS = p.positionCS;
                output.world = p.positionWS;
                output.fog = ComputeFogFactor(p.positionCS.z);
                output.waterDepth = input.waterDepth;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 p = input.world.xz * (1.0 / 128.0);
                float2 footprint = fwidth(p);
                float3 eye = GetCameraPositionWS() - input.world;
                float distanceToEye = length(eye);
                float horizon = 1.0 - abs(eye.y) / max(0.001, distanceToEye);
                horizon *= horizon;
                float detail = (1.0 - smoothstep(0.08, 0.35, max(footprint.x, footprint.y))) *
                    (1.0 - smoothstep(400.0, 3000.0, distanceToEye));
                float ripples = 0.0;
                if (detail > 0.0)
                    ripples = (ReolSurfaceNoise(p + float2(0.02, -0.014) * _Time.y, (int2)_WaterOrigin.xy) - 0.5) * detail;
                half3 color = lerp(half3(0.07, 0.44, 0.49), half3(0.26, 0.66, 0.65),
                                  saturate(ripples * 0.12 + horizon * 0.55 + 0.16));
                half inland = smoothstep(0.0, 4.0, input.waterDepth.y);
                half shallow = (1.0 - smoothstep(0.0, 2.5, input.waterDepth.x)) * inland;
                color = lerp(color, half3(0.21, 0.53, 0.48), shallow * 0.55);
                if (_ReolSkyDaylight > 0.0)
                    color = lerp(color * lerp(0.51, 1.0, _ReolSkyDaylight), _ReolSkyHorizon.rgb, horizon * 0.46);
                half opacity = lerp(0.56, 0.94, horizon) * (1.0 - 0.68 * shallow * (1.0 - 0.7 * horizon));
                float edge = smoothstep(0.0, max(0.04, fwidth(input.waterDepth.x) * 0.65), input.waterDepth.x);
                opacity *= lerp(1.0, edge, inland);
                if (eye.y < 0.0)
                    opacity *= 0.22;
                return half4(MixFog(color, InitializeInputDataFog(float4(input.world, 1), input.fog)), opacity);
            }
            ENDHLSL
        }
    }
}
