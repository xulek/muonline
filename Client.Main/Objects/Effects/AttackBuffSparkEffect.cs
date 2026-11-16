#nullable enable
using System.Threading.Tasks;
using Client.Main.Graphics;
using Client.Main.Objects.Effects.Joints;
using Client.Main.Objects.Player;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Client.Main.Objects.Effects
{
    /// <summary>
    /// SourceMain5.2 eBuff_Attack (Greater Damage, ZzzCharacter.cpp:10571): every frame, for both weapon link bones
    /// (33 / 42) and the bones 6 and 7 below them, CreateSprite(BITMAP_SHINY + 1, 1.5, Luminosity * (1, 0.3, 0.2))
    /// with Luminosity = (rand() % 30 + 70) * 0.01. The SPEARSKILL sub 4 orbit is a separate effect.
    /// </summary>
    internal sealed class AttackBuffSparkEffect : SourceSkillEffectBase
    {
        private const string ShinyTexturePath = "Effect/Shiny02.jpg"; // BITMAP_SHINY + 1
        private static readonly int[] LinkBones = { 33, 42 };

        private Texture2D? _texture;
        private bool _stopped;

        public AttackBuffSparkEffect(WalkerObject owner)
            : base(owner, 0f, 0f, 16)
        {
        }

        public void Stop()
        {
            _stopped = true;
            RemoveSelf();
        }

        protected override async Task LoadEffectContent()
        {
            _texture = await LoadTexture(ShinyTexturePath);
        }

        protected override void OnStart()
        {
        }

        protected override void OnTick(float factor)
        {
        }

        protected override bool HasActiveContent() => !_stopped;

        protected override void OnDraw()
        {
            if (Caster is not PlayerObject player || player.Hidden)
                return;
            if (!Batch.Begin(_texture, Blendings.OneOneAdditive, DepthStencilState.DepthRead, SamplerState.LinearClamp))
                return;

            for (int i = 0; i < LinkBones.Length; i++)
            {
                float luminosity = (MuGame.Random.Next(30) + 70) * 0.01f;
                var light = new Vector3(luminosity, luminosity * 0.3f, luminosity * 0.2f);
                AddBone(player, LinkBones[i], light);
                AddBone(player, LinkBones[i] - 6, light);
                AddBone(player, LinkBones[i] - 7, light);
            }
            Batch.End();
        }

        private void AddBone(PlayerObject player, int bone, Vector3 light)
        {
            if (player.TryGetBoneWorldMatrix(bone, out Matrix matrix))
                Batch.AddSprite(matrix.Translation, 1.5f, light);
        }
    }
}
