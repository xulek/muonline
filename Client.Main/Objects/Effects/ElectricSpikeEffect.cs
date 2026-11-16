#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Dark Lord Electric Spike (AT_SKILL_THUNDER_STRIKE 65).
    ///
    /// ZzzCharacter.cpp AttackStage (PLAYER_SKILL_FLASH, AnimationFrame &gt;= 5.5):
    ///   CalcAddPosition(o, 0, -90, -50, Position);
    ///   CreateEffect(BITMAP_FLARE_FORCE, Position, o-&gt;Angle, o-&gt;Light, 0, o);
    /// which immediately creates five BITMAP_FLARE_FORCE joints (ZzzEffect.cpp):
    ///   sub 0 (scale 250): a straight JointThunder01 beam that shoots forward with an
    ///   accelerating step (Velocity -3, -2 per tail, MultiUse +2 tails per frame, 30 tails);
    ///   sub 1-4 (scale 100): helix strands around the beam (radius 80 shrinking by 2.5,
    ///   roll -20 deg per tail), sub 1/2 start after a 2-4 frame delay, sub 3/4 mirrored.
    /// Lifetime 20 (+delay), light (1, 0.8, 1) fading /1.3 per frame below 10 frames.
    /// packet WSclient.cpp:4928 (PLAYER_SKILL_FLASH / Fenrir PLAYER_FENRIR_ATTACK_DARKLORD_FLASH, no sound),
    /// gate ZzzCharacter.cpp:2717 (AnimationFrame &gt;= 5.5), release ZzzCharacter.cpp:4221 (+80 ride / +40 Fenrir flash,
    /// SOUND_ELEC_STRIKE), joint ZzzEffectJoint.cpp:2449 (Z += 150F, MaxTails 30 / F), 6482 (move), 7049 / 7199 (render).
    /// </summary>
    internal sealed class ElectricSpikeEffect : SourceSkillEffectBase
    {
        private const string TexturePath = "Effect/JointThunder01.jpg";
        private const string SoundPath = "Sound/sDarkElecSpike.wav"; // SOUND_ELEC_STRIKE
        private const int JointCount = 5;
        private const int MaxTails = 30;
        private readonly int _maxTails = SourceJointMath.ScaledMaxTails(MaxTails);

        private static readonly float[] JointScales = { 250f, 100f, 100f, 100f, 100f };

        private readonly Joint[] _joints = new Joint[JointCount];
        private Texture2D? _texture;

        private sealed class Joint
        {
            public SourceTrail Trail = null!;
            public int SubType;
            public float Scale;
            public Vector3 Angle;
            public Vector3 Position;
            public Vector3 StartPosition;
            public Vector3 Light;
            public float Velocity;
            public float MultiUse;
            public float Delay;
            public float Radius;
            public float Roll;
            public float Life;
            public int Pushes;
        }

        public ElectricSpikeEffect(WalkerObject caster)
            : base(caster, triggerFrame: 5.5f, triggerTimeoutSeconds: 14f * 0.04f, maxQuads: JointCount * 200 * 2)
        {
            for (int i = 0; i < JointCount; i++)
                _joints[i] = new Joint { Trail = new SourceTrail(_maxTails) };
        }

        protected override async Task LoadEffectContent()
        {
            _texture = await LoadTexture(TexturePath);
        }

        protected override void OnStart()
        {
            float yaw = CasterYawDegrees;
            Vector3 angle = new(0f, 0f, yaw);
            Vector3 origin = CasterPosition + SourceJointMath.Rotate(new Vector3(0f, -90f, -50f), angle);
            if (Caster is Player.PlayerObject player)
            {
                if (player.CurrentAction == Models.PlayerAction.PlayerAttackRideAttackFlash)
                    origin.Z += 80f;
                else if (player.CurrentAction == Models.PlayerAction.PlayerFenrirAttackDarklordFlash)
                    origin.Z += 40f;
            }
            origin.Z += 150f * SourceJointMath.FrameFactor;   // CreateJoint: o->Position[2] += 150 * F

            for (int i = 0; i < JointCount; i++)
            {
                Joint joint = _joints[i];
                joint.SubType = i;
                joint.Scale = JointScales[i];
                joint.Angle = angle;
                joint.StartPosition = origin;
                joint.Position = origin;
                joint.Light = new Vector3(1f, 0.8f, 1f);
                joint.Velocity = -3f;
                joint.MultiUse = 1f;
                joint.Life = 20f;
                joint.Pushes = 0;
                joint.Trail.Clear();

                if (i >= 1)
                {
                    joint.Radius = 80f;
                    joint.Roll = i == 2 || i == 4 ? -180f : 180f;
                    joint.Delay = i >= 3 ? 0f : MuGame.Random.Next(3) + 2f;
                    joint.Life += joint.Delay;
                    joint.Position = HelixPosition(joint);
                }
            }

            SoundController.Instance.PlayBuffer(SoundPath);
        }

        private static Vector3 HelixPosition(Joint joint)
        {
            Vector3 offset = SourceJointMath.Rotate(new Vector3(0f, 0f, joint.Radius), new Vector3(0f, joint.Roll, joint.Angle.Z));
            return joint.SubType >= 3 ? joint.StartPosition - offset : joint.StartPosition + offset;
        }

        protected override void OnTick(float factor)
        {
            for (int i = 0; i < JointCount; i++)
            {
                Joint joint = _joints[i];
                if (joint.Life <= 0f)
                    continue;

                if (Math.Min(joint.Pushes, _maxTails) < _maxTails - 1)
                {
                    if (joint.SubType == 0)
                        GrowBeam(joint, factor);
                    else if (joint.Delay <= 0f)
                        GrowHelix(joint, factor);
                    else
                        joint.Delay -= factor;
                }

                if (joint.Life < 10f)
                    joint.Light *= MathF.Pow(1f / 1.3f, factor);

                joint.Life -= factor;
            }
        }

        private void GrowBeam(Joint joint, float factor)
        {
            for (int i = 1; i < joint.MultiUse; i++)
            {
                joint.Trail.Push(joint.Position, joint.Angle, joint.Scale);
                joint.Pushes++;
                joint.StartPosition += SourceJointMath.Rotate(new Vector3(0f, joint.Velocity * factor, 0f), joint.Angle);
                joint.Position = joint.StartPosition;
                joint.Velocity -= 2f * factor;
            }

            joint.MultiUse += 2f * factor;
        }

        private void GrowHelix(Joint joint, float factor)
        {
            for (int i = 1; i < joint.MultiUse; i++)
            {
                joint.Trail.Push(joint.Position, joint.Angle, joint.Scale);
                joint.Pushes++;
                joint.StartPosition += SourceJointMath.Rotate(new Vector3(0f, joint.Velocity * factor, 0f), joint.Angle);

                // The C++ `SubType >= 11 || SubType <= 13` test is always true: every strand
                // rolls by -20 deg per tail.
                joint.Roll -= 20f * factor;
                joint.Position = HelixPosition(joint);
                joint.Velocity -= 2f * factor;
                joint.Radius -= 2.5f * factor;
            }

            joint.MultiUse += 2f * factor;
        }

        protected override bool HasActiveContent()
        {
            for (int i = 0; i < JointCount; i++)
                if (_joints[i].Life > 0f)
                    return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (!Batch.Begin(_texture, Graphics.Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearWrap))
                return;

            float scroll = (long)FPSCounter.Instance.WorldTime % 1000 * 0.001f;
            for (int i = 0; i < JointCount; i++)
            {
                Joint joint = _joints[i];
                if (joint.Life <= 0f)
                    continue;

                // RenderJoints BITMAP_FLARE_FORCE sub 0-4: U over (MaxTails - 1) / 2 minus scroll.
                Batch.AddTrail(joint.Trail, SourceQuadBatch.TrailFaces.Both, joint.Light,
                    SourceQuadBatch.TrailShade.FlareForce, (_maxTails - 1) / 2, scroll);
            }

            Batch.End();
        }
    }
}
