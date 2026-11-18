#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>Vepar's BITMAP_BLUR + 1 homing ribbons from both hands during Energy Ball.</summary>
    internal sealed class VeparAttackEffect : EffectObject
    {
        private const int RibbonsPerHand = 4;
        private const int AttackTicksPerSecond = 25;
        // The source defaults to 60 FPS. Its homing step changes shape with the frame factor.
        private const int JointUpdatesPerSecond = 60;
        private const float FrameFactor = (float)AttackTicksPerSecond / JointUpdatesPerSecond;
        private const int MaxTails = 50 * JointUpdatesPerSecond / AttackTicksPerSecond;
        private const int RibbonUpdates = (int)(2f / FrameFactor) + 1;
        private const int EmissionTicks = 14;
        private const float Width = 60f;
        private const float Velocity = 40f;
        private const float HomingStep = Velocity * FrameFactor * FrameFactor * FrameFactor;
        private const float HitDistance = 2f * Velocity * FrameFactor;
        private const float ImpactLifetime = 24f;
        private static readonly float WobbleDamping = MathF.Pow(0.8f, FrameFactor);
        private static readonly Vector3 Tint = new(0f, 0.3f, 1f);

        private struct Ribbon
        {
            public SourceTrail Trail;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 Wobble;
            public int UpdatesLeft;
        }

        private struct Impact
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Pitch;
            public float Scale;
            public float Life;
            public float Gravity;
        }

        private readonly MonsterObject _caster;
        private readonly WalkerObject _target;
        private readonly int _action;
        private readonly int[] _handBones;
        private readonly Ribbon[] _ribbons;
        private readonly Impact[] _impacts = new Impact[96];
        private readonly SourceQuadBatch _batch = new(256);
        private Texture2D? _blurTexture;
        private Texture2D? _impactTexture;
        private double _elapsed;
        private int _update;
        private int _nextImpact;
        private bool _emitting = true;
        private bool _hasRibbons;
        private bool _hasImpacts;

        internal VeparAttackEffect(MonsterObject caster, WalkerObject target, int[] handBones, MonsterActionType action)
        {
            _caster = caster;
            _target = target;
            _action = (int)action;
            _handBones = handBones;
            _ribbons = new Ribbon[RibbonUpdates * handBones.Length * RibbonsPerHand];
            for (int i = 0; i < _ribbons.Length; i++)
                _ribbons[i].Trail = new SourceTrail(MaxTails);

            Position = caster.Position;
            IsTransparent = true;
            AffectedByTransparency = true;
            BoundingBoxLocal = new BoundingBox(new Vector3(-850f, -850f, -260f), new Vector3(850f, 850f, 430f));
        }

        public override async Task LoadContent()
        {
            _batch.Initialize(GraphicsDevice);
            var textures = await Task.WhenAll(
                TextureLoader.Instance.PrepareAndGetTexture("Effect/motion_blur.jpg"),
                TextureLoader.Instance.PrepareAndGetTexture("Effect/Fire01.jpg"));
            _blurTexture = textures[0];
            _impactTexture = textures[1];
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            if (_blurTexture == null || !_caster.Visible || _caster.IsDead || _caster.World != World ||
                !_target.Visible || _target.World != World ||
                _target is MonsterObject { IsDead: true } or PlayerObject { IsDead: true })
            {
                World?.RemoveObject(this);
                Dispose();
                return;
            }

            _emitting &= _caster.CurrentAction == _action;
            _elapsed += Math.Min(gameTime.ElapsedGameTime.TotalSeconds, 1.0 / AttackTicksPerSecond);
            Vector3 endpoint = _target.WorldPosition.Translation + Vector3.UnitZ * 80f;
            int lastUpdate = (int)(_elapsed * JointUpdatesPerSecond);
            while (_update <= lastUpdate)
            {
                _emitting &= _update * FrameFactor < EmissionTicks;
                if (_emitting && MuGame.Random.NextSingle() < FrameFactor)
                    EmitRibbons();

                _hasRibbons = false;
                for (int i = 0; i < _ribbons.Length; i++)
                {
                    ref Ribbon ribbon = ref _ribbons[i];
                    if (ribbon.UpdatesLeft == 0)
                        continue;

                    MoveRibbon(ref ribbon, endpoint);
                    _hasRibbons |= --ribbon.UpdatesLeft > 0;
                }

                MoveImpacts();
                _update++;
            }

            if (!_emitting && !_hasRibbons && !_hasImpacts)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        private void EmitRibbons()
        {
            var bones = _caster.GetBoneTransforms();
            if (bones == null)
                return;

            int firstRibbon = (_update % RibbonUpdates) * _handBones.Length * RibbonsPerHand;
            Matrix casterTransform = _caster.WorldPosition;
            for (int hand = 0; hand < _handBones.Length; hand++)
            {
                int bone = _handBones[hand];
                if ((uint)bone >= (uint)bones.Length)
                    continue;

                Vector3 origin = (bones[bone] * casterTransform).Translation;
                for (int i = 0; i < RibbonsPerHand; i++)
                {
                    ref Ribbon ribbon = ref _ribbons[firstRibbon + hand * RibbonsPerHand + i];
                    ribbon.Position = origin;
                    ribbon.Angle = Vector3.Zero;
                    ribbon.Wobble = Vector3.Zero;
                    ribbon.UpdatesLeft = RibbonUpdates;
                    ribbon.Trail.Clear();
                    ribbon.Trail.Push(origin, Vector3.UnitX, Vector3.UnitZ, Width);
                }
            }
        }

        private void MoveRibbon(ref Ribbon ribbon, Vector3 endpoint)
        {
            // MoveJoint advances once along the previous heading before the homing loop.
            SourceJointMath.AngleBasis(ribbon.Angle, out var endRight, out var endForward, out var endUp);
            ribbon.Position -= endForward * (Velocity * FrameFactor);
            for (int i = 0; i < MaxTails; i++)
            {
                SourceJointMath.MoveHumming(ref ribbon.Position, ref ribbon.Angle, endpoint, 25f, FrameFactor);
                ribbon.Wobble.X = (ribbon.Wobble.X + FrameFactor * MuGame.Random.Next(-128, 128) / Width) * WobbleDamping;
                ribbon.Wobble.Z = (ribbon.Wobble.Z + FrameFactor * MuGame.Random.Next(-128, 128) / Width) * WobbleDamping;
                Vector3 heading = ribbon.Angle + ribbon.Wobble;
                SourceJointMath.AngleBasis(heading, out var right, out var forward, out var up);
                ribbon.Trail.Push(ribbon.Position, right, up, Width);

                if (Vector3.DistanceSquared(ribbon.Position, endpoint) <= HitDistance * HitDistance)
                {
                    if (MuGame.Random.NextSingle() < FrameFactor * FrameFactor * 0.5f)
                        EmitImpact(ribbon.Position, ribbon.Angle);
                    break;
                }

                ribbon.Position -= forward * HomingStep;
            }

            ribbon.Trail.Push(ribbon.Position, endRight, endUp, Width);
        }

        private void EmitImpact(Vector3 position, Vector3 angle)
        {
            Vector3 speed = new(0f, -MuGame.Random.Next(32, 48) * 0.1f, 0f);
            _impacts[_nextImpact] = new Impact
            {
                Position = position,
                Velocity = SourceJointMath.Rotate(speed, angle),
                Pitch = angle.X,
                Scale = MuGame.Random.Next(128, 192) * 0.01f,
                Life = ImpactLifetime
            };
            _nextImpact = (_nextImpact + 1) % _impacts.Length;
        }

        private void MoveImpacts()
        {
            _hasImpacts = false;
            for (int i = 0; i < _impacts.Length; i++)
            {
                ref Impact impact = ref _impacts[i];
                if (impact.Life <= 0f)
                    continue;

                impact.Life -= FrameFactor;
                impact.Position += impact.Velocity * FrameFactor;
                impact.Gravity += 0.004f * FrameFactor;
                impact.Position.Z += impact.Gravity * 10f * FrameFactor;
                impact.Scale -= 0.04f * FrameFactor;
                _hasImpacts |= impact.Life > 0f;
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible)
                return;

            if (_hasRibbons && _batch.Begin(_blurTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.PointClamp))
            {
                foreach (ref readonly Ribbon ribbon in _ribbons.AsSpan())
                    if (ribbon.UpdatesLeft > 0)
                        _batch.AddTrail(ribbon.Trail, SourceQuadBatch.TrailFaces.Both, Tint, SourceQuadBatch.TrailShade.Flat, MaxTails - 1);
                _batch.End();
            }

            if (_hasImpacts && _impactTexture is { } impactTexture &&
                _batch.Begin(impactTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                foreach (ref readonly Impact impact in _impacts.AsSpan())
                {
                    if (impact.Life <= 0f)
                        continue;

                    int frame = Math.Clamp((int)((ImpactLifetime - 1f - impact.Life) / 6f), 0, 3);
                    _batch.AddSpriteRegion(impact.Position, impactTexture.Width * 0.25f * impact.Scale,
                        impactTexture.Height * impact.Scale, Tint, impact.Pitch, frame * 0.25f, 0f, 0.25f, 1f);
                }
                _batch.End();
            }
        }

        public override void Dispose()
        {
            _batch.Dispose();
            base.Dispose();
        }
    }
}
