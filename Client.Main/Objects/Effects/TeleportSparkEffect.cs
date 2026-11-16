#nullable enable
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 CreateTeleportBegin / CreateTeleportEnd (ZzzEffectMagicSkill.cpp:148/158):
    /// CreateEffect(BITMAP_SPARK + 1) at the character position (fixed, not following) and SOUND_MAGIC.
    /// effect   ZzzEffect.cpp:996 (LifeTime 10), 8789 (move: 18 x rand_fps_check(1) column of BITMAP_SPARK+1 sub 1,
    ///          24 units apart, scale 12 for the first / 6, light LifeTime * 0.1)
    /// particle ZzzEffectParticle.cpp:2132 (sub 1: random angle, Velocity rot(0,-50,0)), 6641 (Scale -= 2 * F)
    /// </summary>
    internal sealed class TeleportSparkEffect : SourceSkillEffectBase
    {
        private const string SparkTexturePath = "Effect/Spark03.jpg"; // BITMAP_SPARK + 1
        private const string SoundPath = "Sound/sMagic.wav";         // SOUND_MAGIC
        private const float ColumnScale = 6f;

        private struct Particle
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Velocity;
            public float Light;
            public float Scale;
            public float LifeTime;
        }

        private readonly Vector3 _position;
        private readonly bool _playSound;
        private readonly Particle[] _particles = new Particle[96];
        private Texture2D? _sparkTexture;
        private float _lifeTime = 10f;

        public TeleportSparkEffect(WalkerObject owner, Vector3 position, bool playSound)
            : base(owner, 0f, 0f, 128)
        {
            _position = position;
            _playSound = playSound;
        }

        /// <summary>Spawns the teleport flash at <paramref name="position"/> (begin or end of a teleport).</summary>
        public static TeleportSparkEffect? Spawn(WorldControl? world, Vector3 position, WalkerObject? owner, bool playSound = true)
        {
            if (world == null || owner == null)
                return null;

            var effect = new TeleportSparkEffect(owner, position, playSound);
            world.Objects.Add(effect);
            _ = effect.Load();
            return effect;
        }

        protected override async Task LoadEffectContent()
        {
            _sparkTexture = await LoadTexture(SparkTexturePath);
        }

        protected override void OnStart()
        {
            if (_playSound)
                SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_lifeTime > 0f)
            {
                float light = _lifeTime * 0.1f;
                Vector3 position = _position;
                for (int j = 0; j < 18; j++)
                {
                    if (!FpsCheck(1, factor))
                        continue;

                    position.Z += ColumnScale * 4f;
                    if (FpsCheck(1, factor))
                        SpawnParticle(position, light, j == 0 ? ColumnScale * 2f : ColumnScale);
                }
                _lifeTime -= factor;
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;

                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    continue;
                }

                p.Position += SourceJointMath.Rotate(p.Velocity, p.Angle) * factor;
                p.Scale -= factor * 2f;
                if (p.Scale < 0.2f)
                    p.Live = false;
            }
        }

        private void SpawnParticle(Vector3 position, float light, float scale)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Position = position;
                p.Light = light;
                p.Scale = scale;
                p.LifeTime = 2f;
                p.Angle = new Vector3(MuGame.Random.Next(360), MuGame.Random.Next(360), MuGame.Random.Next(360));
                p.Velocity = SourceJointMath.Rotate(new Vector3(0f, -50f, 0f), p.Angle);
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_lifeTime > 0f)
                return true;
            for (int i = 0; i < _particles.Length; i++) if (_particles[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (!Batch.Begin(_sparkTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    Batch.AddSprite(p.Position, p.Scale, new Vector3(p.Light));
            }
            Batch.End();
        }
    }
}
