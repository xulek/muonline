// SceneBloom.fx - glow, vignette and colour grade for a world rendered into its own target (Graphics/SceneBloom).
// Pixel shaders only, drawn with SpriteBatch:
//   Extract - bright parts of the scene (soft threshold), written into a half/quarter size target;
//   Blur    - 9-tap gaussian along TexelStep (run horizontally, then vertically);
//   Combine - scene + bloom, colour grade and vignette.
// - WindowsDX (SM4): Texture2D/SamplerState + ps_4_0
// - DesktopGL/OpenGL: sampler + tex2D + ps_3_0

float Threshold;      // luminance where glow starts
float Knee;           // softness of the threshold
float2 TexelStep;     // blur direction * texel size of the source
float BloomIntensity;
float3 BloomTint;     // colour of the glow (multiplies the bloom)
float Saturation;     // 1 = unchanged
float Contrast;       // 1 = unchanged
float3 Lift;          // added to the shadows (colour of the darkest parts)
float Vignette;       // 0 = off, 1 = strong darkening of the corners

#if SM4 || SM6
Texture2D SceneTexture : register(t0);
SamplerState SceneSampler : register(s0);
Texture2D BloomTexture : register(t1);
SamplerState BloomSampler : register(s1);
#define SAMPLE_SCENE(uv) SceneTexture.Sample(SceneSampler, uv)
#define SAMPLE_BLOOM(uv) BloomTexture.Sample(BloomSampler, uv)
#define PS_SIG(name) float4 name(float4 pos : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
#else
sampler SceneSampler : register(s0);
sampler BloomSampler : register(s1);
#define SAMPLE_SCENE(uv) tex2D(SceneSampler, uv)
#define SAMPLE_BLOOM(uv) tex2D(BloomSampler, uv)
#define PS_SIG(name) float4 name(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
#endif

PS_SIG(ExtractPS)
{
    // 4 taps: the target is smaller than the source, averaging avoids sparkling single pixels.
    float2 d = TexelStep * 0.5;
    float3 c = (SAMPLE_SCENE(uv + float2(-d.x, -d.y)).rgb + SAMPLE_SCENE(uv + float2(d.x, -d.y)).rgb +
                SAMPLE_SCENE(uv + float2(-d.x, d.y)).rgb + SAMPLE_SCENE(uv + float2(d.x, d.y)).rgb) * 0.25;
    float lum = max(c.r, max(c.g, c.b));
    float soft = clamp(lum - Threshold + Knee, 0.0, 2.0 * Knee);
    soft = soft * soft / (4.0 * Knee + 0.0001);
    float contribution = max(soft, lum - Threshold) / max(lum, 0.0001);
    return float4(c * contribution, 1.0);
}

PS_SIG(BlurPS)
{
    float3 c = SAMPLE_SCENE(uv).rgb * 0.2270270270;
    c += SAMPLE_SCENE(uv + TexelStep * 1.3846153846).rgb * 0.3162162162;
    c += SAMPLE_SCENE(uv - TexelStep * 1.3846153846).rgb * 0.3162162162;
    c += SAMPLE_SCENE(uv + TexelStep * 3.2307692308).rgb * 0.0702702703;
    c += SAMPLE_SCENE(uv - TexelStep * 3.2307692308).rgb * 0.0702702703;
    return float4(c, 1.0);
}

PS_SIG(CombinePS)
{
    float3 scene = SAMPLE_SCENE(uv).rgb;
    float3 bloom = SAMPLE_BLOOM(uv).rgb * BloomTint * BloomIntensity;
    // Screen-like add: strong glow does not burn to flat white.
    float3 c = scene + bloom * (1.0 - saturate(scene) * 0.45);

    c += Lift * (1.0 - saturate(c));
    float lum = dot(c, float3(0.299, 0.587, 0.114));
    c = lerp(float3(lum, lum, lum), c, Saturation);
    c = (c - 0.5) * Contrast + 0.5;

    float2 v = (uv - 0.5) * float2(1.25, 1.0);
    float vig = 1.0 - Vignette * smoothstep(0.30, 0.85, dot(v, v) * 1.6);
    return float4(saturate(c * vig), 1.0);
}

#if SM6
    #define BLOOM_PS ps_6_0
#elif SM4
    #define BLOOM_PS ps_4_0
#else
    #define BLOOM_PS ps_3_0
#endif

technique Extract { pass P0 { PixelShader = compile BLOOM_PS ExtractPS(); } }
technique Blur { pass P0 { PixelShader = compile BLOOM_PS BlurPS(); } }
technique Combine { pass P0 { PixelShader = compile BLOOM_PS CombinePS(); } }
