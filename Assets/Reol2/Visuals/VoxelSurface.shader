Shader "Reol2/Voxel Surface"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        [HideInInspector] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex TerrainVertex
            #pragma fragment TerrainFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Cull;
            CBUFFER_END

            struct TerrainAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct TerrainVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            TerrainVaryings TerrainVertex(TerrainAttributes input)
            {
                TerrainVaryings output;
                VertexPositionInputs vertex = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertex.positionCS;
                output.positionWS = vertex.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color * _BaseColor;
                output.fog = ComputeFogFactor(vertex.positionCS.z);
                output.shadowCoord = GetShadowCoord(vertex);
                return output;
            }

            half4 TerrainFragment(TerrainVaryings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    input.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light sun = GetMainLight(input.shadowCoord);
                half diffuse = saturate(dot(normal, sun.direction));
                half3 ambient = lerp(half3(0.32, 0.35, 0.31), half3(0.55, 0.63, 0.60), normal.y * 0.5 + 0.5);
                half3 lit = input.color.rgb * (ambient + diffuse * sun.color * sun.shadowAttenuation * 0.68);
                return half4(MixFog(lit, input.fog), 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
