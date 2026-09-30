// DynamicLighting.fx - Dynamic lighting shader (optimised revision)
//
// Existing technique names and vertex layouts are preserved. Snow additionally
// binds a baked material texture and selects opaque/base-only variants.
//
//  PERFORMANCE
//   * Shadow-map projection moved from the pixel shader to the vertex shader.
//     Clip-space coordinates are affine in world position, so interpolating them
//     is exact; the pixel shader only does the divide + 4 fetches.
//   * Disabled features are skipped with uniform branches (fog, shadows, debug
//     overlay, additive-cutoff clip, snow overlay texture).
//   * Alpha test now runs BEFORE the dynamic-light loop, so clipped foliage
//     pixels no longer pay for up to 32 lights.
//   * Per-light early-out when the pixel is outside the light radius.
//   * One light evaluation function shared by all loops (less code, same result).
//   * Vertex shaders share one "core" per vertex format (vertexLit is a
//     compile-time constant), so lit / unlit variants are folded by the compiler.
//   * SunOnly pixel shader is specialised (no dynamic-light / debug code at all).
//   * Highlight and AdditiveAlphaTest have their own lean vertex shaders.
//   * Snow: baked, mipmapped material replaces procedural noise per pixel;
//     opaque/base-only variants eliminate discard and unnecessary overlay fetches.
//   * Shadow caster: optional ShadowOpaqueCaster skips the alpha texture fetch.
//
//  QUALITY
//   * Proper bilinear PCF (compare first, then filter) instead of averaging
//     depths with a step() over 4 fixed taps -> smooth penumbra, no stair-steps.
//     Sampling is at exact texel centres, so it no longer depends on the
//     sampler's filter mode / R32F linear-filter support.
//   * tex2Dlod for shadow fetches: no implicit derivatives inside divergent flow.
//   * Skinned shadow casters use the same UVs as the main pass (previously they
//     went through CalculateProceduralUV and could pick up stale terrain state).
//
//  OPTIONAL SWITCHES (define before this file / via effect defines)
//   SHADOW_AFFECTS_DYNAMIC_LIGHTS 1 (default, original look) / 0 (sun shadow only
//                                 darkens ambient+sun; torches are not shadowed)
//   LIGHT_FALLOFF_SMOOTH          0 (default, original) / 1 (squared falloff)
//   NORMALIZE_SUN_DIRECTION       0 (default, original) / 1 (normalise in objects)
//   SUN_DIRECTION_IS_NORMALIZED   see SunDirNormalized()

#if SM6
    #define VS_SHADERMODEL vs_6_0
    #define PS_SHADERMODEL ps_6_0
#elif OPENGL
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

#ifndef SHADOW_AFFECTS_DYNAMIC_LIGHTS
    #define SHADOW_AFFECTS_DYNAMIC_LIGHTS 1
#endif
#ifndef LIGHT_FALLOFF_SMOOTH
    #define LIGHT_FALLOFF_SMOOTH 0
#endif
#ifndef NORMALIZE_SUN_DIRECTION
    #define NORMALIZE_SUN_DIRECTION 0
#endif

// Transformation matrices
float4x4 World;
float4x4 WorldViewProjection; // Includes World * View * Projection
float4x4 ViewProjection;      // Shared by instanced paths whose World varies per instance
#if !OPENGL
float4x4 BoneMatrices[256];
// Multi-pose crowd skinning stores one palette per texture row.
// The row width is selected per flush (32/64/128/256 bones), and each bone
// occupies four consecutive float4 texels (one texel per matrix row).
Texture2D CrowdBonePaletteTexture;
UNIFORM_DEFAULT(float, CrowdBonePaletteRowCount, 1.0);
#endif


// Texture
Texture2D DiffuseTexture;
sampler SamplerState0 = sampler_state
{
    Texture = <DiffuseTexture>;
    AddressU = Wrap;
    AddressV = Wrap;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};

Texture2D SnowOverlayTexture;
float2 SnowBaseUvScale;
float2 SnowOverlayUvScale;
float SnowOverlayEnabled;
float SnowAmbientLight;
sampler SnowOverlaySampler = sampler_state
{
    Texture = <SnowOverlayTexture>;
    AddressU = Wrap;
    AddressV = Wrap;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};

// Baked powder/grain slopes, edge breakup and albedo variation. Mips replace
// procedural octaves and screen-space grain filtering in the pixel shader.
Texture2D SnowMaterialTexture;
sampler SnowMaterialSampler = sampler_state
{
    Texture = <SnowMaterialTexture>;
    AddressU = Wrap;
    AddressV = Wrap;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};

// Lighting parameters
UNIFORM_DEFAULT(float3, AmbientLight, float3(0.8, 0.8, 0.8));
UNIFORM_DEFAULT(float, Alpha, 1.0);
// Additive RGB/JPG light textures have no alpha channel. Reject their near-black
// background using source coverage, before lighting or per-object flicker/fading.
UNIFORM_DEFAULT(float, AdditiveAlphaCutoff, 0.0);
UNIFORM_DEFAULT(float2, TextureCoordinateOffset, float2(0.0, 0.0));
// Per-material tint multiplier for chrome/bright overlay passes
// (SourceMain5.2 glColor(BodyLight) on RENDER_CHROME/BRIGHT body passes).
UNIFORM_DEFAULT(float3, MaterialTint, float3(1.0, 1.0, 1.0));
UNIFORM_DEFAULT(float3, HighlightColor, float3(1.0, 0.0, 0.0));
UNIFORM_DEFAULT(float3, SunDirection, float3(1.0, 0.0, -0.6));
UNIFORM_DEFAULT(float3, SunColor, float3(1.0, 0.95, 0.85));
UNIFORM_DEFAULT(float, SunStrength, 0.8);
UNIFORM_DEFAULT(float, ShadowStrength, 0.5);
float4x4 LightViewProjection;
UNIFORM_DEFAULT(float2, ShadowMapTexelSize, float2(1.0 / 2048.0, 1.0 / 2048.0));
UNIFORM_DEFAULT(float, ShadowBias, 0.0015);
UNIFORM_DEFAULT(float, ShadowNormalBias, 0.0025);
UNIFORM_DEFAULT(float, ShadowsEnabled, 0.0);
// Optional: set to 1 for shadow-caster batches whose textures have no alpha
// cut-outs. Skips the texture fetch + clip in ShadowPS. Default 0 = old behaviour.
UNIFORM_DEFAULT(float, ShadowOpaqueCaster, 0.0);

Texture2D ShadowMap;
sampler ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Point;
};

#if SM6
float4 SampleSamplerState0(float2 uv) { return DiffuseTexture.Sample(SamplerState0, uv); }
float4 SampleSnowOverlaySampler(float2 uv) { return SnowOverlayTexture.Sample(SnowOverlaySampler, uv); }
float4 SampleSnowMaterialSampler(float2 uv) { return SnowMaterialTexture.Sample(SnowMaterialSampler, uv); }
#define tex2D(s, uv) Sample##s(uv)
#endif

// Shadow map fetch at mip 0 (the map has no mips). Explicit LOD means no implicit
// derivatives, so it is safe inside divergent branches.
// If tex2Dlod ever gives trouble on a target, replace with: tex2D(ShadowSampler, uv).r
#if SM6
    #define SHADOW_FETCH(uv) ShadowMap.SampleLevel(ShadowSampler, (uv), 0).r
#else
    #define SHADOW_FETCH(uv) tex2Dlod(ShadowSampler, float4((uv), 0.0, 0.0)).r
#endif

// Dynamic lights
#if OPENGL
#define MAX_LIGHTS 8
#else
#define MAX_LIGHTS 32
#endif
float4 LightPosInvRadius[MAX_LIGHTS];   // xyz = position, w = inverse radius
float4 LightColorIntensity[MAX_LIGHTS]; // rgb = color, w = intensity
UNIFORM_DEFAULT(int, ActiveLightCount, 0); // exact count uploaded by CPU; avoids scanning empty slots

UNIFORM_DEFAULT(float, DebugLightingAreas, 0.0);
UNIFORM_DEFAULT(float, TerrainDynamicIntensityScale, 1.5);
UNIFORM_DEFAULT(float, GlobalLightMultiplier, 1.0);


UNIFORM_DEFAULT(float2, TerrainUvScale, float2(0.0, 0.0));
UNIFORM_DEFAULT(float, UseProceduralTerrainUV, 0.0);
UNIFORM_DEFAULT(float, IsWaterTexture, 0.0);
UNIFORM_DEFAULT(float2, WaterFlowDirection, float2(1.0, 0.0));
UNIFORM_DEFAULT(float, WaterTotal, 0.0);
UNIFORM_DEFAULT(float, DistortionAmplitude, 0.0);
UNIFORM_DEFAULT(float, DistortionFrequency, 0.0);

// World fog (e.g. Valley of Loren siege haze) - disabled unless a world opts in
UNIFORM_DEFAULT(float, FogEnabled, 0.0);
UNIFORM_DEFAULT(float3, FogColor, float3(0.0, 0.0, 0.0));
UNIFORM_DEFAULT(float, FogStart, 2000.0);
UNIFORM_DEFAULT(float, FogEnd, 2700.0);
UNIFORM_DEFAULT(float3, FogCameraPosition, float3(0.0, 0.0, 0.0));

// Uniform branch: worlds without fog skip the length()/divide entirely.
float3 ApplyWorldFog(float3 color, float3 worldPos)
{
    [branch] if (FogEnabled > 0.0)
    {
        float f = saturate((length(worldPos - FogCameraPosition) - FogStart) / max(FogEnd - FogStart, 1.0));
        color = lerp(color, FogColor, f * FogEnabled);
    }
    return color;
}

// Sun direction helpers. Snow always used a normalised direction, objects/shadows
// used the raw uniform. Both behaviours are kept unless NORMALIZE_SUN_DIRECTION=1.
// Zero-safe, so an unset uniform (SM6 has no defaults) cannot produce NaN.
float3 SunDirNormalized()
{
    return SunDirection * rsqrt(max(dot(SunDirection, SunDirection), 1e-8));
}

float3 SunDirForObjects()
{
#if NORMALIZE_SUN_DIRECTION
    return SunDirNormalized();
#else
    return SunDirection;
#endif
}

// ============================================================================
// SHADOW COORDINATES (computed per vertex, interpolated, exact for any
// linear light projection). xy = uv * w, z = depth * w, w = clip w.
// Must NOT live in a COLOR semantic (GL clamps COLOR interpolants to [0,1]).
// ============================================================================
float4 ComputeShadowCoord(float3 worldPos)
{
    float4 sc = float4(0.0, 0.0, 0.0, 1.0);
    [branch] if (ShadowsEnabled >= 0.5)
    {
        float4 clip = mul(float4(worldPos, 1.0), LightViewProjection);
#if OPENGL
        float z = clip.z * 0.5 + clip.w * 0.5;
#else
        float z = clip.z;
#endif
        sc = float4(0.5 * clip.x + 0.5 * clip.w,
                    0.5 * clip.w - 0.5 * clip.y,
                    z,
                    clip.w);
    }
    return sc;
}

// Input structures
struct VertexInput
{
    float3 Position : POSITION0;
    float3 Normal   : NORMAL0;
    float2 TexCoord : TEXCOORD0;
    float4 Color    : COLOR0;
};

#if !OPENGL
struct VertexInputSkinned
{
    float3 Position  : POSITION0;
    float3 Normal    : NORMAL0;
    float2 TexCoord  : TEXCOORD0;
    float4 Color     : COLOR0;
    float2 BoneIndices : TEXCOORD1;
};

struct VertexInputSkinnedInstanced
{
    float3 Position      : POSITION0;
    float3 Normal        : NORMAL0;
    float2 TexCoord      : TEXCOORD0;
    float4 Color         : COLOR0;
    float2 BoneIndices   : TEXCOORD1;
    float4 InstWorld0    : TEXCOORD2;
    float4 InstWorld1    : TEXCOORD3;
    float4 InstWorld2    : TEXCOORD4;
    float4 InstWorld3    : TEXCOORD5;
    float4 InstanceColor : COLOR1;
};

struct VertexInputSkinnedMultiPoseInstanced
{
    float3 Position      : POSITION0;
    float3 Normal        : NORMAL0;
    float2 TexCoord      : TEXCOORD0;
    float4 Color         : COLOR0;
    float2 BoneIndices   : TEXCOORD1;
    float4 InstWorld0    : TEXCOORD2;
    float4 InstWorld1    : TEXCOORD3;
    float4 InstWorld2    : TEXCOORD4;
    float4 InstWorld3    : TEXCOORD5;
    float4 InstanceColor : COLOR1;
    float2 PaletteData   : TEXCOORD6;
};

// Keep the same CPU-composed WVP as individual cutout draws. Re-associating the
// matrix products shifts alpha-tested leaf edges at subpixel precision.
struct VertexInputSkinnedCutoutInstanced
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float2 BoneIndices : TEXCOORD1;
    float4 InstWorld0 : TEXCOORD2;
    float4 InstWorld1 : TEXCOORD3;
    float4 InstWorld2 : TEXCOORD4;
    float4 InstWorld3 : TEXCOORD5;
    float4 InstWvp0 : TEXCOORD6;
    float4 InstWvp1 : TEXCOORD7;
    float4 InstWvp2 : TEXCOORD8;
    float4 InstWvp3 : TEXCOORD9;
};
#endif

struct PixelInput
{
    float4 Position     : SV_POSITION;
    float2 TexCoord     : TEXCOORD0;
    float3 WorldPos     : TEXCOORD1;
    float3 Normal       : TEXCOORD2;
    float4 Color        : COLOR0;
    float3 DynamicLight : TEXCOORD3;
    float4 ShadowCoord  : TEXCOORD4;
};

// Lean interpolants for passes that need nothing but UV / colour.
struct UnlitPixelInput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color    : COLOR0;
};

struct HighlightPixelInput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

struct SnowVertexInput
{
    float3 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 TerrainLight : COLOR1;
};

struct SnowPixelInput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float3 WorldPos : TEXCOORD1;
    float3 Normal : TEXCOORD2;
    float4 Color : COLOR0;
    float3 DynamicLight : TEXCOORD3;
    float4 TerrainLight : COLOR1;
    float4 ShadowCoord : TEXCOORD4;
};

// ============================================================================
// LIGHTING FUNCTIONS
// ============================================================================

#define TERRAIN_MAX_LIGHTS MAX_LIGHTS
#if OPENGL
    #define TERRAIN_LOW_MAX_LIGHTS 2
#else
    #define TERRAIN_LOW_MAX_LIGHTS 8
#endif

// GL (SM3) needs a fully unrolled loop with a constant bound; DX uses a real loop
// bounded by the exact light count uploaded by the CPU.
#if OPENGL
    #define LIGHT_LOOP_BEGIN(maxCount, countVar) [unroll(maxCount)] for (int i = 0; i < (maxCount); i++) { if (i >= (countVar)) break;
#else
    #define LIGHT_LOOP_BEGIN(maxCount, countVar) [loop] for (int i = 0; i < (countVar); i++) {
#endif
#define LIGHT_LOOP_END }

// One point light. 'hemisphere' is a compile-time constant at every call site:
// terrain ignores lights below the surface, objects do not.
float3 EvaluatePointLight(int i, float3 worldPos, float3 normal, bool hemisphere)
{
    float4 lp = LightPosInvRadius[i];
    float4 lc = LightColorIntensity[i];

    float3 toLight = lp.xyz - worldPos;
    float distSq = dot(toLight, toLight);
    float invRad = max(lp.w, 0.0);

    // Quadratic attenuation (linear in distSq)
    float att = saturate(1.0 - distSq * (invRad * invRad));
#if LIGHT_FALLOFF_SMOOTH
    att *= att;
#endif
    float weight = max(lc.w, 0.0) * att;

    if (hemisphere)
        weight *= saturate(toLight.z * invRad);

    float3 result = float3(0.0, 0.0, 0.0);
    // Most lights are out of range for most pixels/vertices: skip the rest.
    [branch] if (weight > 0.0)
    {
        // dot(n, L) * invDist saves two multiplies vs dot(n, L * invDist)
        float diffuse = saturate(dot(normal, toLight) * rsqrt(distSq + 0.0001));
        result = lc.rgb * (weight * diffuse);
    }
    return result;
}

float3 CalculateTerrainLighting(float3 worldPos, float3 normal)
{
    float3 light = float3(0.0, 0.0, 0.0);
    int lightCount = min(max(ActiveLightCount, 0), TERRAIN_MAX_LIGHTS);
    LIGHT_LOOP_BEGIN(TERRAIN_MAX_LIGHTS, lightCount)
        light += EvaluatePointLight(i, worldPos, normal, true);
    LIGHT_LOOP_END
    return light;
}

float3 CalculateTerrainLightingLow(float3 worldPos, float3 normal)
{
    float3 light = float3(0.0, 0.0, 0.0);
    int lightCount = min(max(ActiveLightCount, 0), TERRAIN_LOW_MAX_LIGHTS);
    LIGHT_LOOP_BEGIN(TERRAIN_LOW_MAX_LIGHTS, lightCount)
        light += EvaluatePointLight(i, worldPos, normal, true);
    LIGHT_LOOP_END
    return light;
}

float3 CalculateDynamicLighting(float3 worldPos, float3 normal)
{
    float3 light = float3(0.0, 0.0, 0.0);
    int lightCount = min(max(ActiveLightCount, 0), MAX_LIGHTS);
    LIGHT_LOOP_BEGIN(MAX_LIGHTS, lightCount)
        light += EvaluatePointLight(i, worldPos, normal, false);
    LIGHT_LOOP_END
    return light;
}

// ============================================================================
// VERTEX SHADERS
// ============================================================================

float2 CalculateProceduralUV(float3 worldPos, float2 baseTexCoord)
{
    float2 procUv = worldPos.xy * TerrainUvScale;
    if (IsWaterTexture > 0.5)
    {
        float f = max(0.01, DistortionFrequency);

        float phase = frac(WaterTotal * f * 0.1591549) * 6.2831853;

        float2 offsets;
        offsets.x = sin(procUv.x * f + phase);
        offsets.y = cos(procUv.y * f + phase);

        return procUv + (WaterFlowDirection * WaterTotal) + (offsets * DistortionAmplitude);
    }
    return lerp(baseTexCoord, procUv, UseProceduralTerrainUV);
}

PixelInput BuildPixelInput(float4 clipPos, float3 worldPos, float3 worldNormal,
                           float2 uv, float4 color, float3 dynamicLight)
{
    PixelInput o;
    o.Position = clipPos;
    o.TexCoord = uv;
    o.WorldPos = worldPos;
    o.Normal = worldNormal;
    o.Color = color;
    o.DynamicLight = dynamicLight;
    o.ShadowCoord = ComputeShadowCoord(worldPos);
    return o;
}

PixelInput VS_Terrain(VertexInput input)
{
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    float3 worldNormal = normalize(mul(input.Normal, (float3x3)World));
    return BuildPixelInput(
        mul(float4(input.Position, 1.0), WorldViewProjection), // precomposed WVP
        worldPos, worldNormal,
        CalculateProceduralUV(worldPos, input.TexCoord),
        input.Color,
        CalculateTerrainLighting(worldPos, worldNormal));
}

PixelInput VS_TerrainLow(VertexInput input)
{
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    float3 worldNormal = normalize(mul(input.Normal, (float3x3)World));
    return BuildPixelInput(
        mul(float4(input.Position, 1.0), WorldViewProjection),
        worldPos, worldNormal,
        CalculateProceduralUV(worldPos, input.TexCoord),
        input.Color,
        CalculateTerrainLightingLow(worldPos, worldNormal));
}

SnowPixelInput VS_Snow(SnowVertexInput input)
{
    SnowPixelInput output;
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    float3 worldNormal = normalize(mul(input.Normal, (float3x3)World));
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.WorldPos = worldPos;
    output.Normal = worldNormal;
    output.TexCoord = input.TexCoord;
    output.Color = input.Color;
    output.TerrainLight = input.TerrainLight;
    output.DynamicLight = CalculateTerrainLighting(worldPos, worldNormal);
    output.ShadowCoord = ComputeShadowCoord(worldPos);
    return output;
}

// Static objects. vertexLit is a literal at every call site, so the compiler
// removes the light loop from the non-lit variants.
PixelInput ObjectsCore(VertexInput input, bool vertexLit)
{
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    float3 worldNormal = normalize(mul(input.Normal, (float3x3)World));
    float3 dyn = float3(0.0, 0.0, 0.0);
    if (vertexLit)
        dyn = CalculateDynamicLighting(worldPos, worldNormal);
    return BuildPixelInput(
        mul(float4(input.Position, 1.0), WorldViewProjection),
        worldPos, worldNormal,
        input.TexCoord + TextureCoordinateOffset,
        input.Color, dyn);
}

PixelInput VS_Objects(VertexInput input)         { return ObjectsCore(input, false); }
PixelInput VS_ObjectsVertexLit(VertexInput input) { return ObjectsCore(input, true); }

// For passes that only need UV + vertex colour (AdditiveAlphaTest).
UnlitPixelInput VS_ObjectsUnlit(VertexInput input)
{
    UnlitPixelInput output;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color;
    return output;
}

#if !OPENGL
int BoneIndex(float v)
{
    return min(max((int)v, 0), 255);
}

PixelInput SkinnedCore(VertexInputSkinned input, bool vertexLit)
{
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[BoneIndex(input.BoneIndices.x)]);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[BoneIndex(input.BoneIndices.y)]);

    float3 worldPos = mul(localPos, World).xyz;
    float3 worldNormal = normalize(mul(localNormal, (float3x3)World));
    float3 dyn = float3(0.0, 0.0, 0.0);
    if (vertexLit)
        dyn = CalculateDynamicLighting(worldPos, worldNormal);

    // localPos * WorldViewProjection == worldPos * View * Projection, minus two matrix multiplies.
    return BuildPixelInput(
        mul(localPos, WorldViewProjection),
        worldPos, worldNormal,
        input.TexCoord + TextureCoordinateOffset,
        input.Color, dyn);
}

PixelInput VS_ObjectsSkinned(VertexInputSkinned input)         { return SkinnedCore(input, false); }
PixelInput VS_ObjectsSkinnedVertexLit(VertexInputSkinned input) { return SkinnedCore(input, true); }

// Highlight only needs position + UV; skip normals, world position, shadow coords.
HighlightPixelInput VS_HighlightSkinned(VertexInputSkinned input)
{
    HighlightPixelInput output;
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[BoneIndex(input.BoneIndices.x)]);
    output.Position = mul(localPos, WorldViewProjection);
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    return output;
}

PixelInput SkinnedInstancedCore(VertexInputSkinnedInstanced input, bool vertexLit)
{
    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[BoneIndex(input.BoneIndices.x)]);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[BoneIndex(input.BoneIndices.y)]);

    float4 worldPos = mul(localPos, instanceWorld);
    float3 worldNormal = normalize(mul(localNormal, (float3x3)instanceWorld));
    float3 dyn = float3(0.0, 0.0, 0.0);
    if (vertexLit)
        dyn = CalculateDynamicLighting(worldPos.xyz, worldNormal);

    // Static map geometry trades per-pixel dynamic-light evaluation for per-vertex
    // evaluation (vertexLit) - the dominant shader cost on large opaque buildings.
    return BuildPixelInput(
        mul(worldPos, ViewProjection),
        worldPos.xyz, worldNormal,
        input.TexCoord + TextureCoordinateOffset,
        input.Color * input.InstanceColor, dyn);
}

PixelInput VS_ObjectsSkinnedInstanced(VertexInputSkinnedInstanced input)         { return SkinnedInstancedCore(input, false); }
PixelInput VS_ObjectsSkinnedInstancedVertexLit(VertexInputSkinnedInstanced input) { return SkinnedInstancedCore(input, true); }

PixelInput CutoutInstancedCore(VertexInputSkinnedCutoutInstanced input, bool vertexLit)
{
    float4x4 instanceWorld = transpose(float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3));
    float4x4 instanceWvp = transpose(float4x4(input.InstWvp0, input.InstWvp1, input.InstWvp2, input.InstWvp3));
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[BoneIndex(input.BoneIndices.x)]);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[BoneIndex(input.BoneIndices.y)]);

    float3 worldPos = mul(localPos, instanceWorld).xyz;
    float3 worldNormal = normalize(mul(localNormal, (float3x3)instanceWorld));
    float3 dyn = float3(0.0, 0.0, 0.0);
    if (vertexLit)
        dyn = CalculateDynamicLighting(worldPos, worldNormal);

    return BuildPixelInput(
        mul(localPos, instanceWvp),
        worldPos, worldNormal,
        input.TexCoord + TextureCoordinateOffset,
        input.Color, dyn);
}

PixelInput VS_ObjectsCutoutInstanced(VertexInputSkinnedCutoutInstanced input)         { return CutoutInstancedCore(input, false); }
PixelInput VS_ObjectsCutoutInstancedVertexLit(VertexInputSkinnedCutoutInstanced input) { return CutoutInstancedCore(input, true); }

float4x4 LoadCrowdBoneMatrix(int boneIndex, int paletteRow)
{
    int safeBoneIndex = min(max(boneIndex, 0), 255);
    int safePaletteRow = min(max(paletteRow, 0), max((int)CrowdBonePaletteRowCount - 1, 0));
    int texelX = safeBoneIndex * 4;

    return float4x4(
        CrowdBonePaletteTexture.Load(int3(texelX + 0, safePaletteRow, 0)),
        CrowdBonePaletteTexture.Load(int3(texelX + 1, safePaletteRow, 0)),
        CrowdBonePaletteTexture.Load(int3(texelX + 2, safePaletteRow, 0)),
        CrowdBonePaletteTexture.Load(int3(texelX + 3, safePaletteRow, 0)));
}

PixelInput SkinnedMultiPoseCore(VertexInputSkinnedMultiPoseInstanced input, bool vertexLit)
{
    int positionBoneIndex = BoneIndex(input.BoneIndices.x);
    int normalBoneIndex = BoneIndex(input.BoneIndices.y);
    int paletteRow = (int)(input.PaletteData.x + 0.5);

    float4x4 positionBone = LoadCrowdBoneMatrix(positionBoneIndex, paletteRow);
    float3 localNormal = mul(input.Normal, (float3x3)positionBone);
    // Usually identical bones: only fetch the second matrix when they differ.
    if (normalBoneIndex != positionBoneIndex)
        localNormal = mul(input.Normal, (float3x3)LoadCrowdBoneMatrix(normalBoneIndex, paletteRow));

    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), positionBone);
    float4 worldPos = mul(localPos, instanceWorld);
    float3 worldNormal = normalize(mul(localNormal, (float3x3)instanceWorld));
    float3 dyn = float3(0.0, 0.0, 0.0);
    if (vertexLit)
        dyn = CalculateDynamicLighting(worldPos.xyz, worldNormal);

    return BuildPixelInput(
        mul(worldPos, ViewProjection),
        worldPos.xyz, worldNormal,
        input.TexCoord + TextureCoordinateOffset,
        input.Color * input.InstanceColor, dyn);
}

PixelInput VS_ObjectsSkinnedMultiPoseInstanced(VertexInputSkinnedMultiPoseInstanced input)         { return SkinnedMultiPoseCore(input, false); }
PixelInput VS_ObjectsSkinnedMultiPoseInstancedVertexLit(VertexInputSkinnedMultiPoseInstanced input) { return SkinnedMultiPoseCore(input, true); }

#endif

// ============================================================================
// SHADOWS
// ============================================================================

// Bilinear PCF: 4 point fetches at exact texel centres, depth compare first,
// then bilinear weighting of the comparison results. Same fetch count as before,
// but smooth penumbra and independent of the sampler filter mode.
// Caller guarantees ShadowsEnabled >= 0.5.
float SampleShadow(float4 shadowCoord, float3 normal)
{
    float invW = 1.0 / shadowCoord.w;
    float2 uv = shadowCoord.xy * invW;
    float depth = shadowCoord.z * invW;

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 1.0;

    float ndotl = saturate(dot(normal, -SunDirForObjects()));
    float receiverDepth = depth - (ShadowBias + ShadowNormalBias * (1.0 - ndotl));

    float2 texel = ShadowMapTexelSize;
    float2 texPos = uv / texel - 0.5;   // texel centres at integer coordinates
    float2 texBase = floor(texPos);
    float2 f = texPos - texBase;
    float2 c = (texBase + 0.5) * texel; // centre of the lower-left texel

    float4 s;
    s.x = SHADOW_FETCH(c);
    s.y = SHADOW_FETCH(c + float2(texel.x, 0.0));
    s.z = SHADOW_FETCH(c + float2(0.0, texel.y));
    s.w = SHADOW_FETCH(c + texel);

    float4 lit = step(receiverDepth, s);
    return lerp(lerp(lit.x, lit.y, f.x), lerp(lit.z, lit.w, f.x), f.y);
}

// Light multiplier from a shadow term (identical maths to the original).
float ShadowLightScale(float shadowTerm)
{
    return lerp(1.0, lerp(1.0 - ShadowStrength, 1.0, shadowTerm), ShadowsEnabled);
}

// ============================================================================
// PIXEL SHADERS
// ============================================================================

float3 PrepareNormal(float3 rawNormal)
{
    return normalize(rawNormal + float3(0.0, 0.0, 0.00001));
}

float4 PS_Terrain(PixelInput input) : SV_Target
{
    float4 texColor = tex2D(SamplerState0, input.TexCoord);
    float finalAlpha = texColor.a * Alpha * input.Color.a;

    // Early clip: transparent pixels skip all lighting math below.
    clip(finalAlpha - 0.01);

    float3 finalLight = input.Color.rgb * GlobalLightMultiplier +
                        input.DynamicLight * TerrainDynamicIntensityScale;

    // Normal is only needed for the shadow bias: normalise it only when shadows are on.
    [branch] if (ShadowsEnabled >= 0.5)
        finalLight *= ShadowLightScale(SampleShadow(input.ShadowCoord, PrepareNormal(input.Normal)));

    float3 finalColor = texColor.rgb * finalLight;

    [branch] if (DebugLightingAreas > 0.0)
    {
        float isDebugPixel = DebugLightingAreas * step(0.01, dot(input.DynamicLight, input.DynamicLight));
        finalColor = lerp(finalColor, float3(0.0, 0.0, 0.0), isDebugPixel);
    }

    return float4(ApplyWorldFog(finalColor, input.WorldPos), finalAlpha);
}

float4 PS_Highlight(HighlightPixelInput input) : SV_Target
{
    float textureAlpha = tex2D(SamplerState0, input.TexCoord).a;
    clip(textureAlpha - 0.01);
    return float4(HighlightColor * textureAlpha, textureAlpha * Alpha);
}

// Real snow geometry supplies the groove normals and compaction in TexCoord.x.
// Lighting is evaluated on those normals so the banks read as volume in motion.
// Both flags are literals in each technique: opaque snow has no discard, and
// base-only materials have no overlay texture fetch, including on Shader Model 3.
float4 ShadeSnow(SnowPixelInput input, bool opaque, bool overlay)
{
    float compacted = saturate(input.TexCoord.x);
    float2 materialUv = input.WorldPos.xy;
    float4 material = tex2D(SnowMaterialSampler, materialUv / 1024.0);
    float coverage = 1.0;
    float materialDepth = 1.0;
    if (!opaque)
    {
        clip(input.Color.a - 0.015);
        coverage = smoothstep(0.035, 0.65, input.Color.a + (material.b - 0.5) * 0.16);
        clip(coverage - 0.01);
        materialDepth = smoothstep(0.2, 0.9, input.Color.a);
    }
    float roughness = (1.0 - compacted * 0.55) * materialDepth;
    float2 microSlope = (material.rg - 0.5) * 1.25 * roughness;
    float3 normal = PrepareNormal(input.Normal + float3(-microSlope, 0));

    // Reuse the actual terrain material pair and its original 64-texel-per-tile
    // UVs. No replacement white/blue tint at the join with the original ground.
    float3 albedo = tex2D(SamplerState0, materialUv * SnowBaseUvScale).rgb;
    if (overlay)
    {
        float4 overlayTexel = tex2D(SnowOverlaySampler, materialUv * SnowOverlayUvScale);
        albedo = lerp(albedo, overlayTexel.rgb,
            overlayTexel.a * input.TerrainLight.a * SnowOverlayEnabled);
    }
    albedo *= 1.0 + (material.a - 0.5) * 0.2 * roughness;

    // TerrainRenderer uploads a unit SunDirection for the snow pass.
    float3 sunDir = SunDirection;
    float sunFacing = saturate(dot(normal, -sunDir));

    float shadow = 1.0;
    [branch] if (ShadowsEnabled >= 0.5)
        shadow = ShadowLightScale(SampleShadow(input.ShadowCoord, normal));

    float3 terrainLight = input.TerrainLight.rgb * GlobalLightMultiplier;
    float3 smoothLight = saturate(input.Color.rgb + SnowAmbientLight) * GlobalLightMultiplier;
    float3 light = lerp(terrainLight, smoothLight, materialDepth);
    light += input.DynamicLight * TerrainDynamicIntensityScale;
    light *= shadow;

    // Relief changes illumination around banks, without adding another sun term
    // that made the entire snow sheet brighter and colder than Devias textures.
    float upFacing = saturate(-sunDir.z);
    light *= 1.0 + (sunFacing - upFacing) * SunStrength * 0.5 * materialDepth;
    float cavity = 1.0 - compacted * 0.24;
    return float4(ApplyWorldFog(albedo * light * cavity, input.WorldPos), coverage);
}

float4 PS_Snow(SnowPixelInput input) : SV_Target { return ShadeSnow(input, false, true); }
float4 PS_SnowBase(SnowPixelInput input) : SV_Target { return ShadeSnow(input, false, false); }
float4 PS_SnowOpaque(SnowPixelInput input) : SV_Target { return ShadeSnow(input, true, true); }
float4 PS_SnowOpaqueBase(SnowPixelInput input) : SV_Target { return ShadeSnow(input, true, false); }

technique DynamicLighting_Snow
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_Snow();
        PixelShader = compile PS_SHADERMODEL PS_Snow();
    }
}

technique DynamicLighting_Snow_Base
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_Snow();
        PixelShader = compile PS_SHADERMODEL PS_SnowBase();
    }
}

technique DynamicLighting_Snow_Opaque
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_Snow();
        PixelShader = compile PS_SHADERMODEL PS_SnowOpaque();
    }
}

technique DynamicLighting_Snow_Opaque_Base
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_Snow();
        PixelShader = compile PS_SHADERMODEL PS_SnowOpaqueBase();
    }
}

// Texture fetch + all alpha rejection. Runs BEFORE any light loop so clipped
// (foliage/cut-out) pixels never pay for dynamic lighting.
float3 FetchObjectTexel(PixelInput input, out float finalAlpha)
{
    float4 texColor = tex2D(SamplerState0, input.TexCoord);

    // With cutoff == 0 this clip can never fire, so skip it (uniform branch).
    [branch] if (AdditiveAlphaCutoff > 0.0)
        clip(min(texColor.a, max(texColor.r, max(texColor.g, texColor.b))) - AdditiveAlphaCutoff);

    finalAlpha = texColor.a * Alpha * input.Color.a;
    clip(finalAlpha - 0.01);
    return texColor.rgb;
}

// hasDynamicLight is a literal at each call site; when false the compiler drops
// every dynamic-light / debug instruction (SunOnly path).
float3 ShadeObjectRgb(PixelInput input, float3 texRgb, float3 normal,
                      float3 dynamicLight, bool hasDynamicLight)
{
    float ndotlRaw = dot(normal, -SunDirForObjects());
    float ndotl = saturate(ndotlRaw) + saturate(-ndotlRaw) * 0.35;

    float shadowFactor = saturate(lerp(1.0 - ShadowStrength, 1.0, ndotl));
    float3 baseLight = AmbientLight * shadowFactor + SunColor * (ndotl * SunStrength);

    float3 dynLight = float3(0.0, 0.0, 0.0);
    if (hasDynamicLight)
        dynLight = dynamicLight * TerrainDynamicIntensityScale;

    float shadowScale = 1.0;
    [branch] if (ShadowsEnabled >= 0.5)
        shadowScale = ShadowLightScale(SampleShadow(input.ShadowCoord, normal));

#if SHADOW_AFFECTS_DYNAMIC_LIGHTS
    float3 finalLight = (baseLight + dynLight) * shadowScale;   // original behaviour
#else
    float3 finalLight = baseLight * shadowScale + dynLight;     // sun shadow only
#endif

    float3 finalColor = texRgb * finalLight * MaterialTint;

    if (hasDynamicLight)
    {
        [branch] if (DebugLightingAreas > 0.0)
        {
            float isDebugPixel = DebugLightingAreas * step(0.01, dot(dynamicLight, dynamicLight));
            finalColor = lerp(finalColor, float3(0.0, 0.0, 0.0), isDebugPixel);
        }
    }

    return ApplyWorldFog(finalColor, input.WorldPos);
}

float4 PS_Objects(PixelInput input) : SV_Target
{
    float alpha;
    float3 texRgb = FetchObjectTexel(input, alpha);
    float3 normal = PrepareNormal(input.Normal);
    float3 dyn = CalculateDynamicLighting(input.WorldPos, normal);
    return float4(ShadeObjectRgb(input, texRgb, normal, dyn, true), alpha);
}

float4 PS_ObjectsVertexLit(PixelInput input) : SV_Target
{
    float alpha;
    float3 texRgb = FetchObjectTexel(input, alpha);
    float3 normal = PrepareNormal(input.Normal);
    return float4(ShadeObjectRgb(input, texRgb, normal, input.DynamicLight, true), alpha);
}

// Sun + ambient + shadows only (dynamic lights disabled).
float4 PS_ObjectsSunOnly(PixelInput input) : SV_Target
{
    float alpha;
    float3 texRgb = FetchObjectTexel(input, alpha);
    float3 normal = PrepareNormal(input.Normal);
    return float4(ShadeObjectRgb(input, texRgb, normal, float3(0.0, 0.0, 0.0), false), alpha);
}

// Preserve the existing vertex-color/AlphaTest appearance when dynamic lighting
// is disabled, with the same additive coverage rejection as the lit paths.
float4 PS_AdditiveAlphaTest(UnlitPixelInput input) : SV_Target
{
    float4 texColor = tex2D(SamplerState0, input.TexCoord);
    [branch] if (AdditiveAlphaCutoff > 0.0)
        clip(min(texColor.a, max(texColor.r, max(texColor.g, texColor.b))) - AdditiveAlphaCutoff);
    float4 color = texColor * input.Color * Alpha;
    clip(color.a - 0.01);
    return color;
}

// ============================================================================
// TECHNIQUES
// ============================================================================

technique DynamicLighting
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_Objects();
        PixelShader = compile PS_SHADERMODEL PS_Objects();
    }
}

technique DynamicLighting_VertexLit
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsVertexLit();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
    }
}

technique DynamicLighting_SunOnly
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_Objects();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsSunOnly();
    }
}

#if !OPENGL
technique DynamicLighting_CutoutInstanced
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsCutoutInstanced();
        PixelShader = compile PS_SHADERMODEL PS_Objects();
    }
}

technique DynamicLighting_CutoutInstanced_VertexLit
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsCutoutInstancedVertexLit();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
    }
}

technique DynamicLighting_CutoutInstanced_SunOnly
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsCutoutInstanced();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsSunOnly();
    }
}

technique DynamicLighting_Skinned
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinned();
        PixelShader = compile PS_SHADERMODEL PS_Objects();
    }
}

technique DynamicLighting_Skinned_VertexLit
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinnedVertexLit();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
    }
}

technique DynamicLighting_Skinned_SunOnly
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinned();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsSunOnly();
    }
}

technique DynamicLighting_SkinnedInstanced
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinnedInstanced();
        PixelShader = compile PS_SHADERMODEL PS_Objects();
    }
}

technique DynamicLighting_SkinnedInstanced_VertexLit
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinnedInstancedVertexLit();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
    }
}

technique DynamicLighting_SkinnedInstanced_SunOnly
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinnedInstanced();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsSunOnly();
    }
}

technique DynamicLighting_SkinnedMultiPoseInstanced
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinnedMultiPoseInstanced();
        PixelShader = compile PS_SHADERMODEL PS_Objects();
    }
}

technique DynamicLighting_SkinnedMultiPoseInstanced_VertexLit
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinnedMultiPoseInstancedVertexLit();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
    }
}

technique DynamicLighting_SkinnedMultiPoseInstanced_SunOnly
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinnedMultiPoseInstanced();
        PixelShader = compile PS_SHADERMODEL PS_ObjectsSunOnly();
    }
}

technique Highlight_Skinned
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_HighlightSkinned();
        PixelShader = compile PS_SHADERMODEL PS_Highlight();
    }
}
#endif

technique DynamicLighting_Terrain
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_Terrain();
        PixelShader = compile PS_SHADERMODEL PS_Terrain();
    }
}

technique DynamicLighting_Terrain_Low
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_TerrainLow();
        PixelShader = compile PS_SHADERMODEL PS_Terrain();
    }
}

// ============================================================================
// SHADOW CASTERS
// ============================================================================

struct ShadowVertexOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float2 Depth    : TEXCOORD1;
};

ShadowVertexOutput ShadowVS(VertexInput input)
{
    ShadowVertexOutput output;
    float4 worldPos = mul(float4(input.Position, 1.0), World);
    output.Position = mul(worldPos, LightViewProjection);
    output.TexCoord = CalculateProceduralUV(worldPos.xyz, input.TexCoord) + TextureCoordinateOffset;
    output.Depth = output.Position.zw;
    return output;
}

#if !OPENGL
// Skinned casters use the same UVs as their main pass (VS_ObjectsSkinned*), not
// the terrain procedural UVs, so stale terrain state can no longer corrupt the
// alpha mask of characters/monsters.
ShadowVertexOutput ShadowVS_Skinned(VertexInputSkinned input)
{
    ShadowVertexOutput output;
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[BoneIndex(input.BoneIndices.x)]);
    float4 worldPos = mul(localPos, World);
    output.Position = mul(worldPos, LightViewProjection);
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Depth = output.Position.zw;
    return output;
}

ShadowVertexOutput ShadowVS_SkinnedInstanced(VertexInputSkinnedInstanced input)
{
    ShadowVertexOutput output;
    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[BoneIndex(input.BoneIndices.x)]);
    float4 worldPos = mul(localPos, instanceWorld);
    output.Position = mul(worldPos, LightViewProjection);
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Depth = output.Position.zw;
    return output;
}
#endif

float4 ShadowPS(ShadowVertexOutput input) : SV_TARGET
{
    // Opaque batches can opt out of the alpha fetch (ShadowOpaqueCaster = 1).
    [branch] if (ShadowOpaqueCaster < 0.5)
    {
        float alphaMask = tex2D(SamplerState0, input.TexCoord).a;
        clip(alphaMask - 0.01);
    }

    float depth = input.Depth.x / input.Depth.y;
#if OPENGL
    float linearDepth = depth * 0.5 + 0.5;
#else
    float linearDepth = depth;
#endif
    return float4(linearDepth, linearDepth, linearDepth, 1.0);
}

technique ShadowCaster
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL ShadowVS();
        PixelShader  = compile PS_SHADERMODEL ShadowPS();
    }
}

#if !OPENGL
technique ShadowCaster_Skinned
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL ShadowVS_Skinned();
        PixelShader  = compile PS_SHADERMODEL ShadowPS();
    }
}


technique ShadowCaster_SkinnedInstanced
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL ShadowVS_SkinnedInstanced();
        PixelShader  = compile PS_SHADERMODEL ShadowPS();
    }
}
#endif

technique AdditiveAlphaTest
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsUnlit();
        PixelShader = compile PS_SHADERMODEL PS_AdditiveAlphaTest();
    }
}
