// DynamicLighting.fx - Dynamic lighting shader

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

// Lighting parameters  
UNIFORM_DEFAULT(float3, AmbientLight, float3(0.8, 0.8, 0.8));
UNIFORM_DEFAULT(float, Alpha, 1.0);
UNIFORM_DEFAULT(float2, TextureCoordinateOffset, float2(0.0, 0.0));
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
float4 SampleShadowSampler(float2 uv) { return ShadowMap.Sample(ShadowSampler, uv); }
#define tex2D(s, uv) Sample##s(uv)
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

// World fog (e.g. Valley of Loren siege haze) — disabled unless a world opts in
UNIFORM_DEFAULT(float, FogEnabled, 0.0);
UNIFORM_DEFAULT(float3, FogColor, float3(0.0, 0.0, 0.0));
UNIFORM_DEFAULT(float, FogStart, 2000.0);
UNIFORM_DEFAULT(float, FogEnd, 2700.0);
UNIFORM_DEFAULT(float3, FogCameraPosition, float3(0.0, 0.0, 0.0));

float3 ApplyWorldFog(float3 color, float3 worldPos)
{
    float f = saturate((length(worldPos - FogCameraPosition) - FogStart) / max(FogEnd - FogStart, 1.0));
    return lerp(color, FogColor, f * FogEnabled);
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
#endif

struct PixelInput
{
    float4 Position     : SV_POSITION;
    float2 TexCoord     : TEXCOORD0;
    float3 WorldPos     : TEXCOORD1;
    float3 Normal       : TEXCOORD2;
    float4 Color        : COLOR0;
    float3 DynamicLight : TEXCOORD3; 
};

// ============================================================================
// EXTREME OPTIMIZED LIGHTING FUNCTIONS
// ============================================================================

#define TERRAIN_MAX_LIGHTS MAX_LIGHTS
#if OPENGL
    #define TERRAIN_LOW_MAX_LIGHTS 2
#else
    #define TERRAIN_LOW_MAX_LIGHTS 8
#endif

float3 CalculateTerrainLighting(float3 worldPos, float3 normal)
{
    float3 dynamicLight = float3(0, 0, 0);

    int lightCount = min(max(ActiveLightCount, 0), TERRAIN_MAX_LIGHTS);
#if OPENGL
    [unroll(MAX_LIGHTS)]
    for (int i = 0; i < TERRAIN_MAX_LIGHTS; i++)
    {
        if (i >= lightCount) break;
#else
    [loop]
    for (int i = 0; i < lightCount; i++)
    {
#endif
        float intensity = LightColorIntensity[i].w;
        if (intensity <= 0.0) continue;

        float3 lightPos = LightPosInvRadius[i].xyz;
        float3 lightColor = LightColorIntensity[i].xyz;
        float3 lightDir = lightPos - worldPos;
        float distSq = dot(lightDir, lightDir);
        
        float invRad = max(LightPosInvRadius[i].w, 0.0);
        float invRadSq = invRad * invRad;

        // Quadratic attenuation
        float attenuation = saturate(1.0 - (distSq * invRadSq));
        
        // Hemisphere check
        float vertical = saturate((lightPos.z - worldPos.z) * invRad);
        attenuation *= vertical;

        // ALU TRICK: dot(normal, lightDir) * invDist saves 2 multiplications vs dot(normal, lightDir * invDist)
        float invDist = rsqrt(distSq + 0.0001);
        float diffuse = saturate(dot(normal, lightDir) * invDist);

        dynamicLight += lightColor * (intensity * diffuse * attenuation);
    }
    return dynamicLight;
}

float3 CalculateTerrainLightingLow(float3 worldPos, float3 normal)
{
    float3 dynamicLight = float3(0, 0, 0);

    int lightCount = min(max(ActiveLightCount, 0), TERRAIN_LOW_MAX_LIGHTS);
#if OPENGL
    [unroll(TERRAIN_LOW_MAX_LIGHTS)]
    for (int i = 0; i < TERRAIN_LOW_MAX_LIGHTS; i++)
    {
        if (i >= lightCount) break;
#else
    [loop]
    for (int i = 0; i < lightCount; i++)
    {
#endif
        float intensity = LightColorIntensity[i].w;
        if (intensity <= 0.0) continue;

        float3 lightPos = LightPosInvRadius[i].xyz;
        float3 lightColor = LightColorIntensity[i].xyz;
        float3 lightDir = lightPos - worldPos;
        float distSq = dot(lightDir, lightDir);
        
        float invRad = max(LightPosInvRadius[i].w, 0.0);
        float attenuation = saturate(1.0 - (distSq * (invRad * invRad)));
        
        float vertical = saturate((lightPos.z - worldPos.z) * invRad);
        attenuation *= vertical;

        float invDist = rsqrt(distSq + 0.0001);
        float diffuse = saturate(dot(normal, lightDir) * invDist);

        dynamicLight += lightColor * (intensity * diffuse * attenuation);
    }
    return dynamicLight;
}

float3 CalculateDynamicLighting(float3 worldPos, float3 normal)
{
    float3 dynamicLight = float3(0, 0, 0);

    int lightCount = min(max(ActiveLightCount, 0), MAX_LIGHTS);
#if OPENGL
    [unroll(MAX_LIGHTS)]
    for (int i = 0; i < MAX_LIGHTS; i++)
    {
        if (i >= lightCount) break;
#else
    [loop]
    for (int i = 0; i < lightCount; i++)
    {
#endif
        float intensity = LightColorIntensity[i].w;
        if (intensity <= 0.0) continue;

        float3 lightPos = LightPosInvRadius[i].xyz;
        float3 lightColor = LightColorIntensity[i].xyz;
        float3 lightDir = lightPos - worldPos;
        float distSq = dot(lightDir, lightDir);
        
        float invRad = max(LightPosInvRadius[i].w, 0.0);
        float attenuation = saturate(1.0 - (distSq * (invRad * invRad)));

        float invDist = rsqrt(distSq + 0.0001);
        float diffuse = saturate(dot(normal, lightDir) * invDist);

        dynamicLight += lightColor * (intensity * diffuse * attenuation);
    }
    return dynamicLight;
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

PixelInput VS_Terrain(VertexInput input)
{
    PixelInput output;
    float4 worldPos = mul(float4(input.Position, 1.0), World);
    output.WorldPos = worldPos.xyz;
    // ALU TRICK: Use precalculated WorldViewProjection instead of multiplying View and Projection per vertex
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.Normal = normalize(mul(input.Normal, (float3x3)World));
    output.TexCoord = CalculateProceduralUV(worldPos.xyz, input.TexCoord);
    output.Color = input.Color;
    output.DynamicLight = CalculateTerrainLighting(output.WorldPos, output.Normal);
    return output;
}

PixelInput VS_TerrainLow(VertexInput input)
{
    PixelInput output;
    float4 worldPos = mul(float4(input.Position, 1.0), World);
    output.WorldPos = worldPos.xyz;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.Normal = normalize(mul(input.Normal, (float3x3)World));
    output.TexCoord = CalculateProceduralUV(worldPos.xyz, input.TexCoord);
    output.Color = input.Color;
    output.DynamicLight = CalculateTerrainLightingLow(output.WorldPos, output.Normal);
    return output;
}

PixelInput VS_Objects(VertexInput input)
{
    PixelInput output;
    float4 worldPos = mul(float4(input.Position, 1.0), World);
    output.WorldPos = worldPos.xyz;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.Normal = normalize(mul(input.Normal, (float3x3)World));
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color;
    output.DynamicLight = float3(0, 0, 0); 
    return output;
}

PixelInput VS_ObjectsVertexLit(VertexInput input)
{
    PixelInput output;
    float4 worldPos = mul(float4(input.Position, 1.0), World);
    float3 worldNormal = normalize(mul(input.Normal, (float3x3)World));
    output.WorldPos = worldPos.xyz;
    output.Position = mul(float4(input.Position, 1.0), WorldViewProjection);
    output.Normal = worldNormal;
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color;
    output.DynamicLight = CalculateDynamicLighting(worldPos.xyz, worldNormal);
    return output;
}

#if !OPENGL
PixelInput VS_ObjectsSkinned(VertexInputSkinned input)
{
    PixelInput output;
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[positionBoneIndex]);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[normalBoneIndex]);
    
    output.WorldPos = mul(localPos, World).xyz;
    // ALU TRICK: localPos * WorldViewProjection is mathematically identical to worldPos * View * Projection
    // but saves 2 matrix multiplications per vertex!
    output.Position = mul(localPos, WorldViewProjection);
    output.Normal = normalize(mul(localNormal, (float3x3)World));
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color;
    output.DynamicLight = float3(0, 0, 0);
    return output;
}

PixelInput VS_ObjectsSkinnedVertexLit(VertexInputSkinned input)
{
    PixelInput output;
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[positionBoneIndex]);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[normalBoneIndex]);
    float4 worldPos = mul(localPos, World);
    float3 worldNormal = normalize(mul(localNormal, (float3x3)World));
    output.WorldPos = worldPos.xyz;
    output.Position = mul(localPos, WorldViewProjection);
    output.Normal = worldNormal;
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color;
    output.DynamicLight = CalculateDynamicLighting(worldPos.xyz, worldNormal);
    return output;
}

PixelInput VS_ObjectsSkinnedInstanced(VertexInputSkinnedInstanced input)
{
    PixelInput output;
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[positionBoneIndex]);
    float4 worldPos = mul(localPos, instanceWorld);
    
    output.WorldPos = worldPos.xyz;
    output.Position = mul(worldPos, ViewProjection);
    
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[normalBoneIndex]);
    output.Normal = normalize(mul(localNormal, (float3x3)instanceWorld));
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color * input.InstanceColor;
    output.DynamicLight = float3(0, 0, 0);
    return output;
}

// Static map geometry trades per-pixel dynamic-light evaluation for per-vertex
// evaluation. This preserves light selection and attenuation while reducing the
// dominant shader cost on large opaque buildings and repeated decorations.
PixelInput VS_ObjectsSkinnedInstancedVertexLit(VertexInputSkinnedInstanced input)
{
    PixelInput output;
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[positionBoneIndex]);
    float4 worldPos = mul(localPos, instanceWorld);
    float3 localNormal = mul(input.Normal, (float3x3)BoneMatrices[normalBoneIndex]);
    float3 worldNormal = normalize(mul(localNormal, (float3x3)instanceWorld));

    output.WorldPos = worldPos.xyz;
    output.Position = mul(worldPos, ViewProjection);
    output.Normal = worldNormal;
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color * input.InstanceColor;
    output.DynamicLight = CalculateDynamicLighting(worldPos.xyz, worldNormal);
    return output;
}

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

PixelInput VS_ObjectsSkinnedMultiPoseInstanced(VertexInputSkinnedMultiPoseInstanced input)
{
    PixelInput output;
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    int paletteRow = (int)(input.PaletteData.x + 0.5);

    float4x4 positionBone = LoadCrowdBoneMatrix(positionBoneIndex, paletteRow);
    float3 localNormal = mul(input.Normal, (float3x3)positionBone);
    if (normalBoneIndex != positionBoneIndex)
    {
        float4x4 normalBone = LoadCrowdBoneMatrix(normalBoneIndex, paletteRow);
        localNormal = mul(input.Normal, (float3x3)normalBone);
    }

    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), positionBone);
    float4 worldPos = mul(localPos, instanceWorld);

    output.WorldPos = worldPos.xyz;
    output.Position = mul(worldPos, ViewProjection);
    output.Normal = normalize(mul(localNormal, (float3x3)instanceWorld));
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color * input.InstanceColor;
    output.DynamicLight = float3(0, 0, 0);
    return output;
}

PixelInput VS_ObjectsSkinnedMultiPoseInstancedVertexLit(VertexInputSkinnedMultiPoseInstanced input)
{
    PixelInput output;
    int positionBoneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    int normalBoneIndex = min(max((int)input.BoneIndices.y, 0), 255);
    int paletteRow = (int)(input.PaletteData.x + 0.5);

    float4x4 positionBone = LoadCrowdBoneMatrix(positionBoneIndex, paletteRow);
    float3 localNormal = mul(input.Normal, (float3x3)positionBone);
    if (normalBoneIndex != positionBoneIndex)
    {
        float4x4 normalBone = LoadCrowdBoneMatrix(normalBoneIndex, paletteRow);
        localNormal = mul(input.Normal, (float3x3)normalBone);
    }

    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), positionBone);
    float4 worldPos = mul(localPos, instanceWorld);
    float3 worldNormal = normalize(mul(localNormal, (float3x3)instanceWorld));

    output.WorldPos = worldPos.xyz;
    output.Position = mul(worldPos, ViewProjection);
    output.Normal = worldNormal;
    output.TexCoord = input.TexCoord + TextureCoordinateOffset;
    output.Color = input.Color * input.InstanceColor;
    output.DynamicLight = CalculateDynamicLighting(worldPos.xyz, worldNormal);
    return output;
}

#endif

float SampleShadow(float3 worldPos, float3 normal)
{
    if (ShadowsEnabled < 0.5)
        return 1.0;

    float4 lightPos = mul(float4(worldPos, 1.0), LightViewProjection);
    float3 proj = lightPos.xyz / lightPos.w;

#if OPENGL
    float2 uv = float2(proj.x * 0.5 + 0.5, 0.5 - proj.y * 0.5); 
    float depth = proj.z * 0.5 + 0.5; 
#else
    float2 uv = float2(proj.x * 0.5 + 0.5, 0.5 - proj.y * 0.5); 
    float depth = proj.z; 
#endif

    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 1.0;

    float ndotl = saturate(dot(normal, -SunDirection));
    float bias = ShadowBias + ShadowNormalBias * (1.0 - ndotl);

    float2 off = ShadowMapTexelSize * 0.5;
    
    // ALU TRICK: Pack 4 texture samples into a float4, do ONE step() operation, 
    // and use dot() to sum and multiply by 0.25 simultaneously.
    float4 s;
    s.x = tex2D(ShadowSampler, uv + float2(-off.x, -off.y)).r;
    s.y = tex2D(ShadowSampler, uv + float2( off.x, -off.y)).r;
    s.z = tex2D(ShadowSampler, uv + float2(-off.x,  off.y)).r;
    s.w = tex2D(ShadowSampler, uv + float2( off.x,  off.y)).r;
    
    float4 shadows = step(depth - bias, s);
    return dot(shadows, float4(0.25, 0.25, 0.25, 0.25));
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
    
    // EXTREME OPTIMIZATION: Early clip! 
    // If pixel is transparent, GPU aborts here and skips ALL lighting math below!
    clip(finalAlpha - 0.01);

    float3 normal = PrepareNormal(input.Normal);
    float3 baseLight = input.Color.rgb * GlobalLightMultiplier;

    float3 finalLight = baseLight + input.DynamicLight * TerrainDynamicIntensityScale;

    float isDebugPixel = DebugLightingAreas * step(0.01, dot(input.DynamicLight, input.DynamicLight));

    float shadowTerm = SampleShadow(input.WorldPos, normal);
    float shadowMix = lerp(1.0 - ShadowStrength, 1.0, shadowTerm);
    finalLight *= lerp(1.0, shadowMix, ShadowsEnabled);

    float3 finalColor = lerp(texColor.rgb * finalLight, float3(0, 0, 0), isDebugPixel);
    finalColor = ApplyWorldFog(finalColor, input.WorldPos);

    return float4(finalColor, finalAlpha);
}

float4 PS_Highlight(PixelInput input) : SV_Target
{
    float textureAlpha = tex2D(SamplerState0, input.TexCoord).a;
    clip(textureAlpha - 0.01);
    return float4(HighlightColor * textureAlpha, textureAlpha * Alpha);
}

float4 ShadeObjectPixel(PixelInput input, float3 normal, float3 dynamicLight)
{
    float4 texColor = tex2D(SamplerState0, input.TexCoord);
    float finalAlpha = texColor.a * Alpha * input.Color.a;
    clip(finalAlpha - 0.01);

    float3 sunDir = SunDirection;
    float ndotlRaw = dot(normal, -sunDir);
    float ndotl = saturate(ndotlRaw) + saturate(-ndotlRaw) * 0.35;

    float shadowFactor = saturate(lerp(1.0 - ShadowStrength, 1.0, ndotl));
    float3 sunLight = SunColor * ndotl * SunStrength;
    float3 finalLight = AmbientLight * shadowFactor + sunLight +
                        dynamicLight * TerrainDynamicIntensityScale;

    float isDebugPixel = DebugLightingAreas * step(0.01, dot(dynamicLight, dynamicLight));
    float shadowTerm = SampleShadow(input.WorldPos, normal);
    float shadowMix = lerp(1.0 - ShadowStrength, 1.0, shadowTerm);
    finalLight *= lerp(1.0, shadowMix, ShadowsEnabled);

    float3 finalColor = lerp(texColor.rgb * finalLight, float3(0, 0, 0), isDebugPixel);
    finalColor = ApplyWorldFog(finalColor, input.WorldPos);
    return float4(finalColor, finalAlpha);
}

float4 PS_Objects(PixelInput input) : SV_Target
{
    float3 normal = PrepareNormal(input.Normal);
    return ShadeObjectPixel(input, normal, CalculateDynamicLighting(input.WorldPos, normal));
}

float4 PS_ObjectsVertexLit(PixelInput input) : SV_Target
{
    return ShadeObjectPixel(input, PrepareNormal(input.Normal), input.DynamicLight);
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
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
    }
}

#if !OPENGL
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
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
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
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
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
        PixelShader = compile PS_SHADERMODEL PS_ObjectsVertexLit();
    }
}

technique Highlight_Skinned
{
    pass Pass1
    {
        VertexShader = compile VS_SHADERMODEL VS_ObjectsSkinned();
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
ShadowVertexOutput ShadowVS_Skinned(VertexInputSkinned input)
{
    ShadowVertexOutput output;
    int boneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[boneIndex]);
    float4 worldPos = mul(localPos, World);
    output.Position = mul(worldPos, LightViewProjection);
    output.TexCoord = CalculateProceduralUV(worldPos.xyz, input.TexCoord) + TextureCoordinateOffset;
    output.Depth = output.Position.zw;
    return output;
}

ShadowVertexOutput ShadowVS_SkinnedInstanced(VertexInputSkinnedInstanced input)
{
    ShadowVertexOutput output;
    int boneIndex = min(max((int)input.BoneIndices.x, 0), 255);
    float4x4 instanceWorld = float4x4(input.InstWorld0, input.InstWorld1, input.InstWorld2, input.InstWorld3);
    float4 localPos = mul(float4(input.Position, 1.0), BoneMatrices[boneIndex]);
    float4 worldPos = mul(localPos, instanceWorld);
    output.Position = mul(worldPos, LightViewProjection);
    output.TexCoord = CalculateProceduralUV(worldPos.xyz, input.TexCoord) + TextureCoordinateOffset;
    output.Depth = output.Position.zw;
    return output;
}
#endif

float4 ShadowPS(ShadowVertexOutput input) : SV_TARGET
{
    float alphaMask = tex2D(SamplerState0, input.TexCoord).a;
    clip(alphaMask - 0.01);

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
