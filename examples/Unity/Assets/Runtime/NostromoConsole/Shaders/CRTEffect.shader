Shader "Ghostty/CRTEffect"
{
    Properties
    {
        _MainTex ("Terminal Texture", 2D) = "black" {}
        _PhosphorColor ("Phosphor Color", Color) = (0.0, 1.0, 0.0, 1.0)
        _ScanLineIntensity ("Scan Line Intensity", Range(0, 1)) = 0.3
        _ScanLineFrequency ("Scan Line Frequency", Float) = 400
        _CurvatureAmount ("Screen Curvature", Range(0, 0.5)) = 0.1
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.4
        _GlowIntensity ("Phosphor Glow", Range(0, 1)) = 0.15
        _FlickerIntensity ("Flicker Intensity", Range(0, 0.1)) = 0.02
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.2
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "CRT"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _MainTex_TexelSize; // (1/w, 1/h, w, h) provided by Unity

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _PhosphorColor;
                float _ScanLineIntensity;
                float _ScanLineFrequency;
                float _CurvatureAmount;
                float _VignetteIntensity;
                float _GlowIntensity;
                float _FlickerIntensity;
                float _Brightness;
            CBUFFER_END

            // Barrel distortion for CRT curvature
            float2 BarrelDistortion(float2 uv, float amount)
            {
                float2 centered = uv * 2.0 - 1.0;
                float r2 = dot(centered, centered);
                centered *= 1.0 + amount * r2;
                return centered * 0.5 + 0.5;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Apply barrel distortion
                float2 uv = BarrelDistortion(input.uv, _CurvatureAmount);

                // Discard pixels outside the curved screen bounds
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return half4(0, 0, 0, 1);

                // Sample the terminal texture
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                // Convert to luminance (terminal output is typically monochrome)
                float lum = dot(texColor.rgb, float3(0.299, 0.587, 0.114));

                // Apply phosphor color tint
                half3 color = lum * _PhosphorColor.rgb * _Brightness;

                // Phosphor glow: blur approximation by sampling neighbors
                float2 texelSize = _MainTex_TexelSize.xy;
                float glowLum = 0.0;
                glowLum += dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    uv + float2(-texelSize.x, 0)).rgb, float3(0.299, 0.587, 0.114));
                glowLum += dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    uv + float2(texelSize.x, 0)).rgb, float3(0.299, 0.587, 0.114));
                glowLum += dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    uv + float2(0, -texelSize.y)).rgb, float3(0.299, 0.587, 0.114));
                glowLum += dot(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex,
                    uv + float2(0, texelSize.y)).rgb, float3(0.299, 0.587, 0.114));
                glowLum *= 0.25;
                color += glowLum * _PhosphorColor.rgb * _GlowIntensity;

                // Scan lines
                float scanLine = sin(uv.y * _ScanLineFrequency * 3.14159) * 0.5 + 0.5;
                color *= 1.0 - (_ScanLineIntensity * scanLine);

                // Vignette
                float2 vignetteUV = uv * 2.0 - 1.0;
                float vignette = 1.0 - dot(vignetteUV, vignetteUV) * _VignetteIntensity;
                vignette = saturate(vignette);
                color *= vignette;

                // Flicker
                float flicker = 1.0 - _FlickerIntensity * sin(_Time.y * 60.0);
                color *= flicker;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}
