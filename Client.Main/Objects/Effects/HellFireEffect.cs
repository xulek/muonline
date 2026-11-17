#nullable enable
using System;
using System.Threading.Tasks;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// Hellfire (AT_SKILL_HELL = 10, masters AT_SKILL_HELL_FIRE_UP..+4), SourceMain5.2:
    /// packet  WSclient.cpp:4865       PLAYER_SKILL_HELL
    /// gate    ZzzCharacter.cpp:2876   AttackStage default: AnimationFrame >= 5 (else 14 ticks)
    /// release ZzzCharacter.cpp:4267   MODEL_CIRCLE + MODEL_CIRCLE_LIGHT at the caster, SOUND_HELLFIRE
    /// circle  ZzzEffect.cpp:2230 (init), 10793 (move), Skill/Circle01.bmd, BlendMesh 0
    /// light   ZzzEffect.cpp:2258 (init), 10860 (move: fade, U scroll, MODEL_STONE sub 1), Skill/Circle02.bmd
    /// stone   sub 1: Direction 0, rises with Gravity += 0.5 (ZzzEffect.cpp:2855, 11060)
    /// cast    ZzzCharacter.cpp:5643   while PLAYER_SKILL_HELL: rand_fps_check(1) -> 10 x BITMAP_FIRE sub 0 at random bones
    ///         (emitted from the release on; the frames before AnimationFrame 5 are not covered)
    /// </summary>
    internal sealed class HellFireEffect : SourceSkillEffectBase
    {
        private const string CircleModelPath = "Skill/Circle01.bmd"; // MODEL_CIRCLE
        private const string LightModelPath = "Skill/Circle02.bmd";  // MODEL_CIRCLE_LIGHT
        private static readonly string[] StonePaths = { "Skill/Stone01.bmd", "Skill/Stone02.bmd" };
        private const string SoundPath = "Sound/sHellFire.wav";      // SOUND_HELLFIRE

        private const int SkillId = 10;
        private const float ReleaseDelaySeconds = 14f * 0.04f;
        private const float ModelVelocity = 0.3f;
        private const float EffectScale = 0.9f;

        private struct Stone
        {
            public bool Live;
            public int Model;
            public Vector3 Position;
            public Vector3 Angle;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Stone[] _stones = new Stone[32];

        private struct Fire
        {
            public bool Live;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Scale;
            public float Gravity;
            public float LifeTime;
        }

        private readonly Fire[] _fires = new Fire[256];
        private Microsoft.Xna.Framework.Graphics.Texture2D? _fireTexture;
        private readonly SourceModelRenderer?[] _stoneRenderers = new SourceModelRenderer?[2];
        private SourceModelRenderer? _circleModel;
        private SourceModelRenderer? _lightModel;

        private Vector3 _position;
        private Vector3 _angle;
        private float _circleLifeTime;
        private float _lightLifeTime;
        private float _frame;
        private float _stoneFrame;

        // Dynamic light (slot 0: circle glow at the caster).
        private static readonly Vector3 FireLightColor = new(1f, 0.42f, 0.12f);
        private float _lightAge;

        public HellFireEffect(WalkerObject caster)
            : base(caster, 5f, ReleaseDelaySeconds, 16)
        {
        }

        protected override async Task LoadEffectContent()
        {
            _fireTexture = await LoadTexture("Effect/Fire01.jpg"); // BITMAP_FIRE (4x1)
            _circleModel = await LoadModel(CircleModelPath, 0);
            _lightModel = await LoadModel(LightModelPath, 0);
            if (_circleModel != null)
            {
                _circleModel.AnimationSpeed = 0f;
                _circleModel.UseNormalBlend();
            }
            if (_lightModel != null)
            {
                _lightModel.AnimationSpeed = 0f;
                _lightModel.UseNormalBlend();
            }
            for (int i = 0; i < 2; i++)
            {
                var stone = await LoadModel(StonePaths[i], -1);
                if (stone == null)
                    continue;
                stone.AnimationSpeed = 0f;
                stone.UseNormalBlend();
                _stoneRenderers[i] = stone;
            }
        }

        protected override void OnStart()
        {
            _position = CasterPosition;
            _angle = new Vector3(0f, 0f, CasterYawDegrees);
            _circleLifeTime = 45f;
            _lightLifeTime = 40f;
            PlayUnmappedSound(SkillId, SoundPath);
        }

        protected override void OnTick(float factor)
        {
            _frame += ModelVelocity * factor;

            if (Caster is Client.Main.Objects.Player.PlayerObject player
                && player.CurrentAction == Client.Main.Models.PlayerAction.PlayerSkillHell && FpsCheck(1, factor))
                EmitBoneFires(player);
            MoveFires(factor);

            if (_lightLifeTime > 0f && FpsCheck(4, factor) && FpsCheck(1, factor))
            {
                Vector3 offset = SourceJointMath.Rotate(new Vector3(0f, MuGame.Random.Next(300), 0f), new Vector3(0f, 0f, MuGame.Random.Next(360)));
                SpawnStone(_position + offset);
            }

            if (_lightLifeTime > 0f)
                Client.Main.Graphics.Camera.Instance.SetEarthQuake((MuGame.Random.Next(6) - 6) * 0.1f); // MODEL_CIRCLE_LIGHT, ZzzEffect.cpp:10870

            _circleLifeTime -= factor;
            _lightLifeTime -= factor;
            UpdateLight(factor);

            bool anyStone = false;
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (!s.Live)
                    continue;

                anyStone = true;
                s.Position.Z += s.Gravity * factor;
                s.Gravity += 0.5f * factor;
                s.LifeTime -= factor;
                if (s.LifeTime <= 0f)
                    s.Live = false;
            }
            if (anyStone)
                _stoneFrame += ModelVelocity * factor;
        }

        private void UpdateLight(float factor)
        {
            _lightAge += factor;
            // MODEL_CIRCLE lives 45 ticks; ramp in over 4, release over the last 15.
            float k = EffectLights.Envelope(_lightAge, 45f, 4f, 15f);
            if (k <= 0f)
            {
                Lights.Hide(0);
                return;
            }

            float intensity = 1.4f * k * EffectLights.Flicker(_lightAge * 0.04f, 2.3f, 0.2f);
            Lights.Set(0, _position + new Vector3(0f, 0f, 60f), FireLightColor, 420f, intensity);
        }

        private void EmitBoneFires(Client.Main.Objects.Player.PlayerObject player)
        {
            var bmd = player.Model;
            Matrix[]? bones = player.GetBoneTransforms();
            if (bmd?.Bones == null || bones == null || bones.Length == 0)
                return;

            Matrix world = player.WorldPosition;
            int count = Math.Min(bmd.Bones.Length, bones.Length);
            for (int i = 0; i < 10; i++)
            {
                Vector3 position = Vector3.Transform(bones[MuGame.Random.Next(count)].Translation, world);
                for (int k = 0; k < _fires.Length; k++)
                {
                    ref Fire f = ref _fires[k];
                    if (f.Live)
                        continue;
                    f.Live = true;
                    f.Position = position;
                    f.LifeTime = 24f;
                    f.Gravity = 0f;
                    f.Velocity = SourceJointMath.Rotate(new Vector3(0f, -(MuGame.Random.Next(16) + 32) * 0.1f, 0f), new Vector3(0f, 0f, CasterYawDegrees));
                    f.Scale = (MuGame.Random.Next(64) + 128) * 0.01f;
                    break;
                }
            }
        }

        private void MoveFires(float factor)
        {
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (!f.Live)
                    continue;
                f.LifeTime -= factor;
                if (f.LifeTime <= 0f)
                {
                    f.Live = false;
                    continue;
                }
                // BITMAP_FIRE sub 0
                f.Position += f.Velocity * factor;
                f.Gravity += 0.004f * factor;
                f.Scale -= 0.04f * factor;
                f.Position.Z += f.Gravity * 10f * factor;
            }
        }

        private void SpawnStone(Vector3 position)
        {
            for (int i = 0; i < _stones.Length; i++)
            {
                ref Stone s = ref _stones[i];
                if (s.Live)
                    continue;

                s.Live = true;
                s.Model = MuGame.Random.Next(2);
                s.Position = position;
                s.Angle = _angle;
                s.LifeTime = MuGame.Random.Next(16) + 32;
                s.Scale = (MuGame.Random.Next(4) + 8) * 0.1f;
                s.Angle.Z = MuGame.Random.Next(360);
                s.Gravity = MuGame.Random.Next(16) + 8;
                return;
            }
        }

        protected override bool HasActiveContent()
        {
            if (_circleLifeTime > 0f || _lightLifeTime > 0f)
                return true;
            for (int i = 0; i < _stones.Length; i++) if (_stones[i].Live) return true;
            for (int i = 0; i < _fires.Length; i++) if (_fires[i].Live) return true;
            return false;
        }

        protected override void OnDraw()
        {
            DrawFires();
            var terrain = World?.Terrain;
            Vector3 light = terrain?.EvaluateTerrainLight(_position.X, _position.Y) ?? Vector3.One;
            var angle = new Vector3(0f, 0f, MathHelper.ToRadians(_angle.Z));
            float factor = SourceJointMath.FrameFactor;

            // Move-case values use LifeTime before the tick's decrement.
            if (_circleLifeTime > 0f && _circleModel != null)
            {
                float lt = _circleLifeTime + factor;
                _circleModel.SetAnimationTime(_frame);
                _circleModel.Update(LastGameTime);
                _circleModel.DrawInstance(_position, angle, EffectScale, light, MathF.Max(Quantize(lt * 0.1f), 0.003f));
            }

            if (_lightLifeTime > 0f && _lightModel != null)
            {
                float lt = _lightLifeTime + factor;
                float blendMeshLight = lt >= 30f ? (40f - lt) * 0.1f : lt * 0.1f;
                _lightModel.ScrollU = -lt * 0.01f;
                _lightModel.SetAnimationTime(_frame);
                _lightModel.Update(LastGameTime);
                _lightModel.DrawInstance(_position, angle, EffectScale, light, MathF.Max(Quantize(blendMeshLight), 0.003f));
            }

            for (int m = 0; m < 2; m++)
            {
                SourceModelRenderer? renderer = _stoneRenderers[m];
                if (renderer == null)
                    continue;

                bool updated = false;
                for (int i = 0; i < _stones.Length; i++)
                {
                    ref Stone s = ref _stones[i];
                    if (!s.Live || s.Model != m)
                        continue;

                    if (!updated)
                    {
                        renderer.SetAnimationTime(_stoneFrame);
                        renderer.Update(LastGameTime);
                        updated = true;
                    }

                    Vector3 stoneLight = terrain?.EvaluateTerrainLight(s.Position.X, s.Position.Y) ?? Vector3.One;
                    renderer.DrawInstance(s.Position, new Vector3(0f, 0f, MathHelper.ToRadians(s.Angle.Z)), s.Scale, stoneLight, 1f);
                }
            }
        }

        private void DrawFires()
        {
            if (_fireTexture == null || !Batch.Begin(_fireTexture, Client.Main.Graphics.Blendings.OneOneAdditive,
                    Microsoft.Xna.Framework.Graphics.DepthStencilState.DepthRead, Microsoft.Xna.Framework.Graphics.SamplerState.LinearClamp))
                return;
            for (int i = 0; i < _fires.Length; i++)
            {
                ref Fire f = ref _fires[i];
                if (!f.Live)
                    continue;
                int frame = (int)((23f - f.LifeTime) / 6f);
                Batch.AddSpriteRegion(f.Position, _fireTexture.Width * f.Scale * 0.25f, _fireTexture.Height * f.Scale, Vector3.One, 0f,
                    frame % 4 * 0.25f, 0f, 0.25f, 1f);
            }
            Batch.End();
        }

        private static float Quantize(float value) => MathF.Round(value * 50f) / 50f;

        protected override void DisposeEffect()
        {
            _circleModel?.Dispose();
            _lightModel?.Dispose();
            _stoneRenderers[0]?.Dispose();
            _stoneRenderers[1]?.Dispose();
        }
    }
}
