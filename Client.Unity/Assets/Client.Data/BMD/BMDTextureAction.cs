using UnityEngine;
using System;

namespace Client.Data.BMD
{
    public class BMDTextureAction
    {
        public int NumAnimationKeys { get; set; }
        public bool LockPositions { get; set; }
        public Vector3[] Positions { get; set; } = new Vector3[0];
        public float PlaySpeed { get; set; } = 1f;
    }
}
