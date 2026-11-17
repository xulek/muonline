#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Core.Client;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    // AT_SKILL_REMOVAL_BUFF: ZzzCharacter.cpp:2763/4129; MODEL_SPEARSKILL sub5-7:
    // ZzzEffectJoint.cpp:1498/4295/6921; BITMAP_SHOCK_WAVE sub3: ZzzEffect.cpp:3640/13332/19397.
    internal sealed class RemoveBuffEffect : SourceSkillEffectBase
    {
        private const int JointCount = 6;
        private const int ShockCount = 4;
        private static readonly byte[] TargetBuffs = { 1, 2, 8, 4, 5, 9 };
        private static readonly byte[] CasterBuffs = { 55, 56, 57, 58, 61, 72, 86, 8, 1, 2, 7, 18, 19, 4, 5, 25 };

        private struct Joint
        {
            public bool Live;
            public int SubType;
            public Vector3 Start;
            public Vector3 Direction;
            public Vector3 Angle;
            public Vector3 Light;
            public float Scale;
            public float Weapon;
            public float Life;
            public SourceTrail Trail;
        }

        private struct Shock
        {
            public bool Live;
            public Vector3 Position;
            public float Angle;
            public float Life;
            public float Scale;
            public Vector3 Light;
        }

        private readonly Joint[] _joints = new Joint[JointCount];
        private readonly Shock[] _shocks = new Shock[ShockCount];
        private Texture2D? _ribbonTexture;
        private Texture2D? _shockTexture;
        private static readonly Vector3 RibbonColor = new(0.9f, 0.9f, 1f);
        private Vector3 _shockPosition;
        private float _shockAge = 100f;
        private float _age;

        public RemoveBuffEffect(WalkerObject caster)
            : base(caster, triggerFrame: 3.5f, triggerTimeoutSeconds: 0.56f, maxQuads: 1200)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _ribbonTexture = await LoadTexture("Effect/flare02.jpg");
            _shockTexture = await LoadTexture("Effect/ShockWave.jpg");
        }

        protected override void OnStart()
        {
            ClearCasterBuffs(MuGame.Network?.GetCharacterState(), MuGame.Network?.GetBuffManager(), Caster.NetworkId);
            Vector3 start = CasterPosition + Vector3.UnitZ * 100f;
            Spawn(0, start, 45f, 5);
            start.Z -= 10f;
            Spawn(1, start, 135f, 6);
            start.Z -= 10f;
            Spawn(2, start, 225f, 7);
            Spawn(3, start, 90f, 5);
            start.Z -= 10f;
            Spawn(4, start, 180f, 6);
            start.Z -= 10f;
            Spawn(5, start, 270f, 7);
            if (World?.WorldIndex == 31)
                SoundController.Instance.PlayBuffer("Sound/BattleCastle/sDMagicCancel.wav");
        }

        private void Spawn(int index, Vector3 start, float yaw, int subtype)
        {
            ref Joint joint = ref _joints[index];
            joint.Live = true;
            joint.SubType = subtype;
            joint.Start = start;
            joint.Direction = new Vector3(0f, 800f, 0f);
            joint.Angle = new Vector3(0f, 0f, yaw);
            joint.Light = subtype == 5 ? new Vector3(1f, 1f, 0.8f)
                : subtype == 6 ? new Vector3(1f, 0.8f, 1f) : new Vector3(0.8f, 1f, 1f);
            joint.Scale = 170f;
            joint.Life = 60f;
            joint.Trail = new SourceTrail(SourceJointMath.ScaledMaxTails(30));
            Vector3 position = start + SourceJointMath.Rotate(joint.Direction, joint.Angle);
            joint.Trail.Push(position, joint.Angle, joint.Scale);
        }

        protected override void OnTick(float factor)
        {
            // MoveEffects precedes MoveJoints, so a newly emitted wave starts moving next frame.
            for (int i = 0; i < _shocks.Length; i++)
            {
                ref Shock shock = ref _shocks[i];
                if (!shock.Live)
                    continue;
                shock.Scale += 2f * factor;
                shock.Light = new Vector3(shock.Life / 20f);
                shock.Life -= factor;
                if (shock.Life <= 0f)
                    shock.Live = false;
            }
            for (int j = 0; j < _joints.Length; j++)
            {
                ref Joint joint = ref _joints[j];
                if (!joint.Live)
                    continue;
                for (int i = 0; i < 3; i++)
                {
                    joint.Angle.Z += 10f * factor;
                    Vector3 position = joint.Start + SourceJointMath.Rotate(joint.Direction, joint.Angle);
                    joint.Trail.Push(position, joint.Angle, joint.Scale);
                    if (joint.Weapon == 0f)
                    {
                        joint.Direction.Y *= MathF.Pow(0.95f, factor);
                        if (joint.Direction.Y < 10f)
                            joint.Direction.Y = -10f;
                    }
                    if (joint.Weapon > 40f)
                    {
                        joint.Direction.Y -= 20f * factor;
                        joint.Angle.Z -= 5f * factor;
                        joint.Scale += 15f * factor;
                    }
                    if (joint.Direction.Y < 0f)
                    {
                        if (joint.Weapon == 40f && joint.SubType <= 6 && FpsCheck(1, factor))
                            SpawnShock(joint.Start, joint.Angle.Z, joint.Light);
                        joint.Weapon += factor;
                        if (joint.Weapon < 20f)
                        {
                            joint.Direction.Y = -30f;
                            joint.Scale = 40f;
                        }
                        else
                            joint.Start.Z += 5f * factor;
                    }
                }
                if (joint.Life < 10f)
                    joint.Light *= MathF.Pow(1f / 1.2f, factor);
                joint.Scale -= 5f * factor;
                joint.Life -= factor;
                if (joint.Life < 0f)
                    joint.Live = false;
            }

            // slot 0: glow of the ribbon whirl around the caster (60 frames); slot 1: shock wave flash.
            _age += factor;
            _shockAge += factor;
            Lights.Set(0, CasterPosition + Vector3.UnitZ * 80f, RibbonColor, 300f, 1f * EffectLights.Envelope(_age, 60f, 4f, 15f));
            Lights.Set(1, _shockPosition, RibbonColor, 380f, 1.3f * EffectLights.Flash(_shockAge, 15f, 1.5f));
        }

        private void SpawnShock(Vector3 position, float angle, Vector3 light)
        {
            for (int i = 0; i < _shocks.Length; i++)
            {
                if (_shocks[i].Live)
                    continue;
                _shocks[i] = new Shock { Live = true, Position = position, Angle = angle, Life = 15f, Scale = 1f, Light = light };
                _shockPosition = new Vector3(position.X, position.Y, TerrainHeight(position) + 60f);
                _shockAge = 0f;
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < _joints.Length; i++) if (_joints[i].Live) return true;
            for (int i = 0; i < _shocks.Length; i++) if (_shocks[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (Batch.Begin(_ribbonTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _joints.Length; i++)
                {
                    ref Joint joint = ref _joints[i];
                    if (joint.Live)
                        Batch.AddTrail(joint.Trail, SourceQuadBatch.TrailFaces.One, joint.Light,
                            SourceQuadBatch.TrailShade.Flat, joint.Trail.MaxTails - 1);
                }
                Batch.End();
            }
            if (Batch.Begin(_shockTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _shocks.Length; i++)
                {
                    ref Shock shock = ref _shocks[i];
                    if (shock.Live)
                        Batch.AddTerrainDecal(World?.Terrain, shock.Position.X, shock.Position.Y,
                            shock.Scale, shock.Scale, shock.Light, -shock.Angle);
                }
                Batch.End();
            }
        }

        internal static void ClearTargetBuffs(CharacterState? state, BuffManager? manager, ushort targetId) =>
            ClearBuffs(state, manager, targetId, TargetBuffs);

        private static void ClearCasterBuffs(CharacterState? state, BuffManager? manager, ushort casterId) =>
            ClearBuffs(state, manager, casterId, CasterBuffs);

        private static void ClearBuffs(CharacterState? state, BuffManager? manager, ushort id, byte[] effects)
        {
            id = (ushort)(id & 0x7FFF);
            if (id == 0)
                return;
            foreach (byte effect in effects)
            {
                if (state?.HasActiveBuff(effect, id) == true)
                    state.DeactivateBuff(effect, id);
                manager?.ProcessMagicEffectStatus(id, effect, false);
            }
        }
    }
}
