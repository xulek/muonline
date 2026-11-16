#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>
    /// Chain Lightning (AT_SKILL_ALICE_CHAINLIGHTNING = 215, masters _UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:5265 ReceiveChainMagic (0xBF 0x0A): mount action, per target i:
    ///         CreateEffect(MODEL_CHAIN_LIGHTNING, ..., SubType i, Owner = previous object (caster for i 0), target index)
    ///         when previous != target and target Live
    /// init    ZzzEffect.cpp:875   sub 0..2: LifeTime 20
    /// move    ZzzEffect.cpp:8904  dies when owner / target not Live or target Dead;
    ///         sub 0: bones 37 / 28 of the owner, 4 x CreateJointFpsChecked(BITMAP_JOINT_THUNDER sub 0) each:
    ///         angles (-60, 0, yaw), (0, 0, yaw +/- 60), Scale 50 / 10, target object, light (0.4, 0.4, 1)
    ///         sub 1/2 (owner != target): owner Position + 80 Z, angle = target Angle, Scale 50 / 10
    ///         (int)LifeTime == 15: per target bone CreateParticleFpsChecked(BITMAP_LIGHT sub 5, (0.2, 0.2, 0.8), 3 + (rand%20-10)*0.1)
    /// light   ZzzEffectParticle.cpp:3236 (sub 5 LifeTime 50), 8073 (Light *= 0.9 per frame, Scale *= 0.95^F), default RenderSprite
    /// thunder <see cref="SourceHomingThunderPool"/>
    /// </summary>
    internal sealed class ChainLightningEffect : SourceSkillEffectBase
    {
        private const string LightTexturePath = "Effect/flare01.jpg"; // BITMAP_LIGHT
        private const int MaxHops = 3;
        private const int MaxFlashes = 512;
        private static readonly Vector3 JointLight = new(0.4f, 0.4f, 1f);
        private static readonly Vector3 FlashLight = new(0.2f, 0.2f, 0.8f);

        public struct Hop
        {
            public WalkerObject Owner;
            public WalkerObject Target;
            public int SubType;
        }

        private struct ActiveHop
        {
            public bool Live;
            public WalkerObject Owner;
            public WalkerObject Target;
            public int SubType;
            public float LifeTime;
        }

        private struct Flash
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
        }

        private readonly ActiveHop[] _hops = new ActiveHop[MaxHops];
        private readonly Flash[] _flashes = new Flash[MaxFlashes];
        private readonly SourceHomingThunderPool _pool = SourceHomingThunderPool.Shared;
        private int _flashCount;

        private Texture2D? _thunderTexture;
        private Texture2D? _energyTexture;
        private Texture2D? _smokeTexture;
        private Texture2D? _lightTexture;

        public ChainLightningEffect(WalkerObject caster, Hop[] hops, int hopCount)
            : base(caster, 0f, 0f, 4096)
        {
            int count = Math.Min(hopCount, MaxHops);
            for (int i = 0; i < count; i++)
            {
                _hops[i] = new ActiveHop
                {
                    Live = true,
                    Owner = hops[i].Owner,
                    Target = hops[i].Target,
                    SubType = hops[i].SubType,
                    LifeTime = 20f,
                };
            }
        }

        protected override async Task LoadEffectContent()
        {
            _thunderTexture = await LoadTexture(SourceHomingThunderPool.ThunderTexturePath);
            _energyTexture = await LoadTexture(SourceHomingThunderPool.EnergyTexturePath);
            _smokeTexture = await LoadTexture(SourceHomingThunderPool.SmokeTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _hops.Length; i++)
            {
                ref ActiveHop hop = ref _hops[i];
                if (hop.Live)
                    MoveHop(ref hop, factor);
            }

            _pool.Move(this, factor);
            MoveFlashes(factor);
        }

        private void MoveHop(ref ActiveHop hop, float factor)
        {
            if (!IsPresent(hop.Owner) || !IsPresent(hop.Target) || IsDead(hop.Target))
            {
                hop.Live = false;
                return;
            }

            bool spawn = hop.SubType == 0 || hop.Owner != hop.Target;
            if (spawn)
            {
                Vector3 targetPosition = hop.Target.WorldPosition.Translation + new Vector3(0f, 0f, 80f);
                if (hop.SubType == 0)
                {
                    float yaw = MathHelper.ToDegrees(hop.Owner.Angle.Z);
                    SpawnHandBolts(hop.Target, BonePosition(hop.Owner, 37), yaw, yaw + 60f, targetPosition);
                    SpawnHandBolts(hop.Target, BonePosition(hop.Owner, 28), yaw, yaw - 60f, targetPosition);
                }
                else
                {
                    Vector3 from = hop.Owner.WorldPosition.Translation + new Vector3(0f, 0f, 80f);
                    Vector3 angle = ToDegrees(hop.Target.Angle);
                    SpawnChecked(from, angle, 50f, hop.Target, targetPosition);
                    SpawnChecked(from, angle, 10f, hop.Target, targetPosition);
                }
            }

            if ((int)hop.LifeTime == 15)
                FlashBones(hop.Target);

            hop.LifeTime -= factor;
            if (hop.LifeTime <= 0f)
                hop.Live = false;
        }

        private void SpawnHandBolts(WalkerObject target, Vector3 hand, float yaw, float sideYaw, Vector3 targetPosition)
        {
            var upAngle = new Vector3(-60f, 0f, yaw);
            var sideAngle = new Vector3(0f, 0f, sideYaw);
            SpawnChecked(hand, upAngle, 50f, target, targetPosition);
            SpawnChecked(hand, upAngle, 10f, target, targetPosition);
            SpawnChecked(hand, sideAngle, 50f, target, targetPosition);
            SpawnChecked(hand, sideAngle, 10f, target, targetPosition);
        }

        private void SpawnChecked(Vector3 from, Vector3 angle, float scale, WalkerObject target, Vector3 targetPosition)
        {
            if (FPSCounter.Instance.RandFPSCheck(1))
                _pool.SpawnThunder(this, 0, from, angle, JointLight, scale, target, targetPosition);
        }

        private void FlashBones(WalkerObject target)
        {
            Matrix[]? transforms = target.GetBoneTransforms();
            var bones = target.Model?.Bones;
            if (transforms == null || bones == null)
                return;

            Matrix world = target.WorldPosition;
            int boneCount = Math.Min(transforms.Length, bones.Length);
            for (int i = 0; i < boneCount; i++)
            {
                float scale = 3f + (MuGame.Random.Next(20) - 10) * 0.1f;
                if (!FPSCounter.Instance.RandFPSCheck(1))
                    continue;

                int slot = FindFreeFlash();
                if (slot < 0)
                    return;

                ref Flash f = ref _flashes[slot];
                f.Live = true;
                f.Position = Vector3.Transform(transforms[i].Translation, world);
                f.Light = FlashLight;
                f.Scale = scale;
                f.LifeTime = 50f;
                _flashCount++;
            }
        }

        private int FindFreeFlash()
        {
            for (int i = 0; i < _flashes.Length; i++)
                if (!_flashes[i].Live)
                    return i;
            return -1;
        }

        private void MoveFlashes(float factor)
        {
            if (_flashCount == 0)
                return;

            float scaleDecay = MathF.Pow(0.95f, factor);
            for (int i = 0; i < _flashes.Length; i++)
            {
                ref Flash f = ref _flashes[i];
                if (!f.Live)
                    continue;

                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                {
                    f.Live = false;
                    _flashCount--;
                    continue;
                }

                f.Light *= 0.9f; // VectorScale per frame, not factor-scaled
                f.Scale *= scaleDecay;
            }
        }

        private static Vector3 BonePosition(WalkerObject walker, int bone)
        {
            if (walker is PlayerObject player && player.TryGetBoneWorldMatrix(bone, out Matrix matrix))
                return matrix.Translation;

            Matrix[]? transforms = walker.GetBoneTransforms();
            if (transforms != null && bone < transforms.Length)
                return Vector3.Transform(transforms[bone].Translation, walker.WorldPosition);

            return walker.WorldPosition.Translation;
        }

        private static Vector3 ToDegrees(Vector3 angle)
            => new(MathHelper.ToDegrees(angle.X), MathHelper.ToDegrees(angle.Y), MathHelper.ToDegrees(angle.Z));

        private static bool IsPresent(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;

        private static bool IsDead(WalkerObject walker)
            => walker is MonsterObject { IsDead: true } || walker is PlayerObject { IsDead: true };

        protected override bool HasActiveContent()
        {
            if (_flashCount > 0)
                return true;
            for (int i = 0; i < _hops.Length; i++)
                if (_hops[i].Live)
                    return true;
            return _pool.HasLive(this);
        }

        protected override void OnDraw()
        {
            _pool.Draw(this, Batch, _thunderTexture, _smokeTexture, _energyTexture);

            if (_flashCount == 0 || !Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _flashes.Length; i++)
            {
                ref Flash f = ref _flashes[i];
                if (f.Live)
                    Batch.AddSprite(f.Position, f.Scale, f.Light, 0f);
            }
            Batch.End();
        }

        protected override void DisposeEffect() => _pool.Release(this);
    }
}
