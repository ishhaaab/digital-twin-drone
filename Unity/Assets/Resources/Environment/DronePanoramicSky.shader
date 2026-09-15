Shader "DigitalTwin/PanoramicSky"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Panorama", 2D) = "grey" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0, 8)) = 1
        _Rotation ("Rotation", Range(0, 360)) = 0
        [HideInInspector] _Mapping ("Mapping", Float) = 1
        [HideInInspector] _ImageType ("Image Type", Float) = 0
        [HideInInspector] _MirrorOnBack ("Mirror On Back", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _Tint;
            half _Exposure;
            float _Rotation;

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.direction = input.vertex.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                const float Pi = 3.14159265359;
                float3 direction = normalize(input.direction);
                float rotation = radians(_Rotation);
                float sine = sin(rotation);
                float cosine = cos(rotation);
                direction.xz = float2(
                    direction.x * cosine - direction.z * sine,
                    direction.x * sine + direction.z * cosine);
                float2 uv = float2(
                    atan2(direction.x, direction.z) / (2.0 * Pi) + 0.5,
                    1.0 - acos(clamp(direction.y, -1.0, 1.0)) / Pi);
                return tex2D(_MainTex, uv) * _Tint * _Exposure;
            }
            ENDCG
        }
    }
    Fallback Off
}
