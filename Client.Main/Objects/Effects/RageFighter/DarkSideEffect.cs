#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.RageFighter
{
    /// <summary>
    /// Dark Side (AT_SKILL_DARKSIDE = 263), SourceMain5.2:
    /// packet   ZzzInterface.cpp:2773 (hero, at cast) / WSclient.cpp:13041 (remote), PLAYER_SKILL_DARKSIDE_READY
    /// create   MonkSystem.cpp:1254     BITMAP_EVENT_CLOUD sub 1, BITMAP_MAGIC sub 14, MODEL_WINDFOCE sub 2,
    ///          SOUND_RAGESKILL_DARKSIDE (packet sound map)
    /// cloud    ZzzEffect.cpp:6114, 17215, 18910      magic  ZzzEffect.cpp:1216, 8179, 19148
    /// wind     ZzzEffect.cpp:6125, 17291 (sub 3 at LifeTime 60 / 50), ZzzObject.cpp:2527 (BodyLight = Light * 6)
    /// strike   MonkSystem.cpp:684 DarksideRendering: MODEL_SHOCKWAVE01 sub 3 (ZzzEffect.cpp:6015, 17129, 17153,
    ///          ZzzObject.cpp:2493) once the dashing ghost is 30 units out, BITMAP_DAMAGE2 particle at the target
    ///          (ZzzEffectParticle.cpp:3857, 8919, 9288), SOUND_RAGESKILL_DARKSIDE_ATTACK with the target list
    /// Not ported: the ghost body afterimages and dummies of DarksideRendering (per body-part render calls, global
    /// hero state), the 0x4B target list (up to 5 targets; only the packet target is struck), SHOCKWAVE01 sub 4 of
    /// the dummies. Without the ghost the strike is released at the first SendDarksideAtt
    /// ((int)(2.5 / PlaySpeed) attack ticks, ZzzInterface.cpp:2999) at the first ghost distance above 30 (25^2 * 0.05).
    /// </summary>
    internal sealed class DarkSideEffect : SourceSkillEffectBase
    {
        private const string WindPath = "Effect/wind_foce.bmd";               // MODEL_WINDFOCE
        private const string ShockWavePath = "Effect/shockwave01.bmd";        // MODEL_SHOCKWAVE01
        private const string CloudTexturePath = "Effect/clouds2.jpg";         // BITMAP_EVENT_CLOUD
        private const string MagicTexturePath = "Effect/Magic_Ground1.jpg";   // BITMAP_MAGIC
        private const string Damage2TexturePath = "Effect/Damage2.jpg";       // BITMAP_DAMAGE2
        private const string AttackSoundPath = "Sound/Ragefighter/Rage_Darkside_Att.wav"; // SOUND_RAGESKILL_DARKSIDE_ATTACK
        private const float GhostDistance = 25f * 25f * 0.05f;

        private struct Wind
        {
            public bool Live;
            public int SubType;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
            public int LastMark;
        }

        private struct Damage
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Light;
            public float Scale;
            public float LifeTime;
        }

        private readonly ushort _targetId;
        private readonly Wind[] _winds = new Wind[3];
        private readonly Damage[] _damages = new Damage[2];
        private WalkerObject? _target;
        private SourceModelRenderer? _windRenderer;
        private SourceModelRenderer? _shockRenderer;
        private Texture2D? _cloudTexture;
        private Texture2D? _magicTexture;
        private Texture2D? _damageTexture;

        private Vector3 _origin;
        private Vector3 _ownerAngle;       // degrees, at creation
        private float _cloudLife = 30f;
        private float _cloudScale = 1f;
        private Vector3 _cloudLight = new(1f, 0.6f, 0.6f);
        private float _magicLife = 30f;
        private float _magicScale = 1f;
        private Vector3 _magicLight = new(1f, 0.4f, 0f);
        private float _playSpeed = 0.3f;
        private float _attackTime = 1f;
        private bool _struck;

        private bool _shockLive;
        private int _shockSubType;
        private Vector3 _shockPosition;
        private Vector3 _shockAngle;
        private Vector3 _shockStart;
        private Vector3 _shockLight;
        private float _shockScale;
        private float _shockLife;

        public DarkSideEffect(WalkerObject caster, ushort targetId)
            : base(caster, triggerFrame: 0f, triggerTimeoutSeconds: 0f, maxQuads: 256)
        {
            _targetId = (ushort)(targetId & 0x7FFF);
        }

        protected override async Task LoadEffectContent()
        {
            _windRenderer = await LoadModel(WindPath);
            _shockRenderer = await LoadModel(ShockWavePath);
            if (_windRenderer != null)
                _windRenderer.AnimationSpeed = 0f;
            if (_shockRenderer != null)
                _shockRenderer.AnimationSpeed = 0f;
            _cloudTexture = await LoadTexture(CloudTexturePath);
            _magicTexture = await LoadTexture(MagicTexturePath);
            _damageTexture = await LoadTexture(Damage2TexturePath);
            _shockRenderer?.OverrideAllTextures(_damageTexture);
            await SoundController.Instance.PreloadSoundAsync(AttackSoundPath);
        }

        protected override void OnStart()
        {
            if (_targetId != 0 && (Caster.NetworkId & 0x7FFF) != _targetId &&
                World is WalkableWorldControl walkable && walkable.TryGetWalkerById(_targetId, out WalkerObject target))
            {
                _target = target;
            }

            _origin = CasterPosition;
            _ownerAngle = new Vector3(
                MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));

            if (Caster is ModelObject model && model.Model?.Actions is { } actions &&
                (int)PlayerAction.PlayerSkillDarksideReady < actions.Length && actions[(int)PlayerAction.PlayerSkillDarksideReady]?.PlaySpeed > 0f)
            {
                _playSpeed = actions[(int)PlayerAction.PlayerSkillDarksideReady].PlaySpeed;
            }

            _winds[0] = new Wind { Live = true, SubType = 2, Light = new Vector3(1f, 0.5f, 0.2f), Scale = 1f, LifeTime = 70f, LastMark = -1 };

            if (TargetAlive)
                SoundController.Instance.PlayBuffer(AttackSoundPath);
            else
                _struck = true;
        }

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        protected override void OnTick(float factor)
        {
            if (_cloudLife > 0f)
            {
                _cloudScale = MathF.Min(5f, _cloudScale * MathF.Pow(1.08f, factor));
                _cloudLight *= 0.9f; // per frame
                _cloudLife -= factor;
            }

            if (_magicLife > 0f)
            {
                _magicScale *= MathF.Pow(1.1f, factor);
                _magicLight *= 0.95f; // per frame
                if (_magicScale > 4f)
                    _magicLight *= 0.5f; // per frame
                _magicLife -= factor;
                if (_magicScale > 8f)
                    _magicLife = 0f;
            }

            TickWinds(factor);
            TickStrike(factor);
            TickShock(factor);

            for (int i = 0; i < _damages.Length; i++)
            {
                ref Damage d = ref _damages[i];
                if (!d.Live)
                    continue;
                d.LifeTime -= factor;
                if (d.LifeTime <= 0f)
                {
                    d.Live = false;
                    continue;
                }
                d.Scale *= MathF.Pow(1.2f, factor);
                d.Light *= 0.55f; // per frame
            }
        }

        private void TickWinds(float factor)
        {
            for (int i = 0; i < _winds.Length; i++)
            {
                ref Wind wind = ref _winds[i];
                if (!wind.Live)
                    continue;

                wind.Scale = MathF.Min(3f, wind.Scale + 0.1f * factor);
                wind.Light *= 0.85f; // per frame

                if (wind.SubType == 2)
                {
                    int mark = (int)wind.LifeTime;
                    if (mark != wind.LastMark && (mark == 60 || mark == 50))
                        SpawnWind();
                    wind.LastMark = mark;
                }

                wind.LifeTime -= factor;
                if (wind.LifeTime <= 0f)
                    wind.Live = false;
            }
        }

        private void SpawnWind()
        {
            for (int i = 0; i < _winds.Length; i++)
            {
                if (_winds[i].Live)
                    continue;
                _winds[i] = new Wind { Live = true, SubType = 3, Light = new Vector3(1f, 0.5f, 0f), Scale = 1f, LifeTime = 70f, LastMark = -1 };
                return;
            }
        }

        private void TickStrike(float factor)
        {
            if (_struck)
                return;

            _attackTime += factor;
            if (_attackTime < (int)(2.5f / _playSpeed))
                return;

            _struck = true;
            if (!TargetAlive)
                return;

            Vector3 owner = CasterPosition;
            Vector3 target = _target!.WorldPosition.Translation;
            Vector3 direction = target - owner;
            if (direction.LengthSquared() > 0f)
                direction.Normalize();
            Vector3 position = owner + direction * GhostDistance;
            float yaw = SourceJointMath.CreateAngle2D(owner, target);

            // MODEL_SHOCKWAVE01 sub 3
            _shockLive = true;
            _shockSubType = 3;
            _shockAngle = new Vector3(90f, 0f, yaw - 180f);
            _shockLight = Vector3.One;
            _shockScale = 0.8f;
            _shockLife = 5f / (_playSpeed * 2f);
            var start = new Vector3(position.X - owner.X, position.Y - owner.Y, TerrainHeight(position) - TerrainHeight(owner));
            if (start.LengthSquared() > 0f)
                start.Normalize();
            _shockStart = start * 30f;
            _shockPosition = position + _shockStart * 5f * factor;

            // BITMAP_DAMAGE2 particle
            for (int i = 0; i < _damages.Length; i++)
            {
                ref Damage d = ref _damages[i];
                if (d.Live)
                    continue;
                d.Live = true;
                d.Position = target;
                d.Position.Z += (80f + MuGame.Random.Next(20)) * factor;
                d.Light = Vector3.One;
                d.Scale = 0.9f + (MuGame.Random.Next(2) + 2) * 0.1f;
                d.LifeTime = 15f;
                break;
            }
        }

        private void TickShock(float factor)
        {
            if (!_shockLive)
                return;

            _shockScale *= MathF.Pow(1.15f, factor);
            if (_shockSubType == 3)
            {
                _shockStart *= 0.99f;  // per frame
                _shockPosition -= _shockStart;
                _shockLight *= 0.7f;   // per frame
                if (_shockLight.X > 0.6f)
                    _shockSubType = 5;
                _shockPosition.Z = 180f + _shockScale * 80f;
            }
            else
            {
                _shockStart *= 1.04f;  // per frame
                _shockPosition -= _shockStart;
                _shockLight *= 0.5f;   // per frame
                _shockPosition.Z = 160f + _shockScale * 10f;
            }

            _shockLife -= factor;
            if (_shockLife <= 0f)
                _shockLive = false;
        }

        protected override bool HasActiveContent()
        {
            if (_cloudLife > 0f || _magicLife > 0f || !_struck || _shockLive)
                return true;
            for (int i = 0; i < _winds.Length; i++) if (_winds[i].Live) return true;
            for (int i = 0; i < _damages.Length; i++) if (_damages[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            var terrain = World?.Terrain;
            double worldTime = FPSCounter.Instance.WorldTime;

            if (_magicLife > 0f && Batch.Begin(_magicTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, _magicScale, _magicScale, _magicLight, -_ownerAngle.Z);
                Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, _magicScale, _magicScale, _magicLight, -_ownerAngle.Z);
                Batch.End();
            }

            if (_cloudLife > 0f && Batch.Begin(_cloudTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                float rotation = (float)(-(long)worldTime * 0.5 % 360.0);
                Batch.AddTerrainDecal(terrain, _origin.X, _origin.Y, _cloudScale, _cloudScale, _cloudLight, rotation);
                Batch.End();
            }

            if (_windRenderer != null)
            {
                var angle = new Vector3(
                    MathHelper.ToRadians(_ownerAngle.X), MathHelper.ToRadians(_ownerAngle.Y),
                    MathHelper.ToRadians((float)(-(long)worldTime * 0.9 % 360.0)));
                for (int i = 0; i < _winds.Length; i++)
                {
                    ref Wind wind = ref _winds[i];
                    if (wind.Live)
                        _windRenderer.DrawInstance(_origin, angle, wind.Scale, wind.Light * 6f, 1f);
                }
            }

            if (_shockLive && _shockRenderer != null)
            {
                var angle = new Vector3(MathHelper.ToRadians(_shockAngle.X), MathHelper.ToRadians(_shockAngle.Y), MathHelper.ToRadians(_shockAngle.Z));
                _shockRenderer.DrawInstance(_shockPosition, angle, _shockScale, _shockLight, 1f);
                if (_shockSubType == 3)
                    _shockRenderer.DrawInstance(_shockPosition, angle, _shockScale, _shockLight, 1f);
            }

            if (Batch.Begin(_damageTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                for (int i = 0; i < _damages.Length; i++)
                {
                    ref Damage d = ref _damages[i];
                    if (d.Live)
                        Batch.AddSprite(d.Position, d.Scale, d.Light * 1.4f);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _windRenderer?.Dispose();
            _shockRenderer?.Dispose();
        }
    }
}
