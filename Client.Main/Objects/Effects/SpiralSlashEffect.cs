#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    // AT_SKILL_ONEFLASH 57: WSclient.cpp:4212, ZzzCharacter.cpp:2796/4553;
    // BITMAP_FLARE 23: ZzzEffectJoint.cpp:1949/2932/5782/6921.
    internal sealed class SpiralSlashEffect : SourceSkillEffectBase
    {
        private const int MaxJoints = 500;
        private const int MaxTailCapacity = 200;
        private const int BaseMaxTails = 15;
        private const float JointScale = 40f;
        private const string TexturePath = "Effect/Flare.jpg";
        private const string SoundPath = "Sound/BattleCastle/sCColdAttack.wav";

        private static readonly float[] YawOffsets = { 90f, 90f, -90f, -90f, 180f };
        private static readonly float[] HeightOffsets = { 200f, 10f, 200f, 10f, 100f };
        private static readonly Joint[] JointPool = CreateJointPool();
        private readonly Joint?[] _joints = new Joint?[MaxJoints];
        private int _jointCount;
        private Texture2D? _texture;
        private bool _released;
        private float _attackTime = 1f;

        private sealed class Joint
        {
            public readonly SourceTrail Trail = new(MaxTailCapacity);
            public bool Active;
            public int Kind;
            public Vector3 Angle;
            public Vector3 HeadAngle;
            public Vector3 StartPosition;
            public Vector3 Position;
            public float Velocity;
            public float TurnSpeed;
            public float StepCount;
            public float Life;
        }

        public SpiralSlashEffect(WalkerObject caster)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 1024)
        {
        }

        private static Joint[] CreateJointPool()
        {
            var joints = new Joint[MaxJoints];
            for (int i = 0; i < joints.Length; i++)
                joints[i] = new Joint();
            return joints;
        }

        protected override async Task LoadEffectContent() => _texture = await LoadTexture(TexturePath);

        protected override void OnStart() { }

        protected override void OnTick(float factor)
        {
            if (!_released)
            {
                double frame = Caster.AnimationFramePosition;
                if (frame > 5f && FPSCounter.Instance.RandFPSCheck(1))
                {
                    Spawn(0);
                    Spawn(1);
                    Spawn(4);
                    _released = true;
                    if (World?.WorldIndex == 31)
                        SoundController.Instance.PlayBuffer(SoundPath);
                }
                else if (frame > 2.3f && frame < 2.6f && FPSCounter.Instance.RandFPSCheck(1))
                {
                    Spawn(2);
                    Spawn(3);
                    if (World?.WorldIndex == 31)
                        SoundController.Instance.PlayBuffer(SoundPath);
                }
                _attackTime += factor;
                if ((int)_attackTime >= 15)
                    _released = true;
            }

            for (int i = 0; i < _jointCount;)
            {
                Joint joint = _joints[i]!;
                TickJoint(joint, factor);
                if (!joint.Active)
                {
                    _joints[i] = _joints[--_jointCount];
                    _joints[_jointCount] = null;
                }
                else
                    i++;
            }
        }

        private void Spawn(int kind)
        {
            Joint? joint = null;
            for (int i = 0; i < JointPool.Length; i++)
            {
                if (!JointPool[i].Active)
                {
                    joint = JointPool[i];
                    break;
                }
            }
            if (joint == null)
                return;

            Vector3 angle = Caster.Angle;
            angle = new Vector3(MathHelper.ToDegrees(angle.X), MathHelper.ToDegrees(angle.Y), MathHelper.ToDegrees(angle.Z));
            joint.Active = true;
            _joints[_jointCount++] = joint;
            joint.Kind = kind;
            joint.Angle = angle;
            joint.HeadAngle = angle;
            joint.HeadAngle.Z += YawOffsets[kind];
            joint.StartPosition = CasterPosition + Vector3.UnitZ * HeightOffsets[kind];
            joint.Position = joint.StartPosition;
            joint.Velocity = 0f;
            joint.TurnSpeed = 5f;
            joint.StepCount = 1f;
            joint.Life = 20f + (4 - kind);
            joint.Trail.Reset(SourceJointMath.ScaledMaxTails(BaseMaxTails));
        }

        private static void TickJoint(Joint joint, float factor)
        {
            if (!joint.Active)
                return;

            if (joint.Velocity != 0f)
                joint.Position += SourceJointMath.Rotate(new Vector3(0f, -joint.Velocity, 0f), joint.Angle) * factor;

            if (joint.Life < 20f)
            {
                joint.Velocity = 200f;
                if (joint.Life < 8f)
                {
                    joint.Velocity = 0f;
                    joint.Trail.Push(joint.Position, joint.HeadAngle, JointScale);
                }
                else
                {
                    for (float i = 0f; i < joint.StepCount; i++)
                    {
                        joint.TurnSpeed += 1.5f * factor;
                        float turn = joint.TurnSpeed * factor;
                        switch (joint.Kind)
                        {
                            case 0: joint.HeadAngle.X += 3f * factor; joint.HeadAngle.Z -= turn; break;
                            case 1: joint.HeadAngle.X -= 3f * factor; joint.HeadAngle.Z -= turn; break;
                            case 2: joint.HeadAngle.X += 3f * factor; joint.HeadAngle.Z += turn; break;
                            case 3: joint.HeadAngle.X -= 3f * factor; joint.HeadAngle.Z += turn; break;
                            case 4: joint.HeadAngle.Z -= turn; break;
                        }
                        joint.TurnSpeed = MathF.Min(15f, joint.TurnSpeed);
                        joint.Position = joint.StartPosition +
                            SourceJointMath.Rotate(new Vector3(0f, -joint.Velocity * factor, 0f), joint.HeadAngle);
                        joint.Trail.Push(joint.Position, joint.HeadAngle, JointScale);
                        if (joint.Trail.SegmentCount >= joint.Trail.MaxTails - 1)
                        {
                            joint.Velocity = 0f;
                            break;
                        }
                    }
                    joint.StepCount += 4f * factor;
                }
            }

            joint.Life -= factor;
            if (joint.Life < 0f)
                joint.Active = false;
        }

        protected override bool HasActiveContent()
        {
            return !_released || _jointCount > 0;
        }

        protected override void OnDraw()
        {
            if (!Batch.Begin(_texture, Graphics.Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < _jointCount; i++)
            {
                Joint joint = _joints[i]!;
                Batch.AddTrail(joint.Trail, SourceQuadBatch.TrailFaces.Both, Vector3.One,
                    SourceQuadBatch.TrailShade.Flat, joint.Trail.MaxTails - 1);
            }
            Batch.End();
        }

        protected override void DisposeEffect()
        {
            for (int i = 0; i < _jointCount; i++)
            {
                _joints[i]!.Active = false;
                _joints[i] = null;
            }
            _jointCount = 0;
        }
    }
}
