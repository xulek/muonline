#nullable enable
using System.Threading.Tasks;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Lightning (AT_SKILL_THUNDER = 3), SourceMain5.2:
    /// packet  WSclient.cpp:3917       SOUND_THUNDER01 (packet handler), SetPlayerMagic, AttackTime = 1
    /// cast    ZzzCharacter.cpp:2226   AttackEffect default: every frame until release, rand_fps_check(1):
    ///         2 x BITMAP_JOINT_THUNDER sub 0 (50 / 10) from weapon bone 33 to the target, BITMAP_ENERGY particle
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame &gt;= 5 ends the cast (else 14 ticks)
    /// thunder <see cref="SourceHomingThunderPool"/> (init 1053, move 4688 incl. sub 28 / energy / smoke children, render 6921)
    /// </summary>
    internal sealed class LightningEffect : SourceSkillEffectBase
    {
        private const int WeaponBone = 33;
        private const float LimitAttackTime = 15f;
        private const float ReleaseFrame = 5f;

        private readonly WalkerObject? _target;
        private readonly SourceHomingThunderPool _pool = SourceHomingThunderPool.Shared;

        private Texture2D? _thunderTexture;
        private Texture2D? _energyTexture;
        private Texture2D? _smokeTexture;

        private float _attackTime = 1f;
        private bool _casting;

        public LightningEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, 0f, 0f, 2048)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _thunderTexture = await LoadTexture(SourceHomingThunderPool.ThunderTexturePath);
            _energyTexture = await LoadTexture(SourceHomingThunderPool.EnergyTexturePath);
            _smokeTexture = await LoadTexture(SourceHomingThunderPool.SmokeTexturePath);
        }

        protected override void OnStart()
        {
            _casting = IsAlive(_target);
        }

        protected override void OnTick(float factor)
        {
            if (_casting)
            {
                // AttackStage first: AnimationFrame >= 5 forces AttackTime = 15, AttackEffect still runs this frame.
                bool release = Caster.CurrentFrame >= ReleaseFrame;

                if (FpsCheck(1, factor) && IsAlive(_target))
                {
                    Vector3 hand = GetCasterBonePosition(WeaponBone);
                    var angle = new Vector3(-60f, 0f, CasterYawDegrees);
                    Vector3 targetPosition = _target!.WorldPosition.Translation + new Vector3(0f, 0f, 80f);
                    _pool.SpawnThunder(this, 0, hand, angle, Vector3.One, 50f, _target, targetPosition);
                    _pool.SpawnThunder(this, 0, hand, angle, Vector3.One, 10f, _target, targetPosition);
                    _pool.SpawnEnergy(this, hand, Vector3.One);
                }

                _attackTime = release ? LimitAttackTime : _attackTime + factor;
                if ((int)_attackTime >= LimitAttackTime)
                    _casting = false;
            }

            _pool.Move(this, factor);
        }

        private static bool IsAlive(WalkerObject? walker)
            => walker != null && walker.Status != GameControlStatus.Disposed && walker.World != null;

        protected override bool HasActiveContent() => _casting || _pool.HasLive(this);

        protected override void OnDraw() => _pool.Draw(this, Batch, _thunderTexture, _smokeTexture, _energyTexture);

        protected override void DisposeEffect() => _pool.Release(this);
    }
}
