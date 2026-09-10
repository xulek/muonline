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
static const float GlowIntensityScale = 0.80;
#endif

float4x4 World;
float4x4 WorldViewProjection;
#if !OPENGL
float4x4 BoneMatrices[256];
#endif

float3 EyePosition;
UNIFORM_DEFAULT(float3, LightDirection, float3(0.707, -0.707, 0));

#if SM6
Texture2D DiffuseTexture : register(t0);
SamplerState DiffuseSampler : register(s0)
{
    Filter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

Texture2D Chrome02Texture : register(t1);
SamplerState Chrome02Sampler : register(s1)
{
    Filter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

Texture2D Shiny01Texture : register(t2);
SamplerState Shiny01Sampler : register(s2)
{
    Filter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

Texture2D Chrome01Texture : register(t3);
SamplerState Chrome01Sampler : register(s3)
{
    Filter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};

Texture2D ShadowMap : register(t4);
SamplerState ShadowSampler : register(s4)
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

Texture2D Chrome02Texture;
sampler Chrome02Sampler = sampler_state
{
    Texture = <Chrome02Texture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

Texture2D Shiny01Texture;
sampler Shiny01Sampler = sampler_state
{
    Texture = <Shiny01Texture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

Texture2D Chrome01Texture;
sampler Chrome01Sampler = sampler_state
{
    Texture = <Chrome01Texture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
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
float4 SampleChrome02Sampler(float2 uv) { return Chrome02Texture.Sample(Chrome02Sampler, uv); }
float4 SampleShiny01Sampler(float2 uv) { return Shiny01Texture.Sample(Shiny01Sampler, uv); }
float4 SampleChrome01Sampler(float2 uv) { return Chrome01Texture.Sample(Chrome01Sampler, uv); }
float4 SampleShadowSampler(float2 uv) { return ShadowMap.Sample(ShadowSampler, uv); }
#define tex2D(s, uv) Sample##s(uv)
#define PS_COLOR SV_Target
#else
#define PS_COLOR COLOR
#endif

UNIFORM_DEFAULT(int, ItemOptions, 0);
UNIFORM_DEFAULT(int, ItemMaterialGroup, -1);
UNIFORM_DEFAULT(int, ItemMaterialIndex, -1);
UNIFORM_DEFAULT(float, HighLevelTexturesAvailable, 0.0);
UNIFORM_DEFAULT(float, Time, 0);
UNIFORM_DEFAULT(float, Alpha, 1.0);
UNIFORM_DEFAULT(float3, GlowColor, float3(0.6, 0.5, 0.0));
UNIFORM_DEFAULT(bool, IsAncient, false);
UNIFORM_DEFAULT(bool, IsExcellent, false);
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
    float3 ViewDirection : TEXCOORD3;
};

struct VertexShaderOutputFast
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TextureCoordinate : TEXCOORD2;
};



// HSV to RGB conversion for smooth rainbow

// Custom spectrum for Excellent items: Blue -> Orange -> Violet (NO GREEN)
// blueScale: controls blue intensity (0.30 for +7+, 1.0 for +0-+6)
float3 GetCustomSpectrum(float phase, float blueScale)
{
    phase = frac(phase) * 3.0;
    if (phase < 1.0)
        return lerp(float3(0.0, 0.3, 1.0) * float3(1.0, 1.0, blueScale), float3(1.0, 0.5, 0.0), frac(phase)); // Blue to Orange
    else if (phase < 2.0)
        return lerp(float3(1.0, 0.5, 0.0), float3(0.6, 0.0, 0.8), frac(phase)); // Orange to Violet
    else
        return lerp(float3(0.6, 0.0, 0.8), float3(0.0, 0.3, 1.0) * float3(1.0, 1.0, blueScale), frac(phase)); // Violet to Blue
}


float SampleShadow(float3 worldPos, float3 normal)
{
    // ShadowsEnabled is uniform for the whole draw call. Avoid all nine
    // shadow-map samples when this material is rendered without shadows.
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
    output.ViewDirection = normalize(EyePosition - worldPosition.xyz);
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

VertexShaderOutputFast BuildMaterialVertexFast(float4 localPosition, float3 localNormal, float2 textureCoordinate)
{
    VertexShaderOutputFast output = (VertexShaderOutputFast)0;
    float4 worldPosition = mul(localPosition, World);
    output.Position = mul(localPosition, WorldViewProjection);
    output.WorldPosition = worldPosition.xyz;
    output.Normal = normalize(mul(localNormal, (float3x3)World));
    output.TextureCoordinate = textureCoordinate;
    return output;
}

VertexShaderOutputFast MainVS_Fast(in VertexShaderInput input)
{
    return BuildMaterialVertexFast(input.Position, input.Normal, input.TextureCoordinate);
}

#if !OPENGL
VertexShaderOutputFast MainVS_FastSkinned(in VertexShaderInputSkinned input)
{
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    float4 localPosition = mul(float4(input.Position, 1.0), BoneMatrices[positionBoneIndex]);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[normalBoneIndex]);
    return BuildMaterialVertexFast(localPosition, localNormal, input.TextureCoordinate);
}
#endif


bool IsWarmUpgradeSet(int setIndex)
{
    return setIndex == 4 ||
           setIndex == 14 ||
           setIndex == 15 ||
           setIndex == 17 ||
           (setIndex >= 39 && setIndex <= 42);
}

float3 GetUpgradePrimaryColor(int setIndex, float3 bodyLight)
{
    if (IsWarmUpgradeSet(setIndex))
        return float3(bodyLight.r, bodyLight.g * 0.5, 0.0);

    if (setIndex == 18 || setIndex == 43)
        return float3(0.0, bodyLight.g * 0.5, bodyLight.b);

    if (setIndex == 21 || setIndex == 44)
        return float3(1.0, 1.0, 1.0);

    return bodyLight;
}

float3 GetUpgradeReflectionColor(int setIndex, float3 bodyLight)
{
    if (IsWarmUpgradeSet(setIndex))
        return float3(1.0, 0.5, 0.0);

    if (setIndex == 18 || setIndex == 43)
        return float3(0.0, 0.5, 1.0);

    if (setIndex == 21 || setIndex == 44)
        return float3(1.0, 1.0, 1.0);

    // The complete legacy palette contains set-specific entries that are not
    // available in the asset description. Keep unknown sets neutral instead
    // of assigning an invented hue.
    return lerp(float3(0.72, 0.78, 0.90), bodyLight, 0.35);
}

float4 RenderHighLevelArmor(
    VertexShaderOutput input,
    float4 diffuseSample,
    float3 normal,
    float itemLevel)
{
    float ndotl = saturate(dot(normal, -LightDirection));
    float3 bodyLight = lerp(float3(0.28, 0.30, 0.34), float3(1.0, 1.0, 1.0), ndotl);

    float shadowTerm = SampleShadow(input.WorldPosition, normal);
    float shadowMix = lerp(1.0 - ShadowStrength, 1.0, shadowTerm);

    // SourceMain renders the base armor at 90% of the character light, then
    // adds the three reflective layers without rewriting depth.
    float3 result = diffuseSample.rgb * bodyLight * 0.9 * shadowMix;

    float wave = frac(Time * 0.1);
    float wave2 = frac(Time * 0.2) * 1.2 - 0.4;

    float2 primaryUv;
    if (itemLevel >= 13.0)
    {
        float3 animatedLight = float3(cos(Time), sin(Time * 2.0), 1.0);
        float normalLight = dot(normal, animatedLight);
        primaryUv.x = normalLight + normal.y * 0.5 + animatedLight.y * 3.0;
        primaryUv.y = 1.0 - normalLight - normal.z * 0.5 - wave * 3.0;
    }
    else
    {
        primaryUv.x = (normal.z + normal.x) * 0.8 + wave2 * 2.0;
        primaryUv.y = (normal.y + normal.x) + wave2 * 3.0;
    }

    float2 metalUv = float2(
        normal.z * 0.5 + 0.2,
        normal.y * 0.5 + 0.5);

    float2 chromeUv = float2(
        normal.z * 0.5 + wave,
        normal.y * 0.5 + wave * 2.0);

    float3 primaryColor = GetUpgradePrimaryColor(ItemMaterialIndex, bodyLight);
    float3 reflectionColor = GetUpgradeReflectionColor(ItemMaterialIndex, bodyLight);

    float3 primaryLayer = tex2D(Chrome02Sampler, primaryUv).rgb;
    float3 metalLayer = tex2D(Shiny01Sampler, metalUv).rgb;
    float3 chromeLayer = tex2D(Chrome01Sampler, chromeUv).rgb;

    // Levels 11-12 use the regular CHROME2 movement. Levels 13-15 use
    // CHROME4 and receive a slightly stronger main reflection.
    float levelStrength = saturate((itemLevel - 11.0) * 0.08 + 0.76);
    float primaryStrength = itemLevel >= 13.0 ? 0.82 : 0.72;

    result += primaryLayer * primaryColor * primaryStrength * levelStrength;
    result += metalLayer * reflectionColor * 0.30;
    result += chromeLayer * reflectionColor * 0.20;

    return float4(result, diffuseSample.a * Alpha);
}

float4 MainPS_UpgradeFast(VertexShaderOutputFast input) : PS_COLOR
{
    float4 color = tex2D(DiffuseSampler, input.TextureCoordinate);
    if (color.a < 0.1)
        discard;

    float itemOptions = max(0.0, (float)ItemOptions);
    float itemLevel = itemOptions - floor(itemOptions * (1.0 / 16.0)) * 16.0;
    float3 normal = normalize(input.Normal);
    color.rgb *= max(0.1, dot(normal, -LightDirection));

    float3 effectColor = GlowColor * GlowIntensityScale;
    float brightness;
    float ghostIntensity;
    if (itemLevel < 9.0)
    {
        brightness = 1.6 + (itemLevel - 8.0) * 0.2;
        ghostIntensity = 0.30;
    }
    else if (itemLevel < 10.0)
    {
        brightness = 1.8 + (itemLevel - 9.0) * 0.2;
        ghostIntensity = 0.8;
    }
    else
    {
        brightness = 1.8 + (itemLevel - 10.0) * 0.2;
        ghostIntensity = 0.7 + itemLevel * (1.0 / 30.0);
    }

    float subtlePulse = (1.0 + sin(Time * 0.8)) * 0.03 + 0.97;
    float shimmer = (1.0 + sin(Time * 8.0 + normal.x * 12.0)) * 0.15 + 0.85;
    float2 uv = input.TextureCoordinate;
    float4 ghost1 = tex2D(DiffuseSampler, uv + float2(sin(Time * 0.8) * 0.035, cos(Time * 0.7) * 0.035) * ghostIntensity);
    float4 ghost2 = tex2D(DiffuseSampler, uv + float2(sin(Time * 1.0 + 2.1) * 0.025, cos(Time * 0.9 + 1.8) * 0.025) * ghostIntensity);
    float4 ghost3 = tex2D(DiffuseSampler, uv + float2(sin(Time * 1.2 + 4.2) * 0.02, cos(Time * 1.1 + 3.7) * 0.02) * ghostIntensity);
    float4 ghost4 = tex2D(DiffuseSampler, uv + float2(sin(Time * 0.6 + 1.1) * 0.015, cos(Time * 1.3 + 2.3) * 0.015) * ghostIntensity);

    color.rgb = color.rgb * (effectColor * 0.8) * brightness * subtlePulse;
    color.rgb += ghost1.rgb * (0.8 * ghostIntensity) * shimmer * GlowIntensityScale;
    color.rgb += ghost2.rgb * (0.6 * ghostIntensity) * shimmer * GlowIntensityScale;
    color.rgb += ghost3.rgb * (0.5 * ghostIntensity) * shimmer * GlowIntensityScale;
    color.rgb += ghost4.rgb * (0.4 * ghostIntensity) * shimmer * GlowIntensityScale;

    float level10Mask = step(10.0, itemLevel);
    float extraGlow = (itemLevel - 9.0) * 0.1;
    float glowEffect = (1.0 + sin(Time)) * 0.03 + 0.2;
    color.rgb += effectColor * glowEffect * extraGlow * level10Mask;

    float shadowTerm = SampleShadow(input.WorldPosition, normal);
    color.rgb *= lerp(1.0 - ShadowStrength, 1.0, shadowTerm);
    return color;
}

float4 MainPS(VertexShaderOutput input) : PS_COLOR
{
    float4 color = tex2D(DiffuseSampler, input.TextureCoordinate);
    
    if (color.a < 0.1)
        discard;
    
    float itemOptions = max(0.0, (float)ItemOptions);
    float itemLevel = itemOptions - floor(itemOptions * (1.0 / 16.0)) * 16.0;
    
    float3 normal = normalize(input.Normal);

    bool isArmorPart = ItemMaterialGroup >= 7 && ItemMaterialGroup <= 11;
    bool useHighLevelArmor =
        HighLevelTexturesAvailable > 0.5 &&
        isArmorPart &&
        itemLevel >= 11.0;

    if (useHighLevelArmor == true)
    {
        // Build the +11/+13 chrome material first, then continue through the
        // level-dependent glow section below. Ordinary upgraded armor must
        // keep the same level glow as weapons and non-armor equipment.
        color = RenderHighLevelArmor(input, color, normal, itemLevel);
    }
    else
    {
        float lightIntensity = max(0.1, dot(normal, -LightDirection));
        color.rgb *= lightIntensity;
    }
    
    
    float3 effectColor = GlowColor * GlowIntensityScale;
    float brightness = 1.0;
    float ghostIntensity = 0.0;
    
    if (itemLevel < 7)
    {    
        brightness = 1;
        ghostIntensity = 0;
    }
    else if (itemLevel < 9)
    {
        effectColor = GlowColor * GlowIntensityScale;
        brightness = 1.6 + (itemLevel -8) * 0.2;
        ghostIntensity = 0.30;
    }
    else if (itemLevel < 10)
    {
        effectColor = GlowColor * GlowIntensityScale;
        brightness = 1.8 + (itemLevel - 9) * 0.2;
        ghostIntensity = 0.8;
    }
    else
    {
        effectColor = GlowColor * GlowIntensityScale;
        brightness = 1.8 + (itemLevel -10 ) * 0.2;
        ghostIntensity = 0.7 + (itemLevel * (1.0 / 30.0));
    }
    
    float subtlePulse = (1.0 + sin(Time * 0.8)) * 0.03 + 0.97;
    float shimmer = (1.0 + sin(Time * 8.0 + normal.x * 12.0)) * 0.15 + 0.85;
    
    // Material flags are uniform for a draw call. Conditional sampling removes
    // up to twelve unnecessary diffuse fetches from ordinary/non-special items.
    float4 ghost1 = 0.0;
    float4 ghost2 = 0.0;
    float4 ghost3 = 0.0;
    float4 ghost4 = 0.0;
    // High-level armor keeps its Chrome material and also receives the regular
    // upgrade-level glow unless the Excellent path supplies its own effect.
    bool applyHighLevelLevelGlow = useHighLevelArmor && IsExcellent == false;
    if ((useHighLevelArmor == false || applyHighLevelLevelGlow == true) && itemLevel >= 7.0)
    {
        float2 ghostOffset1 = float2(sin(Time * 0.8) * 0.035, cos(Time * 0.7) * 0.035) * ghostIntensity;
        float2 ghostOffset2 = float2(sin(Time * 1.0 + 2.1) * 0.025, cos(Time * 0.9 + 1.8) * 0.025) * ghostIntensity;
        float2 ghostOffset3 = float2(sin(Time * 1.2 + 4.2) * 0.02, cos(Time * 1.1 + 3.7) * 0.02) * ghostIntensity;
        float2 ghostOffset4 = float2(sin(Time * 0.6 + 1.1) * 0.015, cos(Time * 1.3 + 2.3) * 0.015) * ghostIntensity;
        ghost1 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset1);
        ghost2 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset2);
        ghost3 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset3);
        ghost4 = tex2D(DiffuseSampler, input.TextureCoordinate + ghostOffset4);
    }

    float4 ancientGhost1 = 0.0;
    float4 ancientGhost2 = 0.0;
    if (IsAncient == true)
    {
        float2 ancientOffset1 = float2(sin(Time * 0.5) * 0.02, cos(Time * 0.4) * 0.02);
        float2 ancientOffset2 = float2(sin(Time * 0.7 + 1.0) * 0.015, cos(Time * 0.6 + 1.5) * 0.015);
        ancientGhost1 = tex2D(DiffuseSampler, input.TextureCoordinate + ancientOffset1);
        ancientGhost2 = tex2D(DiffuseSampler, input.TextureCoordinate + ancientOffset2);
    }

    float4 excellentGhost1 = 0.0;
    float4 excellentGhost2 = 0.0;
    float4 excellentGhost3 = 0.0;
    float4 excellentGhost4 = 0.0;
    float4 excellentGhost5 = 0.0;
    float4 excellentGhost6 = 0.0;
    if (IsExcellent == true)
    {
        float2 excellentOffset1 = float2(sin(Time * 0.6) * 0.03, cos(Time * 0.5) * 0.03);
        float2 excellentOffset2 = float2(sin(Time * 0.8 + 1.2) * 0.025, cos(Time * 0.7 + 1.8) * 0.025);
        float2 excellentOffset3 = float2(sin(Time * 1.0 + 2.4) * 0.02, cos(Time * 0.9 + 2.6) * 0.02);
        float2 excellentOffset4 = float2(sin(Time * 0.5 + 3.6) * 0.015, cos(Time * 1.1 + 3.2) * 0.015);
        float2 excellentOffset5 = float2(sin(Time * 0.7 + 4.8) * 0.035, cos(Time * 0.6 + 4.4) * 0.035);
        float2 excellentOffset6 = float2(sin(Time * 0.9 + 6.0) * 0.028, cos(Time * 0.8 + 5.5) * 0.028);
        excellentGhost1 = tex2D(DiffuseSampler, input.TextureCoordinate + excellentOffset1);
        excellentGhost2 = tex2D(DiffuseSampler, input.TextureCoordinate + excellentOffset2);
        excellentGhost3 = tex2D(DiffuseSampler, input.TextureCoordinate + excellentOffset3);
        excellentGhost4 = tex2D(DiffuseSampler, input.TextureCoordinate + excellentOffset4);
        excellentGhost5 = tex2D(DiffuseSampler, input.TextureCoordinate + excellentOffset5);
        excellentGhost6 = tex2D(DiffuseSampler, input.TextureCoordinate + excellentOffset6);
    }
    
    
    if (useHighLevelArmor == false)
    {
        // Legacy material path for lower-level items and non-armor equipment.
        if (itemLevel >= 7)
        {
            float3 metallic = effectColor * 0.8;
            color.rgb = color.rgb * metallic * brightness * subtlePulse;
            color.rgb += ghost1.rgb * (0.8 * ghostIntensity) * shimmer * GlowIntensityScale;
            color.rgb += ghost2.rgb * (0.6 * ghostIntensity) * shimmer * GlowIntensityScale;
            color.rgb += ghost3.rgb * (0.5 * ghostIntensity) * shimmer * GlowIntensityScale;
            color.rgb += ghost4.rgb * (0.4 * ghostIntensity) * shimmer * GlowIntensityScale;
        }
        else
        {
            color.rgb = color.rgb * brightness;
        }

        float level10Mask = step(10.0, itemLevel);
        float extraGlow = (itemLevel - 9.0) * 0.1;
        float glowEffect = (1.0 + sin(Time * 1.0)) * 0.03 + 0.2;
        color.rgb += effectColor * glowEffect * extraGlow * level10Mask;
    }
    else if (applyHighLevelLevelGlow == true)
    {
        // Preserve the +11/+13 Chrome material and add only the old upgrade-level
        // glow on top. Do not recolor or multiply the base material again.
        float levelGlowScale = 0.72 + saturate((itemLevel - 7.0) / 8.0) * 0.48;
        color.rgb += ghost1.rgb * (0.8 * ghostIntensity) * shimmer * GlowIntensityScale * levelGlowScale;
        color.rgb += ghost2.rgb * (0.6 * ghostIntensity) * shimmer * GlowIntensityScale * levelGlowScale;
        color.rgb += ghost3.rgb * (0.5 * ghostIntensity) * shimmer * GlowIntensityScale * levelGlowScale;
        color.rgb += ghost4.rgb * (0.4 * ghostIntensity) * shimmer * GlowIntensityScale * levelGlowScale;

        float level10Mask = step(10.0, itemLevel);
        float extraGlow = max(0.0, itemLevel - 9.0) * 0.1;
        float glowEffect = (1.0 + sin(Time * 1.0)) * 0.03 + 0.2;
        color.rgb += effectColor * glowEffect * extraGlow * level10Mask * levelGlowScale;
    }
    
    // Ancient is uniform for the draw call. Keep all sweep ALU out of ordinary items.
    if (IsAncient == true)
    {
        // Ancient item effect - fast blue sweep with pause
        float ancientEnabled = 1.0;
    float3 ancientColor = float3(0.3, 0.5, 1.0); // More blue color

    // Cycle with pause: sweep takes 12% of cycle, pause is 88%
    float cycleSpeed = 0.1;
    float sweepPortion = 0.15; // Sweep happens in first 12% of cycle, very long pause after

    float cycleProgress = frac(Time * cycleSpeed); // 0 to 1 over cycle
    float sweepProgress = saturate(cycleProgress / sweepPortion); // 0 to 1 during sweep, then stays at 1

    // Fast sweep across mesh using texture coordinate
    float sweepPosition = sweepProgress;
    float meshPosition = input.TextureCoordinate.x;

    // Create sharp beam that sweeps across
    float beamWidth = 0.15;
    float distFromBeam = abs(meshPosition - sweepPosition);
    float beamIntensity = 1.0 - saturate(distFromBeam / beamWidth);
    beamIntensity = pow(beamIntensity, 2.0) * 3.0; // Sharp falloff, bright center

    // Fade out beam at the end of sweep (before pause)
    float fadeOut = 1.0 - smoothstep(0.85, 1.0, sweepProgress);
    beamIntensity *= fadeOut;

    // Add secondary vertical wave for depth
    float wave2 = sin(Time * 3.0 + input.TextureCoordinate.y * 6.0) * 0.3 + 0.7;
    float combinedWave = beamIntensity * wave2;

    float levelBoost = (itemLevel >= 9) ? 2.0 : 1.0;
    color.rgb += ancientGhost1.rgb * ancientColor * combinedWave * 1.5 * levelBoost * ancientEnabled;
    color.rgb += ancientGhost2.rgb * ancientColor * combinedWave * 1.1 * levelBoost * ancientEnabled;

    // Subtle base blue glow (always present)
    float baseGlow = sin(Time * 0.8) * 0.08 + 0.15;
    float baseGlowIntensity = (itemLevel >= 9) ? 0.5 : 0.25;
        color.rgb += color.rgb * ancientColor * baseGlow * baseGlowIntensity * ancientEnabled;
    }

    if (IsExcellent == true)
    {
        // ==================== EXCELLENT SWEEP PULSE EFFECT ====================
    // Similar to Ancient sweep but with semi-transparent violet color (only for +7+)
    float excellentSweepEnabled = itemLevel >= 7 ? 1.0 : 0.0;
    float3 excellentSweepColor = float3(0.5, 0.3, 0.7); // Semi-transparent violet (less white, more violet)
    
    // Cycle with pause: sweep takes 15% of cycle, pause is 85%
    float exCycleSpeed = 0.12; 
    float exSweepPortion = 0.15;
    
    float exCycleProgress = frac(Time * exCycleSpeed);
    float exSweepProgress = saturate(exCycleProgress / exSweepPortion);
    
    // Fast sweep across mesh
    float exSweepPosition = exSweepProgress;
    float exMeshPosition = input.TextureCoordinate.x;
    
    // Create sharp beam
    float exBeamWidth = 0.18;
    float exDistFromBeam = abs(exMeshPosition - exSweepPosition);
    float exBeamIntensity = 1.0 - saturate(exDistFromBeam / exBeamWidth);
    exBeamIntensity = pow(exBeamIntensity, 2.0) * 2.0; // Reduced intensity for semi-transparency
    
    // Fade out beam at the end of sweep
    float exFadeOut = 1.0 - smoothstep(0.85, 1.0, exSweepProgress);
    exBeamIntensity *= exFadeOut;
    
    // Add secondary vertical wave for depth
    float exWave2 = sin(Time * 3.5 + input.TextureCoordinate.y * 6.0) * 0.3 + 0.7;
    float exCombinedWave = exBeamIntensity * exWave2;
    
    float exLevelBoost = (itemLevel >= 9) ? 1.5 : 1.0; // Reduced boost for subtlety
    color.rgb += excellentGhost1.rgb * excellentSweepColor * exCombinedWave * 1.2 * exLevelBoost * excellentSweepEnabled;
    color.rgb += excellentGhost2.rgb * excellentSweepColor * exCombinedWave * 0.9 * exLevelBoost * excellentSweepEnabled;
    
    // Subtle base violet glow (always present for excellent +7+)
    float exBaseGlow = sin(Time * 0.9) * 0.08 + 0.12; // Reduced base glow
    float exBaseGlowIntensity = (itemLevel >= 9) ? 0.4 : 0.2; // Reduced intensity
    color.rgb += color.rgb * excellentSweepColor * exBaseGlow * exBaseGlowIntensity * excellentSweepEnabled;
    
    // ==================== ENHANCED EXCELLENT EFFECT ====================
        float excellentEnabled = 1.0;
        
        // 1. Fresnel/Rim lighting effect - glowing edges
        float3 viewDir = normalize(input.ViewDirection);
        float fresnel = 1.0 - saturate(dot(viewDir, normal));
        fresnel = pow(fresnel, 2.5); // Sharper edge glow
        
        // 2. Custom spectrum color cycling (Blue -> Orange -> Violet, NO GREEN)
        float hueBase = frac(Time * 0.15); // Slow base rotation
        float hueSpatial = input.TextureCoordinate.x * 0.3 + input.TextureCoordinate.y * 0.2; // Spatial variation
        float hueNormal = (normal.x + normal.y) * 0.1; // Normal-based variation
        
        // Blue intensity: 0.30 for +7+, 1.0 for +0-+6
        float blueScale = (itemLevel >= 7) ? 0.01 : 1.0;
        
        // Excellent effect intensity scale
        float exScale = (itemLevel >= 7) ? 1.8 : 1.8;
        
        // Create multiple spectrum colors at different phases
        float3 rainbow1 = GetCustomSpectrum(hueBase + hueSpatial, blueScale);
        float3 rainbow2 = GetCustomSpectrum(hueBase + hueSpatial + 0.33, blueScale);
        float3 rainbow3 = GetCustomSpectrum(hueBase + hueSpatial + 0.66, blueScale);
        float3 rainbow4 = GetCustomSpectrum(hueBase + hueNormal + 0.5, blueScale);
        
        // 3. Sweeping shine effect - diagonal light beams
        float sweepSpeed1 = Time * 1.5;
        float sweepSpeed2 = Time * 1.2;
        float sweepSpeed3 = Time * 0.9;
        
        // Multiple diagonal sweeps at different angles
        float sweep1 = input.TextureCoordinate.x + input.TextureCoordinate.y * 0.5;
        float sweep2 = input.TextureCoordinate.x * 0.7 - input.TextureCoordinate.y * 0.3;
        float sweep3 = input.TextureCoordinate.y + input.TextureCoordinate.x * 0.3;
        
        float beam1 = pow(sin(sweep1 * 6.0 - sweepSpeed1) * 0.5 + 0.5, 8.0);
        float beam2 = pow(sin(sweep2 * 8.0 + sweepSpeed2) * 0.5 + 0.5, 10.0);
        float beam3 = pow(sin(sweep3 * 5.0 - sweepSpeed3) * 0.5 + 0.5, 6.0);
        
        float combinedBeams = beam1 * 0.7 + beam2 * 0.5 + beam3 * 0.4;
        
        // 4. Pulsating aura
        float pulse1 = sin(Time * 1.2) * 0.5 + 0.5;
        float pulse2 = sin(Time * 0.8 + 1.5) * 0.5 + 0.5;
        float pulse3 = sin(Time * 1.5 + 3.0) * 0.5 + 0.5;
        float combinedPulse = (pulse1 + pulse2 + pulse3) / 3.0;
        
        // 6. Color wave effect - colors flowing across the surface
        float colorWave1 = sin(Time * 0.6 + input.TextureCoordinate.x * 4.0) * 0.5 + 0.5;
        float colorWave2 = sin(Time * 0.5 + input.TextureCoordinate.y * 3.0 + 1.0) * 0.5 + 0.5;
        
        // Blend rainbow colors based on waves
        float3 waveColor = rainbow1 * colorWave1 + rainbow2 * colorWave2 + rainbow3 * (1.0 - colorWave1 * colorWave2);
        waveColor = normalize(waveColor) * length(waveColor) * 0.4;
        
        // Apply ghost layers with rainbow colors
        float ghostBaseIntensity = 0.2 * combinedPulse + 0.1;
        
        color.rgb += excellentGhost1.rgb * rainbow1 * ghostBaseIntensity * (1.0 * exScale) * excellentEnabled;
        color.rgb += excellentGhost2.rgb * rainbow2 * ghostBaseIntensity * (0.9 * exScale) * excellentEnabled;
        color.rgb += excellentGhost3.rgb * rainbow3 * ghostBaseIntensity * (0.8 * exScale) * excellentEnabled;
        color.rgb += excellentGhost4.rgb * rainbow4 * ghostBaseIntensity * (0.7 * exScale) * excellentEnabled;
        color.rgb += excellentGhost5.rgb * waveColor * ghostBaseIntensity * (0.6 * exScale) * excellentEnabled;
        color.rgb += excellentGhost6.rgb * rainbow1 * ghostBaseIntensity * (0.5 * exScale) * excellentEnabled;
        
        // Apply sweeping beams with rainbow
        float3 beamColor = lerp(rainbow1, rainbow2, sin(Time * 0.4) * 0.5 + 0.5);
        color.rgb += beamColor * combinedBeams * 0.05 * excellentEnabled;
        
        // Apply rim/fresnel glow with shifting colors
        float3 rimColor = lerp(rainbow3, rainbow4, fresnel);
        color.rgb += rimColor * fresnel * 0.05 * excellentEnabled;
        
        // Subtle overall color enhancement
        float3 overlayColor = waveColor * 0.015;
        color.rgb += color.rgb * overlayColor * excellentEnabled;
        
        // Brightness boost for excellent items
        color.rgb *= lerp(1.0, 1.4, excellentEnabled);
    }

    if (useHighLevelArmor == false)
    {
        float shadowTerm = SampleShadow(input.WorldPosition, normal);
        float shadowMix = lerp(1.0 - ShadowStrength, 1.0, shadowTerm);
        color.rgb *= shadowMix;
    }

    return color;
}

technique BasicColorDrawing_UpgradeFast
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS_Fast();
        PixelShader = compile PS_SHADERMODEL MainPS_UpgradeFast();
    }
}

#if !OPENGL
technique BasicColorDrawing_UpgradeFast_Skinned
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS_FastSkinned();
        PixelShader = compile PS_SHADERMODEL MainPS_UpgradeFast();
    }
}
#endif

technique BasicColorDrawing
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

#if !OPENGL
technique BasicColorDrawing_Skinned
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS_Skinned();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
#endif
