#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Core.Utilities;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    // AT_SKILL_PLASMA_STORM_FENRIR: ZzzCharacter.cpp:4463;
    // MODEL_FENRIR_SKILL_THUNDER and FLARE_FORCE 11-14: ZzzEffectJoint.cpp:975/2449/4573/6482/6921.
    internal sealed class PlasmaStormEffect : SourceSkillEffectBase
    {
        private const int MaxSecondaryTargets = 10;
        private const int MaxJoints = 4 + MaxSecondaryTargets * 4 + 6;
        private readonly WalkerObject? _mainTarget;
        private readonly int _fenrirType;
        private readonly Joint[] _joints = new Joint[MaxJoints];
        private int _jointCount;
        private Texture2D? _thunderTexture;
        private Texture2D? _flashTexture;
        private Vector3 _lightColor;
        private float _lightTime;

        private struct Joint
        {
            public bool Live;
            public bool Coil;
            public int SubType;
            public WalkerObject? Target;
            public Vector3 Position;
            public Vector3 Start;
            public Vector3 TargetPosition;
            public Vector3 Angle;
            public Vector3 Light;
            public float Scale;
            public float Velocity;
            public float Life;
            public float Weapon;
            public float MultiUse;
            public SourceTrail Trail;
        }

        public PlasmaStormEffect(WalkerObject caster, WalkerObject? mainTarget, int fenrirType)
            : base(caster, float.MaxValue, 0.56f, 4096)
        {
            _mainTarget = mainTarget;
            _fenrirType = Math.Clamp(fenrirType, 0, 3);
            BoundingBoxLocal = new BoundingBox(new Vector3(-4000f, -4000f, -1000f), new Vector3(4000f, 4000f, 2000f));
        }

        protected override async Task LoadEffectContent()
        {
            _thunderTexture = await LoadTexture("Effect/JointThunder01.jpg");
            _flashTexture = await LoadTexture("Effect/Flashing.jpg");
        }

        protected override void OnStart()
        {
            SoundController.Instance.PlayBufferSingleChannel("Sound/pWskill.wav");
            _lightColor = _fenrirType switch
            {
                0 => new Vector3(0.7f, 1f, 0.7f),
                1 => new Vector3(1f, 0.6f, 0.6f),
                2 => new Vector3(0.7f, 0.7f, 1f),
                _ => new Vector3(0.9f, 0.9f, 0.3f)
            };
            _ = MuGame.Random.Next(360); // unused iAngle in the source release branch
            if (_mainTarget == null || _mainTarget.World == null || _mainTarget.Status == Models.GameControlStatus.Disposed)
                return;

            for (int i = 0; i < 2; i++)
                SpawnPair(_mainTarget, 3 + _fenrirType);

            if (World is WalkableWorldControl world)
            {
                int count = 0;
                float range = SkillDatabase.GetSkillRange(76) * Constants.TERRAIN_SCALE;
                Vector3 origin = CasterPosition;
                foreach (var entry in world.WalkerObjectsById)
                {
                    if (entry.Value is not MonsterObject monster || monster.IsDead ||
                        monster.Status == Models.GameControlStatus.Disposed || monster.World != world)
                        continue;
                    Vector3 d = monster.WorldPosition.Translation - origin;
                    if (d.X * d.X + d.Y * d.Y > range * range)
                        continue;
                    for (int i = 0; i < 2; i++)
                        SpawnPair(monster, 4 + _fenrirType);
                    if (++count == MaxSecondaryTargets)
                        break;
                }
            }

            for (int i = 0; i < 6; i++)
                SpawnCoil();
        }

        private Vector3 CasterOffset(float y, float z) =>
            CasterPosition + SourceJointMath.Rotate(new Vector3(0f, y, z),
                new Vector3(MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), CasterYawDegrees));

        private void SpawnPair(WalkerObject target, int secondSubtype)
        {
            Vector3 start = CasterOffset(-140f, 130f);
            Vector3 angle = new(MuGame.Random.Next(360), 0f, MuGame.Random.Next(360));
            SpawnThunder(target, start, angle, _fenrirType, 100f);
            SpawnThunder(target, start, angle, secondSubtype, 80f);
        }

        private void SpawnThunder(WalkerObject target, Vector3 start, Vector3 angle, int subtype, float scale)
        {
            if (_jointCount >= _joints.Length)
                return;
            ref Joint joint = ref _joints[_jointCount++];
            joint.Live = true;
            joint.SubType = subtype;
            joint.Target = target;
            joint.Position = start;
            joint.Angle = angle;
            joint.Scale = scale;
            joint.Velocity = 50f;
            joint.Life = 20f;
            joint.Light = subtype switch
            {
                0 => new Vector3(0.7f, 1f, 0.7f),
                1 => new Vector3(1f, 0.6f, 0.6f),
                2 => new Vector3(0.7f, 0.7f, 1f),
                3 => new Vector3(0.9f, 0.9f, 0.3f),
                4 => new Vector3(0.1f, 0.8f, 0.1f),
                5 => new Vector3(1f, 0.3f, 0.2f),
                6 => new Vector3(0.2f, 0.3f, 1f),
                _ => new Vector3(0.8f, 0.8f, 0.1f)
            };
            joint.Trail = new SourceTrail(SourceJointMath.ScaledMaxTails(50));
            // The source's first tail uses the previous occupant's pool scale; use this joint's defined scale.
            joint.Trail.Push(start, angle, scale);
        }

        private void SpawnCoil()
        {
            if (_jointCount >= _joints.Length)
                return;
            Vector3 start = CasterOffset(10f + MuGame.Random.Next(40) - 20f, 130f);
            Vector3 angle = new(MuGame.Random.Next(360), 0f, MuGame.Random.Next(360));
            ref Joint joint = ref _joints[_jointCount++];
            joint.Live = true;
            joint.Coil = true;
            joint.SubType = 11 + _fenrirType;
            joint.Angle = angle;
            joint.Scale = 60f;
            // Subtype 14 takes the generic branch and never creates a visible tail.
            if (joint.SubType == 14)
            {
                joint.Life = 15f;
                return;
            }
            joint.Trail = new SourceTrail(SourceJointMath.ScaledMaxTails(30));
            joint.Trail.Push(start, angle, joint.Scale);
            joint.Light = joint.SubType switch
            {
                11 => new Vector3(0.7f, 1f, 0.7f),
                12 => new Vector3(1f, 0.6f, 0.6f),
                _ => new Vector3(0.7f, 0.7f, 1f)
            };
            joint.Start = start + Vector3.UnitZ * (150f * SourceJointMath.FrameFactor);
            joint.TargetPosition = new Vector3(80f, -180f, 0f);
            joint.Position = joint.Start + SourceJointMath.Rotate(new Vector3(0f, 0f, 80f), joint.TargetPosition);
            joint.Velocity = -3f;
            joint.Weapon = MuGame.Random.Next(3) + 2;
            joint.MultiUse = 1f;
            joint.Life = 20f + joint.Weapon;
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < _jointCount; i++)
            {
                ref Joint joint = ref _joints[i];
                if (!joint.Live)
                    continue;
                if (joint.Coil)
                    MoveCoil(ref joint, factor);
                else
                    MoveThunder(ref joint, factor);
                joint.Life -= factor;
                if (joint.Life < 0f)
                    joint.Live = false;
            }

            UpdateLights(factor);
        }

        private void UpdateLights(float factor)
        {
            _lightTime += factor * 0.04f;
            if (_jointCount == 0)
                return;

            float glow = EffectLights.Envelope(_lightTime, 0.85f, 0.05f, 0.35f) * EffectLights.Flicker(_lightTime, 2.1f, 0.4f);
            Lights.Set(0, CasterOffset(-140f, 130f), _lightColor, 220f, 1.1f * glow);
            if (_mainTarget != null && _mainTarget.World != null && _mainTarget.Status != Models.GameControlStatus.Disposed)
                Lights.Set(1, _mainTarget.WorldPosition.Translation + Vector3.UnitZ * 80f, _lightColor, 340f, 1.9f * glow);
            else
                Lights.Hide(1);
        }

        private static void MoveThunder(ref Joint joint, float factor)
        {
            joint.Position += SourceJointMath.Rotate(new Vector3(0f, -joint.Velocity, 0f), joint.Angle) * factor;
            Vector3 tailAngle = joint.Angle;
            for (int i = 0; i < joint.Trail.MaxTails; i++)
            {
                if (joint.Target != null && joint.Target.Status != Models.GameControlStatus.Disposed)
                    joint.TargetPosition = joint.Target.WorldPosition.Translation + Vector3.UnitZ * 80f;
                float distance = Vector3.Distance(joint.Position, joint.TargetPosition);
                SourceJointMath.MoveHumming(ref joint.Position, ref joint.Angle, joint.TargetPosition, 50f, factor);
                Vector3 jitter = new((MuGame.Random.Next(1024) - 512) / joint.Scale, 0f,
                    (MuGame.Random.Next(1024) - 512) / joint.Scale);
                tailAngle = joint.Angle + jitter;
                joint.Trail.Push(joint.Position, tailAngle, joint.Scale);
                if (distance < joint.Velocity * 1.5f)
                    break;
                // AddTerrainLight is not yet available as the source's per-frame accumulated terrain light.
                _ = MuGame.Random.Next(4);
                joint.Position += SourceJointMath.Rotate(new Vector3(0f, -joint.Velocity * factor, 0f), tailAngle) * factor;
            }
            joint.Trail.Push(joint.Position, tailAngle, joint.Scale);
        }

        private static void MoveCoil(ref Joint joint, float factor)
        {
            if (joint.SubType == 14)
                return;
            if (joint.Trail.SegmentCount < joint.Trail.MaxTails - 1)
            {
                if (joint.Weapon <= 0f)
                {
                    for (int i = 1; i < joint.MultiUse; i++)
                    {
                        joint.Trail.Push(joint.Position, joint.Angle, joint.Scale);
                        joint.Start += SourceJointMath.Rotate(new Vector3(0f, joint.Velocity * factor, 0f), joint.Angle);
                        joint.TargetPosition.Y -= 20f * factor;
                        Vector3 spin = new(0f, joint.TargetPosition.Y, joint.Angle.Z);
                        joint.Position = joint.Start + SourceJointMath.Rotate(new Vector3(0f, 0f, joint.TargetPosition.X), spin);
                        joint.Velocity -= 2f * factor;
                        joint.TargetPosition.X -= 2.5f * factor;
                    }
                    joint.MultiUse += 2f * factor;
                }
                else
                    joint.Weapon -= factor;
            }
            if (joint.Life < 10f)
                joint.Light *= MathF.Pow(1f / 1.3f, factor);
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < _jointCount; i++)
                if (_joints[i].Live)
                    return true;
            return false;
        }

        protected override void OnDraw()
        {
            float factor = SourceJointMath.FrameFactor;
            float scroll = (long)FPSCounter.Instance.WorldTime % 1000 * 0.001f;
            float power = MathF.Pow(2f, factor);
            if (Batch.Begin(_thunderTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
            {
                for (int i = 0; i < _jointCount; i++)
                {
                    ref Joint joint = ref _joints[i];
                    if (!joint.Live || (joint.Coil && joint.SubType == 14) || (!joint.Coil && joint.SubType > 3))
                        continue;
                    if (joint.Coil)
                        Batch.AddTrail(joint.Trail, SourceQuadBatch.TrailFaces.Both, joint.Light, SourceQuadBatch.TrailShade.FlareForce,
                            (joint.Trail.MaxTails - 1) / 2, scroll);
                    else
                        Batch.AddTrail(joint.Trail, SourceQuadBatch.TrailFaces.Both, joint.Light, SourceQuadBatch.TrailShade.Flat,
                            16f / power, scroll * power);
                }
                Batch.End();
            }
            if (Batch.Begin(_flashTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _jointCount; i++)
                {
                    ref Joint joint = ref _joints[i];
                    if (joint.Live && !joint.Coil && joint.SubType > 3)
                        Batch.AddTrail(joint.Trail, SourceQuadBatch.TrailFaces.Both, joint.Light, SourceQuadBatch.TrailShade.Flat,
                            16f / power, scroll * power);
                }
                Batch.End();
            }
        }
    }
}
