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
    /// Shared cast of the Summoner curses / buffs on PLAYER_SKILL_SLEEP (Thorns 217, Berserker 218, Sleep 219, Blind 220,
    /// Weakness 221, Enervation 222), SourceMain5.2:
    /// hand    ZzzCharacter.cpp:10404 while CurrentAction is PLAYER_SKILL_SLEEP..PLAYER_SKILL_SLEEP_FENRIR, bone 37 (L Hand):
    ///         BITMAP_SHINY+5 sprites 1.0 / 0.7 at +-(WorldTime * 0.0006 * 360), BITMAP_PIN_LIGHT sprites 1.7 / 1.5 at rand%360,
    ///         rand_fps_check(1): BITMAP_LIGHT+2 sub 0 (1.0) + BITMAP_CLUD64 sub 3 (0.5); Blind: sprite sub 1 (minus),
    ///         particles sub 4 / 5 (minus). Colours include the original slips (Enervation shiny stays white, Weakness reuses
    ///         its shiny colour).
    /// light+2 ZzzEffectParticle.cpp:827 (LifeTime 16, Scale += (rand%32+48)*0.01*F, Rotation WorldTime%360), 4152 (Light +=
    ///         LifeTime/8*0.02*F, Gravity += 0.2F, Z += Gravity*F, Scale -= 0.1F), 9182 (sub 4 minus)
    /// clud64  ZzzEffectParticle.cpp:984 / 1010 (rand%2: BITMAP_SMOKE, LifeTime 50, Scale + (rand%10+10)*0.01, Rotation rand%360,
    ///         XY += (rand%20-10)F, Z -= 20F), 5153 (Scale += 0.08F, Light *= (1/1.1)^F, dead at Light[0] &lt;= 0.05), 9003 (sub 5 minus)
    /// release the action is outside PLAYER_ATTACK_FIST..PLAYER_RIDE_SKILL: AttackStage default (ZzzCharacter.cpp:2876), AttackTime
    ///         1 on the packet, MoveCharacter releases at (int)AttackTime &gt;= 15.
    /// The original reads the hand colours from the local hero's selected skill; here they follow the cast skill.
    /// </summary>
    internal abstract class AliceSkillEffectBase : SourceSkillEffectBase
    {
        public enum Skill { Sleep, Blind, Thorns, Berserker, Weakness, Enervation }

        protected const string ShinyTexturePath = "Effect/Shiny05.jpg";     // BITMAP_SHINY + 5 (second LoadBitmap wins)
        protected const string PinTexturePath = "Effect/pin_lights.jpg";    // BITMAP_PIN_LIGHT
        private const string CraTexturePath = "Effect/cra_04.jpg";          // BITMAP_LIGHT + 2
        private const string CloudTexturePath = "Effect/Clud64.jpg";        // BITMAP_CLUD64
        private const string SmokeTexturePath = "Effect/smoke01.jpg";       // BITMAP_SMOKE
        private const float LimitAttackTime = 15f;
        private const int MaxHandParticles = 96;

        private struct HandParticle
        {
            public bool Live;
            public byte Kind; // 0 light+2, 1 clud64, 2 clud64 with BITMAP_SMOKE
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float Rotation;
            public float Gravity;
            public float LifeTime;
        }

        private readonly HandParticle[] _handParticles = new HandParticle[MaxHandParticles];
        private readonly Vector3 _shinyLight, _pinLight, _particleLight;
        private Texture2D? _craTexture, _cloudTexture, _smokeTexture;
        private int _handParticleCount;
        private float _attackTime = 1f;
        private bool _released;
        private bool _casting = true;
        private bool _handVisible;
        private Vector3 _handPosition;

        protected AliceSkillEffectBase(WalkerObject caster, Skill skill, int maxQuads)
            : base(caster, 0f, 0f, maxQuads)
        {
            SkillKind = skill;
            Minus = skill == Skill.Blind;
            switch (skill)
            {
                case Skill.Sleep:
                    _shinyLight = new Vector3(0.5f, 0.2f, 0.8f);
                    _pinLight = new Vector3(0.7f, 0f, 0.8f);
                    _particleLight = new Vector3(0.6f, 0.1f, 0.8f);
                    break;
                case Skill.Blind:
                    _shinyLight = _pinLight = _particleLight = Vector3.One;
                    break;
                case Skill.Thorns:
                    _shinyLight = _pinLight = _particleLight = new Vector3(0.8f, 0.5f, 0.2f);
                    break;
                case Skill.Berserker:
                    _shinyLight = _pinLight = _particleLight = new Vector3(1f, 0.1f, 0.2f);
                    break;
                case Skill.Weakness:
                    _shinyLight = _pinLight = _particleLight = new Vector3(0.8f, 0.1f, 0.1f);
                    break;
                default:
                    _shinyLight = Vector3.One; // "Vector(0.25, 1, 0.7, Light)" writes the wrong variable
                    _pinLight = _particleLight = new Vector3(0.25f, 1f, 0.7f);
                    break;
            }
            SourceCastOwnership.Claim(caster, this);
        }

        protected Skill SkillKind { get; }

        /// <summary>Blind: sprites / particles / joints with EnableAlphaBlendMinus.</summary>
        protected bool Minus { get; }

        protected BlendState SpriteBlend => Minus ? Blendings.Negative : Blendings.OneOneAdditive;

        protected Texture2D? ShinyTexture { get; private set; }
        protected Texture2D? PinTexture { get; private set; }

        protected sealed override async Task LoadEffectContent()
        {
            ShinyTexture = await LoadTexture(ShinyTexturePath);
            PinTexture = await LoadTexture(PinTexturePath);
            _craTexture = await LoadTexture(CraTexturePath);
            _cloudTexture = await LoadTexture(CloudTexturePath);
            _smokeTexture = await LoadTexture(SmokeTexturePath);
            await LoadReleaseContent();
        }

        protected abstract Task LoadReleaseContent();

        /// <summary>MoveCharacter release switch.</summary>
        protected abstract void OnRelease();

        protected abstract void TickRelease(float factor);

        protected abstract bool HasReleaseContent();

        /// <summary>Decals, joints and models.</summary>
        protected abstract void DrawReleaseWorld();

        protected abstract void DrawReleaseSprites();

        protected abstract void DrawReleaseParticles();

        protected sealed override void OnStart()
        {
        }

        protected sealed override void OnTick(float factor)
        {
            Position = CasterPosition;
            _handVisible = false;

            if (_casting)
            {
                _casting = SourceCastOwnership.IsOwner(Caster, this) && Caster is PlayerObject player
                    && (int)player.CurrentAction >= (int)PlayerAction.PlayerSkillSleep
                    && (int)player.CurrentAction <= (int)PlayerAction.PlayerSkillSleepFenrir;
                if (_casting && !Caster.Hidden)
                {
                    _handVisible = true;
                    _handPosition = GetCasterBonePosition(37);
                    if (FPSCounter.Instance.RandFPSCheck(1))
                    {
                        SpawnHandLight(factor);
                        SpawnHandCloud(factor);
                    }
                }
            }

            MoveHandParticles(factor);

            if (!_released)
            {
                _attackTime += factor;
                if ((int)_attackTime >= LimitAttackTime)
                {
                    _released = true;
                    OnRelease();
                }
            }
            else
            {
                TickRelease(factor);
            }
        }

        private ref HandParticle NewHandParticle(out bool found)
        {
            for (int i = 0; i < _handParticles.Length; i++)
            {
                if (_handParticles[i].Live)
                    continue;
                found = true;
                _handParticleCount++;
                _handParticles[i].Live = true;
                return ref _handParticles[i];
            }
            found = false;
            return ref _handParticles[0];
        }

        private void SpawnHandLight(float factor)
        {
            ref HandParticle p = ref NewHandParticle(out bool found);
            if (!found)
                return;
            p.Kind = 0;
            p.Position = _handPosition;
            p.Light = _particleLight;
            p.LifeTime = 16f;
            p.Scale = 1f + (MuGame.Random.Next(32) + 48) * 0.01f * factor;
            p.Rotation = (int)FPSCounter.Instance.WorldTime % 360;
            p.Gravity = 0f;
        }

        private void SpawnHandCloud(float factor)
        {
            ref HandParticle p = ref NewHandParticle(out bool found);
            if (!found)
                return;
            p.Kind = (byte)(MuGame.Random.Next(2) != 0 ? 2 : 1);
            p.Light = _particleLight;
            p.LifeTime = 50f;
            p.Scale = 0.5f + (MuGame.Random.Next(10) + 10) * 0.01f;
            p.Rotation = MuGame.Random.Next(360);
            p.Position = _handPosition + new Vector3(
                (MuGame.Random.Next(20) - 10) * factor,
                (MuGame.Random.Next(20) - 10) * factor,
                -20f * factor);
            p.Gravity = 0f;
        }

        private void MoveHandParticles(float factor)
        {
            if (_handParticleCount == 0)
                return;
            float cloudFade = System.MathF.Pow(1f / 1.1f, factor);
            for (int i = 0; i < _handParticles.Length; i++)
            {
                ref HandParticle p = ref _handParticles[i];
                if (!p.Live)
                    continue;
                p.LifeTime -= factor;
                bool dead = p.LifeTime <= 0f;
                if (!dead && p.Kind == 0)
                {
                    p.Light += new Vector3(p.LifeTime / 8f * 0.02f * factor);
                    p.Gravity += 0.2f * factor;
                    p.Position.Z += p.Gravity * factor;
                    p.Scale -= 0.1f * factor;
                }
                else if (!dead)
                {
                    p.Scale += factor * 0.08f;
                    p.Light *= cloudFade;
                    dead = p.Light.X <= 0.05f;
                }
                if (dead)
                {
                    p.Live = false;
                    _handParticleCount--;
                }
            }
        }

        protected sealed override bool HasActiveContent() =>
            _casting || !_released || _handParticleCount > 0 || HasReleaseContent();

        protected sealed override void OnDraw()
        {
            if (_released)
                DrawReleaseWorld();

            if (_handVisible)
            {
                float rot = (float)(FPSCounter.Instance.WorldTime * 0.0006 * 360.0 % 360.0);
                if (Batch.Begin(ShinyTexture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                {
                    Batch.AddSprite(_handPosition, 1f, _shinyLight, rot);
                    Batch.AddSprite(_handPosition, 0.7f, _shinyLight, -rot);
                    Batch.End();
                }
                if (Batch.Begin(PinTexture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                {
                    Batch.AddSprite(_handPosition, 1.7f, _pinLight, MuGame.Random.Next(360));
                    Batch.AddSprite(_handPosition, 1.5f, _pinLight, MuGame.Random.Next(360));
                    Batch.End();
                }
            }

            if (_released)
                DrawReleaseSprites();

            if (_handParticleCount > 0)
            {
                DrawHandParticles(_craTexture, 0);
                DrawHandParticles(_cloudTexture, 1);
                DrawHandParticles(_smokeTexture, 2);
            }

            if (_released)
                DrawReleaseParticles();
        }

        private void DrawHandParticles(Texture2D? texture, byte kind)
        {
            if (!Batch.Begin(texture, SpriteBlend, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _handParticles.Length; i++)
            {
                ref HandParticle p = ref _handParticles[i];
                if (p.Live && p.Kind == kind)
                    Batch.AddSprite(p.Position, p.Scale, p.Light, p.Rotation);
            }
            Batch.End();
        }

        protected override void DisposeEffect()
        {
            SourceCastOwnership.Release(Caster, this);
        }
    }
}
