
using System;
using UnityEngine;

namespace Client.Data.Texture
{
    public class TextureData
    {
        public float Width { get; set; }
        public float Height { get; set; }
        public byte Components { get; set; }
        public byte[] Data { get; set; } = new byte[0];

        public static implicit operator Texture2D(TextureData v)
        {
            throw new NotImplementedException();
        }
    }
}
