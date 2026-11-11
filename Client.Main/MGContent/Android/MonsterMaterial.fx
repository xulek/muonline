// MonsterMaterial.fx - Android (OpenGL ES) variant
//
// Built only by Content.Android.mgcb, under the same asset name as ../MonsterMaterial.fx, which stays the
// shader of every desktop backend. Technique and parameter names match the OpenGL build of the desktop file.
//
// Light, fog and the shadow projection are evaluated per vertex, the sun shadow is a 2-tap point-sampled test
// and the glow uses two ghost samples instead of four (the translation to GLSL ES runs every texture fetch of
// a pixel shader for every pixel, whatever the branch it is written in).

#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0

// World / distance fog, same uniforms and formula as DynamicLighting.fx (WorldFog.Apply).
float FogEnabled = 0.0;
float3 FogColor = float3(0.0, 0.0, 0.0);
float FogStart = 2000.0;
float FogEnd = 2700.0;
float3 FogCameraPosition = float3(0.0, 0.0, 0.0);

float4x4 World;
float4x4 WorldViewProjection;

float3 LightDirection = float3(0.707, -0.707, 0);

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

// Point filtering: float render targets are not filterable on every OpenGL ES device.
Texture2D ShadowMap;
sampler ShadowSampler = sampler_state
{
    Texture = <ShadowMap>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

// Monster-specific parameters
float3 GlowColor = float3(1.0, 0.8, 0.0); // Default gold glow
float GlowIntensity = 1.0;
float Time = 0;
bool EnableGlow = false;
bool SimpleColorMode = false; // Simple color tinting without ghosting
float Alpha = 1.0;
float4x4 LightViewProjection;
float2 ShadowMapTexelSize = float2(1.0 / 2048.0, 1.0 / 2048.0);
float ShadowBias = 0.0015;
float ShadowNormalBias = 0.0025;
float ShadowsEnabled = 0.0;
float ShadowStrength = 0.5;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position    : POSITION0;
    float4 TexCoordFog : TEXCOORD0; // xy = uv, z = fog factor, w = light intensity
    float4 ShadowCoord : TEXCOORD1; // xy = uv * w, z = biased depth * w, w = clip w
    float NormalX      : TEXCOORD2; // drives the glow shimmer
};

VertexShaderOutput MainVS(in VertexShaderInput input)
{
    VertexShaderOutput output;
    float3 worldPos = mul(input.Position, World).xyz;
    float3 normal = normalize(mul(input.Normal, (float3x3)World) + float3(0.0, 0.0, 0.00001));
    float ndotl = dot(normal, -LightDirection);

    float4 clip = mul(float4(worldPos, 1.0), LightViewProjection);
    float bias = ShadowBias + ShadowNormalBias * (1.0 - saturate(ndotl));

    output.Position = mul(input.Position, WorldViewProjection);
    output.TexCoordFog = float4(input.TextureCoordinate,
        saturate((length(worldPos - FogCameraPosition) - FogStart) / max(FogEnd - FogStart, 1.0)) * FogEnabled,
        max(0.1, ndotl));
    output.ShadowCoord = float4(0.5 * clip.x + 0.5 * clip.w,
                                0.5 * clip.w - 0.5 * clip.y,
                                clip.z * 0.5 + clip.w * 0.5 - bias * clip.w,
                                clip.w);
    output.NormalX = normal.x;
    return output;
}

// Light multiplier of the sun shadow: two point-sampled depth tests half a texel either side of the pixel.
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

float4 MainPS_Basic(VertexShaderOutput input) : COLOR0
{
    float4 color = tex2D(DiffuseSampler, input.TexCoordFog.xy);
    clip(color.a - 0.1);

    color.rgb *= input.TexCoordFog.w * ShadowLightScale(input.ShadowCoord);
    color.rgb = lerp(color.rgb, FogColor, input.TexCoordFog.z);
    color.a *= Alpha;
    return color;
}

float4 MainPS(VertexShaderOutput input) : COLOR0
{
    float2 uv = input.TexCoordFog.xy;
    float4 color = tex2D(DiffuseSampler, uv);
    clip(color.a - 0.1);

    float3 originalColor = color.rgb * input.TexCoordFog.w;
    color.rgb = originalColor;

    if (SimpleColorMode == true)
    {
        color.rgb = originalColor * GlowColor * GlowIntensity;
    }
    else if (EnableGlow == true && GlowIntensity > 0.0)
    {
        float subtlePulse = (1.0 + sin(Time * 1.5)) * 0.03 + 0.97;
        float shimmer = (1.0 + sin(Time * 20.0 + input.NormalX * 12.0)) * 0.15 + 0.85;
        float extraGlow = max(0.0, GlowIntensity - 2.0) * 0.5;
        float glowEffect = (1.0 + sin(Time * 4.0)) * 0.1 + 0.8;

        float2 ghostOffset1 = float2(sin(Time * 4.0) * 0.035, cos(Time * 3.5) * 0.035);
        float2 ghostOffset2 = float2(sin(Time * 5.5 + 2.1) * 0.025, cos(Time * 4.8 + 1.8) * 0.025);
        float3 ghost1 = tex2D(DiffuseSampler, uv + ghostOffset1).rgb;
        float3 ghost2 = tex2D(DiffuseSampler, uv + ghostOffset2).rgb;

        // The two ghosts carry the weight of the four of the desktop shader (0.5 + 0.4 + 0.3 + 0.2).
        float3 glowColor = originalColor * GlowColor * (4.4 * GlowIntensity * subtlePulse);
        glowColor += (ghost1 * 0.8 + ghost2 * 0.6) * (GlowIntensity * shimmer);
        glowColor += GlowColor * (glowEffect * extraGlow);
        color.rgb = glowColor;
    }

    color.rgb *= ShadowLightScale(input.ShadowCoord);
    color.rgb = lerp(color.rgb, FogColor, input.TexCoordFog.z);
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

technique MonsterMaterialDrawing
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
