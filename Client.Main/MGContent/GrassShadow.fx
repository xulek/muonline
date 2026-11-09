// GrassShadow.fx - terrain grass with the ground shadows laid over it.
// Ground shadows are also drawn, seen from straight above, into a small mask around the camera (white = lit,
// darker = shadowed; Graphics/GroundShadowMask). Each grass pixel looks the mask up at its own world position, so a
// blade standing in a shadow is darkened over its whole height, as if the shadow fell on it.
// - WindowsDX (SM4): Texture2D/SamplerState + vs/ps_4_0_level_9_3
// - DesktopGL/OpenGL: sampler + tex2D + vs/ps_3_0

float4x4 WorldViewProjection;
float4x4 ShadowMaskViewProjection;
float ShadowStrength;   // 0 = mask ignored, 1 = full mask darkness

#if SM4 || SM6
Texture2D GrassTexture : register(t0);
SamplerState GrassSampler : register(s0);
Texture2D ShadowMask : register(t1);
SamplerState ShadowMaskSampler : register(s1);
#define SAMPLE_GRASS(uv) GrassTexture.Sample(GrassSampler, uv)
#define SAMPLE_MASK(uv) ShadowMask.Sample(ShadowMaskSampler, uv).r
#define POSITION_OUT SV_POSITION
#define TARGET_OUT SV_Target
#else
sampler GrassSampler : register(s0);
sampler ShadowMaskSampler : register(s1);
#define SAMPLE_GRASS(uv) tex2D(GrassSampler, uv)
#define SAMPLE_MASK(uv) tex2D(ShadowMaskSampler, uv).r
#define POSITION_OUT POSITION0
#define TARGET_OUT COLOR0
#endif

struct VS_IN { float4 Position : POSITION0; float4 Color : COLOR0; float2 Tex : TEXCOORD0; };
struct VS_OUT { float4 Position : POSITION_OUT; float4 Color : COLOR0; float2 Tex : TEXCOORD0; float3 Mask : TEXCOORD1; };

VS_OUT GrassVS(VS_IN vin)
{
    VS_OUT o;
    o.Position = mul(vin.Position, WorldViewProjection);
    o.Color = vin.Color;
    o.Tex = vin.Tex;
    float4 m = mul(vin.Position, ShadowMaskViewProjection);
    o.Mask = float3(m.x * 0.5 + 0.5, 0.5 - m.y * 0.5, 0);
    return o;
}

float4 GrassPS(VS_OUT pin) : TARGET_OUT
{
    float4 color = SAMPLE_GRASS(pin.Tex) * pin.Color;
    clip(color.a - 0.01);

    // Outside the mask area nothing is shadowed.
    float2 uv = pin.Mask.xy;
    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    float light = lerp(1.0, SAMPLE_MASK(uv), ShadowStrength * inside);
    color.rgb *= light;
    return color;
}

#if SM6
    #define GRASS_VS vs_6_0
    #define GRASS_PS ps_6_0
#elif SM4
    #define GRASS_VS vs_4_0_level_9_3
    #define GRASS_PS ps_4_0_level_9_3
#else
    #define GRASS_VS vs_3_0
    #define GRASS_PS ps_3_0
#endif

technique Grass
{
    pass P0
    {
        VertexShader = compile GRASS_VS GrassVS();
        PixelShader = compile GRASS_PS GrassPS();
    }
}
