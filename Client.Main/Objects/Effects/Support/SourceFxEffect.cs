#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Support
{
    /// <summary>
    /// Base for ports of SourceMain5.2 composite skill visuals that are built from JOINT
    /// ribbons, per-frame sprites (CreateSprite) and terrain bitmaps (RenderTerrainAlphaBitmap).
    /// Simulation runs at the original 25 Hz tick; sprites and ground bitmaps are
    /// "immediate" (re-emitted every tick, exactly like the C++ per-frame calls).
    /// All storage is preallocated; Update/Draw are allocation-free.
    /// </summary>
    public abstract class SourceFxEffect : EffectObject
    {
        protected const float SourceFrameRate = 25f;
        private const int MaxTicksPerUpdate = 5;

        private struct FxSprite
        {
            public int Slot;
            public Vector3 Position;
            public float Size;
            public float Rotation;
            public Color Color;
        }

        private struct FxGround
        {
            public int Slot;
            public Vector3 Position;
            public float SizeTiles;
            public float Rotation;
            public Color Color;
        }

        private readonly string[] _texturePaths;
        private readonly Texture2D?[] _textures;
        private readonly SamplerState[] _samplers;
        private readonly SourceFxTrail[] _trails;
        private readonly FxSprite[] _sprites;
        private readonly FxGround[] _grounds;
        private int _spriteCount;
        private int _groundCount;
        private float _tickAccumulator;
        private float _delayFrames;
        private bool _started;
        private bool _finished;

        /// <summary>Number of 25 Hz ticks run since the effect started.</summary>
        protected int TickCount { get; private set; }

        protected SourceFxTrail[] Trails => _trails;

        protected SourceFxEffect(
            string[] texturePaths,
            int trailCapacity,
            int spriteCapacity,
            int groundCapacity,
            float delayFrames = 0f)
        {
            _texturePaths = texturePaths;
            _textures = new Texture2D?[texturePaths.Length];
            _samplers = new SamplerState[texturePaths.Length];
            for (int i = 0; i < _samplers.Length; i++)
                _samplers[i] = SamplerState.LinearClamp;

            _trails = new SourceFxTrail[trailCapacity];
            for (int i = 0; i < trailCapacity; i++)
                _trails[i] = new SourceFxTrail();

            _sprites = new FxSprite[spriteCapacity];
            _grounds = new FxGround[groundCapacity];
            _delayFrames = delayFrames;

            IsTransparent = true;
            AffectedByTransparency = true;
            LightEnabled = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-1500f, -1500f, -300f),
                new Vector3(1500f, 1500f, 1500f));
        }

        /// <summary>Uses a wrapping sampler for a texture slot (tile-mapped thunder ribbons).</summary>
        protected void UseWrapSampler(int slot) => _samplers[slot] = SamplerState.LinearWrap;

        protected Texture2D? GetTexture(int slot) => _textures[slot];

        public override async Task LoadContent()
        {
            await base.LoadContent();
            for (int i = 0; i < _texturePaths.Length; i++)
            {
                try
                {
                    _textures[i] = await TextureLoader.Instance.PrepareAndGetTexture(_texturePaths[i]);
                }
                catch
                {
                    _textures[i] = null;
                }
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready || _finished)
                return;

            float frames = (float)gameTime.ElapsedGameTime.TotalSeconds * SourceFrameRate;
            _tickAccumulator += MathHelper.Clamp(frames, 0f, MaxTicksPerUpdate);

            int ticks = 0;
            while (_tickAccumulator >= 1f && ticks < MaxTicksPerUpdate && !_finished)
            {
                _tickAccumulator -= 1f;
                ticks++;
                RunTick();
            }

            if (_finished)
                Finish();
        }

        private void RunTick()
        {
            if (!_started)
            {
                if (_delayFrames > 0f)
                {
                    _delayFrames -= 1f;
                    if (_delayFrames > 0f)
                        return;
                }

                _started = true;
                if (!OnStart())
                {
                    _finished = true;
                    return;
                }
            }

            _spriteCount = 0;
            _groundCount = 0;
            if (!OnTick())
                _finished = true;
            TickCount++;
        }

        /// <summary>Called once after the cast delay. Return false to abort.</summary>
        protected virtual bool OnStart() => true;

        /// <summary>One 25 Hz frame of the original MoveEffect/MoveJoint. Return false when done.</summary>
        protected abstract bool OnTick();

        protected SourceFxTrail? AllocTrail(int slot, int maxTails, float scale)
        {
            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i].Active)
                    continue;
                _trails[i].Begin(slot, maxTails, scale);
                return _trails[i];
            }

            return null;
        }

        protected int ActiveTrailCount()
        {
            int count = 0;
            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i].Active)
                    count++;
            }

            return count;
        }

        /// <summary>CreateSprite: full world width = texture width * scale (RenderSprite).</summary>
        protected void EmitSprite(int slot, Vector3 position, float scale, Vector3 light, float rotationDeg = 0f)
        {
            if (_spriteCount >= _sprites.Length)
                return;

            var texture = _textures[slot];
            float width = (texture?.Width ?? 64) * scale;
            _sprites[_spriteCount++] = new FxSprite
            {
                Slot = slot,
                Position = position,
                Size = width,
                Rotation = rotationDeg,
                Color = new Color(light)
            };
        }

        /// <summary>RenderTerrainAlphaBitmap(slot, x, y, size, size, light, rotation).</summary>
        protected void EmitGround(int slot, Vector3 position, float sizeTiles, Vector3 light, float rotationDeg)
        {
            if (_groundCount >= _grounds.Length)
                return;

            _grounds[_groundCount++] = new FxGround
            {
                Slot = slot,
                Position = position,
                SizeTiles = sizeTiles,
                Rotation = rotationDeg,
                Color = new Color(light)
            };
        }

        protected float TerrainHeight(float x, float y, float fallback)
        {
            var terrain = World?.Terrain;
            return terrain != null ? terrain.RequestTerrainHeight(x, y) : fallback;
        }

        protected static float RandRange(int range) => MuGame.Random.Next(range);

        public override void Draw(GameTime gameTime)
        {
            if (!Visible || !_started || Status != GameControlStatus.Ready)
                return;

            var graphics = GraphicsManager.Instance;
            var device = graphics.GraphicsDevice;
            var effect = graphics.BasicEffect3D;
            var camera = Camera.Instance;
            if (device == null || effect == null || camera == null)
                return;

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
            Vector3 previousDiffuse = effect.DiffuseColor;
            float previousAlpha = effect.Alpha;

            try
            {
                device.BlendState = Blendings.OneOneAdditive;
                device.DepthStencilState = DepthStencilState.DepthRead;
                device.RasterizerState = RasterizerState.CullNone;

                effect.TextureEnabled = true;
                effect.VertexColorEnabled = true;
                effect.LightingEnabled = false;
                effect.DiffuseColor = Vector3.One;
                effect.Alpha = 1f;
                effect.World = Matrix.Identity;
                effect.View = camera.View;
                effect.Projection = camera.Projection;

                float scroll = (float)(gameTime.TotalGameTime.TotalMilliseconds % 1000.0) * 0.001f;

                for (int slot = 0; slot < _textures.Length; slot++)
                {
                    var texture = _textures[slot];
                    if (texture == null)
                        continue;

                    SourceFxBatch.Begin(device, effect, texture, _samplers[slot]);

                    for (int i = 0; i < _grounds.Length && i < _groundCount; i++)
                    {
                        ref FxGround g = ref _grounds[i];
                        if (g.Slot == slot)
                            SourceFxBatch.Ground(g.Position, g.SizeTiles, g.Rotation, g.Color);
                    }

                    for (int i = 0; i < _trails.Length; i++)
                    {
                        var trail = _trails[i];
                        if (trail.Active && trail.Slot == slot)
                            trail.Emit(scroll);
                    }

                    for (int i = 0; i < _spriteCount; i++)
                    {
                        ref FxSprite s = ref _sprites[i];
                        if (s.Slot == slot)
                            SourceFxBatch.Billboard(s.Position, s.Size, s.Rotation, s.Color);
                    }

                    SourceFxBatch.End();
                }
            }
            finally
            {
                device.BlendState = previousBlend;
                device.DepthStencilState = previousDepth;
                device.RasterizerState = previousRasterizer;
                device.SamplerStates[0] = previousSampler;
                effect.TextureEnabled = previousTextureEnabled;
                effect.VertexColorEnabled = previousVertexColorEnabled;
                effect.LightingEnabled = previousLightingEnabled;
                effect.Texture = previousTexture;
                effect.World = previousWorld;
                effect.View = previousView;
                effect.Projection = previousProjection;
                effect.DiffuseColor = previousDiffuse;
                effect.Alpha = previousAlpha;
            }
        }

        /// <summary>Releases owned children/lights; called once before removal.</summary>
        protected virtual void OnFinish()
        {
        }

        private bool _removed;
        private readonly SourceFxModel?[] _models = new SourceFxModel?[8];

        /// <summary>Adds an owned BMD model to the world; it is removed with this effect.</summary>
        protected SourceFxModel? SpawnModel(string path, bool bright, Vector3 position)
        {
            if (World == null)
                return null;

            for (int i = 0; i < _models.Length; i++)
            {
                if (_models[i] != null && _models[i]!.Status != GameControlStatus.Disposed)
                    continue;

                var model = new SourceFxModel(path, bright) { Position = position };
                _models[i] = model;
                World.Objects.Add(model);
                _ = model.Load();
                return model;
            }

            return null;
        }

        private void RemoveModels()
        {
            for (int i = 0; i < _models.Length; i++)
            {
                _models[i]?.Remove();
                _models[i] = null;
            }
        }

        private void Finish()
        {
            if (_removed)
                return;
            _removed = true;
            OnFinish();
            RemoveModels();

            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);
            Dispose();
        }

        public override void Dispose()
        {
            if (!_removed)
            {
                _removed = true;
                OnFinish();
            }

            RemoveModels();
            for (int i = 0; i < _trails.Length; i++)
            {
                _trails[i].Active = false;
                _trails[i].Target = null;
            }

            base.Dispose();
        }

        // ---- shared helpers -------------------------------------------------

        protected static bool IsAlive(WalkerObject? walker) =>
            walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;

        /// <summary>Random point on a model bone (TransformByObjectBone(rand() % NumBones)).</summary>
        protected static Vector3 RandomBonePosition(WalkerObject walker, Vector3 localOffset)
        {
            Matrix[]? bones = walker.GetBoneTransforms();
            Matrix world = walker.WorldPosition;
            if (bones == null || bones.Length == 0)
                return world.Translation + new Vector3(0f, 0f, 100f);

            int index = MuGame.Random.Next(bones.Length);
            return Vector3.Transform(localOffset, bones[index] * world);
        }

        protected static Vector3 BonePosition(WalkerObject walker, int boneIndex)
        {
            Matrix[]? bones = walker.GetBoneTransforms();
            if (bones == null || boneIndex < 0 || boneIndex >= bones.Length)
                return walker.WorldPosition.Translation + new Vector3(0f, 0f, 100f);
            return (bones[boneIndex] * walker.WorldPosition).Translation;
        }

        protected static float SourceYaw(WalkerObject walker) => MathHelper.ToDegrees(walker.Angle.Z);
    }
}
