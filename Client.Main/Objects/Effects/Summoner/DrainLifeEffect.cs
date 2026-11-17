#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    // ZzzCharacter.cpp:5085; MODEL_ALICE_DRAIN_LIFE ZzzEffect.cpp:890/8990;
    // BITMAP_LIGHT+2 sub7 and ghost/energy joints: ZzzEffectParticle.cpp:916/4110,
    // ZzzEffectJoint.cpp:169/2623/2932/3195/6772.
    internal sealed class DrainLifeEffect : SourceSkillEffectBase
    {
        private const int MaxParticles = 3000;
        private const int MaxJoints = 500;
        private struct Particle
        {
            public bool Live;
            public Vector3 Position, Velocity, Force, Light;
            public float Life, Scale, Alpha, Rotation;
        }
        private struct Joint
        {
            public DrainLifeEffect? Owner;
            public bool Ghost, Sprite;
            public Vector3 Position, Angle, Light;
            public float TargetZ, Life, Velocity, Scale;
            public SourceTrail Trail;
        }
        // Shared by Drain Life instances, avoiding a 500 x 200-tail allocation per cast.
        private static readonly Joint[] s_joints = CreateJointPool();
        private readonly Particle[] _particles = new Particle[MaxParticles];
        private readonly Vector3[] _lastFlares = new Vector3[MaxJoints];
        private readonly WalkerObject _target;
        private Texture2D? _lightTexture, _ghostTexture, _energyTexture, _flareTexture;
        private static readonly Vector3 DrainGlow = new(1f, 0.2f, 0.25f);
        private float _life = 70f;
        private float _absorbGlow;
        private int _particleCount, _lastFlareCount;

        private static Joint[] CreateJointPool()
        {
            var joints = new Joint[MaxJoints];
            for (int i = 0; i < joints.Length; i++) joints[i].Trail = new SourceTrail(200);
            return joints;
        }

        public DrainLifeEffect(WalkerObject caster, WalkerObject target) : base(caster, float.MaxValue, .56f, 4096)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _lightTexture = await LoadTexture("Effect/flare01.jpg");
            _ghostTexture = await LoadTexture("Effect/gostmark01.jpg");
            _energyTexture = await LoadTexture("Effect/JointEnergy01.jpg");
            _flareTexture = await LoadTexture("Effect/flareRed.jpg");
        }

        protected override void OnStart() => PlayUnmappedSound(214, "Sound/SE_Ch_summoner_skill07_lifedrain.wav");

        protected override void OnTick(float factor)
        {
            _lastFlareCount = 0;
            bool targetAvailable = _target.Status != GameControlStatus.Disposed && _target.World != null;
            if (_life > 0f)
            {
                if (targetAvailable) Emit(factor);
                _life -= factor;
            }
            MoveParticles(factor);
            MoveJoints(factor, targetAvailable);
            UpdateLights(factor, targetAvailable);
        }

        private void UpdateLights(float factor, bool targetAvailable)
        {
            // Slot 0: red glow on the drained target while the skill emits.
            float drain = targetAvailable ? EffectLights.Envelope(70f - _life, 70f, 8f, 20f) : 0f;
            if (drain > 0f)
                Lights.Set(0, _target.WorldPosition.Translation + new Vector3(0f, 0f, 100f), DrainGlow, 240f,
                    1.1f * drain * EffectLights.Flicker((float)(FPSCounter.Instance.WorldTime * 0.001), 0.7f, 0.12f));
            else
                Lights.Hide(0);

            // Slot 1: pulse on the caster when energy joints arrive.
            _absorbGlow = _lastFlareCount > 0 ? 1f : MathF.Max(0f, _absorbGlow - 0.12f * factor);
            if (_absorbGlow > 0f)
                Lights.Set(1, Caster.WorldPosition.Translation + new Vector3(0f, 0f, 120f), DrainGlow, 200f, 1.2f * _absorbGlow);
            else
                Lights.Hide(1);
        }

        private void Emit(float factor)
        {
            int random = MuGame.Random.Next(10);
            int count = random == 0 ? 0 : random <= 7 ? 1 : 2;
            for (int i = 0; i < count; i++) SpawnMote(Caster.WorldPosition.Translation, Caster.Angle, factor);
            if (_life <= 60f)
                for (int i = 0; i < count; i++) SpawnMote(_target.WorldPosition.Translation, _target.Angle, factor);

            if (_life <= 70f && _life >= 66f)
            {
                random = MuGame.Random.Next(10);
                count = random == 0 ? 0 : random <= 3 ? 1 : random <= 8 ? 3 : 4;
                for (int i = 0; i < count; i++) SpawnGhost();
            }
            if ((int)_life == 64)
            {
                Matrix[]? transforms = _target.GetBoneTransforms();
                var bones = _target.Model?.Bones;
                if (transforms == null || bones == null) return;
                int boneCount = Math.Min(transforms.Length, bones.Length);
                for (int i = 0; i < boneCount; i++)
                {
                    Vector3 source = Vector3.Transform(transforms[i].Translation, _target.WorldPosition);
                    if (FPSCounter.Instance.RandFPSCheck(2) && FPSCounter.Instance.RandFPSCheck(1))
                        SpawnEnergy(source, ToDegrees(_target.Angle));
                }
            }
        }

        private void SpawnMote(Vector3 position, Vector3 angle, float factor)
        {
            position += new Vector3(MuGame.Random.Next(80) - 40, MuGame.Random.Next(60) - 30, 80 + MuGame.Random.Next(180) - 100);
            if (!FPSCounter.Instance.RandFPSCheck(1)) return;
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].Live) continue;
                float scale = 1.8f + (MuGame.Random.Next(4) + 8) * .01f;
                MuGame.Random.Next(1); // original rand()%1 is consumed
                Vector3 degrees = ToDegrees(angle);
                degrees.Z = MuGame.Random.Next(360);
                Vector3 force = SourceJointMath.Rotate(new Vector3(0f, (MuGame.Random.Next(2) + 60) * .1f, 0f), degrees);
                force.Z += 2f * factor;
                _particles[i] = new Particle { Live = true, Position = position, Force = force,
                    Light = new Vector3(1f, .2f, .2f), Life = 18f, Scale = scale, Alpha = 1f,
                    Rotation = (int)FPSCounter.Instance.WorldTime % 360 };
                _particleCount++;
                return;
            }
        }

        private void SpawnGhost()
        {
            Matrix[]? bones = Caster.GetBoneTransforms();
            if (bones == null || bones.Length <= 18) return;
            Vector3 position = Vector3.Transform(bones[18].Translation, Caster.WorldPosition);
            Vector3 angle = ToDegrees(Caster.Angle);
            SourceJointMath.AngleBasis(angle, out _, out Vector3 forward, out _);
            if (forward.LengthSquared() > 0f) forward.Normalize();
            position.X += forward.X * 100f + MuGame.Random.Next(10) * 5f;
            position.Y += forward.Y * 100f + MuGame.Random.Next(10) * 5f;
            position.Z += MuGame.Random.Next(10) * 5f;
            float targetZ = _target.WorldPosition.Translation.Z + 100f + (MuGame.Random.Next(10) - 5) * 4f;
            if (!FPSCounter.Instance.RandFPSCheck(1)) return;
            int index = FindFreeJoint();
            if (index < 0) return;
            ref Joint joint = ref s_joints[index];
            angle.Z += FPSCounter.Instance.RandFPSCheck(2) ? 90f : -90f;
            angle += new Vector3(MuGame.Random.Next(100) - 50, MuGame.Random.Next(100) - 50, MuGame.Random.Next(100) - 50);
            joint.Owner = this; joint.Ghost = true; joint.Sprite = false;
            joint.Position = position; joint.Angle = angle; joint.TargetZ = targetZ;
            joint.Velocity = 1f + MuGame.Random.Next(10) * .2f;
            joint.Life = 30 + MuGame.Random.Next(20) - 10;
            joint.Scale = 40 + MuGame.Random.Next(60) - 30;
            joint.Light = new Vector3(.8f, .1f, .2f);
            joint.Trail.Reset(SourceJointMath.ScaledMaxTails(20 + MuGame.Random.Next(10) - 5));
            joint.Trail.Push(position, angle, joint.Scale);
        }

        private void SpawnEnergy(Vector3 position, Vector3 angle)
        {
            int index = FindFreeJoint();
            if (index < 0) return;
            ref Joint joint = ref s_joints[index];
            joint.Owner = this; joint.Ghost = false; joint.Sprite = false;
            joint.Position = position; joint.Angle = angle; joint.Velocity = 3f;
            joint.Life = 120f; joint.Scale = 10f; joint.Light = new Vector3(1f, 0f, .1f);
            joint.Trail.Reset(SourceJointMath.ScaledMaxTails(8));
            joint.Trail.Push(position, angle, joint.Scale);
        }

        private static int FindFreeJoint()
        {
            for (int i = 0; i < s_joints.Length; i++) if (s_joints[i].Owner == null) return i;
            return -1;
        }

        private void MoveParticles(float factor)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                ref Particle p = ref _particles[i];
                if (!p.Live) continue;
                p.Life -= factor;
                if (p.Life <= 0f) { p.Live = false; _particleCount--; continue; }
                p.Position += p.Velocity * factor;
                p.Force.Z -= .1f * factor;
                p.Position += p.Force * factor;
                p.Scale += .04f * factor;
                if (p.Life < 10f) p.Alpha -= .1f * factor;
                if (p.Life < 20f) { float damping = MathF.Pow(.6f, factor); p.Force.X *= damping; p.Force.Y *= damping; }
                p.Light *= p.Alpha;
                if (p.Alpha < 0f) { p.Live = false; _particleCount--; continue; }
                p.Velocity.X += .05f * factor; p.Velocity.Y += .05f * factor;
                p.Rotation += 2f * factor;
                p.Light += Vector3.One * (p.Life / 8f * .02f * factor);
            }
        }

        private void MoveJoints(float factor, bool targetAvailable)
        {
            for (int i = 0; i < s_joints.Length; i++)
            {
                ref Joint j = ref s_joints[i];
                if (j.Owner != this) continue;
                if (!targetAvailable) { j.Owner = null; continue; }
                Vector3 oldAngle = j.Angle;
                j.Position += SourceJointMath.Rotate(new Vector3(0f, -j.Velocity, 0f), oldAngle) * factor;
                j.Sprite = false;
                if (j.Ghost)
                {
                    Vector3 target = _target.WorldPosition.Translation; target.Z = j.TargetZ;
                    SourceJointMath.MoveHumming(ref j.Position, ref j.Angle, target, j.Velocity, factor);
                    j.Velocity += 2f * factor;
                    if (j.Life < 10f) j.Light *= MathF.Pow(1f / 1.2f, factor);
                }
                else
                {
                    if (j.Life >= 100f) { j.Angle.Z += 10f * factor; j.Position.Z += 6f * factor; }
                    else
                    {
                        j.Velocity = MathF.Min(30f, j.Velocity + 5f * factor);
                        float oldYaw = j.Angle.Z;
                        float distance = SourceJointMath.MoveHumming(ref j.Position, ref j.Angle, Caster.WorldPosition.Translation + Vector3.UnitZ * 120f, j.Velocity, factor);
                        if (distance <= 35f)
                        {
                            // The checked LIGHTNING+1 sub1 child has scale zero. Its terrain light is not ported.
                            FPSCounter.Instance.RandFPSCheck(1);
                            SoundController.Instance.PlayBuffer("Sound/pEnergy.wav");
                            MuGame.Random.Next(4); // AddTerrainLight luminosity in the original
                            _lastFlares[_lastFlareCount++] = j.Position;
                            j.Owner = null;
                            continue;
                        }
                        if (distance <= 70f && MathF.Abs(oldYaw - j.Angle.Z) > 20f && j.Velocity >= 20f) j.Velocity -= 10f * factor;
                    }
                    MuGame.Random.Next(4); // AddTerrainLight luminosity in the original
                    j.Sprite = true;
                }
                j.Trail.Push(j.Position, oldAngle, j.Scale);
                j.Life -= factor;
                if (j.Life < 0f) j.Owner = null;
            }
        }

        private static Vector3 ToDegrees(Vector3 angle) => new(MathHelper.ToDegrees(angle.X), MathHelper.ToDegrees(angle.Y), MathHelper.ToDegrees(angle.Z));

        protected override bool HasActiveContent()
        {
            if (_life > 0f || _particleCount > 0 || _lastFlareCount > 0 || _absorbGlow > 0f) return true;
            for (int i = 0; i < s_joints.Length; i++) if (s_joints[i].Owner == this) return true;
            return false;
        }

        protected override void OnDraw()
        {
            DrawJoints(_ghostTexture, true);
            DrawJoints(_energyTexture, false);
            if (Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _particles.Length; i++)
                {
                    ref Particle p = ref _particles[i];
                    if (p.Live) Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
                }
                Batch.End();
            }
            if (Batch.Begin(_flareTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < s_joints.Length; i++)
                {
                    ref Joint j = ref s_joints[i];
                    if (j.Owner == this && j.Sprite) Batch.AddSprite(j.Position, .3f, Vector3.One);
                }
                for (int i = 0; i < _lastFlareCount; i++) Batch.AddSprite(_lastFlares[i], .3f, Vector3.One);
                Batch.End();
            }
        }

        private void DrawJoints(Texture2D? texture, bool ghost)
        {
            if (!Batch.Begin(texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp)) return;
            for (int i = 0; i < s_joints.Length; i++)
            {
                ref Joint j = ref s_joints[i];
                if (j.Owner == this && j.Ghost == ghost)
                    Batch.AddTrail(j.Trail, SourceQuadBatch.TrailFaces.Both, j.Light, SourceQuadBatch.TrailShade.Flat, j.Trail.MaxTails - 1);
            }
            Batch.End();
        }

        protected override void DisposeEffect()
        {
            for (int i = 0; i < s_joints.Length; i++) if (s_joints[i].Owner == this) s_joints[i].Owner = null;
        }
    }
}
