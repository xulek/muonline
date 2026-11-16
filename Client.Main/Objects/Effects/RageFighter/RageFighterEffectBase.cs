#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Shared lifecycle for the Rage Fighter skill visuals (SourceMain5.2 MonkSystem.cpp
    /// RageCreateEffect and the MODEL_* / BITMAP_* objects it spawns in ZzzEffect.cpp).
    ///
    /// Owns: a fixed set of textures, fixed particle pools and trails rendered through one
    /// <see cref="RageFxBatch"/>, a fixed array of owner-driven <see cref="RageFxModel"/>s and
    /// optional dynamic lights. Everything is released in <see cref="Dispose"/>.
    /// Works for any <see cref="WalkerObject"/> caster: bone lookups fall back to the caster
    /// position when the caster is not a player model.
    /// </summary>
    internal abstract class RageFighterEffectBase : EffectObject
    {
        protected const float LegacyFps = 25f;

        protected readonly WalkerObject Caster;
        private readonly string[] _texturePaths;
        private readonly Texture2D?[] _textures;
        private readonly RageFxModel?[] _models;
        private readonly RageParticlePool[] _pools;
        private readonly RageFxTrail[] _trails;
        private readonly DynamicLight?[] _lights;
        private readonly bool[] _lightsAdded;
        private readonly RageFxBatch _batch = new();
        private TerrainControl? _lightTerrain;
        private bool _initialized;
        private bool _disposed;

        protected RageFighterEffectBase(
            WalkerObject caster,
            string[] texturePaths,
            int modelCapacity,
            RageParticlePool[] pools,
            RageFxTrail[]? trails = null,
            int lightCount = 0)
        {
            Caster = caster ?? throw new ArgumentNullException(nameof(caster));
            _texturePaths = texturePaths;
            _textures = new Texture2D?[texturePaths.Length];
            _models = new RageFxModel?[Math.Max(0, modelCapacity)];
            _pools = pools;
            _trails = trails ?? Array.Empty<RageFxTrail>();
            _lights = new DynamicLight?[lightCount];
            _lightsAdded = new bool[lightCount];

            IsTransparent = true;
            AffectedByTransparency = false;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(new Vector3(-1200f, -1200f, -300f), new Vector3(1200f, 1200f, 900f));
        }

        /// <summary>Frames elapsed since the effect started (25 FPS clock).</summary>
        protected float ElapsedFrames { get; private set; }

        protected WalkableWorldControl? WalkableWorld => World as WalkableWorldControl;

        public override async Task LoadContent()
        {
            await base.LoadContent();
            for (int i = 0; i < _texturePaths.Length; i++)
            {
                _textures[i] = await TextureLoader.Instance.PrepareAndGetTexture(_texturePaths[i])
                    ?? GraphicsManager.Instance.Pixel;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready || _disposed)
                return;

            if (Caster.Status == GameControlStatus.Disposed || Caster.World == null || World == null)
            {
                RemoveSelf();
                return;
            }

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            if (!_initialized)
            {
                _initialized = true;
                Position = Caster.WorldPosition.Translation;
                OnStart();
                if (_disposed)
                    return;
            }

            for (int i = 0; i < _pools.Length; i++)
                _pools[i].Update(factor);
            for (int i = 0; i < _trails.Length; i++)
                _trails[i].Update(factor);

            bool alive = OnUpdate(factor);
            ElapsedFrames += factor;

            if (!alive && !HasLiveParticles())
                RemoveSelf();
        }

        /// <summary>Called once on the first ready frame.</summary>
        protected abstract void OnStart();

        /// <summary>Per-frame logic. Return false once the effect emits nothing more.</summary>
        protected abstract bool OnUpdate(float factor);

        /// <summary>Ground decals are written here (texture slot -> decals).</summary>
        protected virtual void WriteDecals(RageFxBatch batch, int textureSlot)
        {
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || !_initialized || _disposed)
                return;

            GraphicsDevice device = GraphicsDevice;
            for (int slot = 0; slot < _textures.Length; slot++)
            {
                Texture2D? texture = _textures[slot];
                if (texture == null)
                    continue;

                _batch.Begin();
                WriteDecals(_batch, slot);
                for (int i = 0; i < _pools.Length; i++)
                {
                    if (_pools[i].TextureSlot == slot && _pools[i].Count > 0)
                        _pools[i].Write(_batch, texture.Width, texture.Height);
                }

                for (int i = 0; i < _trails.Length; i++)
                {
                    if (_trails[i].TextureSlot == slot)
                        _trails[i].Write(_batch);
                }

                _batch.Flush(device, texture);
            }
        }

        private bool HasLiveParticles()
        {
            for (int i = 0; i < _pools.Length; i++)
            {
                if (_pools[i].Count > 0)
                    return true;
            }

            for (int i = 0; i < _trails.Length; i++)
            {
                if (_trails[i].HasLiveSamples)
                    return true;
            }

            return false;
        }

        // ----------------------------------------------------------------- models

        /// <summary>Spawns an owner-driven model into a free slot; returns the slot or -1.</summary>
        protected int SpawnModel(RageFxModel model, Vector3 position, Vector3 angle, float scale, Vector3 light)
        {
            if (World == null)
                return -1;

            int slot = -1;
            for (int i = 0; i < _models.Length; i++)
            {
                if (_models[i] == null)
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
                return -1;

            model.Position = position;
            model.Angle = angle;
            model.Scale = scale;
            model.Light = light;
            _models[slot] = model;
            World.Objects.Add(model);
            _ = model.Load();
            return slot;
        }

        protected RageFxModel? GetModel(int slot) =>
            slot >= 0 && slot < _models.Length ? _models[slot] : null;

        protected void KillModel(ref int slot)
        {
            if (slot >= 0 && slot < _models.Length)
            {
                _models[slot]?.Kill();
                _models[slot] = null;
            }

            slot = -1;
        }

        protected bool HasFreeModelSlot()
        {
            for (int i = 0; i < _models.Length; i++)
            {
                if (_models[i] == null)
                    return true;
            }

            return false;
        }

        // ----------------------------------------------------------------- multi shot rings

        private const int MultiShotCount = 7;
        private int[]? _multiShots;
        private float[]? _multiShotLife;
        private float[]? _multiShotGrowth;

        /// <summary>
        /// MonkSystem.cpp (AT_SKILL_DRAGON_KICK / AT_SKILL_OCCUPY): 2x MODEL_MULTI_SHOT3 and
        /// 2x MODEL_MULTI_SHOT2 at caster + (0, 20, 0), 3x MODEL_MULTI_SHOT1 at the caster, all
        /// with light (0.8, 0.9, 1.6). CreateEffect adds the (-10, -60, 135) hand offset; each ring
        /// starts at scale 0 and grows 0.25 / 0.2 / 0.3 per frame, fading with LifeTime / 18
        /// (life 12 / 16 / 13).
        /// </summary>
        protected void SpawnMultiShots(float yawDegrees)
        {
            _multiShots ??= new[] { -1, -1, -1, -1, -1, -1, -1 };
            _multiShotLife ??= new float[MultiShotCount];
            _multiShotGrowth ??= new float[MultiShotCount];

            Vector3 angle = new(Caster.Angle.X, Caster.Angle.Y, MathHelper.ToRadians(yawDegrees));
            float yaw = MathHelper.ToRadians(yawDegrees);
            Vector3 hand = RotateYaw(new Vector3(-10f, -60f, 135f), yaw);
            Vector3 forward = RotateYaw(new Vector3(0f, 20f, 0f), yaw);
            Vector3 light = new(0.8f, 0.9f, 1f);

            for (int i = 0; i < MultiShotCount; i++)
            {
                string path;
                Vector3 offset;
                if (i < 2) { path = "Effect/multishot03.bmd"; offset = forward; _multiShotLife[i] = 12f; _multiShotGrowth[i] = 0.25f; }
                else if (i < 5) { path = "Effect/multishot01.bmd"; offset = Vector3.Zero; _multiShotLife[i] = 16f; _multiShotGrowth[i] = 0.2f; }
                else { path = "Effect/multishot02.bmd"; offset = forward; _multiShotLife[i] = 13f; _multiShotGrowth[i] = 0.3f; }

                if (_multiShots[i] >= 0)
                    KillModel(ref _multiShots[i]);
                _multiShots[i] = SpawnModel(new RageFxModel(path, playSpeed: 1f / 6f), CasterPosition + offset + hand, angle,
                    0f, light);
            }
        }

        /// <returns>true while any ring is alive.</returns>
        protected bool UpdateMultiShots(float factor)
        {
            if (_multiShots == null || _multiShotLife == null || _multiShotGrowth == null)
                return false;

            bool alive = false;
            for (int i = 0; i < MultiShotCount; i++)
            {
                RageFxModel? ring = GetModel(_multiShots[i]);
                if (ring == null)
                    continue;

                _multiShotLife[i] -= factor;
                if (_multiShotLife[i] <= 0f)
                {
                    KillModel(ref _multiShots[i]);
                    continue;
                }

                alive = true;
                ring.Scale += _multiShotGrowth[i] * factor;
                ring.BlendMeshLight = _multiShotLife[i] / 18f;
            }

            return alive;
        }

        // ----------------------------------------------------------------- lights

        protected void SetLight(int index, Vector3 position, Vector3 color, float radius, float intensity)
        {
            if (index < 0 || index >= _lights.Length || World?.Terrain == null)
                return;

            DynamicLight light = _lights[index] ??= new DynamicLight { Owner = this };
            light.Position = position;
            light.Color = color;
            light.Radius = radius;
            light.Intensity = intensity;

            if (!_lightsAdded[index] && intensity > 0f)
            {
                _lightTerrain = World.Terrain;
                _lightTerrain.AddDynamicLight(light);
                _lightsAdded[index] = true;
            }
        }

        // ----------------------------------------------------------------- caster helpers

        /// <summary>TransformByObjectBone(out, caster, bone, relative). Falls back to the caster position + height.</summary>
        protected Vector3 CasterBone(int boneIndex, Vector3 relative, float fallbackHeight = 120f)
        {
            if (Caster is ModelObject model)
            {
                Matrix[] bones = model.GetBoneTransforms();
                if (bones != null && boneIndex >= 0 && boneIndex < bones.Length && Caster is PlayerObject)
                    return Vector3.Transform(relative, bones[boneIndex] * Caster.WorldPosition);
            }

            Vector3 position = Caster.WorldPosition.Translation;
            position.Z += fallbackHeight;
            return position + RotateYaw(new Vector3(relative.X, relative.Y, 0f), Caster.Angle.Z);
        }

        protected Vector3 CasterPosition => Caster.WorldPosition.Translation;

        protected float CasterYawDegrees => MathHelper.ToDegrees(Caster.Angle.Z);

        protected static Vector3 RotateYaw(Vector3 local, float yawRadians) =>
            Vector3.TransformNormal(local, Matrix.CreateRotationZ(yawRadians));

        /// <summary>AngleMatrix(angleDegrees) * local, i.e. VectorRotate in the original.</summary>
        protected static Vector3 RotateDegrees(Vector3 local, Vector3 angleDegrees) =>
            Joints.SourceJointMath.Rotate(local, angleDegrees);

        protected static Vector3 ToRadians(Vector3 degrees) =>
            new(MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Y), MathHelper.ToRadians(degrees.Z));

        private ushort _targetId;
        private Vector3? _targetFallback;
        private WalkerObject? _target;

        protected bool HasTarget => _targetId != 0 || _targetFallback.HasValue;

        /// <summary>Stores the skill target (network id and/or the position the packet resolved).</summary>
        public void SetTarget(ushort targetId, Vector3? targetPosition)
        {
            _targetId = (ushort)(targetId & 0x7FFF);
            _targetFallback = targetPosition;
            _target = null;
        }

        /// <summary>CharactersClient[m_sTargetIndex] - the live target walker when it is known.</summary>
        protected WalkerObject? TargetWalker
        {
            get
            {
                if (_target != null && _target.Status != GameControlStatus.Disposed && _target.World != null)
                    return _target;

                _target = null;
                if (_targetId != 0 && TryGetWalker(_targetId, out WalkerObject walker))
                    _target = walker;
                return _target;
            }
        }

        /// <summary>Target position, falling back to the packet position or 150 units in front of the caster.</summary>
        protected Vector3 TargetPosition
        {
            get
            {
                WalkerObject? target = TargetWalker;
                if (target != null)
                {
                    _targetFallback = target.WorldPosition.Translation;
                    return _targetFallback.Value;
                }

                if (_targetFallback.HasValue)
                    return _targetFallback.Value;

                return CasterPosition + RotateYaw(new Vector3(0f, -150f, 0f), Caster.Angle.Z);
            }
        }

        /// <summary>CreateAngle2D(caster, target) in degrees, the caster yaw without a target.</summary>
        protected float YawToTargetDegrees
        {
            get
            {
                if (!HasTarget)
                    return CasterYawDegrees;

                Vector3 delta = TargetPosition - CasterPosition;
                if (delta.X * delta.X + delta.Y * delta.Y < 1f)
                    return CasterYawDegrees;

                // Model forward is local -Y: yaw = atan2(-dx, dy) rotated so that RotateYaw((0,-1,0)) points at the target.
                return MathHelper.ToDegrees(MathF.Atan2(delta.X, -delta.Y));
            }
        }

        protected float GroundHeight(float x, float y) =>
            World?.Terrain?.RequestTerrainHeight(x, y) ?? CasterPosition.Z;

        protected bool TryGetWalker(ushort networkId, out WalkerObject walker)
        {
            walker = null!;
            if (networkId == 0 || WalkableWorld == null)
                return false;

            return WalkableWorld.TryGetWalkerById((ushort)(networkId & 0x7FFF), out walker) && walker != null;
        }

        /// <summary>
        /// The caster's AnimationFrame for <paramref name="action"/>. While the caster plays
        /// that action its real animation key is used; otherwise (remote casters whose action
        /// is driven elsewhere, or a different mapped action) the key is estimated from the
        /// elapsed time and the action's PlaySpeed, so every timing gate still fires.
        /// </summary>
        protected float CasterActionFrame(PlayerAction action, float startFrame = 0f)
        {
            if (Caster is ModelObject model && model.CurrentAction == (int)action)
                return (float)model.GetAnimationTime();

            return startFrame + ElapsedFrames * ActionPlaySpeed(action);
        }

        protected float ActionPlaySpeed(PlayerAction action)
        {
            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)action < actions.Length && actions[(int)action] != null && actions[(int)action].PlaySpeed > 0f)
            {
                return actions[(int)action].PlaySpeed;
            }

            return 0.4f;
        }

        protected int ActionKeyCount(PlayerAction action, int fallback = 12)
        {
            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)action < actions.Length && actions[(int)action] != null)
            {
                return Math.Max(1, actions[(int)action].NumAnimationKeys);
            }

            return fallback;
        }

        protected static float RandomRange(float min, float max) =>
            min + (float)MuGame.Random.NextDouble() * (max - min);

        protected static int Rand(int maxExclusive) => MuGame.Random.Next(Math.Max(1, maxExclusive));

        /// <summary>rand_fps_check(1): one emission per legacy frame regardless of the real frame rate.</summary>
        protected static bool FpsCheck(float factor) =>
            factor >= 1f || MuGame.Random.NextDouble() < factor;

        protected static void PlaySound(string path) => SoundController.Instance.PlayBuffer(path);

        /// <summary>
        /// BITMAP_FIRE_HIK1 / HIK3 / FIRE_CURSEDLICH rising flame (ZzzEffectParticle.cpp sub 0):
        /// life 27..31, scale (0.72..1.44) * scale, shrinking 0.05..0.07, rising 6.4..8.8 per frame,
        /// spinning 3 deg, alpha fades in 0.2..0.3 per frame and out below life 15.
        /// </summary>
        protected static void SpawnRisingFire(RageParticlePool pool, Vector3 position, float scale, Vector3 light,
            float lifeScale = 1f)
        {
            ref RageParticle p = ref pool.Spawn(position, RandomRange(0.72f, 1.44f) * scale,
                (27f + Rand(5)) * lifeScale, light);
            p.Rotation = Rand(360);
            p.RotationSpeed = 3f;
            p.Velocity = new Vector3(0f, 0f, RandomRange(6.4f, 8.8f));
            p.ScaleAdd = -RandomRange(0.05f, 0.07f);
            p.Alpha = 0f;
            p.FadeIn = RandomRange(0.2f, 0.3f);
            p.FadeOutBelow = 15f * lifeScale;
        }

        /// <summary>BITMAP_SPARK sub 2 (CreateBomb sparks): ballistic, falling, short-lived.</summary>
        protected static void SpawnSpark(RageParticlePool pool, Vector3 position, Vector3 light, float scale = 0.6f,
            float speed = 6f)
        {
            float yaw = MathHelper.ToRadians(Rand(360));
            float horizontal = RandomRange(0.3f, 1f) * speed;
            ref RageParticle p = ref pool.Spawn(position, RandomRange(0.7f, 1.2f) * scale, 18f + Rand(12), light);
            p.Velocity = new Vector3(MathF.Cos(yaw) * horizontal, MathF.Sin(yaw) * horizontal, RandomRange(4f, 12f));
            p.Gravity = 1.2f;
            p.Rotation = Rand(360);
            p.LinearFade = 1f;
        }

        // ----------------------------------------------------------------- cleanup

        protected void RemoveSelf()
        {
            if (_disposed)
                return;

            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            for (int i = 0; i < _models.Length; i++)
            {
                _models[i]?.Kill();
                _models[i] = null;
            }

            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lightsAdded[i] && _lights[i] != null)
                    _lightTerrain?.RemoveDynamicLight(_lights[i]!);
                _lightsAdded[i] = false;
                _lights[i] = null;
            }

            for (int i = 0; i < _pools.Length; i++)
                _pools[i].Clear();
            for (int i = 0; i < _trails.Length; i++)
                _trails[i].Reset();

            _batch.Dispose();
            base.Dispose();
        }
    }
}
