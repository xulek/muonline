#nullable enable
using System.Threading.Tasks;
using Client.Main.Content;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects.Joints
{
    /// <summary>
    /// Port of one SourceMain5.2 <c>BLUR</c> record (ZzzEffectBlurSpark.cpp: AddBlur, CreateBlur,
    /// MoveBlurs, RenderBlurs). The original keeps one record per owner; <see cref="Move"/> runs
    /// once per rendered frame and is not scaled by FPS_ANIMATION_FACTOR, like MoveBlurs.
    /// </summary>
    internal sealed class SourceBlur
    {
        public const int MaxTails = 30;          // MAX_BLUR_TAILS
        private const int MaxLifeTime = 30;      // MAX_BLUR_LIFETIME

        private static readonly string[] TexturePaths =
        {
            "Effect/blur01.jpg",          // BITMAP_BLUR
            "Effect/motion_blur.jpg",     // BITMAP_BLUR + 1
            "Effect/motion_blur_r.jpg",   // BITMAP_BLUR + 2
            "Effect/motion_mono.jpg",     // BITMAP_BLUR + 3
            "",                           // BITMAP_BLUR + 4 (not loaded)
            "",                           // BITMAP_BLUR + 5 (not loaded)
            "Effect/motion_blur_r3.jpg",  // BITMAP_BLUR + 6
            "Effect/gra.jpg",             // BITMAP_BLUR + 7
            "Effect/spinmark01.jpg",      // BITMAP_BLUR + 8
            "Effect/flamestani.jpg",      // BITMAP_BLUR + 9  (GL_LINEAR)
            "Effect/sword_blur.jpg",      // BITMAP_BLUR + 10 (GL_LINEAR)
            "Effect/joint_sword_red.jpg", // BITMAP_BLUR + 11 (GL_LINEAR)
        };

        private const string Blur2TexturePath = "Effect/blur02.jpg"; // BITMAP_BLUR2 (Type 3)

        private readonly Vector3[] _p1 = new Vector3[MaxTails];
        private readonly Vector3[] _p2 = new Vector3[MaxTails];
        private int _number;
        private int _lifeTime;
        private int _type;
        private Vector3 _light;

        public bool Live { get; private set; }

        /// <summary>Loads the texture of every blur type the weapon blur can select.</summary>
        public static Task PreloadAsync()
        {
            var tasks = new Task[TexturePaths.Length + 1];
            for (int i = 0; i < TexturePaths.Length; i++)
                tasks[i] = string.IsNullOrEmpty(TexturePaths[i]) ? Task.CompletedTask : TextureLoader.Instance.Prepare(TexturePaths[i]);
            tasks[^1] = TextureLoader.Instance.Prepare(Blur2TexturePath);
            return Task.WhenAll(tasks);
        }

        /// <summary>CreateBlur(Owner, p1, p2, Light, Type, Short) for an owner with a single record.</summary>
        public void Create(Vector3 p1, Vector3 p2, Vector3 light, int type, bool isShort)
        {
            if (!Live)
            {
                Live = true;
                _number = 0;
                _lifeTime = isShort ? 15 : MaxLifeTime;
            }

            _type = type;
            _light = light;
            for (int i = _number - 1; i >= 0; i--)
            {
                _p1[i + 1] = _p1[i];
                _p2[i + 1] = _p2[i];
            }
            _p1[0] = p1;
            _p2[0] = p2;
            _number++;
            if (_number >= MaxTails - 1)
                _number = MaxTails - 1;
        }

        /// <summary>MoveBlurs for this record (once per frame).</summary>
        public void Move()
        {
            if (!Live)
                return;

            _lifeTime--;
            _number--;
            for (int i = _number - 1; i >= 0; i--)
            {
                _p1[i + 1] = _p1[i];
                _p2[i + 1] = _p2[i];
            }

            if (_lifeTime <= 0)
            {
                _number--;
                if (_number <= 0)
                    Live = false;
            }
        }

        public void Clear() => Live = false;

        /// <summary>RenderBlurs for this record; <paramref name="ownerLevel"/> is CHARACTER::Level (1 = murderer).</summary>
        public void Render(SourceQuadBatch batch, int ownerLevel)
        {
            if (!Live || _number < 2)
                return;

            string path = _type switch
            {
                3 => Blur2TexturePath,
                4 => TexturePaths[0],
                5 => TexturePaths[3],
                _ => _type >= 0 && _type < TexturePaths.Length ? TexturePaths[_type] : string.Empty
            };
            if (string.IsNullOrEmpty(path))
                return;

            Texture2D? texture = TextureLoader.Instance.GetTexture2D(path);
            bool additive = ownerLevel == 0 && (_type <= 3 || (_type >= 5 && _type <= 10));
            SamplerState sampler = _type >= 9 ? SamplerState.LinearClamp : SamplerState.PointClamp;
            if (!batch.Begin(texture, additive ? Blendings.OneOneAdditive : Blendings.Negative, DepthStencilState.DepthRead, sampler))
                return;

            float number = _number;
            for (int j = 0; j < _number - 1; j++)
            {
                float light1 = ownerLevel == 0 ? (_number - j) / number : 1f;
                float light2 = ownerLevel == 0 ? (_number - (j + 1)) / number : 1f;
                float u1 = j / number;
                float u2 = (j + 1) / number;

                batch.AddQuad(
                    _p1[j], new Vector2(u1, 1f),
                    _p2[j], new Vector2(u1, 0f),
                    _p2[j + 1], new Vector2(u2, 0f),
                    _p1[j + 1], new Vector2(u2, 1f),
                    SourceQuadBatch.ToColor(_light * light1),
                    SourceQuadBatch.ToColor(_light * light2));
            }

            batch.End();
        }
    }
}
