using Microsoft.Xna.Framework;
using System;
using System.Threading.Tasks;
using Client.Main.Controllers;
using Client.Main.Controls;

namespace Client.Main.Objects.Worlds.Noria
{

    public class ChaosMachineObject : NoriaObject
    {
        internal override int ResolveEffectBoneIndex(int sourceBoneIndex)
        {
            int mergedModelBoneIndex = sourceBoneIndex switch
            {
                57 => 107,
                58 => 108,
                61 => 113,
                62 => 114,
                63 => 115,
                64 => 116,
                65 => 117,
                _ => sourceBoneIndex
            };

            string expectedName = sourceBoneIndex switch
            {
                57 => "light01",
                58 => "light07",
                61 => "light06",
                62 => "light04",
                63 => "light03",
                64 => "light05",
                65 => "light02",
                _ => string.Empty
            };

            if (Model?.Bones != null &&
                (uint)mergedModelBoneIndex < (uint)Model.Bones.Length &&
                string.Equals(
                    Model.Bones[mergedModelBoneIndex].Name,
                    expectedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return mergedModelBoneIndex;
            }

            return sourceBoneIndex;
        }

        public override async Task Load()
        {
            Position = new Vector3(Position.X, Position.Y, Position.Z - 40f);
            await base.Load();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (!Visible || World is not WalkableWorldControl walkableWorld || walkableWorld.AudioSuppressed)
                return;

            Vector3 listenerPosition = walkableWorld.Walker.Position;
            SoundController.Instance.PlayBufferWithAttenuation("Sound/nMix.wav", Position, listenerPosition, maxDistance: 1000f, loop: true);
        }
    }
}
