Shader "Custom/WaterShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DistortionFrequency ("Distortion Frequency", Float) = 1.0
        _DistortionAmplitude ("Distortion Amplitude", Float) = 0.02
        _WaterSpeed ("Water Speed", Float) = 0.1
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest+60" "RenderType"="Transparent" } // Για το νερό, συνήθως χρησιμοποιούμε "Overlay" ή "Background"
        Blend SrcAlpha OneMinusSrcAlpha // Διαφάνεια
        ZWrite Off // Απενεργοποιούμε το ZWrite για να επιτρέψουμε την διαφάνεια να λειτουργεί σωστά
        ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // Δέχεται Vertex Colors
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float _DistortionFrequency;
            float _DistortionAmplitude;
            float _WaterSpeed;

            // Υπολογισμός UVs με distortion
            v2f vert (appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                // Υπολογισμός κυματισμού για την παραμόρφωση των UV
                float timeOffset = _Time.y * _WaterSpeed;
                float wave = sin((v.uv.x + timeOffset) * _DistortionFrequency) * _DistortionAmplitude;
                o.uv.x += wave;
                o.uv.y += wave;

                o.color = v.color; // Περνάμε το color στο Fragment Shader
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                texColor *= i.color; // Συνδυασμός υφής και vertex colors

                // Διατήρηση του alpha από το texture και vertex color
                texColor.a *= texColor.a;

                return texColor; // Επιστροφή του τελικού χρώματος με alpha blending
            }
            ENDCG
        }
    }
}
