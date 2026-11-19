// SceneBloom.fx - glow, vignette and colour grade for a world rendered into its own target (Graphics/SceneBloom).
// Pixel shaders only, drawn with SpriteBatch:
//   Extract - bright parts of the scene (soft threshold), written into a half/quarter size target;
//   Blur    - 9-tap gaussian along TexelStep (run horizontally, then vertically);
//   Combine - scene + bloom, colour grade and vignette.
// - WindowsDX (SM4): Texture2D/SamplerState + ps_4_0
// - DesktopGL/OpenGL: sampler + tex2D + ps_3_0

float Threshold;      // luminance where glow starts
float Knee;           // softness of the threshold
float2 TexelStep;     // blur direction * texel size of the source
float BloomIntensity;
float3 BloomTint;     // colour of the glow (multiplies the bloom)
float Saturation;     // 1 = unchanged
float Contrast;       // 1 = unchanged
float3 Lift;          // added to the shadows (colour of the darkest parts)
float Vignette;       // 0 = off, 1 = strong darkening of the corners
float Sharpen;        // 0 = off; unsharp mask against the 4 direct neighbours (TexelStep = 1 / scene size)
float AoStrength;     // 0 = no ambient occlusion in the combine pass

// Ssao (Graphics/ScreenSpaceAO): source = camera depth written by the ShadowCaster techniques.
float2 ProjScale;     // 1 / Projection.M11, 1 / Projection.M22
float2 ProjDepth;     // Projection.M33, Projection.M43 (view distance = M43 / (depth + M33))
float AoRadius;       // world units
float AoProjRadius;   // AoRadius * M11 * 0.5 (uv radius = AoProjRadius / distance)
float AoIntensity;

// AoFilteredTemporal: s0 = raw AO, s1 (Bloom slot) = AO history, s2 (Ao slot) = this frame's depth,
// s3 = last frame's depth.
float4 ViewToPreviousClipX;
float4 ViewToPreviousClipY;
float4 ViewToPreviousClipW;
float HistoryWeight;  // 0 = no usable history (first frame, resize)

#if SM4 || SM6
Texture2D SceneTexture : register(t0);
SamplerState SceneSampler : register(s0);
Texture2D BloomTexture : register(t1);
SamplerState BloomSampler : register(s1);
#define SAMPLE_SCENE(uv) SceneTexture.Sample(SceneSampler, uv)
#define SAMPLE_BLOOM(uv) BloomTexture.Sample(BloomSampler, uv)
Texture2D AoTexture : register(t2);
SamplerState AoSampler : register(s2);
#define SAMPLE_AO(uv) AoTexture.Sample(AoSampler, uv)
Texture2D HistoryDepthTexture : register(t3);
SamplerState HistoryDepthSampler : register(s3);
#define SAMPLE_HISTORY_DEPTH(uv) HistoryDepthTexture.Sample(HistoryDepthSampler, uv)
#define PS_SIG(name) float4 name(float4 pos : SV_POSITION, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
#else
sampler SceneSampler : register(s0);
sampler BloomSampler : register(s1);
sampler AoSampler : register(s2);
#define SAMPLE_AO(uv) tex2D(AoSampler, uv)
sampler HistoryDepthSampler : register(s3);
#define SAMPLE_HISTORY_DEPTH(uv) tex2D(HistoryDepthSampler, uv)
#define SAMPLE_SCENE(uv) tex2D(SceneSampler, uv)
#define SAMPLE_BLOOM(uv) tex2D(BloomSampler, uv)
#define PS_SIG(name) float4 name(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
#endif

float3 ExtractHighlights(float3 c)
{
    float lum = max(c.r, max(c.g, c.b));
    float soft = clamp(lum - Threshold + Knee, 0.0, 2.0 * Knee);
    soft = soft * soft / (4.0 * Knee + 0.0001);
    float contribution = max(soft, lum - Threshold) / max(lum, 0.0001);
    return c * contribution;
}

PS_SIG(ExtractPS)
{
    // 4 taps: the target is smaller than the source, averaging avoids sparkling single pixels.
    float2 d = TexelStep * 0.5;
    float3 c = (SAMPLE_SCENE(uv + float2(-d.x, -d.y)).rgb + SAMPLE_SCENE(uv + float2(d.x, -d.y)).rgb +
                SAMPLE_SCENE(uv + float2(-d.x, d.y)).rgb + SAMPLE_SCENE(uv + float2(d.x, d.y)).rgb) * 0.25;
    return float4(ExtractHighlights(c), 1.0);
}

float3 SampleHalfResolutionHighlights(float2 uv)
{
    // A bilinear tap at the center of a 2x2 block reproduces the half-resolution average.
    // Quantize it as the intermediate SurfaceFormat.Color target did.
    return floor(saturate(ExtractHighlights(SAMPLE_SCENE(uv).rgb)) * 255.0 + 0.5) / 255.0;
}

PS_SIG(ExtractQuarterPS)
{
    float2 d = TexelStep;
    float3 c = (SampleHalfResolutionHighlights(uv + float2(-d.x, -d.y)) +
                SampleHalfResolutionHighlights(uv + float2( d.x, -d.y)) +
                SampleHalfResolutionHighlights(uv + float2(-d.x,  d.y)) +
                SampleHalfResolutionHighlights(uv + float2( d.x,  d.y))) * 0.25;
    return float4(ExtractHighlights(c), 1.0);
}

PS_SIG(BlurPS)
{
    float3 c = SAMPLE_SCENE(uv).rgb * 0.2270270270;
    c += SAMPLE_SCENE(uv + TexelStep * 1.3846153846).rgb * 0.3162162162;
    c += SAMPLE_SCENE(uv - TexelStep * 1.3846153846).rgb * 0.3162162162;
    c += SAMPLE_SCENE(uv + TexelStep * 3.2307692308).rgb * 0.0702702703;
    c += SAMPLE_SCENE(uv - TexelStep * 3.2307692308).rgb * 0.0702702703;
    return float4(c, 1.0);
}

PS_SIG(CombinePS)
{
    float3 scene = SAMPLE_SCENE(uv).rgb;
    {
        // Unsharp mask (taps are unconditional: ps_3_0 cannot branch around gradient samples; Sharpen 0 = no-op).
        float3 n = (SAMPLE_SCENE(uv + float2(TexelStep.x, 0.0)).rgb + SAMPLE_SCENE(uv - float2(TexelStep.x, 0.0)).rgb +
                    SAMPLE_SCENE(uv + float2(0.0, TexelStep.y)).rgb + SAMPLE_SCENE(uv - float2(0.0, TexelStep.y)).rgb) * 0.25;
        // Clamp to the neighbourhood so edges get crisper without bright halos.
        float3 lo = min(scene, n);
        float3 hi = max(scene, n);
        scene = clamp(scene + (scene - n) * Sharpen, lo * 0.85, hi * 1.15);
    }
    scene *= lerp(1.0, SAMPLE_AO(uv).r, AoStrength);
    float3 bloom = SAMPLE_BLOOM(uv).rgb * BloomTint * BloomIntensity;
    // Screen-like add: strong glow does not burn to flat white.
    float3 c = scene + bloom * (1.0 - saturate(scene) * 0.45);

    c += Lift * (1.0 - saturate(c));
    float lum = dot(c, float3(0.299, 0.587, 0.114));
    c = lerp(float3(lum, lum, lum), c, Saturation);
    c = (c - 0.5) * Contrast + 0.5;

    float2 v = (uv - 0.5) * float2(1.25, 1.0);
    float vig = 1.0 - Vignette * smoothstep(0.30, 0.85, dot(v, v) * 1.6);
    return float4(saturate(c * vig), 1.0);
}

float DepthToNdc(float d)
{
#if !(SM4 || SM6)
    d = d * 2.0 - 1.0; // ShadowPS remaps OpenGL depth into 0..1
#endif
    return d;
}

float ViewDistance(float d)
{
    return ProjDepth.y / (DepthToNdc(d) + ProjDepth.x);
}

float3 ViewPosition(float2 uv, float depth)
{
    float z = ViewDistance(depth);
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    return float3(ndc * ProjScale * z, -z);
}

float3 ViewPosition(float2 uv)
{
    return ViewPosition(uv, SAMPLE_SCENE(uv).r);
}

static const float2 AoKernel[8] =
{
    float2( 0.35,  0.00), float2(-0.25,  0.55), float2(-0.60, -0.30), float2( 0.55, -0.60),
    float2( 0.90,  0.25), float2(-0.10, -0.95), float2(-0.85,  0.50), float2( 0.30,  0.85)
};

// Filter the current occlusion before blending at the reprojected position. Reject history
// when its depth disagrees or the reprojected position falls off screen.
PS_SIG(AoFilteredTemporalPS)
{
    // Four bilinear taps cover the same 4x4 box as the separable blur (offsets -2..+1 on each axis).
    float2 low = uv - TexelStep * 1.5;
    float2 high = uv + TexelStep * 0.5;
    float current = (SAMPLE_SCENE(low).r + SAMPLE_SCENE(float2(high.x, low.y)).r +
                     SAMPLE_SCENE(float2(low.x, high.y)).r + SAMPLE_SCENE(high).r) * 0.25;
    [branch] if (HistoryWeight <= 0.0)
        return float4(current, current, current, 1.0);

    float d = SAMPLE_AO(uv).r;
    [branch] if (d >= 0.99999)
        return float4(current, current, current, 1.0);

    float4 viewPos = float4(ViewPosition(uv, d), 1.0);
    float prevW = dot(viewPos, ViewToPreviousClipW);
    if (prevW <= 0.001)
        return float4(current, current, current, 1.0);

    float2 prevClipXY = float2(dot(viewPos, ViewToPreviousClipX), dot(viewPos, ViewToPreviousClipY));
    float2 prevUv = prevClipXY / prevW * float2(0.5, -0.5) + 0.5;
    if (any(prevUv < 0.0) || any(prevUv > 1.0))
        return float4(current, current, current, 1.0);

    float prevDist = ViewDistance(SAMPLE_HISTORY_DEPTH(prevUv).r);
    float expected = prevW;
    float agree = saturate(1.0 - abs(prevDist - expected) / (expected * 0.03 + 4.0));

    float history = SAMPLE_BLOOM(prevUv).r;
    float ao = lerp(current, history, HistoryWeight * agree);
    return float4(ao, ao, ao, 1.0);
}

PS_SIG(SsaoPS)
{
    float d = SAMPLE_SCENE(uv).r;
    if (d >= 0.99999)
        return float4(1.0, 1.0, 1.0, 1.0); // sky / nothing drawn

    float3 p = ViewPosition(uv, d);
    // Normal from the neighbour on the same surface (smaller depth step) on each axis, so silhouettes do not
    // produce a different normal every time they move by a pixel.
    float3 pr = ViewPosition(uv + float2(TexelStep.x, 0.0));
    float3 pl = ViewPosition(uv - float2(TexelStep.x, 0.0));
    float3 pd = ViewPosition(uv + float2(0.0, TexelStep.y));
    float3 pu = ViewPosition(uv - float2(0.0, TexelStep.y));
    float3 dx = abs(pr.z - p.z) < abs(p.z - pl.z) ? pr - p : p - pl;
    float3 dy = abs(pd.z - p.z) < abs(p.z - pu.z) ? pd - p : p - pu;
    float3 n = normalize(cross(dx, dy));
    if (dot(n, -p) < 0.0)
        n = -n;

    float dist = -p.z;
    float2 radiusUv = AoProjRadius / dist * float2(1.0, ProjScale.x / ProjScale.y);
    // Interleaved 4x4 rotation pattern (16 angles over one kernel step); the filter averages one 4x4 block,
    // so the pattern cancels out instead of shimmering like per-pixel random noise when the camera moves.
    float2 pix = floor(uv / TexelStep);
    float cell = fmod(pix.x, 4.0) * 4.0 + fmod(pix.y, 4.0);
    float a = cell * (6.2831853 / 128.0) + fmod(pix.x + pix.y * 2.0, 4.0) * (6.2831853 / 32.0);
    float s = sin(a);
    float c = cos(a);
    float r2 = AoRadius * AoRadius * 4.0;

    float occlusion = 0.0;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float2 k = AoKernel[i];
        k = float2(k.x * c - k.y * s, k.x * s + k.y * c);
        float3 v = ViewPosition(uv + k * radiusUv) - p;
        float len2 = dot(v, v);
        float cosine = dot(v, n) * rsqrt(len2 + 1.0);
        occlusion += saturate(cosine - 0.1) * saturate(1.0 - len2 / r2);
    }

    float ao = saturate(1.0 - AoIntensity * occlusion * 0.125);
    return float4(ao, ao, ao, 1.0);
}

#if SM6
    #define BLOOM_PS ps_6_0
#elif SM4
    #define BLOOM_PS ps_4_0
#else
    #define BLOOM_PS ps_3_0
#endif

technique Extract { pass P0 { PixelShader = compile BLOOM_PS ExtractPS(); } }
technique ExtractQuarter { pass P0 { PixelShader = compile BLOOM_PS ExtractQuarterPS(); } }
technique Blur { pass P0 { PixelShader = compile BLOOM_PS BlurPS(); } }
technique Combine { pass P0 { PixelShader = compile BLOOM_PS CombinePS(); } }
technique Ssao { pass P0 { PixelShader = compile BLOOM_PS SsaoPS(); } }
technique AoFilteredTemporal { pass P0 { PixelShader = compile BLOOM_PS AoFilteredTemporalPS(); } }
