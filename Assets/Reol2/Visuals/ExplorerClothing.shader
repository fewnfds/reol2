Shader "Reol2/Explorer Clothing"
{
    Properties
    {
        _BaseMap("Skin", 2D) = "white" {}
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
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Cull;
            CBUFFER_END
            half _ReolSkyDaylight;
            half _ReolShelterLight;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 garment : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normal : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 garment : COLOR;
                half fog : TEXCOORD3;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                output.normal = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.garment = input.garment;
                output.fog = ComputeFogFactor(p.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half3 skin = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 cloth = SRGBToLinear(input.garment.rgb) * (0.88 + dot(skin, half3(0.12, 0.16, 0.07)));
                half3 albedo = lerp(skin, cloth, input.garment.a);
                half3 normal = normalize(input.normal);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ambient = _ReolSkyDaylight > 0.0 ? _ReolSkyDaylight : 1.0;
                ambient *= _ReolShelterLight > 0.0 ? _ReolShelterLight : 1.0;
                half diffuse = saturate(dot(normal, sun.direction));
                half3 lighting = half3(0.46, 0.52, 0.57) * ambient + sun.color * diffuse * sun.shadowAttenuation * 0.74;
                return half4(MixFog(albedo * lighting, InitializeInputDataFog(float4(input.positionWS, 1.0), input.fog)), 1.0);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
