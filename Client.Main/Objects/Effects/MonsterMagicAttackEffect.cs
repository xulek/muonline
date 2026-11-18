using Client.Main.Controls;
using Client.Main.Models;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using System;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Short monster magic attack: animated hand/bone origins linked to the packet target.
    /// This mirrors the Cursed Wizard branch in SourceMain5.2 AttackEffect.
    /// </summary>
    public sealed class MonsterMagicAttackEffect : EffectObject
    {
        private const float LifetimeSeconds = 0.6f;

        private readonly ModelObject _sourceModel;
        private readonly ushort _targetId;
        private readonly EffectLights _lights;
        private float _elapsed;
        private Vector3 _targetPosition;

        public MonsterMagicAttackEffect(
            ModelObject sourceModel,
            int[] sourceBones,
            ushort targetId,
            int requiredAction,
            Vector3 sourceOffset = default)
        {
            _sourceModel = sourceModel;
            _targetId = targetId;
            _lights = new EffectLights(this, 1);

            int[] bones = sourceBones == null || sourceBones.Length == 0
                ? new[] { 0 }
                : (int[])sourceBones.Clone();

            Position = sourceModel.Position;
            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = Microsoft.Xna.Framework.Graphics.BlendState.Additive;
            DepthState = Microsoft.Xna.Framework.Graphics.DepthStencilState.DepthRead;
            BoundingBoxLocal = new BoundingBox(
                new Vector3(-5000f, -5000f, -5000f),
                new Vector3(5000f, 5000f, 5000f));

            Children.Add(new MonsterBoneLightningEffect
            {
                SourceModel = sourceModel,
                SourceBoneIndices = bones,
                SourceOffset = sourceOffset,
                TargetProvider = GetTargetPosition,
                LightColor = Color.White,
                LineScale = 0.65f,
                RequiredAction = requiredAction
            });
            Children.Add(new MonsterBoneLightningEffect
            {
                SourceModel = sourceModel,
                SourceBoneIndices = bones,
                SourceOffset = sourceOffset,
                TargetProvider = GetTargetPosition,
                LightColor = new Color(180, 220, 255),
                LineScale = 0.35f,
                RequiredAction = requiredAction
            });
            Children.Add(new MonsterBoneEnergyEffect
            {
                SourceModel = sourceModel,
                SourceBoneIndices = bones,
                SourceOffset = sourceOffset,
                EmitOnce = true,
                RequiredAction = requiredAction
            });
        }

        public override void Update(GameTime gameTime)
        {
            if (Status != GameControlStatus.Ready)
            {
                base.Update(gameTime);
                return;
            }

            _elapsed += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_elapsed >= LifetimeSeconds || World is not WalkableWorldControl world ||
                _sourceModel.World != world || !world.TryGetWalkerById(_targetId, out var target) ||
                !target.Visible || target is MonsterObject { IsDead: true } or PlayerObject { IsDead: true })
            {
                RemoveSelf();
                return;
            }

            _targetPosition = target.WorldPosition.Translation;
            base.Update(gameTime);

            // Cool-white lightning glow at the struck target, flickering with the arcs.
            float glow = 0.85f * EffectLights.Envelope(_elapsed, LifetimeSeconds, 0.05f, 0.35f) *
                EffectLights.Flicker(_elapsed, 1.7f, 0.4f);
            _lights.Set(0, _targetPosition + Vector3.UnitZ * 60f, new Vector3(0.75f, 0.85f, 1f), 260f, glow);
        }

        public override void Dispose()
        {
            _lights.Dispose();
            base.Dispose();
        }

        private Vector3 GetTargetPosition() => _targetPosition;

        private void RemoveSelf()
        {
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }
    }
}
