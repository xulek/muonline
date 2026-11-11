// DynamicLighting.fx - Android (OpenGL ES) variant
//
// Built only by Content.Android.mgcb, under the same asset name as ../DynamicLighting.fx, which stays the
// shader of every desktop backend. Technique names, parameter names and vertex layouts match the OpenGL
// build of the desktop file, so the C# side is shared.
//
// The effect pipeline translates this file to GLSL ES 1.00, so it keeps to what that language guarantees:
//   * no tex2Dlod / tex2Dgrad / ddx / ddy in pixel shaders (no terrain anti-tiling),
//   * no vertex-shader samplers,
//   * the shadow map is read with point filtering (float targets are not filterable everywhere),
//   * world-space maths (sun, dynamic lights, fog, shadow projection and bias) runs per vertex; pixel
//     shaders only combine interpolated values with texture fetches.
//
// Simplifications against the desktop shader: dynamic lights are always per vertex (DynamicLighting ==
// DynamicLighting_VertexLit, Terrain == Terrain_Low), 2-tap shadow test instead of bilinear PCF, snow without
// per-pixel micro normals, no lighting debug overlay.

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

// Transformation matrices
float4x4 World;
float4x4 WorldViewProjection; // Includes World * View * Projection
float4x4 ViewProjection;

// Textures
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

// Baked snow material: b = edge breakup, a = albedo variation.
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
float3 AmbientLight = float3(0.8, 0.8, 0.8);
float Alpha = 1.0;
// Additive RGB/JPG light textures have no alpha channel: reject their near-black background.
float AdditiveAlphaCutoff = 0.0;
// Alpha below which a pixel is dropped (0.5 for the depth-writing pass of a cut-out mesh).
float DepthAlphaCutoff = 0.01;
// Alpha from which a pixel is dropped (the blended second pass of a cut-out mesh draws only its soft part).
float AlphaCeiling = 2.0;
float2 TextureCoordinateOffset = float2(0.0, 0.0);
float3 MaterialTint = float3(1.0, 1.0, 1.0);
float3 SunDirection = float3(1.0, 0.0, -0.6);
float3 SunColor = float3(1.0, 0.95, 0.85);
float SunStrength = 0.8;
float ShadowStrength = 0.5;
float4x4 LightViewProjection;
float2 ShadowMapTexelSize = float2(1.0 / 2048.0, 1.0 / 2048.0);
float ShadowBias = 0.0015;
float ShadowNormalBias = 0.0025;
float ShadowsEnabled = 0.0;
// 1 for shadow-caster batches whose textures have no alpha cut-outs.
float ShadowOpaqueCaster = 0.0;

Texture2D ShadowMap;
sampler ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
    AddressU = Clamp;
    AddressV = Clamp;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
};

// Dynamic lights. Every shader that reads the arrays reads all MAX_LIGHTS elements: the GLSL translation
// gives an array only the registers a shader reads, while all elements are uploaded.
#define MAX_LIGHTS 8
float4 LightPosInvRadius[MAX_LIGHTS];   // xyz = position, w = inverse radius
float4 LightColorIntensity[MAX_LIGHTS]; // rgb = color, w = intensity
int ActiveLightCount = 0;

float TerrainDynamicIntensityScale = 1.5;
float GlobalLightMultiplier = 1.0;

float2 TerrainUvScale = float2(0.0, 0.0);
float UseProceduralTerrainUV = 0.0;
float IsWaterTexture = 0.0;
float2 WaterFlowDirection = float2(1.0, 0.0);
float WaterTotal = 0.0;
float DistortionAmplitude = 0.0;
float DistortionFrequency = 0.0;

// World fog - FogEnabled is 0 unless a world opts in
float FogEnabled = 0.0;
float3 FogColor = float3(0.0, 0.0, 0.0);
float FogStart = 2000.0;
float FogEnd = 2700.0;
float3 FogCameraPosition = float3(0.0, 0.0, 0.0);

// ============================================================================
// PER-VERTEX HELPERS
// ============================================================================

float FogFactor(float3 worldPos)
{
    return saturate((length(worldPos - FogCameraPosition) - FogStart) / max(FogEnd - FogStart, 1.0)) * FogEnabled;
}

float3 WorldNormal(float3 normal)
{
    return normalize(mul(normal, (float3x3)World) + float3(0.0, 0.0, 0.00001));
}

// Shadow-map coordinates with the receiver bias already applied: xy = uv * w, z = biased depth * w, w = clip w.
// Affine in world position, so interpolating them is exact. Must not live in a COLOR semantic (clamped to [0,1]).
float4 ComputeShadowCoord(float3 worldPos, float3 worldNormal)
{
    float4 clip = mul(float4(worldPos, 1.0), LightViewProjection);
    float ndotl = saturate(dot(worldNormal, -SunDirection));
    float bias = ShadowBias + ShadowNormalBias * (1.0 - ndotl);
    return float4(0.5 * clip.x + 0.5 * clip.w,
                  0.5 * clip.w - 0.5 * clip.y,
                  clip.z * 0.5 + clip.w * 0.5 - bias * clip.w,
                  clip.w);
}

// Sum of the uploaded point lights. Slots from ActiveLightCount on hold stale data and are masked out.
// 'hemisphere' is a literal at every call site: terrain ignores lights below the surface, objects do not.
float3 PointLights(float3 worldPos, float3 normal, bool hemisphere)
{
    float3 light = float3(0.0, 0.0, 0.0);
    float count = (float)ActiveLightCount;
    [unroll(MAX_LIGHTS)] for (int i = 0; i < MAX_LIGHTS; i++)
    {
        float4 lp = LightPosInvRadius[i];
        float4 lc = LightColorIntensity[i];

        float3 toLight = lp.xyz - worldPos;
        float distSq = dot(toLight, toLight);
        float invRad = max(lp.w, 0.0);

        float weight = max(lc.w, 0.0) * saturate(1.0 - distSq * (invRad * invRad)) * step(i + 0.5, count);
        if (hemisphere)
            weight *= saturate(toLight.z * invRad);

        float diffuse = saturate(dot(normal, toLight) * rsqrt(distSq + 0.0001));
        light += lc.rgb * (weight * diffuse);
    }
    return light;
}

float3 SunAndAmbient(float3 normal)
{
    float ndotlRaw = dot(normal, -SunDirection);
    float ndotl = saturate(ndotlRaw) + saturate(-ndotlRaw) * 0.35;
    float shadowFactor = saturate(lerp(1.0 - ShadowStrength, 1.0, ndotl));
    return AmbientLight * shadowFactor + SunColor * (ndotl * SunStrength);
}

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

// ============================================================================
// PER-PIXEL HELPERS
// ============================================================================

// Light multiplier of the sun shadow: two point-sampled depth tests half a texel either side of the pixel.
// Both fetches always run (the translation cannot branch around a texture fetch); with shadows disabled the
// result is 1.
float ShadowLightScale(float4 shadowCoord)
{
    float3 p = shadowCoord.xyz / max(shadowCoord.w, 0.0001);
    float2 offset = ShadowMapTexelSize * 0.5;
    float2 depths;
    depths.x = tex2D(ShadowSampler, p.xy - offset).r;
    depths.y = tex2D(ShadowSampler, p.xy + offset).r;
    float lit = dot(step(p.zz, depths), float2(0.5, 0.5));

    // Outside the shadow map everything is lit.
    float2 inside = step(float2(0.0, 0.0), p.xy) * step(p.xy, float2(1.0, 1.0));
    float shadowed = (1.0 - lit) * inside.x * inside.y * step(0.5, ShadowsEnabled);
    return 1.0 - ShadowStrength * shadowed;
}

// ============================================================================
// OBJECTS AND TERRAIN
// ============================================================================

struct VertexInput
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
    float4 Color    : COLOR0;
    float3 Normal   : NORMAL0;
};

struct LitPixelInput
{
    float4 Position    : POSITION0;
    float3 TexCoordFog : TEXCOORD0; // xy = uv, z = fog factor
    float4 Light       : TEXCOORD1; // rgb = light without the sun shadow, a = vertex alpha
    float4 ShadowCoord : TEXCOORD2;
};

LitPixelInput ObjectsCore(VertexInput input, bool dynamicLights)
{
    LitPixelInput output;
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    float3 normal = WorldNormal(input.Normal);

    float3 light = SunAndAmbient(normal);
    if (dynamicLights)
        light += PointLights(worldPos, normal, false) * TerrainDynamicIntensityScale;

    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.TexCoordFog = float3(input.TexCoord + TextureCoordinateOffset, FogFactor(worldPos));
    output.Light = float4(light * MaterialTint, input.Color.a);
    output.ShadowCoord = ComputeShadowCoord(worldPos, normal);
    return output;
}

LitPixelInput VS_Objects(VertexInput input)        { return ObjectsCore(input, true); }
LitPixelInput VS_ObjectsSunOnly(VertexInput input) { return ObjectsCore(input, false); }

LitPixelInput VS_Terrain(VertexInput input)
{
    LitPixelInput output;
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    float3 normal = WorldNormal(input.Normal);

    // Terrain vertex colours carry the baked light map.
    float3 light = input.Color.rgb * GlobalLightMultiplier +
                   PointLights(worldPos, normal, true) * TerrainDynamicIntensityScale;

    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.TexCoordFog = float3(CalculateProceduralUV(worldPos, input.TexCoord), FogFactor(worldPos));
    output.Light = float4(light, input.Color.a);
    output.ShadowCoord = ComputeShadowCoord(worldPos, normal);
    return output;
}

float4 PS_Objects(LitPixelInput input) : COLOR0
{
    float4 texColor = tex2D(SamplerState0, input.TexCoordFog.xy);
    float alpha = texColor.a * Alpha * input.Light.a;

    // With AdditiveAlphaCutoff == 0 the coverage test can never fire.
    float coverage = min(texColor.a, max(texColor.r, max(texColor.g, texColor.b)));
    clip(min(coverage - AdditiveAlphaCutoff,
             min(alpha - max(0.01, DepthAlphaCutoff), AlphaCeiling - alpha)));

    float3 color = texColor.rgb * input.Light.rgb * ShadowLightScale(input.ShadowCoord);
    return float4(lerp(color, FogColor, input.TexCoordFog.z), alpha);
}

float4 PS_Terrain(LitPixelInput input) : COLOR0
{
    float4 texColor = tex2D(SamplerState0, input.TexCoordFog.xy);
    float alpha = texColor.a * Alpha * input.Light.a;
    clip(alpha - 0.01);

    float3 color = texColor.rgb * input.Light.rgb * ShadowLightScale(input.ShadowCoord);
    return float4(lerp(color, FogColor, input.TexCoordFog.z), alpha);
}

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
        VertexShader = compile VS_SHADERMODEL VS_Objects();
        PixelShader = compile PS_SHADERMODEL PS_Objects();
    }
}

technique DynamicLighting_SunOnly
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSunOnly();
        PixelShader = compile PS_SHADERMODEL PS_Objects();
    }
}

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
        VertexShader = compile VS_SHADERMODEL VS_Terrain();
        PixelShader = compile PS_SHADERMODEL PS_Terrain();
    }
}

// ============================================================================
// SNOW
// ============================================================================
// Real snow geometry supplies the groove normals and compaction in TexCoord.x. All lighting is evaluated on
// those vertex normals; the pixel shader adds the textures, the edge breakup and the sun shadow.

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
    float4 Position    : POSITION0;
    float4 MaterialUv  : TEXCOORD0; // xy = baked material uv, zw = base texture uv
    float4 OverlayUv   : TEXCOORD1; // xy = overlay texture uv, z = fog factor, w = roughness
    float4 Light       : TEXCOORD2; // rgb = light without the sun shadow, a = snow depth (vertex alpha)
    float4 ShadowCoord : TEXCOORD3;
    float OverlayBlend : TEXCOORD4;
};

SnowPixelInput SnowCore(SnowVertexInput input, bool opaque)
{
    SnowPixelInput output;
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    float3 normal = WorldNormal(input.Normal);

    float compacted = saturate(input.TexCoord.x);
    float materialDepth = 1.0;
    if (!opaque)
        materialDepth = smoothstep(0.2, 0.9, input.Color.a);

    float3 terrainLight = input.TerrainLight.rgb * GlobalLightMultiplier;
    float3 smoothLight = saturate(input.Color.rgb + SnowAmbientLight) * GlobalLightMultiplier;
    float3 light = lerp(terrainLight, smoothLight, materialDepth);
    light += PointLights(worldPos, normal, true) * TerrainDynamicIntensityScale;

    // Relief changes illumination around banks (TerrainRenderer uploads a unit SunDirection for snow).
    float sunFacing = saturate(dot(normal, -SunDirection));
    float upFacing = saturate(-SunDirection.z);
    light *= 1.0 + (sunFacing - upFacing) * SunStrength * 0.5 * materialDepth;
    light *= 1.0 - compacted * 0.24;

    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.MaterialUv = float4(worldPos.xy / 1024.0, worldPos.xy * SnowBaseUvScale);
    output.OverlayUv = float4(worldPos.xy * SnowOverlayUvScale, FogFactor(worldPos),
                              (1.0 - compacted * 0.55) * materialDepth);
    output.Light = float4(light, input.Color.a);
    output.ShadowCoord = ComputeShadowCoord(worldPos, normal);
    output.OverlayBlend = input.TerrainLight.a * SnowOverlayEnabled;
    return output;
}

SnowPixelInput VS_Snow(SnowVertexInput input)       { return SnowCore(input, false); }
SnowPixelInput VS_SnowOpaque(SnowVertexInput input) { return SnowCore(input, true); }

// Both flags are literals in each technique: opaque snow has no discard, and base-only materials have no
// overlay texture fetch.
float4 ShadeSnow(SnowPixelInput input, bool opaque, bool overlay)
{
    float4 material = tex2D(SnowMaterialSampler, input.MaterialUv.xy);
    float coverage = 1.0;
    if (!opaque)
    {
        coverage = smoothstep(0.035, 0.65, input.Light.a + (material.b - 0.5) * 0.16);
        clip(min(input.Light.a - 0.015, coverage - 0.01));
    }

    float3 albedo = tex2D(SamplerState0, input.MaterialUv.zw).rgb;
    if (overlay)
    {
        float4 overlayTexel = tex2D(SnowOverlaySampler, input.OverlayUv.xy);
        albedo = lerp(albedo, overlayTexel.rgb, overlayTexel.a * input.OverlayBlend);
    }
    albedo *= 1.0 + (material.a - 0.5) * 0.2 * input.OverlayUv.w;

    float3 color = albedo * input.Light.rgb * ShadowLightScale(input.ShadowCoord);
    return float4(lerp(color, FogColor, input.OverlayUv.z), coverage);
}

float4 PS_Snow(SnowPixelInput input) : COLOR0 { return ShadeSnow(input, false, true); }
float4 PS_SnowBase(SnowPixelInput input) : COLOR0 { return ShadeSnow(input, false, false); }
float4 PS_SnowOpaque(SnowPixelInput input) : COLOR0 { return ShadeSnow(input, true, true); }
float4 PS_SnowOpaqueBase(SnowPixelInput input) : COLOR0 { return ShadeSnow(input, true, false); }

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
        VertexShader = compile VS_SHADERMODEL VS_SnowOpaque();
        PixelShader = compile PS_SHADERMODEL PS_SnowOpaque();
    }
}

technique DynamicLighting_Snow_Opaque_Base
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL VS_SnowOpaque();
        PixelShader = compile PS_SHADERMODEL PS_SnowOpaqueBase();
    }
}

// ============================================================================
// SHADOW CASTERS
// ============================================================================

struct ShadowVertexOutput
{
    float4 Position : POSITION0;
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

float4 ShadowPS(ShadowVertexOutput input) : COLOR0
{
    // Opaque batches (ShadowOpaqueCaster = 1) are never clipped.
    float alphaMask = tex2D(SamplerState0, input.TexCoord).a;
    clip(max(alphaMask - 0.01, ShadowOpaqueCaster - 0.5));

    float linearDepth = input.Depth.x / input.Depth.y * 0.5 + 0.5;
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

// ============================================================================
// PROJECTED GROUND SHADOWS
// ============================================================================
// The mesh is flattened by ProjectedShadowMatrix (ModelObject.TryGetShadowMatrix) and every vertex is put on
// the rendered ground under it. The heights of a 10x10 node window around the shadow (GroundPatchOrigin = its
// first node) come as constants, four per register.
float4x4 ProjectedShadowMatrix;
float ProjectedShadowOpacity = 0.5;

static const float GroundTileSize = 100.0;   // Constants.TERRAIN_SCALE
static const float GroundShadowBias = 1.25;  // shadow surface bias above the ground

float4 GroundPatch[25];
float2 GroundPatchOrigin;

float GroundNodeHeight(int x, int y)
{
    int lx = clamp(x - (int)GroundPatchOrigin.x, 0, 9);
    int ly = clamp(y - (int)GroundPatchOrigin.y, 0, 9);
    int index = ly * 10 + lx;
    int slot = index / 4;
    float4 packed = GroundPatch[slot];
    int component = index - slot * 4;
    return component == 0 ? packed.x : component == 1 ? packed.y : component == 2 ? packed.z : packed.w;
}

// TerrainPhysics.RequestTerrainHeight: the tile is split along the h00 -> h11 diagonal.
float GroundHeightAt(float2 worldXY)
{
    if (worldXY.x < 0.0 || worldXY.y < 0.0)
        return 0.0;

    float2 tile = worldXY / GroundTileSize;
    int x0 = (int)floor(tile.x);
    int y0 = (int)floor(tile.y);
    float tx = tile.x - x0;
    float ty = tile.y - y0;
    float h00 = GroundNodeHeight(x0, y0);
    float h10 = GroundNodeHeight(x0 + 1, y0);
    float h11 = GroundNodeHeight(x0 + 1, y0 + 1);
    float h01 = GroundNodeHeight(x0, y0 + 1);
    return tx >= ty
        ? (1.0 - tx) * h00 + (tx - ty) * h10 + ty * h11
        : (1.0 - ty) * h00 + tx * h11 + (ty - tx) * h01;
}

struct ProjectedShadowOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

ProjectedShadowOutput ProjectedShadowVS(VertexInput input)
{
    ProjectedShadowOutput output;
    float4 projected = mul(float4(input.Position, 1.0), ProjectedShadowMatrix);
    float3 ground = float3(projected.xy, GroundHeightAt(projected.xy) + GroundShadowBias);
    output.Position = mul(float4(ground, 1.0), ViewProjection);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 ProjectedShadowPS(ProjectedShadowOutput input) : COLOR0
{
    // The texture alpha cuts the silhouette (opaque casters are a solid shape).
    float alphaMask = max(tex2D(SamplerState0, input.TexCoord).a, step(0.5, ShadowOpaqueCaster));
    return float4(0.0, 0.0, 0.0, alphaMask * ProjectedShadowOpacity);
}

technique ProjectedShadow
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL ProjectedShadowVS();
        PixelShader  = compile PS_SHADERMODEL ProjectedShadowPS();
    }
}

// ============================================================================
// ADDITIVE ALPHA TEST
// ============================================================================
// Vertex colour * texture, with the additive coverage rejection of the lit paths.

struct UnlitPixelInput
{
    float4 Position    : POSITION0;
    float3 TexCoordFog : TEXCOORD0; // xy = uv, z = fog factor
    float4 Color       : COLOR0;
};

UnlitPixelInput VS_ObjectsUnlit(VertexInput input)
{
    UnlitPixelInput output;
    float3 worldPos = mul(float4(input.Position, 1.0), World).xyz;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.TexCoordFog = float3(input.TexCoord + TextureCoordinateOffset, FogFactor(worldPos));
    output.Color = input.Color;
    return output;
}

float4 PS_AdditiveAlphaTest(UnlitPixelInput input) : COLOR0
{
    float4 texColor = tex2D(SamplerState0, input.TexCoordFog.xy);
    float4 color = texColor * input.Color * Alpha;

    float coverage = min(texColor.a, max(texColor.r, max(texColor.g, texColor.b)));
    clip(min(coverage - AdditiveAlphaCutoff, color.a - 0.01));

    // Additive: fog fades the contribution out instead of blending towards the fog colour.
    color.rgb *= 1.0 - input.TexCoordFog.z;
    return color;
}

technique AdditiveAlphaTest
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsUnlit();
        PixelShader = compile PS_SHADERMODEL PS_AdditiveAlphaTest();
    }
}
