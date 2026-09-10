#if SM6
    #define VS_SHADERMODEL vs_6_0
    #define PS_SHADERMODEL ps_6_0
#elif OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_5_0
    #define PS_SHADERMODEL ps_5_0
#endif

#if SM6
    #define UNIFORM_DEFAULT(type, name, value) type name
#else
    #define UNIFORM_DEFAULT(type, name, value) type name = value
#endif

#if OPENGL
static const float GlowIntensityScale = 1.0;
#else
static const float GlowIntensityScale = 0.80; // Tone down glow on DirectX
#endif

float4x4 World;
float4x4 WorldViewProjection;
#if !OPENGL
float4x4 BoneMatrices[256];
#endif

UNIFORM_DEFAULT(float3, LightDirection, float3(0.707, -0.707, 0));

#if SM6
Texture2D DiffuseTexture : register(t0);
SamplerState DiffuseSampler : register(s0)
{
    Filter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

Texture2D ShadowMap : register(t1);
SamplerState ShadowSampler : register(s1)
{
    Filter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};
#else
Texture2D DiffuseTexture;
sampler DiffuseSampler = sampler_state
{
    Texture = <DiffuseTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

Texture2D ShadowMap;
sampler ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};
#endif

#if SM6
float4 SampleDiffuseSampler(float2 uv) { return DiffuseTexture.Sample(DiffuseSampler, uv); }
float4 SampleShadowSampler(float2 uv) { return ShadowMap.Sample(ShadowSampler, uv); }
#define tex2D(s, uv) Sample##s(uv)
#define PS_COLOR SV_Target
#else
#define PS_COLOR COLOR
#endif

// Monster-specific parameters
UNIFORM_DEFAULT(float3, GlowColor, float3(1.0, 0.8, 0.0)); // Default gold glow
UNIFORM_DEFAULT(float, GlowIntensity, 1.0);
UNIFORM_DEFAULT(float, Time, 0);
UNIFORM_DEFAULT(bool, EnableGlow, false);
UNIFORM_DEFAULT(bool, SimpleColorMode, false); // Simple color tinting without ghosting
UNIFORM_DEFAULT(float, Alpha, 1.0);
float4x4 LightViewProjection;
UNIFORM_DEFAULT(float2, ShadowMapTexelSize, float2(1.0 / 2048.0, 1.0 / 2048.0));
UNIFORM_DEFAULT(float, ShadowBias, 0.0015);
UNIFORM_DEFAULT(float, ShadowNormalBias, 0.0025);
UNIFORM_DEFAULT(float, ShadowsEnabled, 0.0); // OpenGL compatible: use 0.0/1.0 instead of bool
UNIFORM_DEFAULT(float, ShadowStrength, 0.5);

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
};

#if !OPENGL
struct VertexShaderInputSkinned
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
    float4 Color : COLOR0;
    float2 BoneIndices : TEXCOORD1;
};
#endif

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TextureCoordinate : TEXCOORD2;
};



float SampleShadow(float3 worldPos, float3 normal)
{
    // ShadowsEnabled is uniform for the whole draw call, so this branch avoids
    // all four shadow-map samples when shadows are disabled.
    if (ShadowsEnabled < 0.5)
        return 1.0;

    float4 lightPos = mul(float4(worldPos, 1.0), LightViewProjection);
    float3 proj = lightPos.xyz / lightPos.w;
    float2 uv = proj.xy * 0.5 + 0.5;
    float depth = proj.z * 0.5 + 0.5;

    // Branchless bounds check
    float2 uvClamped = saturate(uv);
    float inBounds = step(abs(uv.x - uvClamped.x) + abs(uv.y - uvClamped.y), 0.0001);

    float ndotl = saturate(dot(normal, -LightDirection));
    float bias = ShadowBias + ShadowNormalBias * (1.0 - ndotl);

    float2 offset = ShadowMapTexelSize * 0.5;
    float4 depths;
    depths.x = tex2D(ShadowSampler, uv + float2(-offset.x, -offset.y)).r;
    depths.y = tex2D(ShadowSampler, uv + float2( offset.x, -offset.y)).r;
    depths.z = tex2D(ShadowSampler, uv + float2(-offset.x,  offset.y)).r;
    depths.w = tex2D(ShadowSampler, uv + float2( offset.x,  offset.y)).r;
    float4 visibility = step(depth - bias, depths);
    float shadow = dot(visibility, float4(0.25, 0.25, 0.25, 0.25));
    return lerp(1.0, shadow, inBounds);
}

VertexShaderOutput BuildMaterialVertex(float4 localPosition, float3 localNormal, float2 textureCoordinate)
{
    VertexShaderOutput output = (VertexShaderOutput)0;
    float4 worldPosition = mul(localPosition, World);

    output.Position = mul(localPosition, WorldViewProjection);
    output.WorldPosition = worldPosition.xyz;
    output.Normal = normalize(mul(localNormal, (float3x3)World));
    output.TextureCoordinate = textureCoordinate;
    return output;
}

VertexShaderOutput MainVS(in VertexShaderInput input)
{
    return BuildMaterialVertex(input.Position, input.Normal, input.TextureCoordinate);
}

#if !OPENGL
VertexShaderOutput MainVS_Skinned(in VertexShaderInputSkinned input)
{
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    float4 localPosition = mul(float4(input.Position, 1.0), BoneMatrices[positionBoneIndex]);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[normalBoneIndex]);
    return BuildMaterialVertex(localPosition, localNormal, input.TextureCoordinate);
}
#endif

float4 MainPS_Basic(VertexShaderOutput input) : PS_COLOR
{
    float4 color = tex2D(DiffuseSampler, input.TextureCoordinate);
    if (color.a < 0.1)
        discard;

    float3 normal = normalize(input.Normal);
    color.rgb *= max(0.1, dot(normal, -LightDirection));
    float shadowTerm = SampleShadow(input.WorldPosition, normal);
    color.rgb *= lerp(1.0 - ShadowStrength, 1.0, shadowTerm);
    color.a *= Alpha;
    return color;
}

float4 MainPS(VertexShaderOutput input) : PS_COLOR
{
    float4 color = tex2D(DiffuseSampler, input.TextureCoordinate);
    
    if (color.a < 0.1)
        discard;
    
    float3 normal = normalize(input.Normal);
    float lightIntensity = max(0.1, dot(normal, -LightDirection));
    color.rgb *= lightIntensity;

    float3 originalColor = color.rgb;
    float3 effectiveGlowColor = GlowColor * GlowIntensityScale;

    if (SimpleColorMode == true)
    {
        color.rgb = originalColor * effectiveGlowColor * GlowIntensity;
    }
    else if (EnableGlow == true && GlowIntensity > 0.0)
    {
        // EnableGlow is uniform for the draw call. Keeping the ghost samples inside
        // this branch removes four texture fetches from ordinary monsters.
        float subtlePulse = (1.0 + sin(Time * 1.5)) * 0.03 + 0.97;
        float shimmer = (1.0 + sin(Time * 20.0 + normal.x * 12.0)) * 0.15 + 0.85;
        float3 metallic = effectiveGlowColor * 2.0;
        float intensityMultiplier = GlowIntensity * GlowIntensityScale;
        float extraGlow = max(0.0, intensityMultiplier - 2.0) * 0.5;
        float glowEffect = (1.0 + sin(Time * 4.0)) * 0.1 + 0.8;

        float2 ghostOffset1 = float2(sin(Time * 4.0) * 0.035, cos(Time * 3.5) * 0.035);
        float2 ghostOffset2 = float2(sin(Time * 5.5 + 2.1) * 0.025, cos(Time * 4.8 + 1.8) * 0.025);
        float2 ghostOffset3 = float2(sin(Time * 6.2 + 4.2) * 0.02, cos(Time * 5.9 + 3.7) * 0.02);
        float2 ghostOffset4 = float2(sin(Time * 3.3 + 1.1) * 0.015, cos(Time * 6.7 + 2.3) * 0.015);

        float4 ghost1 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset1);
        float4 ghost2 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset2);
        float4 ghost3 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset3);
        float4 ghost4 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset4);

        float3 glowColor = originalColor * metallic * (2.2 * intensityMultiplier) * subtlePulse;
        glowColor += ghost1.rgb * (0.5 * intensityMultiplier) * shimmer;
        glowColor += ghost2.rgb * (0.4 * intensityMultiplier) * shimmer;
        glowColor += ghost3.rgb * (0.3 * intensityMultiplier) * shimmer;
        glowColor += ghost4.rgb * (0.2 * intensityMultiplier) * shimmer;
        glowColor += effectiveGlowColor * glowEffect * extraGlow;
        color.rgb = glowColor;
    }

    float shadowTerm = SampleShadow(input.WorldPosition, normal);
    float shadowMix = lerp(1.0 - ShadowStrength, 1.0, shadowTerm);
    color.rgb *= shadowMix;

    color.a *= Alpha;
    
    return color;
}

technique MonsterMaterialDrawing_Basic
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS_Basic();
    }
}

#if !OPENGL
technique MonsterMaterialDrawing_Basic_Skinned
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS_Skinned();
        PixelShader = compile PS_SHADERMODEL MainPS_Basic();
    }
}
#endif

technique MonsterMaterialDrawing
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

#if !OPENGL
technique MonsterMaterialDrawing_Skinned
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS_Skinned();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
#endif
