using System.Drawing;
//using System.Numerics;
using UnityEngine;

namespace Client.Data.OZB
{
    public class OZB
    {
        public byte Version { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public System.Drawing.Color[] Data { get; set; } = new System.Drawing.Color[0];
    }
}
