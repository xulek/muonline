// HudOrb.fx - liquid resource orb of the Modern HUD (health / mana), drawn with SpriteBatch.
// Pixel shader only: SpriteBatch keeps its own vertex shader. The sprite's texture is a tileable grayscale
// noise (Interface/Ornate/Hud/liquid), drawn with a wrapping sampler over the orb's square.
// - WindowsDX (SM4): Texture2D/SamplerState + ps_4_0_level_9_3
// - DesktopGL/OpenGL: sampler + tex2D + ps_3_0

float Fill;          // 0..1 liquid level
float Trail;         // 0..1 level of recently lost resource (drawn pale above the liquid while it drains)
float Time;          // seconds
float Pulse;         // extra brightness (low resource warning), 0..~0.4
float EdgeSoftness;  // anti-aliasing width of the sphere edge in normalized units (about 2.5 / diameter)
float3 DeepColor;    // liquid at the bottom
float3 BrightColor;  // liquid near the surface, meniscus and bubbles
float3 TrailColor;   // lost-resource band

#if SM4 || SM6
Texture2D NoiseTexture : register(t0);
SamplerState NoiseSampler : register(s0);
#define SAMPLE_NOISE(uv) NoiseTexture.Sample(NoiseSampler, uv).r
#else
sampler NoiseSampler : register(s0);
#define SAMPLE_NOISE(uv) tex2D(NoiseSampler, uv).r
#endif

float Bubble(float2 d, float seed, float below)
{
    float x = frac(sin(seed * 12.9898) * 43758.5453) * 1.1 - 0.55;
    float speed = 0.10 + frac(seed * 0.618) * 0.08;
    float y = 0.95 - frac(Time * speed + seed * 0.37) * 1.9;
    float2 p = float2(x + sin(Time * 2.3 + seed * 4.0) * 0.035, y);
    float size = 0.022 + frac(seed * 0.31) * 0.02;
    float ring = smoothstep(size, size * 0.55, length(d - p));
    return ring * saturate(below * 30.0);
}

float4 ShadeOrb(float2 uv, float4 tint)
{
    float2 d = uv * 2.0 - 1.0;            // -1..1, y grows downward
    float r = length(d);
    float coverage = saturate((1.0 - r) / EdgeSoftness);

    // Liquid surface with two travelling waves; calm when the orb is full or empty.
    float calm = saturate(Fill * (1.0 - Fill) * 8.0);
    float wave = (sin(d.x * 5.5 + Time * 2.1) * 0.032 + sin(d.x * 11.0 - Time * 3.4) * 0.014) * calm;
    float surface = 1.0 - 2.0 * Fill + wave;
    float trailSurface = 1.0 - 2.0 * max(Trail, Fill) + wave * 0.6;
    float below = d.y - surface;           // > 0 inside the liquid

    float n1 = SAMPLE_NOISE(uv * 0.9 + float2(Time * 0.021, Time * 0.055));
    float n2 = SAMPLE_NOISE(uv * 1.7 + float2(-Time * 0.037, Time * 0.083));
    float n = saturate(n1 * 0.7 + n2 * 0.6 - 0.15);

    float depth = saturate(below / max(0.05, 1.0 - surface));
    float3 liquid = lerp(BrightColor, DeepColor, saturate(depth * 0.85 + 0.1));
    liquid *= 0.65 + n * 0.7;
    liquid += BrightColor * 0.45 * exp(-max(below, 0.0) * 22.0);

    float bubbles = Bubble(d, 1.0, below) + Bubble(d, 2.0, below) + Bubble(d, 3.0, below) + Bubble(d, 4.0, below);
    liquid += BrightColor * bubbles * 0.55;

    float inLiquid = smoothstep(-0.012, 0.012, below);
    float inTrail = smoothstep(-0.012, 0.012, d.y - trailSurface) * (1.0 - inLiquid);

    // Empty glass: very dark tinted, with faint drifting smoke.
    float3 col = DeepColor * 0.22 + float3(0.03, 0.03, 0.04) + DeepColor * n * 0.35;
    // Lost amount: a dim, washed-out band clearly unlike the liquid, with slow diagonal streaks.
    float streak = 0.75 + 0.25 * sin((d.x + d.y) * 40.0 - Time * 3.0);
    col = lerp(col, TrailColor * (0.30 + n * 0.12) * streak, inTrail * 0.75);
    col = lerp(col, liquid, inLiquid);

    // Bright meniscus line on the surface (only while partly filled).
    float partly = step(0.002, Fill) * step(Fill, 0.998);
    col += BrightColor * 0.75 * exp(-abs(below) * 85.0) * partly;

    // Glass sphere: darker rim, soft top-left highlight, faint bottom reflection.
    col *= 1.0 - pow(r, 4.0) * 0.6;
    float2 h = (d - float2(-0.30, -0.50)) * float2(1.0, 1.75);
    col += 0.38 * smoothstep(0.45, 0.0, length(h));
    float2 b = (d - float2(0.0, 0.78)) * float2(0.9, 3.0);
    col += BrightColor * 0.10 * smoothstep(0.6, 0.0, length(b));

    col *= 1.0 + Pulse;

    float a = coverage * tint.a;
    return float4(col * a, a);
}

#if SM4 || SM6
struct PS_IN { float4 Pos : SV_POSITION; float4 Color : COLOR0; float2 Tex : TEXCOORD0; };

float4 PS_Main(PS_IN pin) : SV_Target
{
    return ShadeOrb(pin.Tex, pin.Color);
}

#if SM6
    #define ORB_PS_MODEL ps_6_0
#else
    #define ORB_PS_MODEL ps_4_0_level_9_3
#endif

technique HudOrb
{
    pass P0
    {
        PixelShader = compile ORB_PS_MODEL PS_Main();
    }
}
#else
struct PS_IN { float4 Color : COLOR0; float2 Tex : TEXCOORD0; };

float4 PS_Main(PS_IN pin) : COLOR0
{
    return ShadeOrb(pin.Tex, pin.Color);
}

technique HudOrb
{
    pass P0
    {
        PixelShader = compile ps_3_0 PS_Main();
    }
}
#endif
