#nullable enable
using Client.Main.Content;
using Client.Main.Controllers;
using Client.Main.Models;
using Client.Main.Objects;
using Client.Main.Objects.Effects.SkillFx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;

namespace Client.Main.Effects
{
    /// <summary>
    /// SourceMain5.2 eBuff_WizDefense (Soul Barrier): InsertBuffPhysicalEffect creates
    /// 5 x CreateJoint(MODEL_SPEARSKILL, owner, sub 0, Scale 50). Each joint wanders on the
    /// deterministic "magic screw" path around the owner (radius 80, height 110 +- 120, driven by
    /// WorldTime / 40 and the joint index), keeps 30 owner-relative tails rendered with flareBlue.jpg
    /// (darkening with height above the owner) and a flareBlue sprite at its middle tail.
    /// </summary>
    public sealed class ManaShieldBubbleEffect : EffectObject
    {
        private const string FlareBlueTexturePath = "Effect/flareBlue.jpg"; // BITMAP_FLARE_BLUE
        private const int JointCount = 5;
        private const int MaxTails = 30;
        private const float JointScale = 50f;
        private const float TickSeconds = 0.04f;

        private static readonly float[] Speed = { 0.048f, 0.0613f, 0.1113f };

        private readonly WalkerObject _owner;
        private readonly int[] _jointIndex = new int[JointCount];
        private readonly Vector3[] _tails = new Vector3[JointCount * MaxTails]; // owner-relative
        private readonly int[] _tailCount = new int[JointCount];
        private Texture2D? _texture;
        private float _accumulator;

        public ManaShieldBubbleEffect(WalkerObject owner)
        {
            _owner = owner;

            IsTransparent = true;
            AffectedByTransparency = true;
            BlendState = BlendState.Additive;
            DepthState = DepthStencilState.DepthRead;

            BoundingBoxLocal = new BoundingBox(
                new Vector3(-160f, -160f, -40f),
                new Vector3(160f, 160f, 300f));

            for (int i = 0; i < JointCount; i++)
                _jointIndex[i] = MuGame.Random.Next(1, 1000);
        }

        public override async Task Load()
        {
            await base.Load();
            _texture = await TextureLoader.Instance.PrepareAndGetTexture(FlareBlueTexturePath);
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (_owner.Status == GameControlStatus.Disposed || _owner.Hidden)
            {
                RemoveSelf();
                return;
            }

            _accumulator += MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, TickSeconds * 4f);
            double worldTime = gameTime.TotalGameTime.TotalMilliseconds;
            int steps = 0;
            while (_accumulator >= TickSeconds && steps++ < 4)
            {
                _accumulator -= TickSeconds;
                for (int j = 0; j < JointCount; j++)
                    PushTail(j, ComputeOffset(_jointIndex[j], worldTime));
            }
        }

        /// <summary>MoveJoint MODEL_SPEARSKILL sub 0 position relative to TargetPosition (owner + 10 z).</summary>
        private static Vector3 ComputeOffset(int index, double worldTime)
        {
            int frame = (int)(worldTime / 40.0);
            frame = ((index % 2) != 0 ? frame : -frame) + index * 53731;

            float a = (frame + 55555) * Speed[0];
            float b = frame * Speed[1];
            float tx = MathF.Sin(a) * MathF.Cos(b);
            float ty = MathF.Sin(a) * MathF.Sin(b);
            float tz = MathF.Cos(a);

            float sinAdd = MathF.Sin((frame + 11111) * Speed[2]);
            float cosAdd = MathF.Cos((frame + 11111) * Speed[2]);
            float dx = cosAdd * ty - sinAdd * tz;
            float dy = sinAdd * ty + cosAdd * tz;
            float dz = tx;

            return new Vector3(dx * 80f, dy * 80f, 10f + 110f + dz * 120f);
        }

        private void PushTail(int joint, Vector3 offset)
        {
            int baseIndex = joint * MaxTails;
            int count = Math.Min(_tailCount[joint] + 1, MaxTails);
            for (int t = count - 1; t > 0; t--)
                _tails[baseIndex + t] = _tails[baseIndex + t - 1];
            _tails[baseIndex] = offset;
            _tailCount[joint] = count;
        }

        public override void Draw(GameTime gameTime)
        {
            base.Draw(gameTime);

            if (!Visible || _texture == null || !SkillFxQuadBatch.Begin(_texture))
                return;

            Vector3 ownerPos = _owner.WorldPosition.Translation;
            float sprite = _texture.Width * 0.7f * 0.5f;
            float half = JointScale * 0.5f;
            float denom = MaxTails - 1;

            for (int j = 0; j < JointCount; j++)
            {
                int baseIndex = j * MaxTails;
                int count = _tailCount[j];
                for (int t = 0; t < count - 1; t++)
                {
                    Vector3 a = ownerPos + _tails[baseIndex + t];
                    Vector3 b = ownerPos + _tails[baseIndex + t + 1];
                    Color ca = TailColor(_tails[baseIndex + t].Z);
                    Color cb = TailColor(_tails[baseIndex + t + 1].Z);
                    // Light1 = (NumTails - j) / (MaxTails - 1): UV scrolls along the tail.
                    SkillFxQuadBatch.Segment(a, b, half, half, ca, cb, (count - t) / denom, (count - t - 1) / denom);

                    if (t == count / 2)
                        SkillFxQuadBatch.Billboard(a, sprite, sprite, 0f, ca);
                }
            }

            SkillFxQuadBatch.End();
        }

        /// <summary>RenderJoints sub 0: Light - (tailZ - (targetZ + 50)) * 0.01 above the waist.</summary>
        private static Color TailColor(float relativeZ)
        {
            float height = (relativeZ - 50f) * 0.01f;
            float l = height > 0f ? 1f - height : 1f;
            return SkillFxQuadBatch.Light(new Vector3(l));
        }

        private void RemoveSelf()
        {
            if (Parent != null)
                Parent.Children.Remove(this);
            else
                World?.RemoveObject(this);

            Dispose();
        }
    }
}
