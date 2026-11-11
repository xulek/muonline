// ItemMaterial.fx - Android (OpenGL ES) variant
//
// Built only by Content.Android.mgcb, under the same asset name as ../ItemMaterial.fx, which stays the shader
// of every desktop backend. Technique and parameter names match the OpenGL build of the desktop file.
//
// The translation to GLSL ES cannot branch around a texture fetch, so every fetch written in the pixel shader
// runs for every pixel. This variant therefore reads each chrome texture once (twice for Chrome02) with
// coordinates selected by the item state, instead of one fetch per upgrade tier and overlay mode. Sun light,
// fog and the shadow projection are evaluated per vertex; the sun shadow is a 2-tap point-sampled test.

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

int ItemOptions = 0;
float Time = 0;
float Alpha = 1.0;
bool IsAncient = false;
bool IsExcellent = false;
float4x4 LightViewProjection;
float2 ShadowMapTexelSize = float2(1.0 / 2048.0, 1.0 / 2048.0);
float ShadowBias = 0.0015;
float ShadowNormalBias = 0.0025;
float ShadowsEnabled = 0.0;
float ShadowStrength = 0.5;

float3 ItemChromeColor = float3(1.0, 0.5, 0.0);        // PartObjectColor
float4 ItemChrome2Tint = float4(1.0, 1.0, 1.0, 0.0);   // PartObjectColor2 (w=1: absolute colour)
float3 ItemAncientColor = float3(0.1, 0.6, 1.0);       // PartObjectColor3
float3 AmbientLight = float3(0.8, 0.8, 0.8);
float3 SunColor = float3(1.0, 0.95, 0.85);
float SunStrength = 0.8;

static const float3 LegacyLightVector = float3(0.0, -0.1, -0.8); // ZzzBMD.cpp LightVector

// Single additive RENDER_BRIGHT pass used for special wing meshes (RenderPartObjectBody):
//   1 = RENDER_CHROME  (Chrome01 slot, wave texture coordinates)
//   2 = RENDER_CHROME6 (Chrome02 slot, (n.z + n.x) * 0.8 + wave2 * 2)
//   3 = RENDER_TEXTURE (diffuse texture shifted by ChromeOverlayUvOffset)
//   4 = RENDER_TEXTURE with a clamped effect texture (Chrome02 slot) shifted by the offset
//   5 = RENDER_METAL (Shiny01)
//   6 = RENDER_CHROME4, 7 = RENDER_CHROME5 (Chrome02)
int ChromeOverlayMode = 0;
float3 ChromeOverlayColor = float3(1.0, 1.0, 1.0);
float2 ChromeOverlayUvOffset = float2(0.0, 0.0);
float ChromeOverlayAlpha = 1.0; // output alpha for alpha-blended overlays

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float3 Normal : NORMAL0;
    float2 TextureCoordinate : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position    : POSITION0;
    float3 Normal      : TEXCOORD0;
    float3 TexCoordFog : TEXCOORD1; // xy = uv, z = fog factor
    float3 Light       : TEXCOORD2; // ambient + sun, without the sun shadow
    float4 ShadowCoord : TEXCOORD3; // xy = uv * w, z = biased depth * w, w = clip w
};

VertexShaderOutput MainVS(in VertexShaderInput input)
{
    VertexShaderOutput output;
    float3 worldPos = mul(input.Position, World).xyz;
    float3 normal = normalize(mul(input.Normal, (float3x3)World) + float3(0.0, 0.0, 0.00001));

    // Same base lighting as DynamicLighting.fx so upgraded parts match the rest of the body.
    float ndotlRaw = dot(normal, -LightDirection);
    float ndotl = saturate(ndotlRaw) + saturate(-ndotlRaw) * 0.35;
    float shadowFactor = saturate(lerp(1.0 - ShadowStrength, 1.0, ndotl));

    float4 clip = mul(float4(worldPos, 1.0), LightViewProjection);
    float bias = ShadowBias + ShadowNormalBias * (1.0 - saturate(ndotlRaw));

    output.Position = mul(input.Position, WorldViewProjection);
    output.Normal = normal;
    output.TexCoordFog = float3(input.TextureCoordinate,
        saturate((length(worldPos - FogCameraPosition) - FogStart) / max(FogEnd - FogStart, 1.0)) * FogEnabled);
    output.Light = AmbientLight * shadowFactor + SunColor * (ndotl * SunStrength);
    output.ShadowCoord = float4(0.5 * clip.x + 0.5 * clip.w,
                                0.5 * clip.w - 0.5 * clip.y,
                                clip.z * 0.5 + clip.w * 0.5 - bias * clip.w,
                                clip.w);
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

// ---------------------------------------------------------------------------
// Port of SourceMain5.2 RenderPartObjectEffect (ZzzObject.cpp) and the chrome
// texture-coordinate generation of BMD::RenderMesh (ZzzBMD.cpp). The additive
// RENDER_BRIGHT passes of the original collapse into one shader: base + sum(passes).
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
float4 MainPS(VertexShaderOutput input) : COLOR0
{
    float3 normal = normalize(input.Normal);
    float2 uv = input.TexCoordFog.xy;
    float fog = input.TexCoordFog.z;

    float overlayMode = (float)ChromeOverlayMode;
    bool isOverlay = overlayMode > 0.5;

    float itemOptions = max(0.0, (float)ItemOptions);
    float level = itemOptions - floor(itemOptions * (1.0 / 16.0)) * 16.0;
    float lowChromeMode = floor(itemOptions * (1.0 / 32.0)) - floor(itemOptions * (1.0 / 128.0)) * 4.0;
    bool lowChrome = level < 3.0 && lowChromeMode > 0.5;
    bool lowChromeDouble = lowChrome && lowChromeMode < 1.5;

    float wave = frac(Time * 0.1);              // (WorldTime % 10000) * 0.0001
    float wave2 = frac(Time * 0.2) * 1.2 - 0.4; // (WorldTime % 5000) * 0.00024 - 0.4
    float3 movingLight = float3(cos(Time), sin(Time * 2.0), 1.0);
    float moving = dot(normal, movingLight);
    float legacy = dot(normal, LegacyLightVector);

    float2 chromeUv = float2(normal.z * 0.5 + wave, normal.y * 0.5 + wave * 2.0);                  // RENDER_CHROME
    float2 metalUv = float2(normal.z * 0.5 + 0.2, normal.y * 0.5 + 0.5);                           // RENDER_METAL
    float2 chrome2Uv = float2((normal.z + normal.x) * 0.8 + wave2 * 2.0,
                              (normal.y + normal.x) + wave2 * 3.0);                                // RENDER_CHROME2
    float2 chrome4Uv = float2(moving + normal.y * 0.5 + movingLight.y * 3.0,
                              1.0 - moving - (normal.z * 0.5 + wave * 3.0));                       // RENDER_CHROME4
    float2 chrome5Uv = float2(moving + normal.y * 3.0 + movingLight.y * 5.0,
                              1.0 - moving - (normal.z * 2.5 + wave));                             // RENDER_CHROME5

    // First Chrome02 fetch: the overlay pass, or the CHROME2 / CHROME4 pass of the item.
    float2 chrome02Uv = (level >= 13.0 || lowChrome) ? chrome4Uv : chrome2Uv;
    if (isOverlay)
    {
        chrome02Uv = overlayMode < 2.5 ? chrome2Uv.xx
                   : overlayMode < 4.5 ? uv + ChromeOverlayUvOffset
                   : overlayMode < 6.5 ? chrome4Uv
                   : chrome5Uv;
    }
    // Second Chrome02 fetch: the extra CHROME2 pass of low-level chrome items, else the ancient CHROME3 pass.
    float2 extraUv = lowChromeDouble ? chrome2Uv : float2(legacy, 1.0 - legacy);

    float2 diffuseUv = uv;
    if (isOverlay)
        diffuseUv += ChromeOverlayUvOffset;

    float4 diffuse = tex2D(DiffuseSampler, diffuseUv);
    float3 chrome01 = tex2D(Chrome01Sampler, chromeUv).rgb;
    float3 shiny01 = tex2D(Shiny01Sampler, metalUv).rgb;
    float3 chrome02 = tex2D(Chrome02Sampler, chrome02Uv).rgb;
    float3 chrome02Extra = tex2D(Chrome02Sampler, extraUv).rgb;

    // Overlay passes ignore the texture alpha (glBlendFunc(GL_ONE, GL_ONE)).
    clip(max(diffuse.a - 0.1, overlayMode - 0.5));

    // --- Overlay pass: added on top of the fogged body, so it fades out with the fog.
    float3 overlayRgb = chrome02;
    if (overlayMode < 1.5)
        overlayRgb = chrome01;
    else if (overlayMode > 2.5 && overlayMode < 3.5)
        overlayRgb = diffuse.rgb;
    else if (overlayMode > 4.5 && overlayMode < 5.5)
        overlayRgb = shiny01;
    float4 overlay = float4(overlayRgb * ChromeOverlayColor * (Alpha * (1.0 - fog)), ChromeOverlayAlpha);

    // --- Item body
    float3 light = input.Light * ShadowLightScale(input.ShadowCoord);
    float lum = sin(Time * 4.0) * 0.15 + 0.6;
    float3 bodyLight = light;
    if (level >= 3.0)
        bodyLight = light * float3(lum, lum * 0.6, lum * 0.6);
    if (level >= 5.0)
        bodyLight = light * float3(lum * 0.5, lum * 0.7, lum);
    if (level >= 7.0)
        bodyLight = light * 0.8;
    if (level >= 9.0)
        bodyLight = light * 0.9;

    float3 rgb = diffuse.rgb * bodyLight;

    if (lowChrome)
    {
        rgb += chrome02 * light;                    // RENDER_CHROME4
        if (lowChromeDouble)
            rgb += chrome02Extra * light * 2.25;    // RENDER_CHROME2, Bright 1.5^2
        else
            rgb += diffuse.rgb * light * 0.25;      // RENDER_TEXTURE | RENDER_BRIGHT
    }
    if (level >= 7.0)
        rgb += chrome01 * ItemChromeColor;
    if (level >= 9.0)
        rgb += shiny01 * ItemChromeColor;
    if (level >= 11.0)
        rgb += chrome02 * (ItemChrome2Tint.w > 0.5 ? ItemChrome2Tint.xyz : bodyLight * ItemChrome2Tint.xyz);

    if (IsExcellent == true)
    {
        float e = sin(Time * 2.0) * 0.5 + 0.5;
        rgb += diffuse.rgb * float3(e, e * 0.3, 1.0 - e);
    }
    else if (IsAncient == true && !lowChromeDouble)
    {
        rgb += chrome02Extra * ItemAncientColor * max(0.0, sin(Time) * 0.5 + 0.4);
    }

    float4 item = float4(lerp(rgb, FogColor, fog), diffuse.a * Alpha);
    return isOverlay ? overlay : item;
}

technique BasicColorDrawing_UpgradeFast
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}

technique BasicColorDrawing
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
