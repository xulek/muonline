#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Helpers;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Scroll of Meteorite (AT_SKILL_METEO / Skill ID 2).
    /// Reference behavior from SourceMain5.2:
    /// CreateEffect(MODEL_FIRE, targetPos, ...) with falling fire model and stone burst on impact.
    /// </summary>
    public sealed class ScrollOfMeteoriteEffect : EffectObject
    {
        private const string FireBaseName = "Fire";
        private const string StoneBaseName = "Stone";
        private const string ExplosionTexturePath = "Effect/Explotion01.jpg";
        private const string MeteorSoundPath = "Sound/eMeteorite.wav";

        private const float FramesPerSecond = 25f;
        private const float DefaultDurationSeconds = 1.6f; // SourceMain MODEL_FIRE default life: 40 frames
        private const float FallFrames = 20f;               // slowed ~2.5x for more dramatic arc
        private const float StartHeightOffset = 1200f;
        private const float StartXOffsetMin = 40f;
        private const float StartXOffsetMax = 70f;
        private const float StartYOffsetRange = 15f;
        private const float ImpactFlashFrames = 16f;
        private const int ImpactStoneCount = 6;
        private const float ImpactExplosionWorldRadius = 220f;
        private const float ImpactFireDuration = 3.5f;
        private const int ImpactFireLayers = 3;

        private readonly Vector3 _seedTarget;
        private readonly DynamicLight _meteorLight;
        private readonly DynamicLight _impactLight;

        private string _firePath = "Skill/Fire01.bmd";
        private readonly string[] _stonePaths = new string[2];

        private bool _pathsResolved;
        private bool _initialized;
        private bool _impacted;
        private bool _lightsAdded;

        private float _lifeFrames;
        private float _impactFlashFramesRemaining;
        private float _time;

        private Vector3 _targetPosition;
        private Vector3 _meteorPosition;
        private Vector3 _meteorVelocity;
        private float _meteorScale;

        private SpriteBatch _spriteBatch = null!;
        private Texture2D _explosionTexture = null!;

        private MeteorCoreModel? _core;
        private MeteorFireTrail? _trail;

        public ScrollOfMeteoriteEffect(Vector3 targetPosition, float durationSeconds = DefaultDurationSeconds)
        {
            _seedTarget = targetPosition;
            _lifeFrames = MathHelper.Clamp(durationSeconds, 0.8f, 2.6f) * FramesPerSecond;
            _impactFlashFramesRemaining = ImpactFlashFrames;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-320f, -240f, -120f),
                new Vector3(320f, 240f, 520f));

            _meteorLight = new DynamicLight
            {
                Owner = this,
                Position = targetPosition,
                Color = new Vector3(1f, 0.32f, 0.08f),
                Radius = 220f,
                Intensity = 1.2f
            };

            _impactLight = new DynamicLight
            {
                Owner = this,
                Position = targetPosition,
                Color = new Vector3(1f, 0.45f, 0.12f),
                Radius = 340f,
                Intensity = 0f
            };
        }

        public override async Task LoadContent()
        {
            await base.LoadContent();

            await ResolvePaths();
            _ = await TextureLoader.Instance.Prepare(ExplosionTexturePath);

            _spriteBatch = GraphicsManager.Instance.Sprite;
            _explosionTexture = TextureLoader.Instance.GetTexture2D(ExplosionTexturePath) ?? GraphicsManager.Instance.Pixel;

            if (!_lightsAdded && World?.Terrain != null)
            {
                World.Terrain.AddDynamicLight(_meteorLight);
                World.Terrain.AddDynamicLight(_impactLight);
                _lightsAdded = true;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready)
                return;

            if (!_initialized)
                InitializeMeteor();

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _time += dt;

            if (!_impacted)
            {
                _meteorPosition += _meteorVelocity * factor;
                Position = _meteorPosition;

                float groundZ = RequestGroundHeight(_meteorPosition.X, _meteorPosition.Y);
                if (_meteorPosition.Z <= groundZ)
                    TriggerImpact(groundZ);
            }
            else if (_impactFlashFramesRemaining > 0f)
            {
                _impactFlashFramesRemaining -= factor;
            }

            _lifeFrames -= factor;
            UpdateDynamicLights();

            if (_impacted)
            {
                if (_impactFlashFramesRemaining <= 0f)
                    RemoveSelf();
            }
            else if (_lifeFrames <= 0f)
            {
                RemoveSelf();
            }
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            if (!_impacted || _impactFlashFramesRemaining <= 0f || !Visible || _spriteBatch == null || _explosionTexture == null)
                return;

            if (!SpriteBatchScope.BatchIsBegun)
            {
                using (new SpriteBatchScope(_spriteBatch, SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthState))
                {
                    DrawImpactFlash();
                }
            }
            else
            {
                DrawImpactFlash();
            }
        }

        private void InitializeMeteor()
        {
            _targetPosition = _seedTarget;

            if (World?.Terrain != null)
            {
                float groundZ = World.Terrain.RequestTerrainHeight(_seedTarget.X, _seedTarget.Y);
                _targetPosition = new Vector3(_seedTarget.X, _seedTarget.Y, groundZ);
            }

            _meteorPosition = _targetPosition + new Vector3(
                RandomRange(StartXOffsetMin, StartXOffsetMax),
                RandomRange(-StartYOffsetRange, StartYOffsetRange),
                StartHeightOffset);

            _meteorVelocity = (_targetPosition - _meteorPosition) / FallFrames;
            _meteorScale = RandomRange(1.0f, 1.7f);

            _core = new MeteorCoreModel(_firePath)
            {
                Position = Vector3.Zero,
                Angle = new Vector3(75f, 0f, 0f), // fixed downward tilt, consistent across casts
                Scale = _meteorScale
            };

            Children.Add(_core);
            _ = _core.Load();

            if (World != null)
            {
                _trail = new MeteorFireTrail(() => _impacted ? null : (Vector3?)_meteorPosition);
                World.Objects.Add(_trail);
                _ = _trail.Load();
            }

            Position = _meteorPosition;
            UpdateBounds(_meteorPosition, _targetPosition);

            SoundController.Instance.PlayBuffer(MeteorSoundPath);
            _initialized = true;
        }

        private void TriggerImpact(float groundZ)
        {
            _impacted = true;
            _meteorPosition = new Vector3(_meteorPosition.X, _meteorPosition.Y, groundZ);
            _targetPosition = _meteorPosition;
            Position = _meteorPosition;

            if (_core != null)
            {
                Children.Remove(_core);
                _core.Dispose();
                _core = null;
            }

            _trail?.StopEmitting();
            _trail = null;

            SpawnImpactStones();
            SpawnImpactFire();
        }

        private void SpawnImpactFire()
        {
            if (World == null)
                return;

            // Stack multiple FieryAuraEffect layers at different heights
            // to form a tall fire column immediately at impact.
            float[] heights = { 0f, 35f, 70f };
            float[] scales  = { 1.0f, 0.75f, 0.5f };
            float[] durations = { ImpactFireDuration, ImpactFireDuration * 0.8f, ImpactFireDuration * 0.6f };

            for (int i = 0; i < ImpactFireLayers; i++)
            {
                Vector3 pos = _targetPosition + new Vector3(0f, 0f, heights[i]);
                var fire = new MeteorImpactFireEffect(pos, durations[i], scales[i]);
                World.Objects.Add(fire);
                _ = fire.Load();
            }
        }

        private void SpawnImpactStones()
        {
            if (World == null)
                return;

            for (int i = 0; i < ImpactStoneCount; i++)
            {
                string path = _stonePaths[MuGame.Random.Next(0, _stonePaths.Length)];
                Vector3 velocity = new Vector3(
                    RandomRange(-6.5f, 6.5f),
                    RandomRange(-6.5f, 6.5f),
                    RandomRange(6f, 12f));

                var stone = new MeteorStoneModel(path, RandomRange(16f, 24f), velocity)
                {
                    Position = _targetPosition,
                    Angle = new Vector3(
                        RandomRange(0f, 360f),
                        RandomRange(0f, 360f),
                        RandomRange(0f, 360f)),
                    Scale = RandomRange(0.65f, 1.0f)
                };

                World.Objects.Add(stone);
                _ = stone.Load();
            }
        }

        private void UpdateDynamicLights()
        {
            if (!_impacted)
            {
                float pulse = 0.82f + 0.18f * MathF.Sin(_time * 15f);
                _meteorLight.Position = _meteorPosition;
                _meteorLight.Intensity = 1.2f * pulse;
                _meteorLight.Radius = 220f;

                _impactLight.Intensity = 0f;
            }
            else
            {
                float alpha = MathHelper.Clamp(_impactFlashFramesRemaining / ImpactFlashFrames, 0f, 1f);

                _meteorLight.Intensity = 0f;
                _impactLight.Position = _targetPosition;
                _impactLight.Intensity = 3.5f * alpha;
                _impactLight.Radius = MathHelper.Lerp(480f, 260f, 1f - alpha);
            }
        }

        private void DrawImpactFlash()
        {
            float alpha = MathHelper.Clamp(_impactFlashFramesRemaining / ImpactFlashFrames, 0f, 1f);
            if (alpha <= 0f)
                return;

            float t = 1f - alpha; // 0 at start → 1 at end

            var viewport = GraphicsDevice.Viewport;
            var proj = Camera.Instance.Projection;
            var view = Camera.Instance.View;

            // Draw animated explosion atlas at multiple positions around impact
            Vector3[] burstPositions =
            {
                _targetPosition + new Vector3(0f, 0f, 50f),
                _targetPosition + new Vector3(30f, 20f, 30f),
                _targetPosition + new Vector3(-25f, 15f, 60f),
                _targetPosition + new Vector3(15f, -30f, 40f),
            };

            for (int i = 0; i < burstPositions.Length; i++)
            {
                // Stagger each burst slightly
                float burstT = MathHelper.Clamp(t - i * 0.06f, 0f, 1f);
                float burstAlpha = alpha * MathHelper.Clamp(1f - burstT * 1.1f, 0f, 1f);
                if (burstAlpha < 0.01f)
                    continue;

                DrawAnimatedExplosion(burstPositions[i], burstT, burstAlpha, viewport, proj, view);
            }
        }

        private void DrawAnimatedExplosion(Vector3 worldPos, float t, float alpha, Viewport viewport, Matrix proj, Matrix view)
        {
            const int frameColumns = 4;
            const int frameRows = 4;
            const int frameCount = 16;

            int frame = Math.Clamp((int)(t * (frameCount - 1)), 0, frameCount - 1);
            int frameX = frame % frameColumns;
            int frameY = frame / frameColumns;

            int frameWidth = _explosionTexture.Width / frameColumns;
            int frameHeight = _explosionTexture.Height / frameRows;
            Rectangle source = new Rectangle(frameX * frameWidth, frameY * frameHeight, frameWidth, frameHeight);

            Vector3 projected = viewport.Project(worldPos, proj, view, Matrix.Identity);
            if (projected.Z < 0f || projected.Z > 1f)
                return;

            // World-space size projection
            Vector3 edgePos = worldPos + Vector3.UnitX * ImpactExplosionWorldRadius;
            Vector3 projEdge = viewport.Project(edgePos, proj, view, Matrix.Identity);
            float pixelRadius = Vector2.Distance(
                new Vector2(projected.X, projected.Y),
                new Vector2(projEdge.X, projEdge.Y));

            float frameHalf = frameWidth * 0.5f;
            float scale = pixelRadius / frameHalf;

            Color color = new Color(1f * alpha, 0.7f * alpha, 0.35f * alpha, alpha);
            float depth = MathHelper.Clamp(projected.Z, 0f, 1f);

            _spriteBatch.Draw(
                _explosionTexture,
                new Vector2(projected.X, projected.Y),
                source,
                color,
                0f,
                new Vector2(frameHalf, frameHeight * 0.5f),
                scale,
                SpriteEffects.None,
                depth);
        }

        private async Task ResolvePaths()
        {
            if (_pathsResolved)
                return;

            _firePath = await ResolveModelPath(FireBaseName, 1, "Skill/Fire01.bmd", "Skill/Fire1.bmd", "Skill/Fire.bmd");
            _stonePaths[0] = await ResolveModelPath(StoneBaseName, 1, "Skill/Stone01.bmd", "Skill/Stone1.bmd", "Skill/Stone.bmd");
            _stonePaths[1] = await ResolveModelPath(StoneBaseName, 2, _stonePaths[0], "Skill/Stone02.bmd", "Skill/Stone2.bmd");

            _pathsResolved = true;
        }

        private static async Task<string> ResolveModelPath(string baseName, int index, params string[] fallbackCandidates)
        {
            string zeroPath = $"Skill/{baseName}0{index}.bmd";
            if (await BMDLoader.Instance.AssestExist(zeroPath))
                return zeroPath;

            string plainPath = $"Skill/{baseName}{index}.bmd";
            if (await BMDLoader.Instance.AssestExist(plainPath))
                return plainPath;

            for (int i = 0; i < fallbackCandidates.Length; i++)
            {
                string candidate = fallbackCandidates[i];
                if (await BMDLoader.Instance.AssestExist(candidate))
                    return candidate;
            }

            return zeroPath;
        }

        private float RequestGroundHeight(float x, float y)
        {
            if (World?.Terrain != null)
                return World.Terrain.RequestTerrainHeight(x, y);

            return _targetPosition.Z;
        }

        private void UpdateBounds(Vector3 start, Vector3 target)
        {
            Vector3 min = Vector3.Min(start, target);
            Vector3 max = Vector3.Max(start, target);
            Vector3 pad = new Vector3(180f, 180f, 140f);
            min -= pad;
            max += pad;

            Vector3 center = (min + max) * 0.5f;
            Position = center;
            BoundingBoxLocal = new BoundingBox(min - center, max - center);
        }

        private void RemoveSelf()
        {
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }

        private static float RandomRange(float min, float max)
        {
            return (float)(MuGame.Random.NextDouble() * (max - min) + min);
        }

        public override void Dispose()
        {
            if (_core != null)
            {
                Children.Remove(_core);
                _core.Dispose();
                _core = null;
            }

            _trail?.StopEmitting();
            _trail = null;

            if (_lightsAdded && World?.Terrain != null)
            {
                World.Terrain.RemoveDynamicLight(_meteorLight);
                World.Terrain.RemoveDynamicLight(_impactLight);
                _lightsAdded = false;
            }

            base.Dispose();
        }

        private sealed class MeteorImpactFireEffect : EffectObject
        {
            private readonly FieryAuraEffect _fire;
            private readonly float _duration;
            private float _elapsed;
            private bool _deactivated;

            public MeteorImpactFireEffect(Vector3 position, float duration, float fireScale = 0.38f)
            {
                Position = position;
                _duration = duration;
                IsTransparent = true;

                _fire = new FieryAuraEffect(qualityScale: 0.55f, enableDynamicLight: true)
                {
                    Scale = fireScale,
                    Position = Vector3.Zero
                };
                Children.Add(_fire);
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);

                if (Status == GameControlStatus.NonInitialized)
                    _ = Load();

                if (Status != GameControlStatus.Ready)
                    return;

                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                _elapsed += dt;

                if (!_deactivated && _elapsed >= _duration * 0.6f)
                {
                    _fire.SetActive(false);
                    _deactivated = true;
                }

                if (_elapsed >= _duration)
                    RemoveSelf();
            }

            private void RemoveSelf()
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private sealed class MeteorFireTrail : EffectObject
        {
            private const int MaxParticles = 80;
            private const float SpawnRate = 42f;
            private const string TexturePath = "Effect/Flame01.jpg";

            private struct Particle
            {
                public Vector3 Position; // world space - no parent transform issues
                public Vector3 Velocity;
                public float Age, Life, Size, Phase;
            }

            private readonly Particle[] _particles = new Particle[MaxParticles];
            private readonly VertexPositionColorTexture[] _verts = new VertexPositionColorTexture[MaxParticles * 4];
            private readonly short[] _idx = new short[MaxParticles * 6];
            private int _count;
            private float _spawnTimer, _time;
            private bool _emitting = true;
            private readonly Func<Vector3?> _sourcePos;
            private Texture2D _texture = null!;

            public MeteorFireTrail(Func<Vector3?> sourcePos)
            {
                _sourcePos = sourcePos;
                IsTransparent = true;
                BlendState = BlendState.Additive;
                DepthState = DepthStencilState.DepthRead;
                // Covers entire meteor trajectory
                BoundingBoxLocal = new BoundingBox(new Vector3(-5000f), new Vector3(5000f));
                InitIdx();
            }

            public void StopEmitting() => _emitting = false;

            public override async Task LoadContent()
            {
                await base.LoadContent();
                await TextureLoader.Instance.Prepare(TexturePath);
                _texture = TextureLoader.Instance.GetTexture2D(TexturePath) ?? GraphicsManager.Instance.Pixel;
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status == GameControlStatus.NonInitialized)
                    _ = Load();
                if (Status != GameControlStatus.Ready)
                    return;

                float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (dt <= 0f) return;
                _time += dt;

                if (_emitting)
                {
                    Vector3? src = _sourcePos();
                    if (src.HasValue)
                    {
                        _spawnTimer += dt;
                        float interval = 1f / SpawnRate;
                        while (_spawnTimer >= interval && _count < MaxParticles)
                        {
                            _spawnTimer -= interval;
                            Spawn(src.Value);
                        }
                    }
                }

                int i = 0;
                while (i < _count)
                {
                    ref var p = ref _particles[i];
                    p.Age += dt;
                    if (p.Age >= p.Life)
                    {
                        _particles[i] = _particles[--_count];
                        continue;
                    }
                    p.Position += p.Velocity * dt;
                    p.Velocity.Z += 14f * dt;
                    p.Velocity.X *= 0.97f;
                    p.Velocity.Y *= 0.97f;
                    i++;
                }

                if (!_emitting && _count == 0)
                    RemoveSelf();
            }

            private void Spawn(Vector3 worldPos)
            {
                float angle = Rnd(0f, MathHelper.TwoPi);
                float r = Rnd(0f, 28f);
                _particles[_count++] = new Particle
                {
                    Position = worldPos + new Vector3(MathF.Cos(angle) * r, MathF.Sin(angle) * r, Rnd(-10f, 18f)),
                    Velocity = new Vector3(Rnd(-10f, 10f), Rnd(-10f, 10f), Rnd(18f, 65f)),
                    Age = 0f,
                    Life = Rnd(0.18f, 0.48f),
                    Size = Rnd(32f, 78f),
                    Phase = Rnd(0f, MathHelper.TwoPi)
                };
            }

            public override void Draw(GameTime gameTime)
            {
                base.Draw(gameTime);
                if (!Visible || _count == 0 || _texture == null)
                    return;
                DrawParticles();
            }

            private void DrawParticles()
            {
                var gd = GraphicsManager.Instance.GraphicsDevice;
                var fx = GraphicsManager.Instance.BasicEffect3D;
                var cam = Camera.Instance;
                if (fx == null || cam == null) return;

                // Extract camera right/up from view matrix columns (world space axes)
                Matrix view = cam.View;
                Vector3 camRight = Vector3.Normalize(new Vector3(view.M11, view.M21, view.M31));
                Vector3 camUp    = Vector3.Normalize(new Vector3(view.M12, view.M22, view.M32));

                var prevBlend  = gd.BlendState;
                var prevDepth  = gd.DepthStencilState;
                var prevRaster = gd.RasterizerState;
                var prevSamp   = gd.SamplerStates[0];
                bool prevTex = fx.TextureEnabled, prevVc = fx.VertexColorEnabled, prevLit = fx.LightingEnabled;
                var prevFxTex = fx.Texture;
                Matrix prevW = fx.World, prevV = fx.View, prevP = fx.Projection;

                gd.BlendState = BlendState.Additive;
                gd.DepthStencilState = DepthState;
                gd.RasterizerState = RasterizerState.CullNone;
                gd.SamplerStates[0] = SamplerState.LinearClamp;
                fx.TextureEnabled = true;
                fx.VertexColorEnabled = true;
                fx.LightingEnabled = false;
                fx.World = Matrix.Identity; // vertices are already in world space
                fx.View = cam.View;
                fx.Projection = cam.Projection;
                fx.Texture = _texture;

                int q = 0;
                for (int i = 0; i < _count && q < MaxParticles; i++)
                {
                    ref var p = ref _particles[i];
                    float lifeT = p.Age / p.Life;
                    float fadeIn  = MathHelper.Clamp(p.Age / 0.05f, 0f, 1f);
                    float fadeOut = 1f - lifeT;
                    float alpha = fadeIn * fadeOut * 0.88f;
                    if (alpha < 0.01f) continue;

                    float g = MathHelper.Lerp(0.65f, 0.08f, lifeT);
                    float b = MathHelper.Lerp(0.15f, 0.01f, lifeT);
                    var col = new Color(alpha, g * alpha, b * alpha, alpha);

                    float wobble = 0.88f + 0.12f * MathF.Sin(_time * 6.5f + p.Phase);
                    float sz = p.Size * wobble * (1f - lifeT * 0.35f);
                    Vector3 r3 = camRight * sz;
                    Vector3 u3 = camUp    * sz;

                    int vi = q * 4;
                    _verts[vi]     = new VertexPositionColorTexture(p.Position - r3 - u3, col, new Vector2(0, 1));
                    _verts[vi + 1] = new VertexPositionColorTexture(p.Position + r3 - u3, col, new Vector2(1, 1));
                    _verts[vi + 2] = new VertexPositionColorTexture(p.Position + r3 + u3, col, new Vector2(1, 0));
                    _verts[vi + 3] = new VertexPositionColorTexture(p.Position - r3 + u3, col, new Vector2(0, 0));
                    q++;
                }

                if (q > 0)
                {
                    foreach (var pass in fx.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _verts, 0, q * 4, _idx, 0, q * 2);
                    }
                }

                fx.TextureEnabled = prevTex; fx.VertexColorEnabled = prevVc; fx.LightingEnabled = prevLit;
                fx.Texture = prevFxTex; fx.World = prevW; fx.View = prevV; fx.Projection = prevP;
                gd.BlendState = prevBlend; gd.DepthStencilState = prevDepth;
                gd.RasterizerState = prevRaster; gd.SamplerStates[0] = prevSamp;
            }

            private void InitIdx()
            {
                for (int i = 0; i < MaxParticles; i++)
                {
                    int vi = i * 4, ii = i * 6;
                    _idx[ii] = (short)vi; _idx[ii + 1] = (short)(vi + 1); _idx[ii + 2] = (short)(vi + 2);
                    _idx[ii + 3] = (short)vi; _idx[ii + 4] = (short)(vi + 2); _idx[ii + 5] = (short)(vi + 3);
                }
            }

            private void RemoveSelf() { World?.RemoveObject(this); Dispose(); }
            private static float Rnd(float a, float b) => (float)(MuGame.Random.NextDouble() * (b - a) + a);
        }

        private sealed class MeteorCoreModel : ModelObject
        {
            private readonly string _path;

            public MeteorCoreModel(string path)
            {
                _path = path;

                ContinuousAnimation = true;
                AnimationSpeed = 7f;
                BlendMesh = 1;
                BlendMeshState = BlendState.Additive;
                BlendMeshLight = 0.18f;
                LightEnabled = true;
                Light = new Vector3(0.08f, 0.03f, 0.01f); // dark, near-black rock
                IsTransparent = true;
                BlendState = BlendState.Additive;
                DepthState = DepthStencilState.DepthRead;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_path);
                await base.Load();
            }
        }

        private sealed class MeteorStoneModel : ModelObject
        {
            private readonly string _path;
            private Vector3 _velocity;
            private float _lifeFrames;

            public MeteorStoneModel(string path, float lifeFrames, Vector3 initialVelocity)
            {
                _path = path;
                _lifeFrames = lifeFrames;
                _velocity = initialVelocity;

                ContinuousAnimation = true;
                AnimationSpeed = 4f;
                LightEnabled = true;
                IsTransparent = false;
                DepthState = DepthStencilState.DepthRead;
            }

            public override async Task Load()
            {
                Model = await BMDLoader.Instance.Prepare(_path);
                await base.Load();
            }

            public override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
                if (Status != GameControlStatus.Ready)
                    return;

                float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
                Position += _velocity * (0.08f * factor);
                _velocity = new Vector3(_velocity.X * 0.98f, _velocity.Y * 0.98f, _velocity.Z - 1.2f * factor);
                _lifeFrames -= factor;

                if (_lifeFrames <= 0f)
                    RemoveSelf();
            }

            private void RemoveSelf()
            {
                if (Parent != null)
                    Parent.Children.Remove(this);
                else
                    World?.RemoveObject(this);

                Dispose();
            }
        }
    }
}
