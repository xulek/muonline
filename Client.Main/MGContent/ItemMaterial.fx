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

// World / distance fog, same uniforms and formula as DynamicLighting.fx (WorldFog.Apply).
UNIFORM_DEFAULT(float, FogEnabled, 0.0);
UNIFORM_DEFAULT(float3, FogColor, float3(0.0, 0.0, 0.0));
UNIFORM_DEFAULT(float, FogStart, 2000.0);
UNIFORM_DEFAULT(float, FogEnd, 2700.0);
UNIFORM_DEFAULT(float3, FogCameraPosition, float3(0.0, 0.0, 0.0));

float WorldFogFactor(float3 worldPos)
{
    float f = 0.0;
    [branch] if (FogEnabled > 0.0)
        f = saturate((length(worldPos - FogCameraPosition) - FogStart) / max(FogEnd - FogStart, 1.0)) * FogEnabled;
    return f;
}

float3 ApplyWorldFog(float3 color, float3 worldPos)
{
    return lerp(color, FogColor, WorldFogFactor(worldPos));
}

#if OPENGL
static const float GlowIntensityScale = 1.0;
#else
static const float GlowIntensityScale = 0.80;
#endif

float4x4 World;
float4x4 WorldViewProjection;
#if !OPENGL
// The DirectX 12 effect compiler pairs every texture a shader reads with the sampler of the same register and
// rejects a texture that has none, so there the data textures are read through a point sampler at the texel
// centre. Every other backend keeps Load.
#if SM6 && !VULKAN
float4 LoadTexel(Texture2D tex, SamplerState texSampler, int3 p)
{
    float2 size;
    tex.GetDimensions(size.x, size.y);
    return tex.SampleLevel(texSampler, (float2(p.xy) + 0.5) / size, 0);
}
#define DATA_TEXEL(tex, p) LoadTexel(tex, tex##Sampler, p)
#else
#define DATA_TEXEL(tex, p) tex.Load(p)
#endif
// GPU skinning bones come from a per-frame palette texture (1024 float4 texels per row, four texels per
// bone, one matrix row each). BonePaletteBase is the first texel of the drawn palette; a palette never
// crosses a row. Keeps the 16 KB bone array out of the constant buffer uploaded on every draw.
#if SM6
Texture2D BonePaletteTexture : register(t5);
#else
Texture2D BonePaletteTexture;
#endif
#if SM6 && !VULKAN
SamplerState BonePaletteTextureSampler : register(s5)
{
    Filter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};
#endif
float BonePaletteBase;

float4x4 LoadBone(int boneIndex)
{
    int texel = (int)(BonePaletteBase + 0.5) + min(max(boneIndex, 0), 255) * 4;
    int3 p = int3(texel & 1023, texel >> 10, 0);
    return float4x4(
        DATA_TEXEL(BonePaletteTexture, p),
        DATA_TEXEL(BonePaletteTexture, p + int3(1, 0, 0)),
        DATA_TEXEL(BonePaletteTexture, p + int3(2, 0, 0)),
        DATA_TEXEL(BonePaletteTexture, p + int3(3, 0, 0)));
}
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
    float2 BoneIndices : TEXCOORD1;
    float4 Color : COLOR0;
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
    float4 localPosition = mul(float4(input.Position, 1.0), LoadBone(positionBoneIndex));
    float3 localNormal = mul(input.Normal, (float3x3)LoadBone(normalBoneIndex));
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
    float4 localPosition = mul(float4(input.Position, 1.0), LoadBone(positionBoneIndex));
    float3 localNormal = mul(input.Normal, (float3x3)LoadBone(normalBoneIndex));
    return BuildMaterialVertexFast(localPosition, localNormal, input.TextureCoordinate);
}
#endif


// ---------------------------------------------------------------------------
// Port of SourceMain5.2 RenderPartObjectEffect (ZzzObject.cpp) and the chrome
// texture-coordinate generation of BMD::RenderMesh (ZzzBMD.cpp).
//
// The original draws the item once with the character light and then adds
// RENDER_BRIGHT passes with glBlendFunc(GL_ONE, GL_ONE). Each pass samples a
// chrome texture with normal-derived coordinates and is modulated by a colour
// from PartObjectColor / PartObjectColor2 / PartObjectColor3. Because all extra
// passes are additive they collapse into one shader: base + sum(passes).
//
//   +0..+2  base
//   +3..+4  base * (L, 0.6L, 0.6L)        L = sin(t*4)*0.15+0.6 (g_Luminosity)
//   +5..+6  base * (0.5L, 0.7L, L)
//   +7..+8  0.8 base + CHROME
//   +9..+10 0.9 base + CHROME + METAL
//   +11..12 0.9 base + CHROME2 + METAL + CHROME
//   +13..15 0.9 base + CHROME4 + METAL + CHROME
//   excellent: + texture * (E, 0.3E, 1-E)  E = sin(t*2)*0.5+0.5
//   ancient (not excellent): + CHROME3 * PartObjectColor3 * (sin(t)*0.5+0.4)
// ---------------------------------------------------------------------------

UNIFORM_DEFAULT(float3, ItemChromeColor, float3(1.0, 0.5, 0.0));   // PartObjectColor
UNIFORM_DEFAULT(float4, ItemChrome2Tint, float4(1.0, 1.0, 1.0, 0.0)); // PartObjectColor2 (w=1: absolute colour)
UNIFORM_DEFAULT(float3, ItemAncientColor, float3(0.1, 0.6, 1.0));  // PartObjectColor3
UNIFORM_DEFAULT(float3, AmbientLight, float3(0.8, 0.8, 0.8));
UNIFORM_DEFAULT(float3, SunColor, float3(1.0, 0.95, 0.85));
UNIFORM_DEFAULT(float, SunStrength, 0.8);

static const float3 LegacyLightVector = float3(0.0, -0.1, -0.8); // ZzzBMD.cpp LightVector

// Single additive RENDER_BRIGHT pass used for special wing meshes (RenderPartObjectBody):
//   1 = RENDER_CHROME  (Chrome01 slot, wave texture coordinates)
//   2 = RENDER_CHROME6 (Chrome02 slot, (n.z + n.x) * 0.8 + wave2 * 2)
//   3 = RENDER_TEXTURE (diffuse texture shifted by ChromeOverlayUvOffset)
//   4 = RENDER_TEXTURE with a clamped effect texture (Chrome02 slot) shifted by the offset
//   5 = RENDER_METAL (Shiny01)
//   6 = RENDER_CHROME4, 7 = RENDER_CHROME5 (Chrome02)
UNIFORM_DEFAULT(int, ChromeOverlayMode, 0);
UNIFORM_DEFAULT(float3, ChromeOverlayColor, float3(1.0, 1.0, 1.0));
UNIFORM_DEFAULT(float2, ChromeOverlayUvOffset, float2(0.0, 0.0));
UNIFORM_DEFAULT(float, ChromeOverlayAlpha, 1.0); // output alpha for alpha-blended overlays

float4 ShadeOverlay(float3 normal, float2 uv, float mode)
{
    float wave = frac(Time * 0.1);
    float wave2 = frac(Time * 0.2) * 1.2 - 0.4;
    float3 rgb;
    if (mode < 1.5)
    {
        rgb = tex2D(Chrome01Sampler, float2(normal.z * 0.5 + wave, normal.y * 0.5 + wave * 2.0)).rgb;
    }
    else if (mode < 2.5)
    {
        float c = (normal.z + normal.x) * 0.8 + wave2 * 2.0;
        rgb = tex2D(Chrome02Sampler, float2(c, c)).rgb;
    }
    else if (mode < 3.5)
    {
        // glBlendFunc(GL_ONE, GL_ONE) ignores the texture alpha.
        rgb = tex2D(DiffuseSampler, uv + ChromeOverlayUvOffset).rgb;
    }
    else if (mode < 4.5)
    {
        // Mesh texture coordinates shifted over a clamped effect texture (Chrome06/07, energy01).
        rgb = tex2D(Chrome02Sampler, uv + ChromeOverlayUvOffset).rgb;
    }
    else if (mode < 5.5)
    {
        // RENDER_METAL: Shiny01 with static normal-derived coordinates.
        rgb = tex2D(Shiny01Sampler, float2(normal.z * 0.5 + 0.2, normal.y * 0.5 + 0.5)).rgb;
    }
    else if (mode < 6.5)
    {
        // RENDER_CHROME4 (Chrome02, animated light vector).
        float3 l4 = float3(cos(Time), sin(Time * 2.0), 1.0);
        float d4 = dot(normal, l4);
        rgb = tex2D(Chrome02Sampler, float2(d4 + normal.y * 0.5 + l4.y * 3.0, 1.0 - d4 - (normal.z * 0.5 + wave * 3.0))).rgb;
    }
    else
    {
        // RENDER_CHROME5 (Chrome02, stronger distortion).
        float3 l5 = float3(cos(Time), sin(Time * 2.0), 1.0);
        float d5 = dot(normal, l5);
        rgb = tex2D(Chrome02Sampler, float2(d5 + normal.y * 3.0 + l5.y * 5.0, 1.0 - d5 - (normal.z * 2.5 + wave))).rgb;
    }
    return float4(rgb * ChromeOverlayColor * Alpha, ChromeOverlayAlpha);
}

// Same base lighting as DynamicLighting.fx so upgraded parts match the rest of the body.
float3 ComputeItemLight(float3 worldPos, float3 normal)
{
    float ndotlRaw = dot(normal, -LightDirection);
    float ndotl = saturate(ndotlRaw) + saturate(-ndotlRaw) * 0.35;
    float shadowFactor = saturate(lerp(1.0 - ShadowStrength, 1.0, ndotl));
    float3 light = AmbientLight * shadowFactor + SunColor * (ndotl * SunStrength);
    float shadowTerm = SampleShadow(worldPos, normal);
    return light * lerp(1.0 - ShadowStrength, 1.0, shadowTerm);
}

// Item shading for an already sampled, alpha-tested diffuse colour. The parameters are uniforms for single
// items and per-vertex data for merged player meshes (BasicColorDrawing_CompositeSkinned).
float4 ShadeItemCore(float4 diffuse, float3 worldPos, float3 normal, float itemOptionsValue,
                     bool isExcellent, bool isAncient, float3 chromeColor, float4 chrome2Tint, float3 ancientColor)
{

    float itemOptions = max(0.0, itemOptionsValue);
    float level = itemOptions - floor(itemOptions * (1.0 / 16.0)) * 16.0;

    float3 light = ComputeItemLight(worldPos, normal);
    float wave = frac(Time * 0.1);          // (WorldTime % 10000) * 0.0001
    float wave2 = frac(Time * 0.2) * 1.2 - 0.4; // (WorldTime % 5000) * 0.00024 - 0.4

    float lowChromeMode = floor(itemOptions * (1.0 / 32.0)) - floor(itemOptions * (1.0 / 128.0)) * 4.0;

    float3 rgb;
    if (level < 3.0)
    {
        rgb = diffuse.rgb * light;
        if (lowChromeMode > 0.5)
        {
            float3 l4 = float3(cos(Time), sin(Time * 2.0), 1.0);
            float d4 = dot(normal, l4);
            float2 uv4 = float2(d4 + normal.y * 0.5 + l4.y * 3.0, 1.0 - d4 - (normal.z * 0.5 + wave * 3.0));
            rgb += tex2D(Chrome02Sampler, uv4).rgb * light;          // RENDER_CHROME4
            if (lowChromeMode < 1.5)
            {
                float2 uv2 = float2((normal.z + normal.x) * 0.8 + wave2 * 2.0, (normal.y + normal.x) + wave2 * 3.0);
                rgb += tex2D(Chrome02Sampler, uv2).rgb * light * 2.25; // RENDER_CHROME2, Bright 1.5^2
            }
            else
            {
                rgb += diffuse.rgb * light * 0.25;                     // RENDER_TEXTURE | RENDER_BRIGHT
            }
        }
    }
    else if (level < 5.0)
    {
        float lum = sin(Time * 4.0) * 0.15 + 0.6;
        rgb = diffuse.rgb * light * float3(lum, lum * 0.6, lum * 0.6);
    }
    else if (level < 7.0)
    {
        float lum = sin(Time * 4.0) * 0.15 + 0.6;
        rgb = diffuse.rgb * light * float3(lum * 0.5, lum * 0.7, lum);
    }
    else
    {
        float3 bodyLight = light * (level < 9.0 ? 0.8 : 0.9);
        rgb = diffuse.rgb * bodyLight;

        // RENDER_CHROME (Chrome01, GL_REPEAT)
        float2 chromeUv = float2(normal.z * 0.5 + wave, normal.y * 0.5 + wave * 2.0);
        rgb += tex2D(Chrome01Sampler, chromeUv).rgb * chromeColor;

        // RENDER_METAL (Shiny01, clamp) from +9
        if (level >= 9.0)
        {
            float2 metalUv = float2(normal.z * 0.5 + 0.2, normal.y * 0.5 + 0.5);
            rgb += tex2D(Shiny01Sampler, metalUv).rgb * chromeColor;
        }

        // RENDER_CHROME2 (+11/+12) and RENDER_CHROME4 (+13..+15), both Chrome02
        if (level >= 11.0)
        {
            float2 uv2;
            if (level >= 13.0)
            {
                float3 l = float3(cos(Time), sin(Time * 2.0), 1.0);
                float d = dot(normal, l);
                uv2 = float2(d + normal.y * 0.5 + l.y * 3.0,
                             1.0 - d - (normal.z * 0.5 + wave * 3.0));
            }
            else
            {
                uv2 = float2((normal.z + normal.x) * 0.8 + wave2 * 2.0,
                             (normal.y + normal.x) * 1.0 + wave2 * 3.0);
            }
            float3 chrome2Color = chrome2Tint.w > 0.5 ? chrome2Tint.xyz : bodyLight * chrome2Tint.xyz;
            rgb += tex2D(Chrome02Sampler, uv2).rgb * chrome2Color;
        }
    }

    if (isExcellent)
    {
        float e = sin(Time * 2.0) * 0.5 + 0.5;
        rgb += diffuse.rgb * float3(e, e * 0.3, 1.0 - e);
    }
    else if (isAncient)
    {
        float a = max(0.0, sin(Time) * 0.5 + 0.4);
        float d = dot(normal, LegacyLightVector);
        rgb += tex2D(Chrome02Sampler, float2(d, 1.0 - d)).rgb * ancientColor * a;
    }

    return float4(ApplyWorldFog(rgb, worldPos), diffuse.a * Alpha);
}

float4 ShadeItem(float3 worldPos, float3 normal, float2 uv)
{
    float overlayMode = (float)ChromeOverlayMode;
    if (overlayMode > 0.5)
    {
        // Overlay passes are added on top of the fogged body: fade them out with the fog.
        float4 overlay = ShadeOverlay(normal, uv, overlayMode);
        overlay.rgb *= 1.0 - WorldFogFactor(worldPos);
        return overlay;
    }

    float4 diffuse = tex2D(DiffuseSampler, uv);
    if (diffuse.a < 0.1)
        discard;

    return ShadeItemCore(diffuse, worldPos, normal, (float)ItemOptions, IsExcellent == true, IsAncient == true,
                         ItemChromeColor, ItemChrome2Tint, ItemAncientColor);
}

float4 MainPS_UpgradeFast(VertexShaderOutputFast input) : PS_COLOR
{
    return ShadeItem(input.WorldPosition, normalize(input.Normal), input.TextureCoordinate);
}

float4 MainPS(VertexShaderOutput input) : PS_COLOR
{
    return ShadeItem(input.WorldPosition, normalize(input.Normal), input.TextureCoordinate);
}

#if !OPENGL && !SM6
// ---------------------------------------------------------------------------
// Merged player body parts: all ItemMaterial meshes of one player in one draw. Textures come from a shared
// atlas (power-of-two tiles aligned to their size, so generated mips never mix tiles); per-vertex data
// selects the tile and the item parameters, and part colours come from small per-draw arrays.
// ---------------------------------------------------------------------------
Texture2D AtlasTexture;
sampler AtlasSampler = sampler_state
{
    Texture = <AtlasTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};
float AtlasSize = 2048.0;
float4 PartChromeColor[16];
float4 PartChrome2Tint[16];
float4 PartAncientColor[16];

struct VertexShaderInputComposite
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
    float2 BoneIndices : TEXCOORD1;
    float4 AtlasRect : TEXCOORD2;   // xy = tile origin, zw = texture size (atlas UV)
    float4 ItemData : TEXCOORD3;    // x = item options, y = excellent, z = ancient, w = part index
    float4 Color : COLOR0;
};

struct VertexShaderOutputComposite
{
    float4 Position : SV_POSITION;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : TEXCOORD1;
    float2 TextureCoordinate : TEXCOORD2;
    nointerpolation float4 AtlasRect : TEXCOORD3;
    nointerpolation float4 ItemData : TEXCOORD4;
};

VertexShaderOutputComposite MainVS_Composite(in VertexShaderInputComposite input)
{
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    float4 localPosition = mul(float4(input.Position, 1.0), LoadBone(positionBoneIndex));
    float3 localNormal = mul(input.Normal, (float3x3)LoadBone(normalBoneIndex));

    VertexShaderOutputComposite output = (VertexShaderOutputComposite)0;
    output.Position = mul(localPosition, WorldViewProjection);
    output.WorldPosition = mul(localPosition, World).xyz;
    output.Normal = normalize(mul(localNormal, (float3x3)World));
    output.TextureCoordinate = input.TextureCoordinate;
    output.AtlasRect = input.AtlasRect;
    output.ItemData = input.ItemData;
    return output;
}

// Wrapped (repeating) texture coordinates inside an atlas tile. The mip level comes from the unwrapped
// coordinates, and the sample is kept half a texel inside the tile at that level.
float4 SampleAtlas(float2 uv, float4 rect)
{
    float2 tileTexels = rect.zw * AtlasSize;
    float2 dx = ddx(uv) * tileTexels;
    float2 dy = ddy(uv) * tileTexels;
    float lod = 0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8));
    float maxLod = max(log2(min(tileTexels.x, tileTexels.y)) - 1.0, 0.0);
    lod = clamp(lod, 0.0, maxLod);
    float2 halfTexel = (0.5 * exp2(lod) / AtlasSize).xx;
    float2 local = clamp(frac(uv) * rect.zw, halfTexel, rect.zw - halfTexel);
    return tex2Dlod(AtlasSampler, float4(rect.xy + local, 0.0, lod));
}

float4 MainPS_Composite(VertexShaderOutputComposite input) : PS_COLOR
{
    float4 diffuse = SampleAtlas(input.TextureCoordinate, input.AtlasRect);
    if (diffuse.a < 0.1)
        discard;

    int part = min(max((int)(input.ItemData.w + 0.5), 0), 15);
    return ShadeItemCore(diffuse, input.WorldPosition, normalize(input.Normal), input.ItemData.x,
                         input.ItemData.y > 0.5, input.ItemData.z > 0.5,
                         PartChromeColor[part].xyz, PartChrome2Tint[part], PartAncientColor[part].xyz);
}

technique BasicColorDrawing_CompositeSkinned
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS_Composite();
        PixelShader = compile PS_SHADERMODEL MainPS_Composite();
    }
}
#endif

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
