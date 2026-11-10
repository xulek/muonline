// SceneWater.fx - animated water of the character selection scene, drawn additively over the world.
//   Waterfall - the waterfall sheets: two layers of the original texture scrolling at different speeds with a
//               sideways wobble, glints on bright streaks, foam at the lip and a fade into mist at the bottom.
//               Vertex colour: G = fall progress (0 at the lip, 1 at the bottom), A = edge fade.
//   Pool      - caustic light moving over the water pools (procedural, world space), with sparkles.
//               Vertex colour: A = fade towards the pool shore.
// - WindowsDX (SM4): vs/ps_4_0
// - DesktopGL/OpenGL: vs/ps_3_0

float4x4 WorldViewProjection;
float Time;
float Intensity;
float3 Tint;
float3 FoamColor;
float ScrollA;
float ScrollB;
float2 PathStart;     // Sheen: start of the light path on the water (world xy)
float2 PathDir;       // Sheen: unit direction towards the light
float PathLength;
float2 PathWidth;     // Sheen: half width near, far

// The texture scrolls: wrapping must come from the effect itself (a device sampler state set from C# is replaced
// by the effect's own when it is applied, which clamped the scrolled coordinates to the edge row).
#if SM6
Texture2D WaterTexture : register(t0);
SamplerState WaterSampler : register(s0)
{
    Filter = Linear;
    AddressU = Wrap;
    AddressV = Wrap;
};
#define SAMPLE_WATER(uv) WaterTexture.Sample(WaterSampler, uv)
#else
Texture2D WaterTexture;
sampler WaterSampler = sampler_state
{
    Texture = <WaterTexture>;
    AddressU = Wrap;
    AddressV = Wrap;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
};
#define SAMPLE_WATER(uv) tex2D(WaterSampler, uv)
#endif

#if SM4 || SM6
#define POSITION_OUT SV_POSITION
#define TARGET_OUT SV_Target
#else
#define POSITION_OUT POSITION0
#define TARGET_OUT COLOR0
#endif

struct VS_IN { float4 Position : POSITION0; float4 Color : COLOR0; float2 Tex : TEXCOORD0; };
struct VS_OUT { float4 Position : POSITION_OUT; float4 Color : COLOR0; float2 Tex : TEXCOORD0; float2 World : TEXCOORD1; };

VS_OUT WaterVS(VS_IN vin)
{
    VS_OUT o;
    o.Position = mul(vin.Position, WorldViewProjection);
    o.Color = vin.Color;
    o.Tex = vin.Tex;
    o.World = vin.Position.xy;
    return o;
}

float Hash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float4 WaterfallPS(VS_OUT pin) : TARGET_OUT
{
    float2 uv = pin.Tex;
    float progress = pin.Color.g;
    float t = Time;

    float wobble = sin(uv.y * 17.0 + t * 3.1) * 0.010 + sin(uv.y * 6.0 - t * 1.7 + uv.x * 5.0) * 0.008;
    float3 a = SAMPLE_WATER(float2(uv.x + wobble, uv.y - t * ScrollA)).rgb;
    float3 b = SAMPLE_WATER(float2(uv.x * 1.3 - wobble * 0.7 + 0.31, uv.y * 0.8 - t * ScrollB)).rgb;
    float3 c = a * 0.55 + b * 0.45;

    // Bright streaks catch light.
    float lum = dot(c, float3(0.3, 0.5, 0.2));
    c += pow(saturate(lum * 1.5), 3.0) * float3(0.75, 0.95, 1.0) * 0.35;

    // Foam where the water tips over the edge, churning with the faster layer.
    float churn = SAMPLE_WATER(float2(uv.x * 2.1 + 0.17, uv.y * 2.7 - t * ScrollA * 1.8)).g;
    float lip = saturate(1.0 - progress * 7.0);
    c += FoamColor * lip * (0.2 + churn * 0.5);

    // Thins out into mist at the bottom.
    float bottom = saturate((1.0 - progress) * 3.5);
    float fade = pin.Color.a * bottom;
    return float4(c * Tint * Intensity * fade, 1.0);
}

// Caustic web: a few rounds of domain warping, the bright lines are where the warped field folds.
float Caustic(float2 p, float t)
{
    float2 i = p;
    float c = 1.0;
    const float inten = 0.005;
    [unroll] for (int n = 0; n < 4; n++)
    {
        float tt = t * (1.0 - (3.5 / (n + 1.0)));
        i = p + float2(cos(tt - i.x) + sin(tt + i.y), sin(tt - i.y) + cos(tt + i.x));
        c += 1.0 / length(float2(p.x / (sin(i.x + tt) / inten), p.y / (cos(i.y + tt) / inten)));
    }
    c /= 4.0;
    c = 1.17 - pow(abs(c), 1.4);
    return pow(abs(c), 8.0);
}

float4 PoolPS(VS_OUT pin) : TARGET_OUT
{
    float2 p = pin.World * 0.0042;
    float t = Time * 0.45;
    float web = Caustic(p * 6.2831, t);
    float web2 = Caustic(p * 3.7 + float2(1.7, 4.3), t * 0.8 + 3.0);
    float light = saturate(web * 0.9 + web2 * 0.5);

    // Sparkles: a few cells per tile light up briefly.
    float2 cell = floor(pin.World * 0.03);
    float h = Hash(cell);
    float phase = frac(Time * (0.15 + h * 0.25) + h * 7.0);
    float2 local = frac(pin.World * 0.03) - 0.5;
    float spark = smoothstep(0.09, 0.0, length(local)) * smoothstep(0.93, 1.0, phase) * step(0.82, h);

    float3 c = Tint * light + FoamColor * spark * 2.5;
    return float4(c * Intensity * pin.Color.a, 1.0);
}

float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash(i);
    float b = Hash(i + float2(1.0, 0.0));
    float c = Hash(i + float2(0.0, 1.0));
    float e = Hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, e, f.x), f.y);
}

// Moonlight on open water: a band from the viewer towards the light, where moving wave crests flash.
float4 SheenPS(VS_OUT pin) : TARGET_OUT
{
    float2 d = pin.World - PathStart;
    float along = dot(d, PathDir) / PathLength;
    float across = dot(d, float2(-PathDir.y, PathDir.x));
    float width = lerp(PathWidth.x, PathWidth.y, saturate(along));
    float band = exp(-(across * across) / (width * width) * 2.0);
    band *= smoothstep(0.0, 0.08, along) * smoothstep(1.02, 0.8, along);

    // Glitter: many small glints. Two drifting layers of value noise multiplied, so a glint shows only where both
    // peak; stretched across the path, so on screen they are short horizontal flecks. A finer third layer makes them
    // twinkle as the waves move.
    float t = Time;
    float alongDist = dot(d, PathDir);
    float2 q = float2(across * 0.0088, alongDist * 0.027);
    float n1 = ValueNoise(q + float2(t * 0.35, t * 0.55));
    float n2 = ValueNoise(q * float2(1.6, 1.35) + float2(-t * 0.3, t * 0.42) + 17.3);
    float n3 = ValueNoise(q * float2(2.7, 3.1) + float2(t * 1.1, -t * 0.9) + 41.1);
    float w = n1 * n2;
    float glint = pow(saturate(w * 1.9 - 0.62), 3.0) * (0.35 + n3 * 1.1);
    // Far away the flecks get smaller than a pixel: let them merge into a soft sheen instead of sparkling noise.
    float far = smoothstep(0.55, 1.0, along);
    float3 c = Tint * band * (0.035 + lerp(glint * 3.6, w * 0.28, far));
    return float4(c * Intensity * pin.Color.a, 1.0);
}

#if SM6
    #define WATER_VS vs_6_0
    #define WATER_PS ps_6_0
#elif SM4
    #define WATER_VS vs_4_0
    #define WATER_PS ps_4_0
#else
    #define WATER_VS vs_3_0
    #define WATER_PS ps_3_0
#endif

technique Waterfall
{
    pass P0
    {
        VertexShader = compile WATER_VS WaterVS();
        PixelShader = compile WATER_PS WaterfallPS();
    }
}

technique Pool
{
    pass P0
    {
        VertexShader = compile WATER_VS WaterVS();
        PixelShader = compile WATER_PS PoolPS();
    }
}

technique Sheen
{
    pass P0
    {
        VertexShader = compile WATER_VS WaterVS();
        PixelShader = compile WATER_PS SheenPS();
    }
}
