#nullable enable
using System;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Monsters
{
    /// <summary>
    /// SourceMain5.2 CreateWeaponBlur (ZzzCharacter.cpp:3593) for monsters. Only the crust branch (3700, Mega Crust,
    /// Alpha Crust, Omega Wing: attack action, BlurType 1 / BlurMapping 6) produces a blur: the sword branch (3745)
    /// needs Weapon[0].Type in the sword range and the Aegis / Death Centurion / Shriker branches (3708-3745) only
    /// create blurs when a weapon type is set (3838), and monsters keep Weapon[].Type = -1 (ZzzCharacter.cpp:11556).
    /// One sample per frame at c->Weapon[0].LinkBone (3807-3813), Light white, MoveBlurs / RenderBlurs through
    /// <see cref="SourceBlur"/>.
    /// </summary>
    internal sealed class MonsterWeaponBlur : IDisposable
    {
        private static readonly Vector3 Pos1 = new(0f, -20f, 0f);   // BlurType 1
        private static readonly Vector3 Pos2 = new(0f, -120f, 0f);

        private SourceBlur? _blur;
        private SourceQuadBatch? _batch;
        private object? _classified;
        private bool _primary;

        /// <summary>CreateWeaponBlur followed by MoveBlurs (both once per frame).</summary>
        public void Update(WeaponObject weapon, MonsterObject owner)
        {
            if (!ReferenceEquals(_classified, weapon.Model))
                Classify(weapon, owner);

            if (_primary)
                Create(weapon, owner);

            _blur?.Move();
        }

        public void Draw(GraphicsDevice device)
        {
            if (_blur is not { Live: true })
                return;

            if (_batch == null)
            {
                _batch = new SourceQuadBatch(SourceBlur.MaxTails);
                _batch.Initialize(device);
                _ = SourceBlur.PreloadAsync();
            }

            _blur.Render(_batch, ownerLevel: 0);
        }

        public void Dispose() => _batch?.Dispose();

        private void Classify(WeaponObject weapon, MonsterObject owner)
        {
            _classified = weapon.Model;
            _primary = false;
            if (weapon.Model == null || owner is not (MegaCrust or AlphaCrust)) // OmegaWing derives from AlphaCrust
                return;

            // c->Weapon[0]: the first weapon attached to the monster.
            foreach (var child in owner.Children)
            {
                if (child is WeaponObject first)
                {
                    _primary = ReferenceEquals(first, weapon);
                    break;
                }
            }
        }

        private void Create(WeaponObject weapon, MonsterObject owner)
        {
            if (!Constants.ENABLE_WEAPON_TRAIL || owner.AnimationFramePosition < 3.0)
                return;

            int action = owner.CurrentAction;
            if (action < (int)MonsterActionType.Attack1 || action > (int)MonsterActionType.Attack2)
                return;

            Matrix[]? bones = owner.GetBoneTransforms();
            int link = weapon.ParentBoneLink;
            if (bones == null || link < 0 || link >= bones.Length)
                return;

            Matrix boneWorld = bones[link] * owner.WorldPosition;
            _blur ??= new SourceBlur();
            _blur.Create(Vector3.Transform(Pos1, boneWorld), Vector3.Transform(Pos2, boneWorld), Vector3.One, 6, isShort: false);
        }
    }
}
