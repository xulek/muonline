#nullable enable
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Increase Critical Damage (AT_SKILL_ADD_CRITICAL = 64), SourceMain5.2:
    /// packet   WSclient.cpp:4417       PLAYER_SKILL_HAND1 (Dark Horse: PLAYER_ATTACK_RIDE_ATTACK_MAGIC, Fenrir: PLAYER_FENRIR_ATTACK_MAGIC)
    /// gate     ZzzCharacter.cpp:2877   no case: default, AnimationFrame &gt;= 5
    /// release  ZzzCharacter.cpp:4200   MODEL_DARKLORD_SKILL sub 0 on Weapon[0].LinkBone (not arrows), sub 1 on
    ///          Weapon[1].LinkBone (not bolt / shield), Light (1,0.6,0.3), SOUND_CRITICAL
    /// model    ZzzEffect.cpp:3551 (LifeTime 10, Scale 0.2, Velocity 0.1, Angle (45, +-45, 0)), 13236 (break),
    ///          ZzzObject.cpp:954 (BodyLight = Light, RENDER_TEXTURE, BlendMesh -1)
    /// </summary>
    internal sealed class DarkLordCriticalDamageEffect : SourceSkillEffectBase
    {
        private const string ModelPath = "Skill/DarkLordSkill.bmd";  // MODEL_DARKLORD_SKILL
        private const string SoundPath = "Sound/sDarkCritical.wav";   // SOUND_CRITICAL
        private static readonly Vector3 FlashLight = new(1f, 0.6f, 0.3f);

        private readonly Vector3[] _positions = new Vector3[2];
        private readonly bool[] _active = new bool[2];
        private SourceModelRenderer? _renderer;
        private float _life = 10f;
        private float _frame;

        public DarkLordCriticalDamageEffect(WalkerObject caster)
            : base(caster, triggerFrame: 5f, triggerTimeoutSeconds: 14f * 0.04f, maxQuads: 1)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _renderer = await LoadModel(ModelPath, -1);
            if (_renderer != null)
            {
                _renderer.AnimationSpeed = 0f;
                _renderer.UseNormalBlend();
            }
        }

        protected override void OnStart()
        {
            if (Caster is PlayerObject player)
            {
                WeaponObject? right = player.Weapon1;
                WeaponObject? left = player.Weapon2;
                bool arrows = right?.Model != null && right.MaterialItemGroup == 4 && right.MaterialItemIndex == 15;
                bool boltOrShield = left?.Model != null &&
                    (left.MaterialItemGroup == 6 || (left.MaterialItemGroup == 4 && left.MaterialItemIndex == 7));
                _active[0] = !arrows && TryLinkBone(player, right, PlayerObject.RightHandBoneIndex, out _positions[0]);
                _active[1] = !boltOrShield && TryLinkBone(player, left, PlayerObject.LeftHandBoneIndex, out _positions[1]);
            }
            else
            {
                _active[0] = true;
                _positions[0] = CasterPosition;
            }

            SoundController.Instance.PlayBuffer(SoundPath);
        }

        private static bool TryLinkBone(PlayerObject player, WeaponObject? weapon, int defaultBone, out Vector3 position)
        {
            position = default;
            int bone = weapon != null && weapon.ParentBoneLink >= 0 ? weapon.ParentBoneLink : defaultBone;
            if (!player.TryGetBoneWorldMatrix(bone, out Matrix matrix))
                return false;
            position = matrix.Translation;
            return true;
        }

        protected override void OnTick(float factor)
        {
            _frame += 0.1f * factor;
            _life -= factor;

            // Weapon flash (LifeTime 10): quick rise, fades out with the model.
            float glow = 1.1f * EffectLights.Envelope(10f - _life, 10f, 1f, 6f);
            for (int i = 0; i < 2; i++)
                Lights.Set(i, _positions[i], FlashLight, 180f, _active[i] ? glow : 0f);
        }

        protected override bool HasActiveContent() => _life > 0f;

        protected override void OnDraw()
        {
            if (_renderer == null)
                return;

            _renderer.SetAnimationTime(_frame);
            _renderer.Update(LastGameTime);
            for (int i = 0; i < 2; i++)
            {
                if (!_active[i])
                    continue;
                var angle = new Vector3(MathHelper.ToRadians(45f), MathHelper.ToRadians(45f - 90f * i), 0f);
                _renderer.DrawInstance(_positions[i], angle, 0.2f, FlashLight, 1f);
            }
        }

        protected override void DisposeEffect()
        {
            _renderer?.Dispose();
            _renderer = null;
        }
    }
}
