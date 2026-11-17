#nullable enable
using Client.Main.Content;
using Client.Main.Models;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Port of SourceMain5.2 MODEL_BLIZZARD (AttackEffect MONSTER_QUEEN_RAINER:
    /// 20x CreateEffect at the target). Sub0 falls from +600 Z with wobbling X/Y and
    /// accelerating gravity; on terrain impact it spawns the static sub1 glow burst.
    /// </summary>
    public sealed class MonsterBlizzardStrikeEffect : EffectObject
    {
        private const int BlizzardCount = 20;

        private readonly BlizzardPiece[] _blizzards = new BlizzardPiece[BlizzardCount];

        private const float LightFadeOutSeconds = 0.4f;
        private static readonly Vector3 IceLightColor = new(0.4f, 0.7f, 1f);
        private readonly EffectLights _lights;
        private float _age;
        private float _impactPulse;
        private float _fadeOut = -1f;
        private float _lastGlow;

        public MonsterBlizzardStrikeEffect(Vector3 position)
        {
            Position = position;
            _lights = new EffectLights(this, 1);
            IsTransparent = true;
            AffectedByTransparency = true;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-400f, -400f, -100f),
                new Vector3(400f, 400f, 900f));
        }

        public override async Task Load()
        {
            await base.Load();
            if (Status != GameControlStatus.Ready)
                return;

            var blizzardModel = await BMDLoader.Instance.Prepare("Skill/Blizzard.bmd");
            if (blizzardModel == null)
                return;

            float groundZ = World?.Terrain != null
                ? World.Terrain.RequestTerrainHeight(Position.X, Position.Y)
                : Position.Z;

            for (int i = 0; i < _blizzards.Length; i++)
            {
                var piece = new BlizzardPiece(blizzardModel);
                _blizzards[i] = piece;
                Children.Add(piece);
                await piece.Load();

                // CreateJoint/BLIZZARD init: LifeTime rand(15..30), Gravity -20-rand(10..40),
                // jitter +-150 XY around target (rangeX 300 / rangeY 150), spawn +600 Z,
                // Scale 0.5.
                piece.LifeFrames = MuGame.Random.Next(15) + 15;
                piece.Gravity = -(MuGame.Random.Next(30) + 10) - 20f;
                double a = MuGame.Random.NextDouble() * MathHelper.TwoPi;
                float r = (float)MuGame.Random.NextDouble() * 150f;
                piece.StartPosition = new Vector3(
                    Position.X + MathF.Cos((float)a) * r,
                    Position.Y + MathF.Sin((float)a) * r,
                    groundZ + 600f);
                piece.Position = piece.StartPosition;
                piece.ScaleValue = 0.5f;
            }
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _age += dt;
            _impactPulse *= MathF.Exp(-dt * 8f);

            if (_fadeOut >= 0f)
            {
                // All shards have landed: fade the ice glow out, then remove.
                _fadeOut += dt;
                float k = 1f - _fadeOut / LightFadeOutSeconds;
                if (k <= 0f)
                {
                    World?.RemoveObject(this);
                    Dispose();
                    return;
                }
                SetIceLight(_lastGlow * k);
                return;
            }

            bool anyAlive = false;

            foreach (var piece in _blizzards)
            {
                if (piece == null || piece.Dead)
                    continue;

                piece.LifeFrames -= dt * 25f;

                // Move: X/Y wobble around StartPosition, StartPosition drifts -X, Z falls.
                piece.WobblePhase += dt * 7f;
                float wobble = MathF.Sin(piece.WobblePhase) * 10f;
                piece.StartPosition += new Vector3(-10f * dt, 0f, 0f);
                piece.Gravity -= MuGame.Random.Next(5) * dt;

                piece.Position = new Vector3(
                    piece.StartPosition.X + wobble,
                    piece.StartPosition.Y + wobble,
                    piece.Position.Z + piece.Gravity * dt);

                float groundZ = World?.Terrain != null
                    ? World.Terrain.RequestTerrainHeight(piece.Position.X, piece.Position.Y)
                    : Position.Z;

                if (piece.Position.Z <= groundZ || piece.LifeFrames <= 0f)
                {
                    // Impact: static sub1 glow burst at the hit point.
                    piece.Dead = true;
                    piece.Position = new Vector3(piece.Position.X, piece.Position.Y, groundZ + 40f);
                    piece.PlayImpactGlow();
                    _impactPulse = MathF.Min(1f, _impactPulse + 0.35f);
                    continue;
                }

                anyAlive = true;
            }

            float glow = MathF.Min(1.2f, 0.55f * MathHelper.Clamp(_age / 0.2f, 0f, 1f) + 0.65f * _impactPulse);

            if (!anyAlive)
            {
                // Keep the visuals' original end (shards hidden now) but let the light fade.
                if (_lastGlow > 0.01f)
                {
                    for (int i = 0; i < _blizzards.Length; i++)
                        if (_blizzards[i] != null)
                            _blizzards[i].Hidden = true;
                    _lastGlow = glow;
                    _fadeOut = 0f;
                    SetIceLight(glow);
                    return;
                }

                World?.RemoveObject(this);
                Dispose();
                return;
            }

            _lastGlow = glow;
            SetIceLight(glow);
        }

        private void SetIceLight(float intensity)
        {
            float ground = World?.Terrain != null
                ? World.Terrain.RequestTerrainHeight(Position.X, Position.Y)
                : Position.Z;
            _lights.Set(0, new Vector3(Position.X, Position.Y, ground + 60f), IceLightColor, 340f, intensity);
        }

        public override void Dispose()
        {
            _lights.Dispose();
            base.Dispose();
        }

        private sealed class BlizzardPiece : ModelObject
        {
            private readonly Client.Data.BMD.BMD _model;

            public Vector3 StartPosition { get; set; }
            public float Gravity { get; set; }
            public float LifeFrames { get; set; }
            public float WobblePhase { get; set; }
            public float ScaleValue { get; set; } = 0.5f;
            public bool Dead { get; set; }

            public BlizzardPiece(Client.Data.BMD.BMD model)
            {
                _model = model;
                RenderShadow = false;
                ContinuousAnimation = true;
                AnimationSpeed = 6f;
                IsTransparent = true;
                AffectedByTransparency = true;
            }

            public override async Task Load()
            {
                Model = _model;
                await base.Load();
            }

            /// <summary>MODEL_BLIZZARD sub1: static glow, LifeTime 20 frames.</summary>
            public async void PlayImpactGlow()
            {
                BlendMeshLight = 1.2f;
                Alpha = 0.9f;
                Dead = true;

                await Task.Delay(800);
                Hidden = true;
            }
        }
    }
}
