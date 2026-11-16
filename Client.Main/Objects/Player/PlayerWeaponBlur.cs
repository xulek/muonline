#nullable enable
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Player
{
    /// <summary>
    /// SourceMain5.2 CreateWeaponBlur (ZzzCharacter.cpp:3593) for MODEL_PLAYER, feeding the owner's
    /// single BLUR record (<see cref="SourceBlur"/>). Called after the player's animation step.
    /// </summary>
    internal sealed class PlayerWeaponBlur
    {
        private const int Interpolations = 10;   // inter = 10.f
        private const int RightHandBone = 33;    // c->Weapon[0].LinkBone
        private const int LeftHandBone = 42;     // c->Weapon[1].LinkBone

        // Item groups / indices of the MODEL_* weapons tested by CreateWeaponBlur.
        private const int GroupSword = 0, GroupAxe = 1, GroupMace = 2, GroupSpear = 3;

        private readonly PlayerObject _owner;
        private readonly SourceBlur _blur = new();
        private readonly SourceQuadBatch _batch = new(SourceBlur.MaxTails);

        private int _lastAction = -1;
        private double _lastFramePos;

        public PlayerWeaponBlur(PlayerObject owner)
        {
            _owner = owner;
        }

        public void Initialize(GraphicsDevice device)
        {
            if (_batch.IsReady)
                return;

            _batch.Initialize(device);
            _ = SourceBlur.PreloadAsync();
        }

        /// <summary>CreateWeaponBlur followed by MoveBlurs (both once per frame).</summary>
        public void Update()
        {
            int action = (int)_owner.CurrentAction;
            double framePos = _owner.AnimationFramePosition;
            double startPos = action == _lastAction && framePos >= _lastFramePos ? _lastFramePos : framePos;
            _lastAction = action;
            _lastFramePos = framePos;

            if (Constants.ENABLE_WEAPON_TRAIL)
                CreateWeaponBlur((PlayerAction)action, framePos, startPos);

            _blur.Move();
        }

        public void Draw()
        {
            if (!_blur.Live || !_batch.IsReady)
                return;

            // CHARACTER::Level is 1 only for murderers; the client does not track PK state here.
            _blur.Render(_batch, ownerLevel: 0);
        }

        public void Clear() => _blur.Clear();

        public void Dispose() => _batch.Dispose();

        private void CreateWeaponBlur(PlayerAction action, double framePos, double startPos)
        {
            if (!(framePos >= 3.0 || action == PlayerAction.PlayerAttackTwoHandSwordTwo))
                return;

            bool leftHand = action == PlayerAction.PlayerAttackSwordLeft1 || action == PlayerAction.PlayerAttackSwordLeft2;
            WeaponObject weapon = leftHand ? _owner.Weapon2 : _owner.Weapon1;
            bool hasWeapon = weapon?.Model != null;
            int group = hasWeapon ? weapon!.MaterialItemGroup : -1;
            int index = hasWeapon ? weapon!.MaterialItemIndex : -1;
            int level = hasWeapon ? weapon!.ItemLevel : 0;

            bool Is(int g, int i) => group == g && index == i;

            int blurType = 0;
            int blurMapping = 0;

            if (action == PlayerAction.PlayerAttackOneFlash || action == PlayerAction.PlayerAttackRush)
            {
                blurType = 1;
                blurMapping = 2;
            }
            else if (action == PlayerAction.PlayerAttackSkillSword2 || action == PlayerAction.PlayerAttackSkillSword3
                || action == PlayerAction.PlayerAttackSkillSword4)
            {
                blurType = 1;
                // MODEL_LIGHTING_SWORD, MODEL_DARK_REIGN_BLADE, MODEL_RUNE_BLADE
                blurMapping = Is(GroupSword, 14) || Is(GroupSword, 21) || Is(GroupSword, 31) ? 1 : 2;
            }
            else if (action == PlayerAction.PlayerAttackStrike)
            {
                blurType = 1;
                blurMapping = 2;
            }
            else if (action == PlayerAction.PlayerSkillLightningOrb || action == PlayerAction.PlayerSkillLightningOrbUni
                || action == PlayerAction.PlayerSkillLightningOrbDino || action == PlayerAction.PlayerSkillLightningOrbFenrir)
            {
                blurType = 1;
                blurMapping = 1;
            }
            else if (action == PlayerAction.PlayerSkillBlowOfDestruction && framePos >= 2.0 && framePos <= 8.0)
            {
                blurType = 1;
                blurMapping = 2;
            }
            else if (action == PlayerAction.PlayerAttackSkillSword5)
            {
                blurType = 1;
                blurMapping = Is(GroupMace, 5) ? 1 : 2; // MODEL_CRYSTAL_SWORD
            }
            else if (group == GroupSword)
            {
                if ((action >= PlayerAction.PlayerAttackSwordRight1 && action <= PlayerAction.PlayerAttackTwoHandSword3)
                    || action == PlayerAction.PlayerAttackTwoHandSwordTwo)
                {
                    blurType = 1;
                    if (Is(GroupSword, 17)) // MODEL_DARK_BREAKER
                        blurMapping = 6;
                    else if (action == PlayerAction.PlayerAttackTwoHandSword3 || action == PlayerAction.PlayerAttackTwoHandSwordTwo)
                        blurMapping = Is(GroupSword, 25) ? 2 : 1; // MODEL_SWORD_DANCER
                }
            }
            else if (Is(GroupAxe, 3) || (group == GroupAxe && index >= 5) || group == GroupMace)
            {
                // MODEL_TOMAHAWK || MODEL_BATTLE_AXE <= Type < MODEL_MACE + MAX_ITEM_INDEX
                if (action >= PlayerAction.PlayerAttackSkillSword1 && action <= PlayerAction.PlayerAttackSkillSword5)
                {
                    blurType = 1;
                    blurMapping = 2;
                }
            }
            else if (group == GroupSpear)
            {
                if (action >= PlayerAction.PlayerAttackSpear1 && action <= PlayerAction.PlayerAttackScythe3)
                {
                    blurType = 3;
                    if (Is(GroupSpear, 10)) // MODEL_DRAGON_SPEAR
                    {
                        blurType = 1;
                        blurMapping = 0;
                    }
                    else if (action == PlayerAction.PlayerAttackScythe3)
                    {
                        blurMapping = 1;
                    }
                }
            }

            if (blurType <= 0)
                return;

            Vector3 pos1 = blurType switch
            {
                1 => new Vector3(0f, -20f, 0f),
                2 => new Vector3(0f, -80f, 0f),
                3 => new Vector3(0f, -100f, 0f),
                _ => Vector3.Zero
            };
            Vector3 pos2 = new(0f, -120f, 0f);

            Vector3 light;
            if (Is(GroupSword, 13) || Is(GroupMace, 6) || Is(GroupSpear, 9)) // Double Blade, Chaos Dragon Axe, Bill of Balrog
                light = new Vector3(1f, 0.2f, 0.2f);
            else if (blurMapping == 0)
            {
                if (level >= 7) light = new Vector3(1f, 0.6f, 0.2f);
                else if (level >= 5) light = new Vector3(0.2f, 0.4f, 1f);
                else if (level >= 3) light = new Vector3(1f, 0.2f, 0.2f);
                else light = new Vector3(0.8f, 0.8f, 0.8f);
            }
            else
            {
                light = Vector3.One;
            }

            int bone = leftHand ? LeftHandBone : RightHandBone;
            Matrix world = _owner.WorldPosition;

            // MODEL_KATACHE, MODEL_GLADIUS, MODEL_SWORD_OF_SALAMANDER, MODEL_LEGENDARY_SWORD, MODEL_SERPENT_SPEAR:
            // one sample of the current pose, long blur.
            if (Is(GroupSword, 3) || Is(GroupSword, 6) || Is(GroupSword, 9) || Is(GroupSword, 11) || Is(GroupSpear, 4))
            {
                if (_owner.TryGetBoneWorldMatrix(bone, out Matrix boneWorld))
                    _blur.Create(Vector3.Transform(pos1, boneWorld), Vector3.Transform(pos2, boneWorld), light, blurMapping, isShort: false);
                return;
            }

            // Rage Fighter sword-form gloves: g_CMonkSystem.MoveBlurEffect (MonkSystem.cpp:264).
            if (group == GroupSword && index >= 32 && index <= 35)
            {
                if (startPos > 5.5)
                    return;

                double gloveStep = (framePos - startPos) / Interpolations;
                for (int i = 0; i < Interpolations; i++)
                {
                    if (!_owner.TryComputeBoneAt((int)action, startPos + gloveStep * i, bone, out Matrix gloveBone))
                        return;

                    Matrix gloveWorld = gloveBone * world;
                    _blur.Create(
                        Vector3.Transform(new Vector3(-30f, 0f, 0f), gloveWorld),
                        Vector3.Transform(new Vector3(30f, 0f, 0f), gloveWorld),
                        Vector3.One, 5, isShort: true);
                }
                return;
            }

            if (!hasWeapon && _owner.Weapon2?.Model == null && _owner.Weapon1?.Model == null)
                return;

            // Re-animate the hand bone between the previous and current frame in 10 steps.
            int actionIndex = (int)action;
            double step = (framePos - startPos) / Interpolations;
            for (int i = 0; i < Interpolations; i++)
            {
                if (!_owner.TryComputeBoneAt(actionIndex, startPos + step * i, bone, out Matrix boneModel))
                    return;

                Matrix boneWorld = boneModel * world;
                _blur.Create(Vector3.Transform(pos1, boneWorld), Vector3.Transform(pos2, boneWorld), light, blurMapping, isShort: true);
            }
        }
    }
}
