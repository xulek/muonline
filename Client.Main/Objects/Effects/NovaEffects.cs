#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Nova charge (AT_SKILL_BLAST_HELL_BEGIN = 58), SourceMain5.2:
    /// packet  WSclient.cpp:4491        PLAYER_SKILL_HELL_BEGIN, SOUND_NUKE1, m_bySkillCount = 0; ReceiveSkillCount 5371
    /// visual  ZzzCharacter.cpp:5621    every frame while PLAYER_SKILL_HELL_BEGIN: bones 0, 2 .. 38 -> (count + 1) x
    ///         BITMAP_LIGHT sub 6 (scale 1.3 + count * 0.08, light (0.3, 0.3, 1)), CreateForce (ZzzEffect.cpp:90:
    ///         3 x BITMAP_JOINT_HEALING sub 8, ZzzEffectJoint.cpp:462 / 3593)
    /// </summary>
    internal sealed class NovaChargeEffect : SourceSkillEffectBase
    {
        private const string LightTexturePath = "Effect/flare01.jpg";          // BITMAP_LIGHT
        private const string HealingTexturePath = "Effect/JointEnergy01.jpg";  // BITMAP_JOINT_HEALING
        private const byte MaxStage = 12;

        private static readonly Dictionary<ushort, NovaChargeEffect> Active = new();
        private static readonly Dictionary<ushort, byte> RememberedStage = new();

        private struct Light
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Color;
            public float Scale;
            public float LifeTime;
        }

        private struct Healing
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 TargetPosition;
            public Vector3 Light;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly ushort _casterId;
        private readonly Light[] _lights = new Light[1024];
        private readonly Healing[] _healings = new Healing[192];
        private Texture2D? _lightTexture;
        private Texture2D? _healingTexture;
        private bool _finishing;
        private byte _stage;

        // Dynamic light (slot 0: charge glow around the caster, grows with the stage).
        private static readonly Vector3 ChargeLightColor = new(0.4f, 0.45f, 1f);
        private float _glow;
        private float _lightTime;

        private NovaChargeEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 4096)
        {
            _casterId = caster.NetworkId;
        }

        public byte Stage => _stage;

        public static NovaChargeEffect? GetOrCreate(WorldControl? world, WalkerObject? caster)
        {
            if (world == null || caster == null)
                return null;

            if (Active.TryGetValue(caster.NetworkId, out var active) && !active._finishing)
                return active;

            var effect = new NovaChargeEffect(caster);
            Active[caster.NetworkId] = effect;
            RememberedStage.Remove(caster.NetworkId);
            world.Objects.Add(effect);
            _ = effect.Load();
            return effect;
        }

        public static void StopForCaster(WorldControl? world, ushort casterId)
        {
            if (Active.TryGetValue(casterId, out var effect))
                effect.Finish();
        }

        public static void UpdateStage(WorldControl? world, ushort casterId, byte stage)
        {
            stage = Math.Min(stage, MaxStage);
            RememberedStage[casterId] = stage;
            if (Active.TryGetValue(casterId, out var effect) && !effect._finishing)
            {
                effect._stage = stage;
                return;
            }

            if (world is WalkableWorldControl walkable && walkable.TryGetWalkerById(casterId, out var caster))
            {
                var created = GetOrCreate(world, caster);
                if (created != null)
                    created._stage = stage;
            }
        }

        public static byte ConsumeStageAndStop(WorldControl? world, ushort casterId)
        {
            byte stage = 0;
            if (Active.TryGetValue(casterId, out var effect))
            {
                stage = effect._stage;
                effect.Finish();
            }
            else if (RememberedStage.TryGetValue(casterId, out var remembered))
            {
                stage = remembered;
            }
            RememberedStage.Remove(casterId);
            return stage;
        }

        private void Finish()
        {
            _finishing = true;
            if (Active.TryGetValue(_casterId, out var current) && ReferenceEquals(current, this))
                Active.Remove(_casterId);
        }

        protected override async Task LoadEffectContent()
        {
            _lightTexture = await LoadTexture(LightTexturePath);
            _healingTexture = await LoadTexture(HealingTexturePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            if (!_finishing && Caster is PlayerObject player && player.CurrentAction == PlayerAction.PlayerSkillHellBegin)
                EmitCharge(player);

            float lightDecay = MathF.Pow(0.95f, factor);
            for (int i = 0; i < _lights.Length; i++)
            {
                ref Light l = ref _lights[i];
                if (!l.Live)
                    continue;

                l.LifeTime -= factor;
                if (l.LifeTime <= 0f)
                {
                    l.Live = false;
                    continue;
                }

                // BITMAP_LIGHT sub 6
                l.Position.X += (MuGame.Random.Next(2001) - 1000) * (0.001f * 0.4f) * factor * 0.5f;
                l.Position.Y += (MuGame.Random.Next(2001) - 1000) * (0.001f * 0.4f) * factor * 0.5f;
                l.Position.Z += 5f * 0.5f * factor;
                l.Scale *= lightDecay;
                l.Color *= lightDecay;
                if (l.Scale <= 0.1f)
                    l.Live = false;
            }

            MoveHealings(factor);
            UpdateLight(factor);
        }

        private void UpdateLight(float factor)
        {
            _lightTime += factor * 0.04f;
            bool charging = !_finishing && Caster is PlayerObject p && p.CurrentAction == PlayerAction.PlayerSkillHellBegin;
            // Ease toward on/off so start and stop never pop.
            float target = charging ? 1f : 0f;
            _glow += (target - _glow) * MathHelper.Clamp(factor * (charging ? 0.15f : 0.3f), 0f, 1f);
            if (_glow < 0.02f && !charging)
            {
                _glow = 0f;
                Lights.Hide(0);
                return;
            }

            float stage = _stage / (float)MaxStage;
            float intensity = (0.6f + 0.6f * stage) * _glow * EffectLights.Flicker(_lightTime, 3.1f, 0.12f);
            Lights.Set(0, CasterPosition + new Vector3(0f, 0f, 100f), ChargeLightColor, 200f + 120f * stage, intensity);
        }

        private void EmitCharge(PlayerObject player)
        {
            var bmd = player.Model;
            Matrix[]? bones = player.GetBoneTransforms();
            if (bmd?.Bones == null || bones == null)
                return;

            Matrix world = player.WorldPosition;
            int count = _stage;
            var color = new Vector3(0.3f, 0.3f, 1f);
            float scale = 1.3f + count * 0.08f;
            for (int i = 0; i < 40; i += 2)
            {
                if (i >= bmd.Bones.Length || i >= bones.Length || bmd.Bones[i] == Client.Data.BMD.BMDTextureBone.Dummy)
                    continue;

                Vector3 position = Vector3.Transform(bones[i].Translation, world);
                for (int j = 0; j < count + 1; j++)
                    SpawnLight(position, color, scale);
            }

            CreateForce(CasterPosition);
        }

        private void SpawnLight(Vector3 position, Vector3 color, float scale)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                ref Light l = ref _lights[i];
                if (l.Live)
                    continue;

                l.Live = true;
                l.Position = position;
                l.Color = color;
                l.Scale = scale;
                l.LifeTime = 2f;
                return;
            }
        }

        // CreateForce: 3 x BITMAP_JOINT_HEALING sub 8 from 500 units away toward the caster + 100.
        private void CreateForce(Vector3 position)
        {
            int maxTails = SourceJointMath.ScaledMaxTails(3);
            for (int j = 0; j < 3; j++)
            {
                var angle = new Vector3(MuGame.Random.Next(90), 0f, MuGame.Random.Next(360));
                Vector3 start = position - SourceJointMath.Rotate(new Vector3(0f, -500f, 0f), angle);
                start.Z += 120f;

                for (int i = 0; i < _healings.Length; i++)
                {
                    ref Healing h = ref _healings[i];
                    if (h.Live)
                        continue;

                    h.Live = true;
                    h.Position = start;
                    h.Angle = angle;
                    h.TargetPosition = position + new Vector3(0f, 0f, 100f);
                    h.Light = new Vector3(0.5f, 0.5f, MuGame.Random.Next(128) / 255f + 0.5f);
                    h.Velocity = 0f;
                    h.LifeTime = 17f;
                    if (h.Trail == null || h.Trail.MaxTails != maxTails)
                        h.Trail = new SourceTrail(maxTails);
                    else
                        h.Trail.Clear();
                    h.Trail.Push(start, angle, 10f);
                    break;
                }
            }
        }

        private void MoveHealings(float factor)
        {
            float fade = MathF.Pow(1f / 1.8f, factor);
            for (int i = 0; i < _healings.Length; i++)
            {
                ref Healing h = ref _healings[i];
                if (!h.Live)
                    continue;

                SourceJointMath.AngleBasis(h.Angle, out Vector3 right, out Vector3 forward, out Vector3 up);
                h.Position += -forward * (h.Velocity * factor);
                h.Velocity += factor * 4f;
                if (h.LifeTime > 10f)
                    SourceJointMath.MoveHumming(ref h.Position, ref h.Angle, h.TargetPosition, 10f, factor);
                else if (h.LifeTime < 6f)
                    h.Light *= fade;
                h.Trail.Push(h.Position, right, up, 10f);

                h.LifeTime -= factor;
                if (h.LifeTime < 0f)
                    h.Live = false;
            }
        }

        protected override bool HasActiveContent()
        {
            if (!_finishing)
                return true;
            for (int i = 0; i < _lights.Length; i++) if (_lights[i].Live) return true;
            for (int i = 0; i < _healings.Length; i++) if (_healings[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
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

            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                // CreateSprite(BITMAP_LIGHT, Position, 1, Light) of every healing joint.
                for (int i = 0; i < _healings.Length; i++)
                {
                    ref Healing h = ref _healings[i];
                    if (h.Live)
                        Batch.AddSprite(h.Position, 1f, h.Light);
                }
                for (int i = 0; i < _lights.Length; i++)
                {
                    ref Light l = ref _lights[i];
                    if (l.Live)
                        Batch.AddSprite(l.Position, l.Scale, l.Color);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            Finish();
        }
    }

    /// <summary>
    /// Nova release (AT_SKILL_BLAST_HELL = 40), SourceMain5.2:
    /// gate    ZzzCharacter.cpp:2672   PLAYER_SKILL_HELL_START, AnimationFrame >= 14 (else 14 ticks)
    /// release ZzzCharacter.cpp:4278   CreateEffect(MODEL_CIRCLE, o->Position, o->Angle, o->Light, 1, o), SOUND_NUKE2
    /// circle  ZzzEffect.cpp:2241 (sub 1: LifeTime 45, HiddenMesh -2), 10796 (36 x BITMAP_JOINT_SPIRIT sub 6 while
    ///         LifeTime > 44 - SkillCount, sub 7 once), spirit ZzzEffectJoint.cpp:692/719 (init), 3847 (move)
    /// </summary>
    internal sealed class NovaReleaseEffect : SourceSkillEffectBase
    {
        private const string SpiritTexturePath = "Effect/JointSpirit01.jpg"; // BITMAP_JOINT_SPIRIT
        private const string LightTexturePath = "Effect/flare01.jpg";        // BITMAP_LIGHT
        private const string SoundPath = "Sound/eHellFire2_2.wav";           // SOUND_NUKE2
        private const float ReleaseDelaySeconds = 14f * 0.04f;

        private struct Spirit
        {
            public bool Live;
            public bool Tails;
            public Vector3 Position;
            public Vector3 Forward;
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Light;
            public float Velocity;
            public float LifeTime;
            public SourceTrail Trail;
        }

        private readonly byte _stage;
        private readonly Spirit[] _spirits = new Spirit[1024];
        private Texture2D? _spiritTexture;
        private Texture2D? _lightTexture;
        private Vector3 _position;
        private float _circleLifeTime;

        // Dynamic light (slot 0: blast flash expanding with the spirits).
        private static readonly Vector3 BlastLightColor = new(0.4f, 0.5f, 1f);
        private float _lightAge;

        public NovaReleaseEffect(WalkerObject caster, byte stage)
            : base(caster, 14f, ReleaseDelaySeconds, 2048)
        {
            _stage = stage;
        }

        protected override async Task LoadEffectContent()
        {
            _spiritTexture = await LoadTexture(SpiritTexturePath);
            _lightTexture = await LoadTexture(LightTexturePath);
        }

        protected override void OnStart()
        {
            _position = CasterPosition;
            _circleLifeTime = 45f;
            SoundController.Instance.PlayBuffer(SoundPath);
        }

        protected override void OnTick(float factor)
        {
            if (_circleLifeTime > 0f)
            {
                float limit = 44f - _stage;
                if (_circleLifeTime > limit)
                {
                    Vector3 start = _position + new Vector3(0f, 0f, 100f);
                    bool ring = (int)_circleLifeTime == (int)limit + 1;
                    for (int i = 0; i < 36; i++)
                    {
                        if (FpsCheck(1, factor))
                            SpawnSpirit(start, new Vector3(-10f, 0f, i * (10f + MuGame.Random.Next(10))), 6);
                        if (ring && FpsCheck(1, factor))
                            SpawnSpirit(start, new Vector3(-10f, 0f, i * 10f), 7);
                    }
                }
                _circleLifeTime -= factor;
            }

            float fade = MathF.Pow(1f / 1.2f, factor);
            for (int i = 0; i < _spirits.Length; i++)
            {
                ref Spirit s = ref _spirits[i];
                if (!s.Live)
                    continue;

                s.Position += -s.Forward * (s.Velocity * factor);
                if (s.LifeTime < 10f)
                    s.Light *= fade;
                s.Velocity += factor * 5f;
                if (s.Tails)
                    s.Trail.Push(s.Position, s.Right, s.Up, 60f);

                s.LifeTime -= factor;
                if (s.LifeTime < 0f)
                    s.Live = false;
            }

            UpdateLight(factor);
        }

        private void UpdateLight(float factor)
        {
            _lightAge += factor * 0.04f;
            float duration = 0.8f + _stage * 0.04f;
            float k = MathF.Min(1f, _lightAge / 0.06f) * EffectLights.Flash(_lightAge, duration, 1.3f);
            if (k <= 0f)
            {
                Lights.Hide(0);
                return;
            }

            float stage = _stage / 12f;
            float radius = 320f + 160f * stage + 160f * (1f - k);
            Lights.Set(0, _position + new Vector3(0f, 0f, 100f), BlastLightColor, radius, (1.6f + 0.8f * stage) * k);
        }

        private void SpawnSpirit(Vector3 position, Vector3 angle, int subType)
        {
            for (int i = 0; i < _spirits.Length; i++)
            {
                ref Spirit s = ref _spirits[i];
                if (s.Live)
                    continue;

                SourceJointMath.AngleBasis(angle, out s.Right, out s.Forward, out s.Up);
                s.Live = true;
                s.Position = position;
                s.Light = new Vector3(0.3f, 0.3f, 1f);
                s.LifeTime = 20f;
                // sub 6: PKKey = rand() % 5, tails only when 0 (RENDER_FACE_TWO); sub 7: RenderFace 0, Velocity 10.
                s.Velocity = subType == 7 ? 10f : 50f;
                s.Tails = subType == 6 && MuGame.Random.Next(5) == 0;
                int maxTails = SourceJointMath.ScaledMaxTails(5);
                if (s.Trail == null || s.Trail.MaxTails != maxTails)
                    s.Trail = new SourceTrail(maxTails);
                else
                    s.Trail.Clear();
                s.Trail.Push(position, s.Right, s.Up, 60f);
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_circleLifeTime > 0f)
                return true;
            for (int i = 0; i < _spirits.Length; i++) if (_spirits[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_spiritTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spirits.Length; i++)
                {
                    ref Spirit s = ref _spirits[i];
                    if (s.Live && s.Tails)
                        Batch.AddTrail(s.Trail, SourceQuadBatch.TrailFaces.Two, s.Light, SourceQuadBatch.TrailShade.Flat, s.Trail.MaxTails - 1);
                }
                Batch.End();
            }

            // CreateSprite(BITMAP_LIGHT, o->Position, 4 + (20 - LifeTime) / 5, Light) of every spirit.
            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _spirits.Length; i++)
                {
                    ref Spirit s = ref _spirits[i];
                    if (s.Live)
                        Batch.AddSprite(s.Position, 4f + (20f - s.LifeTime) / 5f, s.Light, MuGame.Random.Next(360));
                }
                Batch.End();
            }
        }
    }
}
