#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Lance (AT_SKILL_JAVELIN = 45), SourceMain5.2:
    /// packet   WSclient.cpp:4152       SetPlayerMagic, AnimationFrame = 0, AttackTime = 1
    /// gate     ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame &gt;= 5 (else 14 ticks)
    /// release  ZzzCharacter.cpp:4875   3 x MODEL_SKILL_JAVELIN sub 0 / 1 / 2 at the caster, owner = target, SOUND_BCS_JAVELIN
    /// javelin  ZzzEffect.cpp:4348 (LifeTime 35, Gravity 2, Velocity 10, Scale 1.2, Direction (0,-5,0), z + 150,
    ///          HeadAngle.z += (sub - 1) * (rand%80+10)), 14526 (LifeTime &gt;= 25: spin up; &lt; 20: MoveHumming to the target +150,
    ///          BITMAP_POUNDING_BALL sub 1; sub 1 / 2 ride +-sin(LifeTime*0.1)*30 at the target height; Alpha = LifeTime / 10),
    ///          17934 (no MoveParticle), ZzzObject.cpp:1589 (BodyLight (1,0.6,0.3), RENDER_TEXTURE + chrome pass without mesh 1)
    /// ball     ZzzEffectParticle.cpp:3293 (LifeTime 15, Velocity.y -(rand%16+32)*0.1, Scale (rand%128+128)*0.01, Light 0.5),
    ///          8194 (the sub 0 / 1 branch is unreachable: generic MovePosition only), default sprite
    /// Not ported: AddTerrainLight.
    /// </summary>
    internal sealed class LanceEffect : SourceSkillEffectBase
    {
        private const string JavelinPath = "Skill/Javelin.bmd";                     // MODEL_SKILL_JAVELIN
        private const string BallTexturePath = "Effect/PoundingBall.jpg";           // BITMAP_POUNDING_BALL
        private const string SoundPath = "Sound/battlecastle/sCShockWave.wav";      // SOUND_BCS_JAVELIN
        private static readonly Vector3 BodyLight = new(1f, 0.6f, 0.3f);

        private struct Javelin
        {
            public bool Live;
            public int SubType;
            public Vector3 Position;
            public Vector3 Angle;
            public Vector3 HeadAngle;
            public float Direction;
            public float Gravity;
            public float Velocity;
            public float Scale;
            public float LifeTime;
        }

        private struct Ball
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Step;
            public float Scale;
            public float Rotation;
            public float LifeTime;
        }

        private readonly WalkerObject? _target;
        private readonly Javelin[] _javelins = new Javelin[3];
        private readonly Ball[] _balls = new Ball[64];
        private SourceModelRenderer? _renderer;
        private Texture2D? _ballTexture;
        private Vector3 _startPosition;
        private int _ballCount;

        public LanceEffect(WalkerObject caster, WalkerObject? target)
            : base(caster, triggerFrame: 5f, triggerTimeoutSeconds: 14f * 0.04f, maxQuads: 64)
        {
            _target = target;
        }

        protected override async Task LoadEffectContent()
        {
            _renderer = await LoadModel(JavelinPath, -1);
            if (_renderer != null)
            {
                _renderer.AnimationSpeed = 0f;
                _renderer.UseNormalBlend();
                _renderer.ChromeBrightPass = true;
                _renderer.ChromePassHiddenMesh = 1;
            }
            _ballTexture = await LoadTexture(BallTexturePath);
            await SoundController.Instance.PreloadSoundAsync(SoundPath);
        }

        protected override void OnStart()
        {
            const float factor = 1f; // CreateEffect / CreateParticle init offsets: the 5.2 port scales them by FPS_ANIMATION_FACTOR, which shrinks them at high FPS; 25 Hz values
            Vector3 angle = new(MathHelper.ToDegrees(Caster.Angle.X), MathHelper.ToDegrees(Caster.Angle.Y), MathHelper.ToDegrees(Caster.Angle.Z));
            _startPosition = TargetAlive ? _target!.WorldPosition.Translation : CasterPosition;
            for (int i = 0; i < 3; i++)
            {
                ref Javelin j = ref _javelins[i];
                j.Live = true;
                j.SubType = i;
                j.LifeTime = 35f;
                j.Gravity = 2f;
                j.Velocity = 10f;
                j.Scale = 1.2f;
                j.Direction = -5f;
                j.Angle = angle;
                j.HeadAngle = angle;
                j.Position = CasterPosition;
                j.Position.Z += 150f * factor;
                float spread = MuGame.Random.Next(80) + 10;
                j.HeadAngle.Z += (i * spread - spread) * factor;
            }

            SoundController.Instance.PlayBuffer(SoundPath);
        }

        private bool TargetAlive => _target != null && _target.Status != GameControlStatus.Disposed && _target.World != null;

        protected override void OnTick(float factor)
        {
            if (TargetAlive)
            {
                _startPosition = _target!.WorldPosition.Translation;
                _startPosition.Z += 150f * factor;
            }

            for (int i = 0; i < _javelins.Length; i++)
            {
                ref Javelin j = ref _javelins[i];
                if (!j.Live)
                    continue;

                j.Scale += 0.015f * factor;
                if (j.LifeTime < 25f)
                {
                    if (j.LifeTime < 20f)
                    {
                        float distance = SourceJointMath.MoveHumming(ref j.Position, ref j.HeadAngle, _startPosition, j.Velocity, factor);
                        if (distance < 100f)
                        {
                            j.Position = _startPosition;
                            j.Scale += 0.04f * factor;
                            if ((int)j.LifeTime % 3 == 0 && FpsCheck(1, factor))
                                SpawnBall(j.Position, j.Angle);
                        }
                        else if (FpsCheck(1, factor))
                        {
                            SpawnBall(j.Position, j.Angle);
                        }
                    }

                    j.Velocity += 1.5f * factor;
                    j.Position += SourceJointMath.Rotate(new Vector3(0f, j.Direction, 0f), j.HeadAngle) * factor;
                    j.Gravity = MuGame.Random.Next(30) + 30f;
                    if (j.Direction > -50f)
                        j.Direction -= 8f * factor;
                }
                else
                {
                    j.Gravity += 10f * factor;
                    j.Position += SourceJointMath.Rotate(new Vector3(0f, j.Direction, 0f), j.HeadAngle) * factor;
                }

                j.Angle.Z += j.Gravity * factor;
                float height = MathF.Sin(j.LifeTime * 0.1f) * 30f;
                if (j.SubType == 1)
                    j.Position.Z = _startPosition.Z + height;
                else if (j.SubType == 2)
                    j.Position.Z = _startPosition.Z - height;

                j.LifeTime -= factor;
                if (j.LifeTime <= 0f)
                    j.Live = false;
            }

            if (_ballCount > 0)
            {
                for (int i = 0; i < _balls.Length; i++)
                {
                    ref Ball b = ref _balls[i];
                    if (!b.Live)
                        continue;
                    b.LifeTime -= factor;
                    if (b.LifeTime <= 0f)
                    {
                        b.Live = false;
                        _ballCount--;
                        continue;
                    }
                    b.Position += b.Step * factor;
                }
            }
        }

        private void SpawnBall(Vector3 position, Vector3 angleDeg)
        {
            for (int i = 0; i < _balls.Length; i++)
            {
                ref Ball b = ref _balls[i];
                if (b.Live)
                    continue;

                b.Live = true;
                b.Position = position;
                b.Step = SourceJointMath.Rotate(new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f), angleDeg);
                b.Scale = (MuGame.Random.Next(128) + 128) * 0.01f;
                b.Rotation = MuGame.Random.Next(360);
                b.LifeTime = 15f;
                _ballCount++;
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_ballCount > 0)
                return true;
            for (int i = 0; i < _javelins.Length; i++) if (_javelins[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            if (_renderer != null)
            {
                for (int i = 0; i < _javelins.Length; i++)
                {
                    ref Javelin j = ref _javelins[i];
                    if (!j.Live)
                        continue;
                    var angle = new Vector3(MathHelper.ToRadians(j.Angle.X), MathHelper.ToRadians(j.Angle.Y), MathHelper.ToRadians(j.Angle.Z));
                    _renderer.DrawInstance(j.Position, angle, j.Scale, BodyLight, 1f, MathF.Min(1f, j.LifeTime / 10f));
                }
            }

            if (_ballCount > 0 && Batch.Begin(_ballTexture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
            {
                var light = new Vector3(0.5f);
                for (int i = 0; i < _balls.Length; i++)
                {
                    ref Ball b = ref _balls[i];
                    if (b.Live)
                        Batch.AddSprite(b.Position, b.Scale, light, b.Rotation);
                }
                Batch.End();
            }
        }

        protected override void DisposeEffect()
        {
            _renderer?.Dispose();
        }
    }
}
