Shader "Reol2/Voxel Surface"
{
    Properties
    {
        _BaseColor("Tint", Color) = (1,1,1,1)
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _TerrainStencilWrite("Terrain stencil write mask", Float) = 0
        [HideInInspector] _TerrainStencilCompare("Terrain stencil comparison", Float) = 8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask [_TerrainStencilWrite]
                Comp [_TerrainStencilCompare]
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TerrainVertex
            #pragma fragment TerrainFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_local _ REOL_HEIGHT_PROXY REOL_CAVE_PROXY
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SurfaceNoise.hlsl"
            half _ReolSkyDaylight;
            half _ReolShelterLight;
            float4 _ReolGroundOrigin;

            #if defined(REOL_HEIGHT_PROXY) || defined(REOL_CAVE_PROXY)
                TEXTURE3D(_ResidentTerrain);
                SAMPLER(sampler_ResidentTerrain);
                float4 _ResidentTerrainOrigin;
            #endif
            #if defined(REOL_HEIGHT_PROXY)
                TEXTURE3D(_CaveCoverage);
                SAMPLER(sampler_CaveCoverage);
                float4 _CaveCoverageBounds;
            #endif

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Cull;
                float _TerrainStencilWrite;
                float _TerrainStencilCompare;
            CBUFFER_END

            struct TerrainAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                #if defined(REOL_HEIGHT_PROXY)
                    float2 uv : TEXCOORD0;
                #endif
            };

            struct TerrainVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                #if defined(REOL_HEIGHT_PROXY)
                    float heightOffset : TEXCOORD4;
                #endif
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
                #if defined(REOL_HEIGHT_PROXY)
                    output.heightOffset = input.uv.x;
                #endif
                return output;
            }

            half4 TerrainFragment(TerrainVaryings input) : SV_Target
            {
                #if defined(REOL_HEIGHT_PROXY) || defined(REOL_CAVE_PROXY)
                    float3 proxyTop = input.positionWS;
                #endif
                #if defined(REOL_HEIGHT_PROXY)
                    proxyTop.y += input.heightOffset;
                    float3 cave = (proxyTop - _CaveCoverageBounds.xyz) * _CaveCoverageBounds.w;
                    if (_CaveCoverageBounds.w > 0 && all(cave >= 0) && all(cave < 1))
                        clip(0.5 - SAMPLE_TEXTURE3D_LOD(_CaveCoverage, sampler_CaveCoverage, cave, 0).r);
                #endif
                #if defined(REOL_HEIGHT_PROXY) || defined(REOL_CAVE_PROXY)
                    float3 coverage = (proxyTop - _ResidentTerrainOrigin.xyz) * (1.0 / 128.0);
                    if (all(coverage >= 0) && all(coverage < 1))
                    {
                        float resident = SAMPLE_TEXTURE3D_LOD(_ResidentTerrain, sampler_ResidentTerrain, coverage, 0).r;
                        #if defined(REOL_HEIGHT_PROXY)
                            clip(0.25 - resident);
                        #else
                            // Leave the continuous coarse wall behind the exact terrain. Edited pages own their volume.
                            clip(0.75 - resident);
                        #endif
                    }
                #endif
                half3 normal = normalize(input.normalWS);
                #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    input.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                // Use URP's distance fade so fragments beyond the shadow cascades stay lit.
                Light sun = GetMainLight(input.shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half diffuse = saturate(dot(normal, sun.direction));
                half3 ambient = lerp(half3(0.32, 0.35, 0.31), half3(0.55, 0.63, 0.60), normal.y * 0.5 + 0.5);
                ambient *= _ReolSkyDaylight > 0.0 ? _ReolSkyDaylight : 1.0;
                ambient *= _ReolShelterLight > 0.0 ? _ReolShelterLight : 1.0;
                half variation = 1.0;
                if (_ReolGroundOrigin.w > 0.0)
                {
                    // The same surface location receives the same fine tone across render proxies.
                    float2 p = (input.positionWS.xz + input.positionWS.y * float2(0.34, -0.18)) * 0.125;
                    float2 footprint = fwidth(p);
                    float3 toEye = GetCameraPositionWS() - input.positionWS;
                    float distanceToEye = length(toEye);
                    float facing = saturate(dot(normal, toEye) / max(distanceToEye, 0.001));
                    float strength = (1.0 - smoothstep(0.15, 0.6, max(footprint.x, footprint.y)))
                        * (1.0 - smoothstep(55.0, 260.0, distanceToEye))
                        * smoothstep(0.12, 0.42, facing);
                    if (strength > 0.0)
                    {
                        float2 origin = _ReolGroundOrigin.xy;
                        float2 yOffset = _ReolGroundOrigin.zz * float2(0.34, -0.18);
                        float2 anchor = floor(yOffset);
                        float fleck = ReolSurfaceNoise(p + (yOffset - anchor), (int2)(origin + anchor)) - 0.5;
                        variation += fleck * strength * 0.18;
                    }
                }
                half3 lit = input.color.rgb * variation * (ambient + diffuse * sun.color * sun.shadowAttenuation * 0.68);
                return half4(MixFog(lit, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fog)), 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
