#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Objects;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Effects
{
    /// <summary>
    /// Berserker status (eBuff_Berserker = 81), SourceMain5.2:
    /// spawn   WSclient.cpp:15158 / ZzzCharacter.cpp:10646: BITMAP_ORORA sub 0..3 (0.9, 0, 0.1), BITMAP_SPARK+2 sub 2 / 3 (1, 1, 1),
    ///         BITMAP_LIGHT_MARKS sub 0 (0.9, 0, 0.1); WSclient.cpp:15363 deletes the effects, their particles finish
    /// orora   ZzzEffect.cpp:1316 (LifeTime 100 / 25, creates the particle), 8428 (LifeTime &lt;= 5: replaced by a new effect);
    ///         particle ZzzEffectParticle.cpp:2764 (Scale 0.2, LifeTime 100 / 25), 7430 (bone 37 (sub 0, 2) / 28 (1, 3),
    ///         Rotation +-5F / +-20F, Scale += 0.01F / 0.04F, from Scale 0.8 Light *= (1/1.05)^F / (1/1.33)^F), default sprite
    /// spark+2 ZzzEffect.cpp:8441 (rand_fps_check(60): particle sub 2 / 3, 0.5); particle ZzzEffectParticle.cpp:2468 (Scale 0.4,
    ///         LifeTime 16), 7061 (Frame = (16 - LifeTime) / 4, bone 37 / 28), 8986 (2 x 2 sheet)
    /// marks   ZzzEffect.cpp:1331 (LifeTime 65), 8453 (LifeTime &lt;= 5: 65), 18644 (Light *= (1/1.035)^F from 35 up, else
    ///         *= 1.035^F; sprite Scale 0.9 * fScale on 14 bones).
    /// The replacement effect is CreateEffectFpsChecked; the 25 Hz behaviour (always replaced) is kept.
    /// </summary>
    internal sealed class BerserkerBuffEffect : SourceSkillEffectBase
    {
        private const string OroraTexturePath = "Effect/hikorora.jpg";   // BITMAP_ORORA
        private const string SparkTexturePath = "Effect/Spark.jpg";      // BITMAP_SPARK + 2
        private const string MarksTexturePath = "Effect/lightmarks.jpg"; // BITMAP_LIGHT_MARKS
        private static readonly Vector3 AuraLight = new(0.9f, 0f, 0.1f);
        private static readonly int[] MarkBones = { 20, 20, 19, 18, 17, 2, 35, 26, 36, 27, 37, 28, 39, 30 };
        private static readonly float[] MarkScales = { 1.5f, 1.5f, 0.6f, 1.1f, 0.9f, 0.8f, 0.6f, 0.6f, 0.8f, 0.8f, 0.8f, 0.8f, 0.7f, 0.7f };

        private struct Particle
        {
            public bool Live;
            public bool Spark;
            public int SubType;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float LifeTime;
            public int Frame;
        }

        private readonly Particle[] _particles = new Particle[24];
        private readonly float[] _ororaLife = new float[4];
        private Texture2D? _ororaTexture, _sparkTexture, _marksTexture;
        private Vector3 _marksLight = AuraLight;
        private float _marksLife = 65f;
        private int _particleCount;
        private bool _stopped;

        public BerserkerBuffEffect(WalkerObject owner)
            : base(owner, 0f, 0f, 64)
        {
        }

        /// <summary>Status ended: the effects are deleted, live particles finish.</summary>
        public void Stop() => _stopped = true;

        protected override async Task LoadEffectContent()
        {
            _ororaTexture = await LoadTexture(OroraTexturePath);
            _sparkTexture = await LoadTexture(SparkTexturePath);
            _marksTexture = await LoadTexture(MarksTexturePath);
        }

        protected override void OnStart()
        {
            for (int i = 0; i < 4; i++)
                StartOrora(i);
        }

        private void StartOrora(int subType)
        {
            float life = subType < 2 ? 100f : 25f;
            _ororaLife[subType] = life;
            ref Particle p = ref NewParticle(out bool found);
            if (!found)
                return;
            p.Spark = false;
            p.SubType = subType;
            p.Position = CasterPosition;
            p.Light = AuraLight;
            p.Scale = 0.2f;
            p.Rotation = 0f;
            p.LifeTime = life;
        }

        private ref Particle NewParticle(out bool found)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].Live)
                    continue;
                _particles[i] = default;
                _particles[i].Live = true;
                _particleCount++;
                found = true;
                return ref _particles[i];
            }
            found = false;
            return ref _particles[0];
        }

        protected override void OnTick(float factor)
        {
            Position = CasterPosition;
            bool player = Caster is PlayerObject;

            if (!_stopped && player)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (_ororaLife[i] <= 5f)
                        StartOrora(i);
                    _ororaLife[i] -= factor;
                }

                var fps = FPSCounter.Instance;
                for (int sub = 2; sub <= 3; sub++)
                {
                    if (!fps.RandFPSCheck(60) || !fps.RandFPSCheck(1))
                        continue;
                    ref Particle p = ref NewParticle(out bool found);
                    if (!found)
                        continue;
                    p.Spark = true;
                    p.SubType = sub;
                    p.Position = CasterPosition;
                    p.Light = Vector3.One;
                    p.Scale = 0.4f;
                    p.LifeTime = 16f;
                }

                if (_marksLife <= 5f)
                    _marksLife = 65f;
                float pulse = MathF.Pow(1.035f, factor);
                _marksLight = _marksLife >= 35f ? _marksLight / pulse : _marksLight * pulse;
                _marksLife -= factor;
            }

            if (_particleCount == 0)
                return;
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                if (p.LifeTime <= 0f)
                {
                    p.Live = false;
                    _particleCount--;
                    continue;
                }

                bool left = (p.SubType & 1) == 0; // sub 0 / 2: bone 37, sub 1 / 3: bone 28
                p.Position = GetCasterBonePosition(left ? 37 : 28);
                if (p.Spark)
                {
                    p.Frame = (int)((16f - p.LifeTime) / 4f);
                    continue;
                }

                bool fast = p.SubType >= 2;
                p.Rotation += (left ? 1f : -1f) * (fast ? 20f : 5f) * factor;
                p.Scale += (fast ? 0.04f : 0.01f) * factor;
                if (p.Scale >= 0.8f)
                    p.Light *= MathF.Pow(1f / (fast ? 1.33f : 1.05f), factor);
            }
        }

        protected override bool HasActiveContent() => !_stopped || _particleCount > 0;

        protected override void OnDraw()
        {
            if (Caster.Hidden)
                return;

            // RenderEffects (marks sprites) and particles are all additive sprites.
            if (!_stopped && Caster is PlayerObject
                && Batch.Begin(_marksTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < MarkBones.Length; i++)
                    Batch.AddSprite(GetCasterBonePosition(MarkBones[i]), 0.9f * MarkScales[i], _marksLight, 0f);
                Batch.End();
            }

            if (_particleCount == 0)
                return;

            if (Batch.Begin(_ororaTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && !p.Spark)
                        Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }

            if (_sparkTexture != null && Batch.Begin(_sparkTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live && p.Spark)
                        Batch.AddSpriteRegion(p.Position, _sparkTexture.Width * p.Scale, _sparkTexture.Height * p.Scale, p.Light, 0f,
                            p.Frame % 2 * 0.5f, p.Frame / 2 * 0.5f, 0.5f, 0.5f);
                }
                Batch.End();
            }
        }
    }
}
