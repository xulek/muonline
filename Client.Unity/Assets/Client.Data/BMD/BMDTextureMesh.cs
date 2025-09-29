using UnityEngine;
using System.IO;

namespace Client.Data.BMD
{
    public class BMDTextureMesh
    {
        public BMDTextureVertex[] Vertices { get; set; } = new BMDTextureVertex[0];
        public BMDTextureNormal[] Normals { get; set; } = new BMDTextureNormal[0];
        public BMDTexCoord[] TexCoords { get; set; } = new BMDTexCoord[0];
        public BMDTriangle[] Triangles { get; set; } = new BMDTriangle[0];
        public short Texture { get; set; } = 0;
        public string TexturePath { get; set; } = string.Empty;
    }
}
