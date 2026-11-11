#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 BITMAP_FLAME effect (skill 5 / Scroll of Flame).
    ///
    /// The original is a 2x2 terrain bitmap plus BITMAP_FLAME particles. It is
    /// not a cylindrical wall, and it does not use sparks, flares or a second
    /// particle texture.
    /// </summary>
    public sealed class ScrollOfFlameEffect : EffectObject
    {
        private const ushort FlameSkillId = 5;
        private const string FlameTexturePath = "Effect/Flame01.jpg";

        private const float ReferenceFps = 25f;
        private const float ParticleLifetime = 20f / ReferenceFps;
        private const float AreaEffectLifetime = 40f / ReferenceFps;
        private const float TargetedEffectLifetime = 20f / ReferenceFps;
        private const float TargetedParticleScale = 0.9f;
        private const float TerrainBitmapSize = 2f;
        private const float TerrainHeightOffset = 5f;

        private const int AreaParticlesPerFrame = 6;
        private const int TargetedParticlesPerFrame = 1;
        private const int MaxFlameParticles = 256;
        private const int TerrainTileRadius = 3;
        private const int MaxTerrainQuads = (TerrainTileRadius * 2 + 1) * (TerrainTileRadius * 2 + 1);
        private const float DamageRadius = 150f;
        private const float DamageTickSeconds = 20f / ReferenceFps;
        private const int MaxHitTargets = 5;
        private const double LastCastMatchWindowMs = 1500;

        private readonly Vector3 _center;
        private readonly float _rotation;
        private readonly bool _isTargeted;
        private readonly bool _dealsDamage;
        private readonly float _effectLifetime;
        private float _time;

        private readonly byte _targetTileX;
        private readonly byte _targetTileY;
        private byte _animationCounter;
        private byte _hitCounter;
        private readonly Dictionary<ushort, float> _nextHitTimeByTarget = new();
        private readonly ILogger? _logger = MuGame.AppLoggerFactory?.CreateLogger<ScrollOfFlameEffect>();
        private readonly DynamicLight _flameLight;
        private TerrainControl? _lightTerrain;
        private bool _lightsAdded;
        private static readonly List<ScrollOfFlameEffect> LateWorldDrawQueue = new(32);

        private Texture2D? _flameTexture;
        private readonly FlameParticle[] _particles = new FlameParticle[MaxFlameParticles];
        private int _particleCount;
        private int _lateWorldDrawFrame = -1;

        private readonly VertexPositionColorTexture[] _terrainVertices =
            new VertexPositionColorTexture[MaxTerrainQuads * 4];
        private static readonly short[] QuadIndices = QuadIndexCache.Get(MaxFlameParticles);
        private int _terrainQuadCount;
        private bool _terrainMeshBuilt;

        private readonly VertexPositionColorTexture[] _particleVertices =
            new VertexPositionColorTexture[MaxFlameParticles * 4];

        private struct FlameParticle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Gravity;
            public float LifeTime;
            public float Scale;
        }

        public ScrollOfFlameEffect(
            Vector3 center,
            bool isTargeted = false,
            bool dealsDamage = false,
            float rotation = 0f)
        {
            _center = center;
            _rotation = rotation;
            _isTargeted = isTargeted;
            _dealsDamage = dealsDamage;
            _effectLifetime = isTargeted ? TargetedEffectLifetime : AreaEffectLifetime;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;

            _targetTileX = (byte)Math.Clamp(
                (int)(_center.X / Constants.TERRAIN_SCALE), 0, Constants.TERRAIN_SIZE - 1);
            _targetTileY = (byte)Math.Clamp(
                (int)(_center.Y / Constants.TERRAIN_SCALE), 0, Constants.TERRAIN_SIZE - 1);

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-350f, -350f, -20f),
                new Vector3(350f, 350f, 1250f));

            Position = center;
            _flameLight = new DynamicLight
            {
                Owner = this,
                Position = center,
                Color = new Vector3(1f, 0.4f, 0f),
                Radius = Constants.TERRAIN_SCALE * 3f,
                Intensity = 1f
            };
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            _ = await TextureLoader.Instance.Prepare(FlameTexturePath);
            _flameTexture = await TextureLoader.Instance.PrepareAndGetTexture(FlameTexturePath)
                ?? GraphicsManager.Instance.Pixel;
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0f)
                return;

            if (_time < _effectLifetime)
                EmitSourceParticles(dt);

            _time += dt;
            UpdateParticles(dt);
            UpdateDamage();
            UpdateDynamicLight();

            // Source particles outlive the effect object by 20 reference frames.
            if (_time >= _effectLifetime && _particleCount == 0)
                RemoveSelf();
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || _flameTexture == null)
                return;

            int frame = MuGame.FrameIndex;
            if (_lateWorldDrawFrame != frame)
            {
                _lateWorldDrawFrame = frame;
                LateWorldDrawQueue.Add(this);
            }
        }

        public static void FlushLateWorldDraws(WorldControl world)
        {
            try
            {
                for (int i = 0; i < LateWorldDrawQueue.Count; i++)
                {
                    ScrollOfFlameEffect effect = LateWorldDrawQueue[i];
                    if (ReferenceEquals(effect.World, world) && effect.Visible)
                        effect.DrawEffect();
                }
            }
            finally
            {
                LateWorldDrawQueue.Clear();
            }
        }

        private void EmitSourceParticles(float dt)
        {
            // CreateParticleFpsChecked(...): at 25 FPS this is one attempt per
            // frame; at higher FPS the source probabilistically scales the call.
            float chance = MathHelper.Clamp(dt * ReferenceFps, 0f, 1f);
            int attempts = _isTargeted ? TargetedParticlesPerFrame : AreaParticlesPerFrame;

            for (int i = 0; i < attempts; i++)
            {
                if (chance < 1f && MuGame.Random.NextDouble() > chance)
                    continue;

                SpawnParticle(_isTargeted ? TargetedParticleScale : 1f);
            }
        }

        private void SpawnParticle(float scaleMultiplier)
        {
            if (_particleCount >= MaxFlameParticles)
                return;

            _particles[_particleCount++] = new FlameParticle
            {
                Position = _center + new Vector3(
                    MuGame.Random.Next(50) - 25,
                    MuGame.Random.Next(50) - 25,
                    0f),
                Velocity = new Vector3(
                    0f,
                    0f,
                    (MuGame.Random.Next(128) + 128) * 0.15f),
                Gravity = 0f,
                LifeTime = ParticleLifetime,
                Scale = scaleMultiplier * (MuGame.Random.Next(64) + 64) * 0.01f
            };
        }

        private void UpdateParticles(float dt)
        {
            float frameFactor = MathF.Min(1f, dt * ReferenceFps);

            for (int i = 0; i < _particleCount;)
            {
                ref FlameParticle particle = ref _particles[i];
                particle.LifeTime -= dt;

                if (particle.LifeTime <= 0f)
                {
                    _particles[i] = _particles[--_particleCount];
                    continue;
                }

                // MoveParticles() for BITMAP_FLAME subtype 0. The order matches
                // SourceMain5.2: move, add gravity, grow, accelerate, then add
                // the gravity contribution to the vertical position.
                particle.Position += particle.Velocity * frameFactor;
                particle.Gravity += 0.02f * frameFactor;
                particle.Scale += particle.Gravity * frameFactor;
                particle.Velocity *= MathF.Pow(1.05f, frameFactor);
                particle.Position.Z += particle.Gravity * 20f * frameFactor;
                i++;
            }
        }

        private void DrawEffect()
        {
            var graphics = GraphicsManager.Instance;
            var device = graphics.GraphicsDevice;
            var effect = graphics.BasicEffect3D;
            var camera = Camera.Instance;
            if (device == null || effect == null || camera == null)
                return;

            if (!_terrainMeshBuilt && World?.Terrain != null)
                BuildTerrainMesh();

            int terrainQuads = 0;
            Vector3 terrainDiffuse = Vector3.One;
            if (_time < _effectLifetime)
            {
                float luminosity = (MuGame.Random.Next(4) + 8) * 0.1f;
                terrainDiffuse = new Vector3(luminosity);
                terrainQuads = _terrainMeshBuilt
                    ? _terrainQuadCount
                    : BuildFallbackTerrainMesh();
            }

            int particleQuads = BuildParticleVertices(camera);

            BlendState previousBlend = device.BlendState;
            DepthStencilState previousDepth = device.DepthStencilState;
            RasterizerState previousRasterizer = device.RasterizerState;
            SamplerState previousSampler = device.SamplerStates[0];

            bool previousTextureEnabled = effect.TextureEnabled;
            bool previousVertexColorEnabled = effect.VertexColorEnabled;
            bool previousLightingEnabled = effect.LightingEnabled;
            Texture2D? previousTexture = effect.Texture;
            Matrix previousWorld = effect.World;
            Matrix previousView = effect.View;
            Matrix previousProjection = effect.Projection;
            Vector3 previousDiffuseColor = effect.DiffuseColor;

            try
            {
                device.BlendState = BlendState.Additive;
                device.DepthStencilState = DepthState;
                device.RasterizerState = RasterizerState.CullNone;
                device.SamplerStates[0] = SamplerState.LinearClamp;

                effect.TextureEnabled = true;
                effect.VertexColorEnabled = true;
                effect.LightingEnabled = false;
                effect.Texture = _flameTexture;
                effect.World = Matrix.Identity;
                effect.View = camera.View;
                effect.Projection = camera.Projection;

                effect.DiffuseColor = terrainDiffuse;
                DrawQuads(device, effect, _terrainVertices, terrainQuads * 4,
                    QuadIndices, terrainQuads * 6);

                effect.DiffuseColor = Vector3.One;
                DrawQuads(device, effect, _particleVertices, particleQuads * 4,
                    QuadIndices, particleQuads * 6);
            }
            finally
            {
                effect.TextureEnabled = previousTextureEnabled;
                effect.VertexColorEnabled = previousVertexColorEnabled;
                effect.LightingEnabled = previousLightingEnabled;
                effect.Texture = previousTexture;
                effect.World = previousWorld;
                effect.View = previousView;
                effect.Projection = previousProjection;
                effect.DiffuseColor = previousDiffuseColor;

                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
            }
        }

        private void UpdateDynamicLight()
        {
            EnsureDynamicLightAttached();
            _flameLight.Position = _center;
            _flameLight.Radius = Constants.TERRAIN_SCALE * 3f;

            if (_time >= _effectLifetime)
            {
                _flameLight.Intensity = 0f;
                return;
            }

            float luminosity = (MuGame.Random.Next(4) + 8) * 0.1f;
            _flameLight.Color = new Vector3(luminosity, luminosity * 0.4f, 0f);
            _flameLight.Intensity = 1f;
        }

        private void EnsureDynamicLightAttached()
        {
            if (_lightsAdded || Status != GameControlStatus.Ready)
                return;

            TerrainControl? terrain = World?.Terrain;
            if (terrain == null)
                return;

            _lightTerrain = terrain;
            terrain.AddDynamicLight(_flameLight);
            _lightsAdded = true;
        }

        private void DetachDynamicLight()
        {
            TerrainControl? terrain = _lightTerrain ?? World?.Terrain;
            terrain?.RemoveDynamicLight(_flameLight);
            terrain?.RemoveDynamicLightsByOwner(this);
            _lightTerrain = null;
            _lightsAdded = false;
        }

        private void BuildTerrainMesh()
        {
            var terrain = World?.Terrain;
            if (terrain == null)
                return;

            float mxf = _center.X / Constants.TERRAIN_SCALE;
            float myf = _center.Y / Constants.TERRAIN_SCALE;
            int mxi = (int)mxf;
            int myi = (int)myf;
            float texU = (mxi - mxf) + 0.5f * TerrainBitmapSize;
            float texV = (myi - myf) + 0.5f * TerrainBitmapSize;
            float texScale = 1f / TerrainBitmapSize;
            int quad = 0;

            for (int y = -TerrainTileRadius; y <= TerrainTileRadius; y++)
            {
                for (int x = -TerrainTileRadius; x <= TerrainTileRadius; x++)
                {
                    int tileX = mxi + x;
                    int tileY = myi + y;
                    if (tileX < 0 || tileY < 0 ||
                        tileX >= Constants.TERRAIN_SIZE - 1 ||
                        tileY >= Constants.TERRAIN_SIZE - 1)
                    {
                        continue;
                    }

                    int vertex = quad * 4;
                    float worldX = tileX * Constants.TERRAIN_SCALE;
                    float worldY = tileY * Constants.TERRAIN_SCALE;
                    Vector2 uv00 = TransformTerrainUv((texU + x) * texScale, (texV + y) * texScale);
                    Vector2 uv10 = TransformTerrainUv((texU + x + 1f) * texScale, (texV + y) * texScale);
                    Vector2 uv11 = TransformTerrainUv((texU + x + 1f) * texScale, (texV + y + 1f) * texScale);
                    Vector2 uv01 = TransformTerrainUv((texU + x) * texScale, (texV + y + 1f) * texScale);

                    _terrainVertices[vertex] = new VertexPositionColorTexture(
                        TerrainVertex(terrain, worldX, worldY), Color.White, uv00);
                    _terrainVertices[vertex + 1] = new VertexPositionColorTexture(
                        TerrainVertex(terrain, worldX + Constants.TERRAIN_SCALE, worldY), Color.White, uv10);
                    _terrainVertices[vertex + 2] = new VertexPositionColorTexture(
                        TerrainVertex(terrain, worldX + Constants.TERRAIN_SCALE, worldY + Constants.TERRAIN_SCALE), Color.White, uv11);
                    _terrainVertices[vertex + 3] = new VertexPositionColorTexture(
                        TerrainVertex(terrain, worldX, worldY + Constants.TERRAIN_SCALE), Color.White, uv01);

                    quad++;
                }
            }

            _terrainQuadCount = quad;
            _terrainMeshBuilt = true;
        }

        private Vector3 TerrainVertex(TerrainControl terrain, float x, float y)
        {
            return new Vector3(x, y, terrain.RequestTerrainHeight(x, y) + TerrainHeightOffset);
        }

        private Vector2 TransformTerrainUv(float u, float v)
        {
            float x = u - 0.5f;
            float y = v - 0.5f;
            // Source RenderTerrainAlphaBitmap receives -o->Angle[2].
            float sourceRotation = -_rotation;
            float cos = MathF.Cos(sourceRotation);
            float sin = MathF.Sin(sourceRotation);
            return new Vector2(
                (x * cos - y * sin) + 0.5f,
                (x * sin + y * cos) + 0.5f);
        }

        private int BuildFallbackTerrainMesh()
        {
            const float halfSize = Constants.TERRAIN_SCALE;
            float z = _center.Z + TerrainHeightOffset;
            _terrainVertices[0] = new VertexPositionColorTexture(
                new Vector3(_center.X - halfSize, _center.Y - halfSize, z), Color.White, new Vector2(0f, 0f));
            _terrainVertices[1] = new VertexPositionColorTexture(
                new Vector3(_center.X + halfSize, _center.Y - halfSize, z), Color.White, new Vector2(1f, 0f));
            _terrainVertices[2] = new VertexPositionColorTexture(
                new Vector3(_center.X + halfSize, _center.Y + halfSize, z), Color.White, new Vector2(1f, 1f));
            _terrainVertices[3] = new VertexPositionColorTexture(
                new Vector3(_center.X - halfSize, _center.Y + halfSize, z), Color.White, new Vector2(0f, 1f));
            return 1;
        }

        private int BuildParticleVertices(Camera camera)
        {
            if (_flameTexture == null)
                return 0;

            Vector3 right = camera.Right;
            Vector3 up = camera.Up;
            int quad = 0;

            for (int i = 0; i < _particleCount && quad < MaxFlameParticles; i++)
            {
                ref FlameParticle particle = ref _particles[i];
                float width = _flameTexture.Width * particle.Scale;
                float height = _flameTexture.Height * particle.Scale;
                Vector3 halfRight = right * (width * 0.5f);
                Vector3 halfUp = up * (height * 0.5f);
                int vertex = quad * 4;

                _particleVertices[vertex] = new VertexPositionColorTexture(
                    particle.Position - halfRight - halfUp, Color.White, new Vector2(0f, 1f));
                _particleVertices[vertex + 1] = new VertexPositionColorTexture(
                    particle.Position + halfRight - halfUp, Color.White, new Vector2(1f, 1f));
                _particleVertices[vertex + 2] = new VertexPositionColorTexture(
                    particle.Position + halfRight + halfUp, Color.White, new Vector2(1f, 0f));
                _particleVertices[vertex + 3] = new VertexPositionColorTexture(
                    particle.Position - halfRight + halfUp, Color.White, new Vector2(0f, 0f));

                quad++;
            }

            return quad;
        }

        private static void DrawQuads(
            GraphicsDevice device,
            BasicEffect effect,
            VertexPositionColorTexture[] vertices,
            int vertexCount,
            short[] indices,
            int indexCount)
        {
            if (vertexCount == 0 || indexCount == 0)
                return;

            EffectPass pass = effect.CurrentTechnique.Passes[0];
            pass.Apply();
            device.DrawUserIndexedPrimitives(
                    PrimitiveType.TriangleList,
                    vertices,
                    0,
                    vertexCount,
                    indices,
                    0,
                    indexCount / 3);
        }

        private void UpdateDamage()
        {
            if (!_dealsDamage || _isTargeted)
                return;

            if (_time >= _effectLifetime)
                return;

            if (World is not WalkableWorldControl { Walker: PlayerObject hero })
                return;

            if (hero.IsDead)
                return;

            if (MuGame.Network == null || !MuGame.Network.IsConnected)
                return;

            EnsureAnimationCounterInitialized();

            Span<ushort> targetBuffer = stackalloc ushort[MaxHitTargets];
            int targetCount = CollectTargetsToHit(targetBuffer);
            if (targetCount <= 0)
                return;

            if (_animationCounter == 0)
            {
                _logger?.LogTrace(
                    "ScrollOfFlame: sending AreaSkillHit with AnimationCounter=0 (tile={X},{Y}, targets={Count}).",
                    _targetTileX, _targetTileY, targetCount);
            }

            var targets = new ushort[targetCount];
            for (int i = 0; i < targetCount; i++)
                targets[i] = targetBuffer[i];

            unchecked { _hitCounter++; }

            _logger?.LogTrace(
                "ScrollOfFlame: AreaSkillHit skill={SkillId} tile=({X},{Y}) targets={Count} hitCounter={HitCounter} animCounter={AnimCounter} t={Time:F2}s",
                FlameSkillId, _targetTileX, _targetTileY, targetCount, _hitCounter, _animationCounter, _time);

            _ = MuGame.Network
                .GetCharacterService()
                .SendAreaSkillHitAsync(FlameSkillId, _targetTileX, _targetTileY, _hitCounter, targets, _animationCounter);
        }

        private void EnsureAnimationCounterInitialized()
        {
            if (_animationCounter != 0)
                return;

            var characterState = MuGame.Network?.GetCharacterState();
            if (characterState == null || characterState.LastAreaSkillId != FlameSkillId)
                return;

            double nowMs = MuGame.Instance?.GameTime?.TotalGameTime.TotalMilliseconds ?? Environment.TickCount64;
            double elapsedMs = nowMs - characterState.LastAreaSkillSentAtMs;
            if (elapsedMs < 0 || elapsedMs > LastCastMatchWindowMs)
                return;

            if (characterState.LastAreaSkillTargetX != _targetTileX ||
                characterState.LastAreaSkillTargetY != _targetTileY)
            {
                return;
            }

            _animationCounter = characterState.LastAreaSkillAnimationCounter;
        }

        private int CollectTargetsToHit(Span<ushort> targetBuffer)
        {
            if (World == null)
                return 0;

            float rangeSq = DamageRadius * DamageRadius;
            int count = 0;
            var monsters = World.Monsters;

            for (int i = 0; i < monsters.Count && count < targetBuffer.Length; i++)
            {
                var monster = monsters[i];
                if (monster == null || monster.IsDead)
                    continue;

                float dx = monster.Position.X - _center.X;
                float dy = monster.Position.Y - _center.Y;
                if (dx * dx + dy * dy > rangeSq)
                    continue;

                ushort targetId = monster.NetworkId;
                if (_nextHitTimeByTarget.TryGetValue(targetId, out float nextHitTime) &&
                    _time < nextHitTime)
                {
                    continue;
                }

                _nextHitTimeByTarget[targetId] = _time + DamageTickSeconds;
                targetBuffer[count++] = targetId;
            }

            return count;
        }

        private void RemoveSelf()
        {
            DetachDynamicLight();

            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }

        public override void Dispose()
        {
            DetachDynamicLight();
            base.Dispose();
        }
    }
}
