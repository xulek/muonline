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

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Shared skeleton for the SourceMain5.2 skill ports: waits for the caster animation to
    /// reach the original trigger frame (AttackStage checks such as
    /// <c>o-&gt;AnimationFrame &gt;= 3</c>), advances legacy 25 Hz frame counters through
    /// FPS_ANIMATION_FACTOR, owns a <see cref="SourceQuadBatch"/> and removes itself once
    /// <see cref="HasActiveContent"/> turns false. Works for any <see cref="WalkerObject"/>.
    /// </summary>
    internal abstract class SourceSkillEffectBase : EffectObject
    {
        private readonly float _triggerFrame;
        private readonly float _triggerTimeoutSeconds;
        private readonly double? _createdAtSeconds;
        private float _waitSeconds;
        private bool _started;
        private bool _disposed;
        private bool _releaseImmediately;

        protected SourceSkillEffectBase(WalkerObject caster, float triggerFrame, float triggerTimeoutSeconds, int maxQuads)
        {
            Caster = caster ?? throw new ArgumentNullException(nameof(caster));
            _triggerFrame = triggerFrame;
            _triggerTimeoutSeconds = triggerTimeoutSeconds;
            _createdAtSeconds = MuGame.Instance?.GameTime?.TotalGameTime.TotalSeconds;
            Batch = new SourceQuadBatch(maxQuads);

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Blendings.OneOneAdditive;
            DepthState = DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(new Vector3(-1500f, -1500f, -400f), new Vector3(1500f, 1500f, 1200f));
        }

        protected WalkerObject Caster { get; }
        protected SourceQuadBatch Batch { get; }
        protected bool Started => _started;

        /// <summary>
        /// Local hero cast: start at the click instead of waiting for the AttackStage release gate (the server
        /// applies the damage at request time, so the gated visual would trail the damage numbers).
        /// </summary>
        internal void ReleaseImmediately() => _releaseImmediately = true;

        protected bool IsReleasedImmediately => _releaseImmediately;

        /// <summary>GameTime of the current Update, for ticking owned model renderers.</summary>
        protected GameTime LastGameTime { get; private set; } = new GameTime();

        public override async Task LoadContent()
        {
            await base.LoadContent();
            Batch.Initialize(GraphicsDevice);
            await LoadEffectContent();
        }

        protected abstract Task LoadEffectContent();

        /// <summary>Called once when the trigger frame is reached.</summary>
        protected abstract void OnStart();

        /// <summary>One update with the legacy frame factor (1 = one 25 Hz frame).</summary>
        protected abstract void OnTick(float factor);

        protected abstract bool HasActiveContent();

        protected abstract void OnDraw();

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            LastGameTime = gameTime;

            if (Status == GameControlStatus.NonInitialized)
                _ = Load();

            if (Status != GameControlStatus.Ready || _disposed)
                return;

            if (Caster.Status == GameControlStatus.Disposed || Caster.World == null)
            {
                RemoveSelf();
                return;
            }

            if (!_started)
            {
                // AttackTime starts with the skill packet, while texture/model loading may
                // keep this object out of the world update list for several frames.
                _waitSeconds = _createdAtSeconds.HasValue
                    ? (float)Math.Max(0d, gameTime.TotalGameTime.TotalSeconds - _createdAtSeconds.Value)
                    : _waitSeconds + (float)gameTime.ElapsedGameTime.TotalSeconds;
                if (!_releaseImmediately && _triggerFrame > 0f && CasterFrame < _triggerFrame && _waitSeconds < _triggerTimeoutSeconds)
                    return;

                _started = true;
                Position = Caster.WorldPosition.Translation;
                OnStart();
            }

            OnTick(FPSCounter.Instance.FPS_ANIMATION_FACTOR);

            if (!HasActiveContent())
                RemoveSelf();
        }

        public override void DrawAfter(GameTime gameTime)
        {
            base.DrawAfter(gameTime);

            if (!Visible || Status != GameControlStatus.Ready || !_started || _disposed)
                return;

            OnDraw();
        }

        // ----------------------------------------------------------------- helpers

        protected static Texture2D? GetTexture(string path) =>
            TextureLoader.Instance.GetTexture2D(path) ?? GraphicsManager.Instance.Pixel;

        protected static async Task<Texture2D> LoadTexture(string path)
        {
            await TextureLoader.Instance.Prepare(path);
            return await TextureLoader.Instance.PrepareAndGetTexture(path) ?? GraphicsManager.Instance.Pixel;
        }

        protected async Task<SourceModelRenderer?> LoadModel(string path, int blendMesh = -2, int hiddenMesh = -1)
        {
            if (!await BMDLoader.Instance.AssestExist(path))
                return null;

            var renderer = new SourceModelRenderer(path, blendMesh, hiddenMesh);
            return await renderer.LoadInto(World) ? renderer : null;
        }

        /// <summary>
        /// Plays a sound the original client plays for this skill unless the packet handler
        /// already maps one through <see cref="Client.Data.BMD.SkillDefinitions.GetSkillSound"/>.
        /// </summary>
        protected static void PlayUnmappedSound(int skillId, string path)
        {
            if (string.IsNullOrEmpty(Client.Data.BMD.SkillDefinitions.GetSkillSound(skillId)))
                SoundController.Instance.PlayBuffer(path);
        }

        protected Vector3 CasterPosition => Caster.WorldPosition.Translation;

        /// <summary>C++ o->AnimationFrame of the caster (fractional key position of the current action).</summary>
        protected float CasterFrame => (float)Caster.AnimationFramePosition;

        protected float CasterYawDegrees => MathHelper.ToDegrees(Caster.Angle.Z);

        /// <summary>C++ BoneTransform[bone] origin of the caster (falls back to the caster origin + height).</summary>
        protected Vector3 GetCasterBonePosition(int bone, float fallbackHeight = 100f)
        {
            if (Caster is PlayerObject player && player.TryGetBoneWorldMatrix(bone, out Matrix matrix))
                return matrix.Translation;

            return CasterPosition + Vector3.UnitZ * fallbackHeight;
        }

        protected float TerrainHeight(Vector3 position) =>
            World?.Terrain?.RequestTerrainHeight(position.X, position.Y) ?? position.Z;

        protected static float RandomRange(int range) => MuGame.Random.Next(range);

        protected static bool FpsCheck(int referenceFrames, float factor)
        {
            // rand_fps_check(n): one chance in n per 25 Hz frame.
            return MuGame.Random.NextDouble() < MathHelper.Clamp(factor / referenceFrames, 0f, 1f);
        }

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

        protected virtual void DisposeEffect()
        {
        }

        public override void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DisposeEffect();
            Batch.Dispose();
            base.Dispose();
        }
    }
}
