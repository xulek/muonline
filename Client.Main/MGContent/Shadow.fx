#if SM6
    // WindowsDX12 and DesktopVK use DXC and require Shader Model 6.0.
    #define VS_SHADERMODEL vs_6_0
    #define PS_SHADERMODEL ps_6_0
#elif OPENGL
    // DesktopGL → SM 3.0
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    // WindowsDX11
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif


float4x4 WorldViewProjection;
float4   ShadowTint;
#if SM6
// Native backends (DX12/Vulkan) bind resources by register; without explicit slots the Vulkan
// reflection reports a sampler slot outside the device sampler collection and Apply() throws.
Texture2D ShadowTexture : register(t0);
SamplerState ShadowSampler : register(s0)
{
    Filter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

float4 SampleShadowTexture(float2 uv) { return ShadowTexture.Sample(ShadowSampler, uv); }
#define tex2D(s, uv) SampleShadowTexture(uv)
#else
Texture2D ShadowTexture;

sampler ShadowSampler = sampler_state
{
    Texture   = <ShadowTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};
#endif

//---------------- VS -------------------------
struct VS_IN  { float4 Position : POSITION0; float2 Tex : TEXCOORD0; };
struct VS_OUT { float4 Position : SV_POSITION; float2 Tex : TEXCOORD0; };

VS_OUT ShadowVS(VS_IN vin)
{
    VS_OUT vout;
    vout.Position = mul(vin.Position, WorldViewProjection);
    vout.Tex      = vin.Tex;
    return vout;
}

//---------------- PS -------------------------
float4 ShadowPS(VS_OUT pin) : SV_TARGET
{
    float alphaMask = tex2D(ShadowSampler, pin.Tex).a;
    return float4(0,0,0, alphaMask * ShadowTint.a);
}

technique Shadow
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL ShadowVS();
        PixelShader  = compile PS_SHADERMODEL ShadowPS();
    }
}

//---------------- Batched blob shadows -------
// World-space vertices of many blobs in one draw; the per-blob opacity travels in the vertex alpha.
struct VS_BATCH_IN  { float4 Position : POSITION0; float4 Color : COLOR0; float2 Tex : TEXCOORD0; };
struct VS_BATCH_OUT { float4 Position : SV_POSITION; float4 Color : COLOR0; float2 Tex : TEXCOORD0; };

VS_BATCH_OUT ShadowBatchedVS(VS_BATCH_IN vin)
{
    VS_BATCH_OUT vout;
    vout.Position = mul(vin.Position, WorldViewProjection);
    vout.Color    = vin.Color;
    vout.Tex      = vin.Tex;
    return vout;
}

float4 ShadowBatchedPS(VS_BATCH_OUT pin) : SV_TARGET
{
    float alphaMask = tex2D(ShadowSampler, pin.Tex).a;
    return float4(0,0,0, alphaMask * pin.Color.a);
}

technique ShadowBatched
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL ShadowBatchedVS();
        PixelShader  = compile PS_SHADERMODEL ShadowBatchedPS();
    }
}
