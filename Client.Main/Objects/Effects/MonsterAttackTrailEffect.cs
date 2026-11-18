#nullable enable
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Graphics;
using Client.Main.Models;
using Client.Main.Objects.Effects.Joints;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>Samples a short weapon swing from its BMD action once at release.</summary>
    internal sealed class MonsterAttackTrailEffect : EffectObject
    {
        private const int Samples = 12;
        private const float Lifetime = 0.28f;
        private readonly SourceObjectBlur _blur = new(Samples + 1);
        private readonly SourceQuadBatch _batch = new(Samples - 1);
        private readonly Vector3 _tint;
        private Texture2D? _texture;
        private float _age;

        private MonsterAttackTrailEffect(Vector3 position, Vector3 tint)
        {
            Position = position;
            _tint = tint;
            IsTransparent = true;
            AffectedByTransparency = true;
            BoundingBoxLocal = new BoundingBox(new Vector3(-480f, -480f, -160f), new Vector3(480f, 480f, 520f));
        }

        internal static void Spawn(MonsterObject caster, MonsterActionType action, float firstFrame, float lastFrame,
            int startBone, int endBone, Vector3 tint, Vector3 startOffset = default, Vector3 endOffset = default)
        {
            if (caster.World is not { } world)
                return;

            var effect = new MonsterAttackTrailEffect(caster.Position, tint);
            Matrix transform = caster.WorldPosition;
            for (int i = 0; i < Samples; i++)
            {
                double frame = MathHelper.Lerp(firstFrame, lastFrame, i / (float)(Samples - 1));
                if (caster.TryComputeBoneAt((int)action, frame, startBone, out Matrix startPose) &&
                    caster.TryComputeBoneAt((int)action, frame, endBone, out Matrix endPose))
                {
                    effect._blur.Add(Vector3.Transform(startOffset, startPose * transform),
                        Vector3.Transform(endOffset, endPose * transform));
                }
            }

            if (!effect._blur.IsLive)
            {
                effect.Dispose();
                return;
            }

            world.Objects.Add(effect);
            _ = effect.Load();
        }

        public override async Task LoadContent()
        {
            _texture = await TextureLoader.Instance.PrepareAndGetTexture("Effect/motion_blur_r.jpg");
            if (!IsDisposeRequested)
                _batch.Initialize(GraphicsDevice);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
            if (Status != GameControlStatus.Ready)
                return;

            _age += (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (_age >= Lifetime || _texture == null)
            {
                World?.RemoveObject(this);
                Dispose();
            }
        }

        public override void DrawAfter(GameTime gameTime)
        {
            if (!Visible || Status != GameControlStatus.Ready ||
                !_batch.Begin(_texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            _blur.Append(_batch, _tint * (1f - _age / Lifetime));
            _batch.End();
        }

        public override void Dispose()
        {
            _batch.Dispose();
            base.Dispose();
        }
    }
}
