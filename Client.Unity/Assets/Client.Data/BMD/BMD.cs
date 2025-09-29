using UnityEngine;

namespace Client.Data.BMD
{
    public class BMD
    {
        public byte Version { get; set; } = 0x0C;
        public string Name { get; set; } = string.Empty;

        public BMDTextureMesh[] Meshes { get; set; } = new BMDTextureMesh[0];
        public BMDTextureBone[] Bones { get; set; } = new BMDTextureBone[0];
        public BMDTextureAction[] Actions { get; set; } = new BMDTextureAction[0];
    }
}
