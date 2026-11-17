#nullable enable
using System.Threading.Tasks;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Explosion (AT_SKILL_EXPLODE = 79), SourceMain5.2 ReceiveMagic WSclient.cpp:4530: no action, no sound;
    /// 3 x CreateBomb(target + (rand%160, rand%160, 0), true) (ZzzEffect.cpp:6393, see SourceBombPool).
    /// </summary>
    internal sealed class DarkLordExplosionEffect : SourceSkillEffectBase
    {
        private readonly Vector3 _target;
        private readonly SourceBombPool _bombs = new(3);
        private Texture2D? _sparkTexture;
        private Texture2D? _explosionTexture;
        private static readonly Vector3 BlastColor = new(1f, 0.5f, 0.18f);
        private Vector3 _blastCenter;
        private float _age;

        public DarkLordExplosionEffect(WalkerObject caster, Vector3 target)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 3 * 20 + 3)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _sparkTexture = await LoadTexture(SourceSparkPool.ParticleTexturePath);
            _explosionTexture = await LoadTexture(SourceBombPool.ExplosionTexturePath);
        }

        protected override void OnStart()
        {
            _blastCenter = new Vector3(_target.X + 80f, _target.Y + 80f, _target.Z + 80f);
            for (int i = 0; i < 3; i++)
            {
                float addX = MuGame.Random.Next(160);
                float addY = MuGame.Random.Next(160);
                _bombs.Spawn(new Vector3(_target.X + addX, _target.Y + addY, _target.Z));
            }
        }

        protected override void OnTick(float factor)
        {
            _bombs.Move(factor, World?.Terrain);

            // Explosion flash over the three bombs (BITMAP_EXPLOTION lives 20 frames).
            _age += factor;
            float flash = EffectLights.Flash(_age, 20f, 1.4f) * EffectLights.Flicker(_age * 0.04f, 3.1f, 0.2f);
            Lights.Set(0, _blastCenter, BlastColor, 420f - 120f * MathHelper.Clamp(_age / 20f, 0f, 1f), 2.2f * flash);
        }

        protected override bool HasActiveContent() => _bombs.HasLive;

        protected override void OnDraw() => _bombs.Draw(Batch, _sparkTexture, _explosionTexture);
    }
}
