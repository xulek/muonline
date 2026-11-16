#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Effects
{
    /// <summary>
    /// eBuff_Life aura, SourceMain5.2:
    /// spawn    WSclient.cpp:4462     CreateEffect(BITMAP_LIGHT, ..., sub 1, owner)
    /// emitter  ZzzEffect.cpp:9493    two BITMAP_LIGHT sub 4 particles per 25 Hz frame on g_byUpperBoneLocation bones
    /// particle ZzzEffectParticle.cpp:3229 (init), 8091 (move)
    /// </summary>
    internal sealed class SwellLifeBuffEffect : SourceSkillEffectBase
    {
        private const string LightTexturePath = "Effect/flare01.jpg"; // BITMAP_LIGHT
        private const int MaxParticles = 48;

        // g_byUpperBoneLocation (ZzzEffect.cpp:34)
        private static readonly byte[] UpperBoneLocation = { 25, 26, 27, 20, 34, 35, 36 };

        private struct Particle
        {
            public bool Live;
            public int Bone;
            public Vector3 Position;
            public Vector3 Light;
            public float Gravity;
            public float Scale;
            public float LifeTime;
        }

        private readonly Particle[] _particles = new Particle[MaxParticles];
        private Texture2D? _lightTexture;
        private bool _stopped;

        public SwellLifeBuffEffect(WalkerObject owner)
            : base(owner, 0f, 0f, MaxParticles)
        {
        }

        /// <summary>Buff ended: the emitter dies at once, live particles vanish with it.</summary>
        public void Stop()
        {
            _stopped = true;
            RemoveSelf();
        }

        protected override async Task LoadEffectContent()
        {
            _lightTexture = await LoadTexture(LightTexturePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            Position = CasterPosition;

            // Sub 4 particles only live on MODEL_PLAYER owners.
            if (Caster is not PlayerObject)
                return;

            // MoveEffects runs before MoveParticles: new particles are moved in the same frame.
            if (!Caster.Hidden && FpsCheck(1, factor))
            {
                int index = MuGame.Random.Next(7);
                Spawn(UpperBoneLocation[index]);
                Spawn(UpperBoneLocation[6 - index]);
            }

            float fade = MathF.Pow(1f / 1.35f, factor);
            for (int i = 0; i < MaxParticles; i++)
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

                p.Gravity += (MuGame.Random.Next(40) + 60) / 100f * 9.5f * factor;
                p.Scale -= factor * (MuGame.Random.Next(400) + 400) / 10000f;
                p.Position = GetCasterBonePosition(p.Bone);
                p.Position.Z += p.Gravity * factor;
                p.Light *= fade;
            }
        }

        private void Spawn(int bone)
        {
            for (int i = 0; i < MaxParticles; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    continue;

                p.Live = true;
                p.Bone = bone;
                p.Position = CasterPosition;
                p.Light = new Vector3(1f, 0.5f, 0.1f);
                p.Gravity = 0f;
                p.Scale = 2f;
                p.LifeTime = 10f;
                return;
            }
        }

        protected override bool HasActiveContent() => !_stopped;

        protected override void OnDraw()
        {
            if (!Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < MaxParticles; i++)
            {
                ref Particle p = ref _particles[i];
                if (p.Live)
                    Batch.AddSprite(p.Position, p.Scale, p.Light, p.Bone); // o->Rotation holds the bone index
            }
            Batch.End();
        }
    }
}
