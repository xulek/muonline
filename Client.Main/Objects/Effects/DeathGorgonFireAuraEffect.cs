#nullable enable
using System;
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
    /// Persistent fiery cloud aura used by Death Gorgon.
    /// Tuned for low CPU/GPU overhead while keeping chaotic fire motion.
    /// </summary>
    public sealed class DeathGorgonFireAuraEffect : EffectObject
    {
        private const string PrimaryTexturePath = "Effect/Flame01.jpg";
        private const string SecondaryTexturePath = "Effect/firehik01.jpg";
        private const string GlowTexturePath = "Effect/flare.jpg";

        private const int MaxParticles = 72;
        private const int CoreCloudQuads = 3;
        private const int MaxQuads = MaxParticles + CoreCloudQuads;

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
        private readonly short[] _indices = new short[MaxQuads * 6];

        private readonly FireParticle[] _particles = new FireParticle[MaxParticles];
        private int _particleCount;
        private float _spawnTimer;
        private float _time;
        private float _fade = 1f;
        private bool _active = true;

        private Texture2D _primaryTexture = null!;
        private Texture2D _secondaryTexture = null!;
        private Texture2D _glowTexture = null!;

        private readonly DynamicLight _auraLight;
        private bool _lightAdded;

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

        public DeathGorgonFireAuraEffect()
        {
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

            InitializeIndices();
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

            _primaryTexture = TextureLoader.Instance.GetTexture2D(PrimaryTexturePath) ?? GraphicsManager.Instance.Pixel;
            _secondaryTexture = TextureLoader.Instance.GetTexture2D(SecondaryTexturePath) ?? _primaryTexture;
            _glowTexture = TextureLoader.Instance.GetTexture2D(GlowTexturePath) ?? GraphicsManager.Instance.Pixel;

            if (World?.Terrain != null && !_lightAdded)
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

            _time += dt;

            float targetFade = _active ? 1f : 0f;
            float lerpFactor = MathHelper.Clamp(dt * 7f, 0f, 1f);
            _fade = MathHelper.Lerp(_fade, targetFade, lerpFactor);

            if (_active)
                SpawnParticles(dt);

            UpdateParticles(dt);
            UpdateLight();

            if (!_active && _fade < 0.02f && _particleCount == 0)
                Hidden = true;
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            if (!Visible || (_particleCount == 0 && _fade <= 0.01f))
                return;

            if (_primaryTexture == null || _glowTexture == null)
                return;

            DrawAura();
        }

        private void DrawAura()
        {
            var gd = GraphicsManager.Instance.GraphicsDevice;
            var effect = GraphicsManager.Instance.BasicEffect3D;
            var camera = Camera.Instance;
            if (effect == null || camera == null)
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
            effect.View = camera.View;
            effect.Projection = camera.Projection;

            Matrix worldInverse = Matrix.Invert(WorldPosition);
            Vector3 localCameraPosition = Vector3.Transform(camera.Position, worldInverse);

            int quadIndex = 0;

            int primaryStart = quadIndex;
            BuildParticles(localCameraPosition, 0, ref quadIndex);
            int primaryCount = quadIndex - primaryStart;

            int secondaryStart = quadIndex;
            BuildParticles(localCameraPosition, 1, ref quadIndex);
            int secondaryCount = quadIndex - secondaryStart;

            int glowStart = quadIndex;
            BuildCoreCloud(localCameraPosition, ref quadIndex);
            int glowCount = quadIndex - glowStart;

            if (primaryCount > 0)
            {
                effect.Texture = _primaryTexture;
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        _vertices, primaryStart * 4, primaryCount * 4,
                        _indices, primaryStart * 6, primaryCount * 2);
                }
            }

            if (secondaryCount > 0)
            {
                effect.Texture = _secondaryTexture;
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        _vertices, secondaryStart * 4, secondaryCount * 4,
                        _indices, secondaryStart * 6, secondaryCount * 2);
                }
            }

            if (glowCount > 0)
            {
                effect.Texture = _glowTexture;
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        _vertices, glowStart * 4, glowCount * 4,
                        _indices, glowStart * 6, glowCount * 2);
                }
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

        private void BuildParticles(Vector3 localCameraPosition, byte variant, ref int quadIndex)
        {
            for (int i = 0; i < _particleCount; i++)
            {
                ref var particle = ref _particles[i];
                if (particle.TextureVariant != variant)
                    continue;

                float lifeT = particle.Age / particle.Life;
                if (lifeT >= 1f)
                    continue;

                float fadeIn = MathHelper.Clamp(particle.Age / 0.1f, 0f, 1f);
                float fadeOut = 1f - lifeT;
                float alpha = fadeIn * fadeOut * _fade * TotalAlpha;
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
                    particle.Position,
                    localCameraPosition,
                    width,
                    height,
                    particle.Phase,
                    color,
                    ref quadIndex);
            }
        }

        private void BuildCoreCloud(Vector3 localCameraPosition, ref int quadIndex)
        {
            for (int i = 0; i < CoreCloudQuads; i++)
            {
                float phase = _time * (1.05f + i * 0.17f) + i * 1.9f;
                float pulse = 0.84f + 0.16f * (0.5f + 0.5f * MathF.Sin(phase));
                float alpha = (0.22f + 0.14f * pulse) * _fade * TotalAlpha;
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
                BuildBillboard(localPosition, localCameraPosition, width, height, phase, color, ref quadIndex);
            }
        }

        private void BuildBillboard(
            Vector3 position,
            Vector3 localCameraPosition,
            float width,
            float height,
            float phase,
            Color color,
            ref int quadIndex)
        {
            if (quadIndex >= MaxQuads)
                return;

            Vector3 toCamera = localCameraPosition - position;
            if (toCamera.LengthSquared() < 0.001f)
                toCamera = Vector3.UnitY;
            toCamera.Normalize();

            Vector3 right = Vector3.Cross(Vector3.UnitZ, toCamera);
            if (right.LengthSquared() < 0.001f)
                right = Vector3.UnitX;
            right.Normalize();

            Vector3 up = Vector3.Cross(toCamera, right);
            up.Normalize();

            float distortionX = MathF.Sin(_time * 7.8f + phase * 1.15f) * width * 0.07f;
            float distortionY = MathF.Sin(_time * 4.9f + phase * 1.75f) * height * 0.04f;
            Vector3 distortedPosition = position + right * distortionX + up * distortionY;

            Vector3 r = right * (width * 0.5f);
            Vector3 u = up * (height * 0.5f);

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

            while (_spawnTimer >= spawnInterval && _particleCount < MaxParticles)
            {
                _spawnTimer -= spawnInterval;
                SpawnParticle();
            }
        }

        private void SpawnParticle()
        {
            if (_particleCount >= MaxParticles)
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

                float lifeT = p.Age / p.Life;
                float turbulence = (1f - lifeT) * 12f;

                p.Position += p.Velocity * dt;
                p.Position.X += MathF.Cos(p.Phase + _time * 3.1f) * turbulence * dt;
                p.Position.Y += MathF.Sin(p.Phase * 1.37f + _time * 2.6f) * turbulence * dt;

                p.Velocity.Z += 16f * dt;
                p.Velocity.X *= 0.99f;
                p.Velocity.Y *= 0.99f;

                i++;
            }
        }

        private void UpdateLight()
        {
            _auraLight.Position = WorldPosition.Translation + new Vector3(0f, 0f, 84f);

            if (_fade <= 0.01f)
            {
                _auraLight.Intensity = 0f;
                return;
            }

            float flicker = 0.86f + 0.14f * MathF.Sin(_time * 12.2f + 0.8f);
            _auraLight.Intensity = (0.8f + 0.3f * flicker) * _fade;
            _auraLight.Radius = 215f + 12f * MathF.Sin(_time * 4.2f);
        }

        private void InitializeIndices()
        {
            for (int i = 0; i < MaxQuads; i++)
            {
                int vi = i * 4;
                int ii = i * 6;
                _indices[ii] = (short)vi;
                _indices[ii + 1] = (short)(vi + 1);
                _indices[ii + 2] = (short)(vi + 2);
                _indices[ii + 3] = (short)vi;
                _indices[ii + 4] = (short)(vi + 2);
                _indices[ii + 5] = (short)(vi + 3);
            }
        }

        private static float RandomRange(float min, float max)
        {
            return (float)(MuGame.Random.NextDouble() * (max - min) + min);
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
