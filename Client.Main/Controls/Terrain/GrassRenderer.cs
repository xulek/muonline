using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using static Client.Main.Utils;
using Client.Main.Content;

namespace Client.Main.Controls.Terrain
{
    /// <summary>
    /// Renders grass tufts on the terrain.
    /// </summary>
    public class GrassRenderer
    {
        // Grass density distances (squared)
        private const float GrassNearSq = 3000f * 3000f;   // full density
        private const float GrassMidSq = 4000f * 4000f;   // two tufts
        private const float GrassFarSq = 5000f * 5000f;   // one tuft
        private const int GrassBatchQuads = 16384;       // 4096 tufts per batch
        private const int GrassBatchVerts = GrassBatchQuads * 6;

        private readonly GraphicsDevice _graphicsDevice;
        private readonly TerrainData _data;
        private readonly TerrainPhysics _physics;
        private readonly WindSimulator _wind;

        private Texture2D _grassSpriteTexture;
        private AlphaTestEffect _grassEffect;

        private readonly VertexPositionColorTexture[] _grassBatch = new VertexPositionColorTexture[GrassBatchVerts];
        private int _grassBatchCount = 0;

        public float GrassBrightness { get; set; } = 2f;
        public HashSet<byte> GrassTextureIndices { get; } = new() { 0 };
        public int Flushes { get; private set; }
        public int DrawnTriangles { get; private set; }

        public GrassRenderer(GraphicsDevice graphicsDevice, TerrainData data, TerrainPhysics physics, WindSimulator wind)
        {
            _graphicsDevice = graphicsDevice;
            _data = data;
            _physics = physics;
            _wind = wind;
        }

        public async void LoadContent(short worldIndex)
        {
            if (Constants.DRAW_GRASS)
            {
                var grassSpritePath = Path.Combine($"World{worldIndex}", "TileGrass01.ozt");
                try
                {
                    _grassSpriteTexture = await TextureLoader.Instance.PrepareAndGetTexture(grassSpritePath);
                    if (_grassSpriteTexture != null)
                        PremultiplyAlpha(_grassSpriteTexture);
                    else
                        Console.WriteLine($"Warning: Could not load grass sprite texture: {grassSpritePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading grass sprite texture '{grassSpritePath}': {ex.Message}");
                }

                _grassEffect = GraphicsManager.Instance.AlphaTestEffect3D;
            }
        }

        public void ResetMetrics()
        {
            Flushes = 0;
            DrawnTriangles = 0;
        }

        public void RenderGrassForTile(int xi, int yi, float xf, float yf, float lodFactor, short worldIndex)
        {
            if (!Constants.DRAW_GRASS
                || worldIndex == 11
                || _grassSpriteTexture == null)
                return;

            byte baseTex = _physics.GetBaseTextureIndexAt(xi, yi);
            if (!GrassTextureIndices.Contains(baseTex))
                return;

            var camPos = Camera.Instance.Position;
            float tileCx = (xf + 0.5f * lodFactor) * Constants.TERRAIN_SCALE;
            float tileCy = (yf + 0.5f * lodFactor) * Constants.TERRAIN_SCALE;
            float dx = camPos.X - tileCx, dy = camPos.Y - tileCy;
            float distSq = dx * dx + dy * dy;

            int grassPerTile = GrassCount(distSq);
            if (grassPerTile == 0) return;

            int terrainIndex = yi * Constants.TERRAIN_SIZE + xi;
            var tileLight = terrainIndex < _data.FinalLightMap.Length
                          ? _data.FinalLightMap[terrainIndex]
                          : Color.White;
            float windBase = _wind.GetWindValue(xi, yi);

            const float GrassUWidth = 0.30f;
            const float ScaleMin = 1.0f;
            const float ScaleMax = 3.0f;
            const float RotJitterDeg = 90f;
            const float HeightOffset = 55f;

            for (int i = 0; i < grassPerTile; i++)
            {
                float u0 = PseudoRandom(xi, yi, 123 + i) * (1f - GrassUWidth);
                float u1 = u0 + GrassUWidth;
                float halfUV = GrassUWidth * 0.5f;
                float maxOffset = 0.5f - halfUV;

                float rx = (PseudoRandom(xi, yi, 17 + i) * 2f - 1f) * maxOffset;
                float ry = (PseudoRandom(xi, yi, 91 + i) * 2f - 1f) * maxOffset;

                float worldX = (xf + 0.5f * lodFactor + rx * lodFactor) * Constants.TERRAIN_SCALE;
                float worldY = (yf + 0.5f * lodFactor + ry * lodFactor) * Constants.TERRAIN_SCALE;
                float h = _physics.RequestTerrainHeight(worldX, worldY);

                float scale = MathHelper.Lerp(ScaleMin, ScaleMax, PseudoRandom(xi, yi, 33 + i));
                float jitter = MathHelper.ToRadians((PseudoRandom(xi, yi, 57 + i) - 0.5f) * 2f * RotJitterDeg);
                float windZ = MathHelper.ToRadians(windBase * 0.05f) + jitter;

                RenderGrassQuad(
                    new Vector3(worldX, worldY, h + HeightOffset),
                    lodFactor * scale,
                    windZ,
                    tileLight,
                    u0, u1
                );
            }
        }

        private void RenderGrassQuad(
            Vector3 position,
            float lodFactor,
            float windRotationZ,
            Color lightColor,
            float u0,
            float u1)
        {
            const float BaseW = 130f, BaseH = 30f;
            float w = BaseW * (u1 - u0) * lodFactor;
            float h = BaseH * lodFactor;
            float hw = w * 0.5f;

            var p1 = new Vector3(-hw, 0, 0);
            var p2 = new Vector3(hw, 0, 0);
            var p3 = new Vector3(-hw, 0, h);
            var p4 = new Vector3(hw, 0, h);

            var t1 = new Vector2(u0, 1);
            var t2 = new Vector2(u1, 1);
            var t3 = new Vector2(u0, 0);
            var t4 = new Vector2(u1, 0);

            var world = Matrix.CreateRotationZ(MathHelper.ToRadians(45f) + windRotationZ)
                      * Matrix.CreateTranslation(position);

            var wp1 = Vector3.Transform(p1, world);
            var wp2 = Vector3.Transform(p2, world);
            var wp3 = Vector3.Transform(p3, world);
            var wp4 = Vector3.Transform(p4, world);

            var finalColor = new Color(
                (byte)Math.Min(lightColor.R * GrassBrightness, 255f),
                (byte)Math.Min(lightColor.G * GrassBrightness, 255f),
                (byte)Math.Min(lightColor.B * GrassBrightness, 255f)
            );

            if (_grassBatchCount + 6 >= GrassBatchVerts)
                Flush();

            _grassBatch[_grassBatchCount++] = new VertexPositionColorTexture(wp1, finalColor, t1);
            _grassBatch[_grassBatchCount++] = new VertexPositionColorTexture(wp2, finalColor, t2);
            _grassBatch[_grassBatchCount++] = new VertexPositionColorTexture(wp3, finalColor, t3);
            _grassBatch[_grassBatchCount++] = new VertexPositionColorTexture(wp2, finalColor, t2);
            _grassBatch[_grassBatchCount++] = new VertexPositionColorTexture(wp4, finalColor, t4);
            _grassBatch[_grassBatchCount++] = new VertexPositionColorTexture(wp3, finalColor, t3);
        }

        public void Flush()
        {
            if (!Constants.DRAW_GRASS
                || _grassBatchCount == 0
                || _grassSpriteTexture == null
                || _grassEffect == null) // Added null check for _grassEffect
                return;

            var dev = _graphicsDevice;
            var prevBlend = dev.BlendState;
            var prevDepth = dev.DepthStencilState;
            var prevRaster = dev.RasterizerState;
            var prevSampler = dev.SamplerStates[0];

            dev.BlendState = BlendState.AlphaBlend;
            dev.DepthStencilState = DepthStencilState.Default;
            dev.RasterizerState = RasterizerState.CullNone;
            dev.SamplerStates[0] = SamplerState.PointClamp;

            _grassEffect.World = Matrix.Identity;
            _grassEffect.View = Camera.Instance.View;
            _grassEffect.Projection = Camera.Instance.Projection;
            _grassEffect.Texture = _grassSpriteTexture;
            _grassEffect.AlphaFunction = CompareFunction.Greater;
            _grassEffect.ReferenceAlpha = 64;
            _grassEffect.VertexColorEnabled = true;

            int triCount = _grassBatchCount / 3;
            if (_grassEffect.CurrentTechnique == null) return; // Added null check for CurrentTechnique
            foreach (var pass in _grassEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                dev.DrawUserPrimitives(PrimitiveType.TriangleList, _grassBatch, 0, triCount);
                DrawnTriangles += triCount;
            }

            Flushes++;
            _grassBatchCount = 0;

            dev.BlendState = prevBlend;
            dev.DepthStencilState = prevDepth;
            dev.RasterizerState = prevRaster;
            dev.SamplerStates[0] = prevSampler;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GrassCount(float distSq)
        {
            if (distSq < GrassNearSq) return 6;
            if (distSq < GrassMidSq) return 2;
            if (distSq < GrassFarSq) return 1;
            return 0;
        }

        private static void PremultiplyAlpha(Texture2D tex)
        {
            if (tex.Format != SurfaceFormat.Color || tex.IsDisposed) return;
            int len = tex.Width * tex.Height;
            var px = new Color[len];
            tex.GetData(px);
            for (int i = 0; i < len; i++)
            {
                var c = px[i];
                if (c.A == 255) continue;
                px[i] = new Color(
                    (byte)(c.R * c.A / 255),
                    (byte)(c.G * c.A / 255),
                    (byte)(c.B * c.A / 255),
                    c.A);
            }
            tex.SetData(px);
        }

        // 32-bit Xorshift* hash → float [0..1]
        private static float PseudoRandom(int x, int y, int salt = 0)
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
            h ^= h >> 13; h *= 0x165667B1u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }
}
