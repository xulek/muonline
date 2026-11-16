#nullable enable
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Aqua Beam (AT_SKILL_FLASH = 12), SourceMain5.2:
    /// packet  WSclient.cpp:4856       PLAYER_SKILL_FLASH
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4399   CalcAddPosition(o, -20, -90, 100), CreateEffect(BITMAP_BOSS_LASER, sub 0), SOUND_FLASH
    /// laser   ZzzEffect.cpp:1001 (init: LifeTime 20, Light (0.5,0.7,1), Scale 16, Direction rot(0,-50,0)), not moved,
    ///         18334 (render: 20 x BITMAP_SPARK+1 sprites along Direction)
    /// </summary>
    internal sealed class AquaBeamEffect : SourceSkillEffectBase
    {
        private const string SparkTexturePath = "Effect/Spark03.jpg"; // BITMAP_SPARK + 1
        private const string SoundPath = "Sound/sAquaFlash.wav";     // SOUND_FLASH

        private const int SkillId = 12;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private static readonly Vector3 BeamLight = new(0.5f, 0.7f, 1f);

        private Texture2D? _sparkTexture;
        private Vector3 _position;
        private Vector3 _direction;
        private float _lifeTime;

        public AquaBeamEffect(WalkerObject caster)
            : base(caster, 5f, ReleaseDelaySeconds, 32)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _sparkTexture = await LoadTexture(SparkTexturePath);
        }

        protected override void OnStart()
        {
            var angle = new Vector3(0f, 0f, CasterYawDegrees);
            _position = CasterPosition + SourceJointMath.Rotate(new Vector3(-20f, -90f, 100f), angle);
            _direction = SourceJointMath.Rotate(new Vector3(0f, -50f, 0f), angle);
            _lifeTime = 20f;
            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            _lifeTime -= factor;
        }

        protected override bool HasActiveContent() => _lifeTime > 0f;

        protected override void OnDraw()
        {
            if (_lifeTime <= 0f || !Batch.Begin(_sparkTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            Vector3 position = _position;
            for (int j = 0; j < 20; j++)
            {
                Batch.AddSprite(position, 16f, BeamLight);
                position += _direction;
            }
            Batch.End();
        }
    }
}
