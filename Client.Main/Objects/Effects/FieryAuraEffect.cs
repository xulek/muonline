#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Persistent fiery cloud aura reusable across monsters/effects.
    /// Tuned for low CPU/GPU overhead while keeping chaotic fire motion.
    /// </summary>
    public sealed class FieryAuraEffect : EffectObject
    {
        private const string PrimaryTexturePath = "Effect/Flame01.jpg";
        private const string SecondaryTexturePath = "Effect/firehik01.jpg";
        private const string GlowTexturePath = "Effect/flare.jpg";

        private const int MaxParticles = 72;
        private const int CoreCloudQuads = 3;
        private const int MaxQuads = MaxParticles + CoreCloudQuads;
        private const float MediumLodDistance = 1700f;
        private const float LowLodDistance = 2800f;
        private const float HardCullDistance = 4200f;
        private const float MaxParticleLocalRange = 360f;
        private const float TeleportResetDistance = 900f;
        private const float MaxParentOffsetDistance = 420f;
        private const int OutlierDrawSuppressFrames = 2;
        private const int MinStableFramesToDraw = 2;
        private const float WorldBoundsMargin = 320f;
        private const float WorldMinZ = -1500f;
        private const float WorldMaxZ = 8000f;
        private const float AttachedModelDepthOffset = 65f;
        private const int BatchInitialQuads = 512;
        private const int BatchMaxVertices = short.MaxValue - 4;
        private const int DensityFullAurasPerTile = 1;
        private const int DensityReducedAurasPerTile = 2;
        private const float DensityReducedParticleFactor = 0.08f;
        private const float DensityReducedSpawnMultiplier = 0.06f;
        private const float DensityReducedAlphaScale = 0.25f;

        private const float SpawnRate = 52f;
        private const float LifeMin = 0.72f;
        private const float LifeMax = 1.45f;
        private const float WidthMin = 64f;
        private const float WidthMax = 120f;
        private const float HeightRatioMin = 1.2f;
        private const float HeightRatioMax = 1.85f;
        private const float RadiusX = 46f;
        private const float RadiusY = 38f;
        private const float HeightMin = 10f;
        private const float HeightMax = 128f;
        private const float RiseSpeedMin = 46f;
        private const float RiseSpeedMax = 108f;

        private readonly VertexPositionColorTexture[] _vertices = new VertexPositionColorTexture[MaxQuads * 4];
        private static readonly short[] QuadIndices = QuadIndexCache.Get(MaxQuads);

        private readonly FireParticle[] _particles = new FireParticle[MaxParticles];
        private int _particleCount;
        private float _spawnTimer;
        private float _time;
        private float _fade = 1f;
        private bool _active = true;
        private readonly float _qualityScale;
        private readonly bool _enableDynamicLight;
        private readonly int _maxConfiguredParticles;
        private int _particleTarget;
        private float _spawnMultiplier = 1f;
        private int _particleStride = 1;
        private bool _drawSecondary = true;
        private float _lightScale = 1f;
        private float _cameraDistSq = float.MaxValue;
        private bool _skipDrawing;
        private bool _densityCulled;
        private float _densityAlphaScale = 1f;
        private int _lowLodFrameGate;
        private Vector3 _lastWorldPosition;
        private bool _hasLastWorldPosition;
        private int _suppressDrawFrames;
        private int _stableFrameCount;

        private Texture2D _primaryTexture = null!;
        private Texture2D _secondaryTexture = null!;
        private Texture2D _glowTexture = null!;

        private readonly DynamicLight _auraLight;
        private bool _lightAdded;

        private static readonly BatchGeometry PrimaryBatch = new(BatchInitialQuads);
        private static readonly BatchGeometry SecondaryBatch = new(BatchInitialQuads);
        private static readonly BatchGeometry GlowBatch = new(BatchInitialQuads / 4);
        private static int _batchedAurasThisFrame;
        private static int _batchFlushesThisFrame;
        private static int _batchDrawCallsThisFrame;
        private static int _batchQuadsThisFrame;
        private static readonly Dictionary<long, int> DensityTileCounts = new(128);
        private static readonly List<FieryAuraEffect> LateWorldDrawQueue = new(64);
        private static long _densityUpdateTicks = -1;
        private static int _densityFullThisTick;
        private static int _densityReducedThisTick;
        private static int _densityCulledThisTick;

        public static int LastFrameBatchedAuras { get; private set; }
        public static int LastFrameBatchFlushes { get; private set; }
        public static int LastFrameBatchDrawCalls { get; private set; }
        public static int LastFrameBatchQuads { get; private set; }
        public static int LastFrameDensityFull { get; private set; }
        public static int LastFrameDensityReduced { get; private set; }
        public static int LastFrameDensityCulled { get; private set; }

        private struct FireParticle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public float Life;
            public float Width;
            public float Height;
            public float Phase;
            public byte TextureVariant;
        }

        private sealed class BatchGeometry
        {
            public VertexPositionColorTexture[] Vertices;
            public short[] Indices;
            public int VertexCount;
            public int IndexCount;
            public Texture2D? Texture;

            public BatchGeometry(int initialQuads)
            {
                int quads = Math.Max(1, initialQuads);
                Vertices = new VertexPositionColorTexture[quads * 4];
                Indices = QuadIndexCache.Get(quads);
            }

            public bool HasGeometry => IndexCount > 0;

            public bool NeedsFlush(Texture2D texture, int quadCount)
            {
                if (quadCount <= 0)
                    return false;

                return (Texture != null && !ReferenceEquals(Texture, texture)) ||
                       VertexCount + quadCount * 4 > BatchMaxVertices;
            }

            public void EnsureCapacity(int vertexCount, int indexCount)
            {
                if (Vertices.Length < vertexCount)
                    Array.Resize(ref Vertices, Math.Max(vertexCount, Vertices.Length * 2));

                if (Indices.Length < indexCount)
                {
                    int currentQuads = Math.Max(1, Indices.Length / 6);
                    int requiredQuads = (indexCount + 5) / 6;
                    Indices = QuadIndexCache.Get(Math.Max(requiredQuads, currentQuads * 2));
                }
            }

            public void Clear()
            {
                VertexCount = 0;
                IndexCount = 0;
                Texture = null;
            }
        }

        public FieryAuraEffect(float qualityScale = 1f, bool enableDynamicLight = true)
        {
            _qualityScale = MathHelper.Clamp(qualityScale, 0.25f, 1f);
            _enableDynamicLight = enableDynamicLight;
            _maxConfiguredParticles = Math.Clamp((int)MathF.Round(MaxParticles * _qualityScale), 10, MaxParticles);
            _particleTarget = _maxConfiguredParticles;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-105f, -105f, -25f),
                new Vector3(105f, 105f, 205f));

            _auraLight = new DynamicLight
            {
                Owner = this,
                Position = Vector3.Zero,
                Color = new Vector3(1f, 0.30f, 0.08f),
                Radius = 220f,
                Intensity = 0f
            };

        }

        public void SetActive(bool active)
        {
            _active = active;
            if (active)
            {
                Hidden = false;
            }
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _ = await TextureLoader.Instance.Prepare(PrimaryTexturePath);
            _ = await TextureLoader.Instance.Prepare(SecondaryTexturePath);
            _ = await TextureLoader.Instance.Prepare(GlowTexturePath);

            _primaryTexture = await TextureLoader.Instance.PrepareAndGetTexture(PrimaryTexturePath) ?? GraphicsManager.Instance.Pixel;
            _secondaryTexture = await TextureLoader.Instance.PrepareAndGetTexture(SecondaryTexturePath) ?? _primaryTexture;
            _glowTexture = await TextureLoader.Instance.PrepareAndGetTexture(GlowTexturePath) ?? GraphicsManager.Instance.Pixel;

            if (_enableDynamicLight && World?.Terrain != null && !_lightAdded)
            {
                World.Terrain.AddDynamicLight(_auraLight);
                _lightAdded = true;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            Vector3 worldPos = WorldPosition.Translation;
            if (!IsFinite(worldPos))
            {
                ResetParticles();
                SuppressDrawingAfterOutlier();
                return;
            }

            if (!IsWithinWorldBounds(worldPos))
            {
                ResetParticles();
                SuppressDrawingAfterOutlier();
                return;
            }

            if (Parent != null)
            {
                Vector3 parentWorldPos = Parent.WorldPosition.Translation;
                if (IsFinite(parentWorldPos) &&
                    !IsWithinWorldBounds(parentWorldPos))
                {
                    ResetParticles();
                    SuppressDrawingAfterOutlier();
                    return;
                }

                if (IsFinite(parentWorldPos) &&
                    Vector3.DistanceSquared(worldPos, parentWorldPos) > MaxParentOffsetDistance * MaxParentOffsetDistance)
                {
                    // Ignore one-frame transform outliers detached from parent, they cause remote ghost quads.
                    ResetParticles();
                    SuppressDrawingAfterOutlier();
                    return;
                }
            }

            if (_hasLastWorldPosition &&
                Vector3.DistanceSquared(worldPos, _lastWorldPosition) > TeleportResetDistance * TeleportResetDistance)
            {
                // Drop stale particles after abrupt object relocation to avoid one-frame artifacts far away.
                ResetParticles();
                SuppressDrawingAfterOutlier();
            }

            _lastWorldPosition = worldPos;
            _hasLastWorldPosition = true;

            _time += dt;

            float targetFade = _active ? 1f : 0f;
            float lerpFactor = MathHelper.Clamp(dt * 7f, 0f, 1f);
            _fade = MathHelper.Lerp(_fade, targetFade, lerpFactor);

            UpdateLodSettings(gameTime);

            if (_active)
                SpawnParticles(dt * _spawnMultiplier);

            if (_particleCount > _particleTarget)
                _particleCount = _particleTarget;

            if (_particleStride >= 4)
            {
                _lowLodFrameGate = (_lowLodFrameGate + 1) % 2;
                if (_lowLodFrameGate == 0)
                    UpdateParticles(dt * 2f);
            }
            else
            {
                UpdateParticles(dt);
            }
            UpdateLight();

            if (_suppressDrawFrames > 0)
                _suppressDrawFrames--;

            if (_stableFrameCount < MinStableFramesToDraw)
                _stableFrameCount++;

            if (!_active && _fade < 0.02f && _particleCount == 0)
                Hidden = true;
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!CanDrawAuraGeometry())
                return;

            if (!LateWorldDrawQueue.Contains(this))
                LateWorldDrawQueue.Add(this);
        }

        public static void FlushLateWorldDraws(WorldControl world)
        {
            try
            {
                for (int i = 0; i < LateWorldDrawQueue.Count; i++)
                {
                    FieryAuraEffect aura = LateWorldDrawQueue[i];
                    if (ReferenceEquals(aura.World, world) && aura.CanDrawAuraGeometry())
                        aura.DrawAura();
                }
            }
            finally
            {
                LateWorldDrawQueue.Clear();
            }
        }

        public static bool HasPendingBatches =>
            PrimaryBatch.HasGeometry ||
            SecondaryBatch.HasGeometry ||
            GlowBatch.HasGeometry;

        public static void BeginFrameBatchMetrics()
        {
            LastFrameBatchedAuras = _batchedAurasThisFrame;
            LastFrameBatchFlushes = _batchFlushesThisFrame;
            LastFrameBatchDrawCalls = _batchDrawCallsThisFrame;
            LastFrameBatchQuads = _batchQuadsThisFrame;
            LastFrameDensityFull = _densityFullThisTick;
            LastFrameDensityReduced = _densityReducedThisTick;
            LastFrameDensityCulled = _densityCulledThisTick;
            _batchedAurasThisFrame = 0;
            _batchFlushesThisFrame = 0;
            _batchDrawCallsThisFrame = 0;
            _batchQuadsThisFrame = 0;
        }

        public static bool TryQueueForBatch(FieryAuraEffect aura)
        {
            if (aura == null || Constants.DRAW_BOUNDING_BOXES)
                return false;

            if (!aura.CanDrawAuraGeometry())
                return true;

            if (!aura.TryBuildAuraGeometry(
                    out int primaryCount,
                    out int secondaryStart,
                    out int secondaryCount,
                    out int glowStart,
                    out int glowCount,
                    out int totalQuads))
            {
                return true;
            }

            if (totalQuads <= 0)
                return true;

            if (PrimaryBatch.NeedsFlush(aura._primaryTexture, primaryCount) ||
                SecondaryBatch.NeedsFlush(aura._secondaryTexture, secondaryCount) ||
                GlowBatch.NeedsFlush(aura._glowTexture, glowCount))
            {
                FlushBatches();
            }

            aura.AppendBatchRange(PrimaryBatch, aura._primaryTexture, 0, primaryCount);
            aura.AppendBatchRange(SecondaryBatch, aura._secondaryTexture, secondaryStart, secondaryCount);
            aura.AppendBatchRange(GlowBatch, aura._glowTexture, glowStart, glowCount);
            _batchedAurasThisFrame++;
            _batchQuadsThisFrame += totalQuads;
            return true;
        }

        public static void FlushBatches()
        {
            if (!HasPendingBatches)
                return;

            var gd = GraphicsManager.Instance.GraphicsDevice;
            var effect = GraphicsManager.Instance.BasicEffect3D;
            var camera = Camera.Instance;
            if (effect == null || camera == null)
            {
                ClearBatches();
                return;
            }

            var prevBlend = gd.BlendState;
            var prevDepth = gd.DepthStencilState;
            var prevRaster = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];

            bool prevTexEnabled = effect.TextureEnabled;
            bool prevVcEnabled = effect.VertexColorEnabled;
            bool prevLightEnabled = effect.LightingEnabled;
            var prevTex = effect.Texture;
            Matrix prevWorld = effect.World;
            Matrix prevView = effect.View;
            Matrix prevProj = effect.Projection;

            gd.BlendState = BlendState.Additive;
            gd.DepthStencilState = DepthStencilState.DepthRead;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            effect.TextureEnabled = true;
            effect.VertexColorEnabled = true;
            effect.LightingEnabled = false;
            effect.World = Matrix.Identity;
            effect.View = camera.View;
            effect.Projection = camera.Projection;

            DrawBatchGroup(gd, effect, PrimaryBatch);
            DrawBatchGroup(gd, effect, SecondaryBatch);
            DrawBatchGroup(gd, effect, GlowBatch);
            _batchFlushesThisFrame++;

            effect.TextureEnabled = prevTexEnabled;
            effect.VertexColorEnabled = prevVcEnabled;
            effect.LightingEnabled = prevLightEnabled;
            effect.Texture = prevTex;
            effect.World = prevWorld;
            effect.View = prevView;
            effect.Projection = prevProj;

            gd.BlendState = prevBlend;
            gd.DepthStencilState = prevDepth;
            gd.RasterizerState = prevRaster;
            gd.SamplerStates[0] = prevSampler;

            ClearBatches();
        }

        private static void DrawBatchGroup(GraphicsDevice gd, BasicEffect effect, BatchGeometry batch)
        {
            if (!batch.HasGeometry || batch.Texture == null)
                return;

            effect.Texture = batch.Texture;
            effect.CurrentTechnique.Passes[0].Apply();
            gd.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList,
                batch.Vertices,
                0,
                batch.VertexCount,
                batch.Indices,
                0,
                batch.IndexCount / 3);
            _batchDrawCallsThisFrame++;
        }

        private static void ClearBatches()
        {
            PrimaryBatch.Clear();
            SecondaryBatch.Clear();
            GlowBatch.Clear();
        }

        private bool CanDrawAuraGeometry()
        {
            if (!Visible || (_particleCount == 0 && _fade <= 0.01f))
                return false;

            if (_skipDrawing || _densityCulled)
                return false;

            if (_suppressDrawFrames > 0)
                return false;

            if (_stableFrameCount < MinStableFramesToDraw)
                return false;

            return _primaryTexture != null && _glowTexture != null;
        }

        private void DrawAura()
        {
            var gd = GraphicsManager.Instance.GraphicsDevice;
            var effect = GraphicsManager.Instance.BasicEffect3D;
            if (effect == null || Camera.Instance == null)
                return;

            if (!TryBuildAuraGeometry(
                    out int primaryCount,
                    out int secondaryStart,
                    out int secondaryCount,
                    out int glowStart,
                    out int glowCount,
                    out int totalQuads))
            {
                return;
            }

            if (totalQuads <= 0)
                return;

            var prevBlend = gd.BlendState;
            var prevDepth = gd.DepthStencilState;
            var prevRaster = gd.RasterizerState;
            var prevSampler = gd.SamplerStates[0];

            bool prevTexEnabled = effect.TextureEnabled;
            bool prevVcEnabled = effect.VertexColorEnabled;
            bool prevLightEnabled = effect.LightingEnabled;
            var prevTex = effect.Texture;
            Matrix prevWorld = effect.World;
            Matrix prevView = effect.View;
            Matrix prevProj = effect.Projection;

            gd.BlendState = BlendState.Additive;
            gd.DepthStencilState = DepthState;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            effect.TextureEnabled = true;
            effect.VertexColorEnabled = true;
            effect.LightingEnabled = false;
            effect.World = WorldPosition;
            effect.View = Camera.Instance.View;
            effect.Projection = Camera.Instance.Projection;

            if (primaryCount > 0)
            {
                effect.Texture = _primaryTexture;
                effect.CurrentTechnique.Passes[0].Apply();
                gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices, 0, totalQuads * 4,
                    QuadIndices, 0, primaryCount * 2);
            }

            if (secondaryCount > 0)
            {
                effect.Texture = _secondaryTexture;
                effect.CurrentTechnique.Passes[0].Apply();
                gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices, 0, totalQuads * 4,
                    QuadIndices, secondaryStart * 6, secondaryCount * 2);
            }

            if (glowCount > 0)
            {
                effect.Texture = _glowTexture;
                effect.CurrentTechnique.Passes[0].Apply();
                gd.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    _vertices, 0, totalQuads * 4,
                    QuadIndices, glowStart * 6, glowCount * 2);
            }

            effect.TextureEnabled = prevTexEnabled;
            effect.VertexColorEnabled = prevVcEnabled;
            effect.LightingEnabled = prevLightEnabled;
            effect.Texture = prevTex;
            effect.World = prevWorld;
            effect.View = prevView;
            effect.Projection = prevProj;

            gd.BlendState = prevBlend;
            gd.DepthStencilState = prevDepth;
            gd.RasterizerState = prevRaster;
            gd.SamplerStates[0] = prevSampler;
        }

        private bool TryBuildAuraGeometry(
            out int primaryCount,
            out int secondaryStart,
            out int secondaryCount,
            out int glowStart,
            out int glowCount,
            out int totalQuads)
        {
            primaryCount = 0;
            secondaryStart = 0;
            secondaryCount = 0;
            glowStart = 0;
            glowCount = 0;
            totalQuads = 0;

            var camera = Camera.Instance;
            if (camera == null)
                return false;

            if (!IsFinite(WorldPosition) || !IsFinite(WorldPosition.Translation))
                return false;

            Matrix worldInverse = Matrix.Invert(WorldPosition);
            Vector3 localCameraPosition = Vector3.Transform(camera.Position, worldInverse);
            if (!IsFinite(localCameraPosition))
                return false;

            Vector3 toCameraDir = localCameraPosition;
            if (toCameraDir.LengthSquared() < 0.001f)
                toCameraDir = Vector3.UnitY;
            toCameraDir.Normalize();

            Vector3 sharedRight = Vector3.Cross(Vector3.UnitZ, toCameraDir);
            if (sharedRight.LengthSquared() < 0.001f)
                sharedRight = Vector3.UnitX;
            sharedRight.Normalize();

            Vector3 sharedUp = Vector3.Cross(toCameraDir, sharedRight);
            sharedUp.Normalize();
            Vector3 selfOcclusionOffset = Parent is ModelObject ? toCameraDir * AttachedModelDepthOffset : Vector3.Zero;

            int quadIndex = 0;

            BuildParticles(sharedRight, sharedUp, selfOcclusionOffset, 0, _particleStride, ref quadIndex);
            primaryCount = quadIndex;

            secondaryStart = quadIndex;
            if (_drawSecondary)
            {
                BuildParticles(sharedRight, sharedUp, selfOcclusionOffset, 1, _particleStride, ref quadIndex);
                secondaryCount = quadIndex - secondaryStart;
            }

            glowStart = quadIndex;
            BuildCoreCloud(sharedRight, sharedUp, selfOcclusionOffset, ref quadIndex);
            glowCount = quadIndex - glowStart;
            totalQuads = quadIndex;
            return true;
        }

        private void AppendBatchRange(BatchGeometry batch, Texture2D texture, int startQuad, int quadCount)
        {
            if (quadCount <= 0)
                return;

            if (batch.Texture == null)
                batch.Texture = texture;

            int requiredVertices = batch.VertexCount + quadCount * 4;
            int requiredIndices = batch.IndexCount + quadCount * 6;
            batch.EnsureCapacity(requiredVertices, requiredIndices);

            Matrix world = WorldPosition;
            int destinationVertex = batch.VertexCount;
            for (int q = 0; q < quadCount; q++)
            {
                int sourceVertex = (startQuad + q) * 4;
                for (int i = 0; i < 4; i++)
                {
                    var vertex = _vertices[sourceVertex + i];
                    vertex.Position = Vector3.Transform(vertex.Position, world);
                    batch.Vertices[destinationVertex++] = vertex;
                }
            }

            batch.VertexCount = destinationVertex;
            batch.IndexCount += quadCount * 6;
        }

        private void BuildParticles(Vector3 sharedRight, Vector3 sharedUp, Vector3 selfOcclusionOffset, byte variant, int stride, ref int quadIndex)
        {
            int sampleStride = Math.Max(1, stride);
            for (int i = 0; i < _particleCount; i += sampleStride)
            {
                ref var particle = ref _particles[i];
                if (particle.TextureVariant != variant)
                    continue;

                if (!IsFinite(particle.Position) || !IsFinite(particle.Velocity))
                    continue;

                if (particle.Position.LengthSquared() > MaxParticleLocalRange * MaxParticleLocalRange)
                    continue;

                float lifeT = particle.Age / particle.Life;
                if (lifeT >= 1f)
                    continue;

                float fadeIn = MathHelper.Clamp(particle.Age / 0.1f, 0f, 1f);
                float fadeOut = 1f - lifeT;
                float alpha = fadeIn * fadeOut * _fade * TotalAlpha * _densityAlphaScale;
                if (alpha <= 0.01f)
                    continue;

                float wobbleWide = 0.9f + 0.18f * (0.5f + 0.5f * MathF.Sin(_time * 6.1f + particle.Phase));
                float wobbleTall = 0.88f + 0.25f * (0.5f + 0.5f * MathF.Sin(_time * 5.2f + particle.Phase * 1.2f));

                float width = particle.Width * wobbleWide;
                float height = particle.Height * wobbleTall;

                float heat = lifeT;
                float r = 1f;
                float g = MathHelper.Lerp(0.80f, 0.14f, heat);
                float b = MathHelper.Lerp(0.30f, 0.03f, heat);
                var color = new Color(r * alpha, g * alpha, b * alpha, alpha);

                BuildBillboard(
                    particle.Position + selfOcclusionOffset,
                    sharedRight,
                    sharedUp,
                    width,
                    height,
                    particle.Phase,
                    color,
                    ref quadIndex);
            }
        }

        private void BuildCoreCloud(Vector3 sharedRight, Vector3 sharedUp, Vector3 selfOcclusionOffset, ref int quadIndex)
        {
            for (int i = 0; i < CoreCloudQuads; i++)
            {
                float phase = _time * (1.05f + i * 0.17f) + i * 1.9f;
                float pulse = 0.84f + 0.16f * (0.5f + 0.5f * MathF.Sin(phase));
                float alpha = (0.22f + 0.14f * pulse) * _fade * TotalAlpha * _densityAlphaScale;
                if (alpha <= 0.01f)
                    continue;

                float width = (112f + i * 14f) * pulse;
                float height = width * (1.1f + i * 0.05f);
                float ringRadius = 8f + i * 7f;

                Vector3 localPosition = new(
                    MathF.Cos(phase * 0.7f) * ringRadius,
                    MathF.Sin(phase * 0.9f) * ringRadius,
                    82f + i * 14f + 5f * MathF.Sin(phase * 1.2f));

                var color = new Color(alpha, alpha * 0.45f, alpha * 0.16f, alpha);
                BuildBillboard(localPosition + selfOcclusionOffset, sharedRight, sharedUp, width, height, phase, color, ref quadIndex);
            }
        }

        private void BuildBillboard(
            Vector3 position,
            Vector3 sharedRight,
            Vector3 sharedUp,
            float width,
            float height,
            float phase,
            Color color,
            ref int quadIndex)
        {
            if (quadIndex >= MaxQuads)
                return;

            if (!IsFinite(position) ||
                !IsFinite(width) || !IsFinite(height) ||
                width <= 0.1f || height <= 0.1f)
            {
                return;
            }

            float distortionX = MathF.Sin(_time * 7.8f + phase * 1.15f) * width * 0.07f;
            float distortionY = MathF.Sin(_time * 4.9f + phase * 1.75f) * height * 0.04f;
            Vector3 distortedPosition = position + sharedRight * distortionX + sharedUp * distortionY;

            Vector3 r = sharedRight * (width * 0.5f);
            Vector3 u = sharedUp * (height * 0.5f);

            int vi = quadIndex * 4;
            _vertices[vi] = new VertexPositionColorTexture(distortedPosition - r - u, color, new Vector2(0f, 1f));
            _vertices[vi + 1] = new VertexPositionColorTexture(distortedPosition + r - u, color, new Vector2(1f, 1f));
            _vertices[vi + 2] = new VertexPositionColorTexture(distortedPosition + r + u, color, new Vector2(1f, 0f));
            _vertices[vi + 3] = new VertexPositionColorTexture(distortedPosition - r + u, color, new Vector2(0f, 0f));

            quadIndex++;
        }

        private void SpawnParticles(float dt)
        {
            _spawnTimer += dt;
            float spawnInterval = 1f / SpawnRate;

            while (_spawnTimer >= spawnInterval && _particleCount < _particleTarget)
            {
                _spawnTimer -= spawnInterval;
                SpawnParticle();
            }
        }

        private void SpawnParticle()
        {
            if (_particleCount >= _maxConfiguredParticles)
                return;

            float angle = RandomRange(0f, MathHelper.TwoPi);
            float radius = MathF.Sqrt(RandomRange(0f, 1f));
            float radialX = RadiusX * radius;
            float radialY = RadiusY * radius;

            float life = RandomRange(LifeMin, LifeMax);
            float width = RandomRange(WidthMin, WidthMax);
            float height = width * RandomRange(HeightRatioMin, HeightRatioMax);

            _particles[_particleCount++] = new FireParticle
            {
                Position = new Vector3(
                    MathF.Cos(angle) * radialX,
                    MathF.Sin(angle) * radialY,
                    RandomRange(HeightMin, HeightMax)),
                Velocity = new Vector3(
                    RandomRange(-9f, 9f),
                    RandomRange(-9f, 9f),
                    RandomRange(RiseSpeedMin, RiseSpeedMax)),
                Age = 0f,
                Life = life,
                Width = width,
                Height = height,
                Phase = RandomRange(0f, MathHelper.TwoPi),
                TextureVariant = (byte)(MuGame.Random.NextDouble() < 0.5 ? 0 : 1)
            };
        }

        private void UpdateParticles(float dt)
        {
            int i = 0;
            while (i < _particleCount)
            {
                ref var p = ref _particles[i];
                p.Age += dt;

                if (p.Age >= p.Life)
                {
                    _particles[i] = _particles[--_particleCount];
                    continue;
                }

                if (!IsFinite(p.Position) || !IsFinite(p.Velocity))
                {
                    _particles[i] = _particles[--_particleCount];
                    continue;
                }

                if (p.Position.LengthSquared() > MaxParticleLocalRange * MaxParticleLocalRange)
                {
                    _particles[i] = _particles[--_particleCount];
                    continue;
                }

                p.Position += p.Velocity * dt;

                if (_particleStride < 2)
                {
                    float lifeT = p.Age / p.Life;
                    float turbulence = (1f - lifeT) * 12f;
                    p.Position.X += MathF.Cos(p.Phase + _time * 3.1f) * turbulence * dt;
                    p.Position.Y += MathF.Sin(p.Phase * 1.37f + _time * 2.6f) * turbulence * dt;
                }

                p.Velocity.Z += 16f * dt;
                p.Velocity.X *= 0.99f;
                p.Velocity.Y *= 0.99f;

                i++;
            }
        }

        private void UpdateLight()
        {
            if (!_enableDynamicLight || !_lightAdded)
                return;

            _auraLight.Position = WorldPosition.Translation + new Vector3(0f, 0f, 84f);

            if (_fade <= 0.01f || _lightScale <= 0.001f)
            {
                _auraLight.Intensity = 0f;
                return;
            }

            float flicker = 0.86f + 0.14f * MathF.Sin(_time * 12.2f + 0.8f);
            _auraLight.Intensity = (0.8f + 0.3f * flicker) * _fade * _lightScale;
            _auraLight.Radius = 215f + 12f * MathF.Sin(_time * 4.2f);
        }

        private void UpdateLodSettings(GameTime gameTime)
        {
            _cameraDistSq = float.MaxValue;
            var camera = Camera.Instance;
            if (camera != null)
            {
                _cameraDistSq = Vector3.DistanceSquared(camera.Position, WorldPosition.Translation);
            }

            float mediumSq = MediumLodDistance * MediumLodDistance;
            float lowSq = LowLodDistance * LowLodDistance;
            float cullSq = HardCullDistance * HardCullDistance;

            bool useLow = LowQuality || _cameraDistSq > lowSq;
            bool useMedium = !useLow && _cameraDistSq > mediumSq;

            if (useLow)
            {
                _particleTarget = Math.Max(8, (int)(_maxConfiguredParticles * 0.28f));
                _spawnMultiplier = 0.35f;
                _particleStride = 4;
                _drawSecondary = false;
                _lightScale = 0f;
            }
            else if (useMedium)
            {
                _particleTarget = Math.Max(12, (int)(_maxConfiguredParticles * 0.55f));
                _spawnMultiplier = 0.6f;
                _particleStride = 2;
                _drawSecondary = true;
                _lightScale = 0.65f;
            }
            else
            {
                _particleTarget = _maxConfiguredParticles;
                _spawnMultiplier = 1f;
                _particleStride = 1;
                _drawSecondary = true;
                _lightScale = 1f;
            }

            if (!_enableDynamicLight)
                _lightScale = 0f;

            _skipDrawing = LowQuality && _cameraDistSq > cullSq;

            int densitySlot = RegisterDensitySlot(gameTime, WorldPosition.Translation);
            _densityCulled = densitySlot >= DensityReducedAurasPerTile;
            _densityAlphaScale = 1f;

            if (_densityCulled)
            {
                _particleTarget = 0;
                _spawnMultiplier = 0f;
                _particleStride = 8;
                _drawSecondary = false;
                _lightScale = 0f;
                _skipDrawing = true;
                return;
            }

            if (densitySlot >= DensityFullAurasPerTile)
            {
                _particleTarget = Math.Min(
                    _particleTarget,
                    Math.Max(2, (int)(_maxConfiguredParticles * DensityReducedParticleFactor)));
                _spawnMultiplier = Math.Min(_spawnMultiplier, DensityReducedSpawnMultiplier);
                _particleStride = Math.Max(_particleStride, 12);
                _drawSecondary = false;
                _lightScale = 0f;
                _densityAlphaScale = DensityReducedAlphaScale;
            }
        }

        private static int RegisterDensitySlot(GameTime gameTime, Vector3 worldPosition)
        {
            long ticks = gameTime.TotalGameTime.Ticks;
            if (_densityUpdateTicks != ticks)
            {
                LastFrameDensityFull = _densityFullThisTick;
                LastFrameDensityReduced = _densityReducedThisTick;
                LastFrameDensityCulled = _densityCulledThisTick;
                _densityFullThisTick = 0;
                _densityReducedThisTick = 0;
                _densityCulledThisTick = 0;
                DensityTileCounts.Clear();
                _densityUpdateTicks = ticks;
            }

            int tileX = (int)MathF.Floor(worldPosition.X / Constants.TERRAIN_SCALE);
            int tileY = (int)MathF.Floor(worldPosition.Y / Constants.TERRAIN_SCALE);
            long key = ((long)tileX << 32) ^ (uint)tileY;

            DensityTileCounts.TryGetValue(key, out int slot);
            DensityTileCounts[key] = slot + 1;

            if (slot < DensityFullAurasPerTile)
                _densityFullThisTick++;
            else if (slot < DensityReducedAurasPerTile)
                _densityReducedThisTick++;
            else
                _densityCulledThisTick++;

            return slot;
        }

        private void ResetParticles()
        {
            _particleCount = 0;
            _spawnTimer = 0f;
        }

        private void SuppressDrawingAfterOutlier()
        {
            _suppressDrawFrames = OutlierDrawSuppressFrames;
            _stableFrameCount = 0;
        }

        private static bool IsWithinWorldBounds(Vector3 worldPosition)
        {
            float maxXY = Constants.TERRAIN_SIZE * Constants.TERRAIN_SCALE + WorldBoundsMargin;
            float minXY = -WorldBoundsMargin;
            return
                worldPosition.X >= minXY && worldPosition.X <= maxXY &&
                worldPosition.Y >= minXY && worldPosition.Y <= maxXY &&
                worldPosition.Z >= WorldMinZ && worldPosition.Z <= WorldMaxZ;
        }

        private static float RandomRange(float min, float max)
        {
            return (float)(MuGame.Random.NextDouble() * (max - min) + min);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
        }

        private static bool IsFinite(Matrix value)
        {
            return
                IsFinite(value.M11) && IsFinite(value.M12) && IsFinite(value.M13) && IsFinite(value.M14) &&
                IsFinite(value.M21) && IsFinite(value.M22) && IsFinite(value.M23) && IsFinite(value.M24) &&
                IsFinite(value.M31) && IsFinite(value.M32) && IsFinite(value.M33) && IsFinite(value.M34) &&
                IsFinite(value.M41) && IsFinite(value.M42) && IsFinite(value.M43) && IsFinite(value.M44);
        }

        public override void Dispose()
        {
            if (_lightAdded && World?.Terrain != null)
            {
                World.Terrain.RemoveDynamicLight(_auraLight);
                _lightAdded = false;
            }

            base.Dispose();
        }
    }
}
