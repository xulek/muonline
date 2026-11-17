#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Fairy Elf support cast, SourceMain5.2: CreateEffect(BITMAP_MAGIC + 1, to->Position, to->Angle, to->Light, sub, to)
    /// (Heal sub 1, Greater Defense sub 2, Greater Damage / summons sub 3, ZzzCharacter.cpp:4770-4813).
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 of the caster (else 14 ticks)
    /// effect  ZzzEffect.cpp:1224 (LifeTime 20), 8193 (move: CreateHealing 67 every tick), 19186/19237 (Magic_Ground2 decal)
    /// joint   BITMAP_JOINT_HEALING sub 1..3 (ZzzEffectJoint.cpp:433 init, 3584 move: homing on owner + 120, Shiny02 at the end)
    /// </summary>
    internal sealed class HealingCircleEffect : SourceSkillEffectBase
    {
        private const string MagicTexturePath = "Effect/Magic_Ground2.jpg";   // BITMAP_MAGIC + 1
        private const string HealingTexturePath = "Effect/JointEnergy01.jpg"; // BITMAP_JOINT_HEALING
        private const string ShinyTexturePath = "Effect/Shiny02.jpg";         // BITMAP_SHINY + 1
        private const float ReleaseDelaySeconds = 14f * 0.04f;

        private struct Healing
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Light;
            public float Velocity;
            public float LifeTime;
            public bool Flash;
            public Vector3 FlashPosition;
            public SourceTrail Trail;
        }

        private readonly WalkerObject _owner;
        private readonly Vector3 _tint;
        private readonly string? _sound;
        private readonly Healing[] _healings = new Healing[96];
        private Texture2D? _magicTexture;
        private Texture2D? _healingTexture;
        private Texture2D? _shinyTexture;
        private Vector3 _center;
        private float _angleZ;
        private float _lifeTime;
        private float _age;

        public HealingCircleEffect(WalkerObject caster, WalkerObject owner, int subType, string? sound)
            : base(caster, 5f, ReleaseDelaySeconds, 256)
        {
            _owner = owner;
            _sound = sound;
            _tint = subType switch
            {
                2 => new Vector3(0.4f, 1f, 0.6f),
                3 => new Vector3(1f, 0.6f, 0.4f),
                _ => new Vector3(0.4f, 0.6f, 1f)
            };

            // ReceiveMagic plays the cast sound immediately, before the AttackStage gate.
            if (!string.IsNullOrEmpty(_sound))
                SoundController.Instance.PlayBuffer(_sound);
        }

        protected override async Task LoadEffectContent()
        {
            _magicTexture = await LoadTexture(MagicTexturePath);
            _healingTexture = await LoadTexture(HealingTexturePath);
            _shinyTexture = await LoadTexture(ShinyTexturePath);
        }

        protected override void OnStart()
        {
            _center = OwnerAlive ? _owner.WorldPosition.Translation : CasterPosition;
            _angleZ = MathHelper.ToDegrees(_owner.Angle.Z);
            _lifeTime = 20f;
        }

        private bool OwnerAlive => _owner.Status != GameControlStatus.Disposed && _owner.World != null;

        protected override void OnTick(float factor)
        {
            if (_lifeTime > 0f)
            {
                CreateHealing();
                _lifeTime -= factor;
            }

            // Soft glow of the support circle in its tint (20 frames of circle + 12 of joints).
            _age += factor;
            Vector3 glowPosition = (OwnerAlive ? _owner.WorldPosition.Translation : _center) + Vector3.UnitZ * 70f;
            Lights.Set(0, glowPosition, _tint, 300f, 1f * EffectLights.Envelope(_age, 32f, 4f, 14f));

            Vector3 target = (OwnerAlive ? _owner.WorldPosition.Translation : _center) + new Vector3(0f, 0f, 120f * factor);
            for (int i = 0; i < _healings.Length; i++)
            {
                ref Healing h = ref _healings[i];
                if (!h.Live)
                    continue;

                SourceJointMath.AngleBasis(h.Angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                h.Position += -forward * (h.Velocity * factor);
                h.Velocity += factor * 4f;
                SourceJointMath.MoveHumming(ref h.Position, ref h.Angle, target, 10f, factor);

                float luminosity = (12f - h.LifeTime) * 0.1f;
                h.Light = _tint * luminosity;
                // Every frame of the (int)LifeTime == 1 window, as in the original.
                h.Flash = (int)h.LifeTime == 1;
                h.FlashPosition = target;

                h.Trail.Push(h.Position, right, up, 5f);
                h.LifeTime -= factor;
                if (h.LifeTime < 0f)
                    h.Live = false;
            }
        }

        // CreateHealing: 3 x BITMAP_JOINT_HEALING from a 200-unit dome toward the owner.
        private void CreateHealing()
        {
            int maxTails = SourceJointMath.ScaledMaxTails(2);
            for (int j = 0; j < 3; j++)
            {
                var angle = new Vector3(MuGame.Random.Next(90), 0f, MuGame.Random.Next(360));
                Vector3 start = _center - SourceJointMath.Rotate(new Vector3(0f, -200f, 0f), angle);
                start.Z += 120f;

                for (int i = 0; i < _healings.Length; i++)
                {
                    ref Healing h = ref _healings[i];
                    if (h.Live)
                        continue;

                    h.Live = true;
                    h.Position = start;
                    h.Angle = angle;
                    h.Velocity = 0f;
                    h.LifeTime = 12f;
                    h.Flash = false;
                    h.Light = Vector3.Zero;
                    if (h.Trail == null || h.Trail.MaxTails != maxTails)
                        h.Trail = new SourceTrail(maxTails);
                    else
                        h.Trail.Clear();
                    h.Trail.Push(start, angle, 5f);
                    break;
                }
            }
        }

        protected override bool HasActiveContent()
        {
            if (_lifeTime > 0f)
                return true;
            for (int i = 0; i < _healings.Length; i++) if (_healings[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_lifeTime > 0f && Batch.Begin(_magicTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float luminosity = _lifeTime < 5f ? 1f - (5f - _lifeTime) * 0.2f : 1f;
                float size = (20f - _lifeTime) * 0.15f;
                Batch.AddTerrainDecal(World?.Terrain, _center.X, _center.Y, size, size, _tint * luminosity, -_angleZ);
                Batch.End();
            }

            if (Batch.Begin(_healingTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _healings.Length; i++)
                {
                    ref Healing h = ref _healings[i];
                    if (h.Live)
                        Batch.AddTrail(h.Trail, SourceQuadBatch.TrailFaces.Both, h.Light, SourceQuadBatch.TrailShade.Flat, h.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            // (int)LifeTime == 1: CreateSprite(BITMAP_SHINY + 1, p, (rand % 8 + 8) * 0.2, Light) for one frame.
            if (Batch.Begin(_shinyTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _healings.Length; i++)
                {
                    ref Healing h = ref _healings[i];
                    if (h.Live && h.Flash)
                        Batch.AddSprite(h.FlashPosition, (MuGame.Random.Next(8) + 8) * 0.2f, h.Light, MuGame.Random.Next(360));
                }
                Batch.End();
            }
        }
    }
}
