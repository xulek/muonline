#nullable enable
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Summoner
{
    /// <summary>
    /// Chain Lightning cast (AT_SKILL_ALICE_CHAINLIGHTNING = 215) on the 0x1E packet, SourceMain5.2:
    /// hands   ZzzCharacter.cpp:10545 while CurrentAction == PLAYER_SKILL_CHAIN_LIGHTNING (not the mount variants):
    ///         bones 37 / 28: rand_fps_check(1) -> 2 x MODEL_FENRIR_THUNDER sub 2 (0.4, 0.4, 0.8), BITMAP_LIGHT sprite 1.5
    /// release ReceiveMagicContinue AttackTime = 1 (WSclient.cpp:5250); the action is outside
    ///         PLAYER_ATTACK_FIST..PLAYER_RIDE_SKILL, so AttackStage default (ZzzCharacter.cpp:2876) releases after 14 ticks
    ///         and MoveCharacter (ZzzCharacter.cpp:5080) plays SOUND_SKILL_CHAIN_LIGHTNING.
    /// The bolts are <see cref="ChainLightningEffect"/> (0xBF 0x0A).
    /// </summary>
    internal sealed class ChainLightningCastEffect : SourceSkillEffectBase
    {
        private const string SoundPath = "Sound/SE_Ch_summoner_skill08_chainlightning.wav";
        private const float LimitAttackTime = 15f;
        private static readonly Vector3 HandLight = new(0.4f, 0.4f, 0.8f);

        private readonly SourceFenrirThunderPool _thunders = SourceFenrirThunderPool.Shared;
        private SourceModelRenderer? _thunderModel;
        private Texture2D? _lightTexture;
        private float _attackTime = 1f;
        private bool _released;
        private bool _casting = true;
        private int _handSprites;
        private readonly Vector3[] _handSpritePositions = new Vector3[2];

        public ChainLightningCastEffect(WalkerObject caster)
            : base(caster, 0f, 0f, 64)
        {
            SourceCastOwnership.Claim(caster, this);
        }

        protected override async Task LoadEffectContent()
        {
            _lightTexture = await LoadTexture(SourceFenrirThunderPool.LightTexturePath);
            _thunderModel = await LoadModel(SourceFenrirThunderPool.ModelPath, -1);
            if (_thunderModel != null)
            {
                _thunderModel.UseNormalBlend();
                _thunderModel.AnimationSpeed = 0f;
            }
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
            _thunders.Move(this, factor);
            _handSprites = 0;

            if (_casting)
            {
                _casting = SourceCastOwnership.IsOwner(Caster, this) && Caster is PlayerObject player
                    && player.CurrentAction == PlayerAction.PlayerSkillChainLightning;
                if (_casting)
                {
                    EmitHand(GetCasterBonePosition(37), factor);
                    EmitHand(GetCasterBonePosition(28), factor);
                }
            }

            if (!_released)
            {
                _attackTime += factor;
                if ((int)_attackTime >= LimitAttackTime)
                {
                    _released = true;
                    SoundController.Instance.PlayBuffer(SoundPath);
                }
            }
        }

        private void EmitHand(Vector3 hand, float factor)
        {
            if (FPSCounter.Instance.RandFPSCheck(1))
            {
                _thunders.Spawn(this, 2, hand, HandLight, factor);
                _thunders.Spawn(this, 2, hand, HandLight, factor);
            }
            _handSpritePositions[_handSprites++] = hand;
        }

        protected override bool HasActiveContent() => _casting || !_released || _thunders.HasLive(this);

        protected override void OnDraw()
        {
            _thunders.DrawModels(this, _thunderModel, World?.Terrain);

            if (!Batch.Begin(_lightTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _handSprites; i++)
                Batch.AddSprite(_handSpritePositions[i], 1.5f, HandLight, 0f);
            Batch.End();
            _thunders.DrawSprites(this, Batch, _lightTexture);
        }

        protected override void DisposeEffect()
        {
            SourceCastOwnership.Release(Caster, this);
            _thunders.Release(this);
        }
    }
}
