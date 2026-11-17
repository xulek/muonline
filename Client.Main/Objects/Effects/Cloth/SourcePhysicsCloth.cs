#nullable enable
using Client.Main.Controllers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Client.Main.Objects.Effects.Cloth
{
    /// <summary>CPhysicsCloth type flags (PhysicsManager.h PCT_*).</summary>
    [Flags]
    public enum PhysicsClothType : uint
    {
        Flat = 0x00000000,
        Curved = 0x00000001,
        Sticked = 0x00000002,
        MaskShape = 0x00000003,

        ShapeNormal = 0x00000000,
        ShortShoulder = 0x00000004,
        Cylinder = 0x00000008,
        MaskShapeExt = 0x0000000C,

        ShapeHalloween = 0x00000010,
        MaskShapeExt2 = 0x00000030,

        Cotton = 0x00000000,
        Rubber = 0x00000100,
        Rubber2 = 0x00000200,
        MaskElastic = 0x00000300,

        NormalThickness = 0x00000000,
        Heavy = 0x00000400,
        MaskWeight = 0x00000C00,

        DrawBlit = 0x00000000,
        DrawAlpha = 0x00001000,
        DrawBlend = 0x00002000,
        MaskDraw = 0x00003000,

        ElasticHalloween = 0x00004000,
        ElasticRageL = 0x00008000,
        ElasticRageR = 0x0000C000,
        MaskElasticExt = 0x0000E000,

        OptMeshProg = 0x10000000,
        OptCorrectedForce = 0x20000000,
        MaskLight = 0x40000000,
        OptHair = 0x80000000,
    }

    /// <summary>
    /// Port of SourceMain5.2 CPhysicsCloth (PhysicsManager.cpp): a NumHor x NumVer mass-spring sheet in world space whose
    /// top row is pinned to one bone of the owner. Owner-agnostic: any <see cref="ModelObject"/> with a skeleton.
    ///   Create            PhysicsManager.cpp:324-481   (initial sheet, links, SetFixedVertices with the owner angle matrix)
    ///   SetFixedVertices  499-557                     (top row in bone space, local (x, y, z) -> (z, -y, x))
    ///   Move2 / Move      569-619                     (wind, MoveVertices, PreventFromStretching)
    ///   InitForces        631-639, CPhysicsVertex::UpdateForce 49-107, Move 116-128
    ///   MoveVertices      641-696   PreventFromStretching 698-759   collisions 275-292, 905-935
    ///   global wind       CPhysicsManager::Move 1203-1207, called once per frame (ZzzScene.cpp:2370)
    /// Call <see cref="Move2"/> once per rendered frame with the owner's current skeleton, as RenderCharacter does.
    /// A false result means the cloth tore (strict link > 20x rest length): the caller drops it and creates it again
    /// (DeleteCloth, ZzzCharacter.cpp:2904).
    /// </summary>
    public sealed class SourcePhysicsCloth
    {
        private const float Gravity = 9.8f;           // CPhysicsVertex::s_Gravity
        private const float Mass = 0.0025f;           // s_fMass
        private const float InverseMass = 400f;       // s_fInvOfMass
        private const float RateShortShoulder = 0.6f; // RATE_SHORT_SHOULDER
        private const int WorldLoginScene = 55;       // WD_55LOGINSCENE

        private const byte LinkLooseDistance = 0x01;  // PLS_LOOSEDISTANCE
        private const byte LinkSpring = 0x02;         // PLS_SPRING
        private const byte LinkStrictDistance = 0x04; // PLS_STRICTDISTANCE

        private struct Link
        {
            public int A;
            public int B;
            public float DistanceSmall;
            public float DistanceLarge;
            public byte Style;
        }

        private struct Sphere
        {
            public Vector3 LocalCenter; // m_vCenterBeforeTransform
            public float Radius;
            public int Bone;
            public Vector3 Center;
        }

        // CPhysicsManager::s_fWind: random walk shared by every cloth, stepped once per frame.
        private static float s_globalWind;
        private static double s_globalWindTime = double.NaN;

        private readonly List<Sphere> _spheres = new(4);
        private Vector3[] _position = Array.Empty<Vector3>();
        private Vector3[] _velocity = Array.Empty<Vector3>();
        private Vector3[] _force = Array.Empty<Vector3>();
        private Vector3[] _oneTimeMove = Array.Empty<Vector3>();
        private int[] _oneTimeMoveCount = Array.Empty<int>();
        private bool[] _fixed = Array.Empty<bool>();
        private Link[] _links = Array.Empty<Link>();
        private int _linkCount;

        private int _bone;
        private PhysicsClothType _type;
        private Vector3 _offset;
        private float _width = 1f;
        private float _height = 1f;
        private float _unitWidth = 1f;
        private float _unitHeight = 1f;
        private float _wind;
        private Vector3 _windVector;
        private byte _windMax = 1;
        private byte _windMin = 1;

        public bool IsCreated { get; private set; }
        public int HorizontalCount { get; private set; }
        public int VerticalCount { get; private set; }
        public int VertexCount => HorizontalCount * VerticalCount;
        public PhysicsClothType Type => _type;
        public ReadOnlySpan<Vector3> Positions => _position;

        /// <summary>SetWindMinMax (PhysicsManager.h): only used by PCT_RUBBER2 cloths.</summary>
        public void SetWindMinMax(byte min, byte max)
        {
            _windMin = min;
            _windMax = max;
        }

        /// <summary>AddCollisionSphere(x, y, z, radius, bone), PhysicsManager.cpp:905. Centre in bone space, same axes as the original.</summary>
        public void AddCollisionSphere(float x, float y, float z, float radius, int bone)
        {
            _spheres.Add(new Sphere { LocalCenter = new Vector3(x, y, z), Radius = radius, Bone = bone });
        }

        public void ClearCollisionSpheres() => _spheres.Clear();

        public void Destroy()
        {
            IsCreated = false;
            _linkCount = 0;
        }

        /// <summary>CPhysicsCloth::Create (PhysicsManager.cpp:324-481).</summary>
        public bool Create(ModelObject owner, int bone, float xPos, float yPos, float zPos, int numHor, int numVer,
            float width, float height, PhysicsClothType type)
        {
            IsCreated = false;
            if (numHor < 2 || numVer < 2 || owner.GetBoneTransforms() is not { } bones || (uint)bone >= (uint)bones.Length)
                return false;

            _bone = bone;
            _type = type;
            _offset = new Vector3(xPos, yPos, zPos);
            _width = width;
            _height = height;
            HorizontalCount = numHor;
            VerticalCount = numVer;

            int count = numHor * numVer;
            if (_position.Length != count)
            {
                _position = new Vector3[count];
                _velocity = new Vector3[count];
                _force = new Vector3[count];
                _oneTimeMove = new Vector3[count];
                _oneTimeMoveCount = new int[count];
                _fixed = new bool[count];
            }
            else
            {
                Array.Clear(_velocity);
                Array.Clear(_force);
                Array.Clear(_oneTimeMove);
                Array.Clear(_oneTimeMoveCount);
                Array.Clear(_fixed);
            }

            int maxLinks = 2 * ((numHor - 1) * numVer + numHor * (numVer - 1));
            if (_links.Length < maxLinks)
                _links = new Link[maxLinks];

            _unitWidth = width / (numHor - 1);
            _unitHeight = height / (numVer - 1);

            // AngleMatrix(o->Angle) with the translation of BoneTransform[bone]: rotation of the object, not of the bone.
            Matrix createMatrix = Matrix.CreateTranslation(bones[bone].Translation) * owner.WorldPosition;

            bool cylinder = false;
            for (int j = 0; j < numVer; ++j)
            {
                float rowWidth = width;
                float unitWidth = _unitWidth;
                float unitHeight = _unitHeight;
                switch (_type & PhysicsClothType.MaskShapeExt)
                {
                    case PhysicsClothType.ShortShoulder:
                        rowWidth *= RateShortShoulder + (1f - RateShortShoulder) * j / (numVer - 1);
                        unitWidth = rowWidth / (numHor - 1);
                        break;
                    case PhysicsClothType.Cylinder:
                        cylinder = true;
                        break;
                }

                for (int i = 0; i < numHor; ++i)
                {
                    Vector3 p;
                    if (cylinder)
                    {
                        float posY = MathF.Sin(i / (float)(numHor - 1) * MathF.PI) * 100f;
                        p = new Vector3(unitWidth * i, posY - 30f, unitHeight * j);
                    }
                    else
                    {
                        p = new Vector3(unitWidth * i - 0.5f * rowWidth, 20f, -unitHeight * j);
                    }

                    switch (_type & PhysicsClothType.MaskShape)
                    {
                        case PhysicsClothType.Curved:
                            float move = 2f * MathF.Abs(i / (float)(numHor - 1) - 0.5f);
                            p.Y -= 10f * move * move;
                            break;
                        case PhysicsClothType.Sticked:
                            p.Y = 0f;
                            break;
                    }

                    _position[numHor * j + i] = Vector3.Transform(p, createMatrix);
                }
            }

            byte verLinkStyle = 0;
            byte crossLinkStyle = 0;
            if ((_type & PhysicsClothType.MaskElastic) != PhysicsClothType.Rubber)
            {
                verLinkStyle |= LinkStrictDistance;
                crossLinkStyle |= LinkLooseDistance;
            }

            int link = 0;
            for (int j = 0; j < numVer; ++j)
            {
                for (int i = 0; i < numHor; ++i)
                {
                    int v = numHor * j + i;
                    if (j < numVer - 1)
                    {
                        float d = Vector3.Distance(_position[v], _position[v + numHor]);
                        SetLink(link++, v, v + numHor, d * 0.8f, d, (byte)(LinkSpring | verLinkStyle));
                    }
                    if (i < numHor - 1)
                    {
                        float d = Vector3.Distance(_position[v], _position[v + 1]);
                        SetLink(link++, v, v + 1, d * 0.8f, d, LinkSpring | LinkLooseDistance);

                        if (j < numVer - 1)
                        {
                            float dc = Vector3.Distance(_position[v], _position[v + 1 + numHor]);
                            SetLink(link++, v, v + 1 + numHor, dc * 0.8f, dc, crossLinkStyle);
                        }
                        if (j > 1)
                        {
                            float dc = Vector3.Distance(_position[v], _position[v + 1 - numHor]);
                            SetLink(link++, v, v + 1 - numHor, dc * 0.8f, dc, crossLinkStyle);
                        }
                    }
                }
            }
            _linkCount = link;

            SetFixedVertices(createMatrix);
            IsCreated = true;
            return true;
        }

        private void SetLink(int index, int a, int b, float small, float large, byte style)
        {
            _links[index] = new Link { A = a, B = b, DistanceSmall = small, DistanceLarge = large, Style = style };
        }

        // CPhysicsCloth::SetFixedVertices (PhysicsManager.cpp:499-557). matrix = bone (or create) matrix * owner world.
        private void SetFixedVertices(Matrix matrix)
        {
            int numHor = HorizontalCount;
            bool cylinder = false;
            for (int i = 0; i < numHor; ++i)
            {
                float rowWidth = _width;
                float unitWidth = _unitWidth;
                switch (_type & PhysicsClothType.MaskShapeExt)
                {
                    case PhysicsClothType.ShortShoulder:
                        rowWidth *= RateShortShoulder;
                        unitWidth = rowWidth / (numHor - 1);
                        break;
                    case PhysicsClothType.Cylinder:
                        cylinder = true;
                        break;
                }

                var p = new Vector3(unitWidth * i - 0.5f * rowWidth, 10f, 0f);
                p.X += _offset.X;
                if (cylinder)
                {
                    float posY = MathF.Sin(i / (float)(numHor - 1) * MathF.PI) * 130f;
                    p.Y = posY - 110f;
                }
                else
                {
                    p.Y = _offset.Y;
                }
                p.Z = _offset.Z;

                if ((_type & PhysicsClothType.MaskShapeExt2) == PhysicsClothType.ShapeHalloween)
                {
                    p.X += 5f;
                    p.Y += 10f;
                }

                if ((_type & PhysicsClothType.MaskShape) == PhysicsClothType.Curved)
                {
                    float move = 2f * MathF.Abs(i / (float)(numHor - 1) - 0.5f);
                    p.Y -= 10f * move * move;
                }

                _position[i] = Vector3.Transform(new Vector3(p.Z, -p.Y, p.X), matrix);
                _fixed[i] = true;
            }
        }

        // CPhysicsManager::Move (PhysicsManager.cpp:1203-1207): once per frame for all cloths.
        private static void StepGlobalWind()
        {
            double now = FPSCounter.Instance.WorldTime;
            if (now == s_globalWindTime)
                return;
            s_globalWindTime = now;

            float plus = (MuGame.Random.Next(200) - 100) * 0.001f;
            s_globalWind += plus * FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            s_globalWind = Math.Clamp(s_globalWind, -0.2f, 1f);
        }

        /// <summary>CPhysicsCloth::Move2(fTime, iCount): RenderCharacter passes (0.005, 5), fTime 0 while stunned / asleep.</summary>
        public bool Move2(ModelObject owner, float time, int count)
        {
            if (!IsCreated)
                return false;

            StepGlobalWind();
            for (int i = 0; i < count; ++i)
            {
                if (!Move(owner, time))
                    return false;
            }
            return true;
        }

        // CPhysicsCloth::Move (PhysicsManager.cpp:582-619).
        private bool Move(ModelObject owner, float time)
        {
            if (owner.GetBoneTransforms() is not { } bones || (uint)_bone >= (uint)bones.Length)
                return false;

            if ((_type & PhysicsClothType.MaskElastic) == PhysicsClothType.Rubber2)
                _wind = (MuGame.Random.Next(Math.Max(1, (int)_windMax)) + _windMin) / 100f;
            else if (owner.World?.WorldIndex == WorldLoginScene)
                _wind = (MuGame.Random.Next(40) + 10) / 50f;
            else
                _wind = s_globalWind;

            if ((_type & PhysicsClothType.MaskShapeExt) == PhysicsClothType.Cylinder)
                _wind = (MuGame.Random.Next(10) + 25) / 50f;

            float factor = FPSCounter.Instance.FPS_ANIMATION_FACTOR;
            _wind *= factor;

            float yawRadians = (180f + MathHelper.ToDegrees(owner.Angle.Z)) * MathF.PI / 180f;
            _windVector = new Vector3(_wind * MathF.Sin(yawRadians), -_wind * MathF.Cos(yawRadians), 0f);

            MoveVertices(owner, bones, time, factor);
            return PreventFromStretching(owner, bones);
        }

        // CPhysicsCloth::InitForces + CPhysicsVertex::UpdateForce (PhysicsManager.cpp:631-639, 49-107).
        private void InitForces(float factor)
        {
            int numHor = HorizontalCount;
            int count = VertexCount;
            double worldTime = FPSCounter.Instance.WorldTime;
            int seed = (int)((long)(worldTime / 400.0) * 101 % count);
            float sinTime = MathF.Sin((float)(worldTime * 0.003));

            for (int v = 0; v < count; ++v)
            {
                if (_fixed[v])
                {
                    _force[v] = Vector3.Zero;
                    continue;
                }

                int key = Math.Abs(seed % numHor - v % numHor) + Math.Abs(seed / numHor - v / numHor);
                // min(max(0, 5 - iKey), 4) with unsigned iKey and the Windows min/max macros: 5 - iKey wraps for
                // iKey > 5, so those vertices get 4 as well; only iKey == 5 gives 0.
                int temp = key > 5 ? 4 : Math.Min(5 - key, 4);
                float rand = temp == 0 ? 0f : temp + 2;

                Vector3 f = rand * _windVector - _velocity[v] * 0.01f;

                switch (_type & PhysicsClothType.MaskElastic)
                {
                    case PhysicsClothType.Rubber:
                        f.Z += rand * (_wind + 0.1f) * 1f * factor;
                        break;
                    case PhysicsClothType.Rubber2:
                        f.Z += rand * _wind * factor;
                        break;
                }

                switch (_type & PhysicsClothType.MaskElasticExt)
                {
                    case PhysicsClothType.ElasticHalloween:
                        f.X += -(rand * _wind * 0.5f * factor);
                        f.Z += rand * (_wind + 0.1f) * 0.5f * sinTime * 5f * factor;
                        f.Z -= Gravity * Mass * 50f * factor;
                        break;
                    case PhysicsClothType.ElasticRageL:
                        f.X += -(rand * _wind * 0.8f * factor);
                        break;
                    case PhysicsClothType.ElasticRageR:
                        f.X -= -(rand * _wind * 0.8f * factor);
                        break;
                }

                f.Z -= (_type & PhysicsClothType.MaskWeight) == PhysicsClothType.Heavy
                    ? Gravity * Mass * 180f * factor
                    : Gravity * Mass * 100f * factor;

                _force[v] = f;
            }
        }

        // CPhysicsCloth::MoveVertices (PhysicsManager.cpp:641-696).
        private void MoveVertices(ModelObject owner, Matrix[] bones, float time, float factor)
        {
            InitForces(factor);

            float scale = factor;
            if ((_type & PhysicsClothType.MaskElastic) == PhysicsClothType.Rubber)
                scale *= 3f;
            if ((_type & PhysicsClothType.MaskElasticExt) == PhysicsClothType.ElasticHalloween)
                scale *= 2f;
            bool corrected = (_type & PhysicsClothType.OptCorrectedForce) != 0;

            for (int l = 0; l < _linkCount; ++l)
            {
                ref Link link = ref _links[l];
                if ((link.Style & LinkSpring) == 0)
                    continue;

                Vector3 distance = _position[link.A] - _position[link.B];
                float length = MathF.Max(0.001f, distance.Length());
                if (length <= link.DistanceLarge + 0.01f)
                    continue;

                Vector3 force = (length - link.DistanceLarge) * distance / length * scale;
                if (corrected)
                    force *= link.DistanceLarge / 32f;

                _force[link.A] -= force;
                _force[link.B] += force;
            }

            SetFixedVertices(bones[_bone] * owner.WorldPosition);

            int count = VertexCount;
            for (int v = 0; v < count; ++v)
            {
                if (_fixed[v])
                    continue;
                _velocity[v] += _force[v] * InverseMass * time;
                _position[v] += _velocity[v] * time;
            }
        }

        // CPhysicsCloth::PreventFromStretching + ProcessCollision (PhysicsManager.cpp:698-759, 914-935).
        private bool PreventFromStretching(ModelObject owner, Matrix[] bones)
        {
            int count = VertexCount;
            Matrix world = owner.WorldPosition;
            for (int s = 0; s < _spheres.Count; ++s)
            {
                Sphere sphere = _spheres[s];
                if ((uint)sphere.Bone >= (uint)bones.Length)
                    continue;
                Vector3 local = new(sphere.LocalCenter.Z, -sphere.LocalCenter.Y, sphere.LocalCenter.X);
                sphere.Center = Vector3.Transform(local, bones[sphere.Bone] * world);
                _spheres[s] = sphere;
            }

            if (_spheres.Count > 0)
            {
                for (int s = 0; s < _spheres.Count; ++s)
                {
                    Sphere sphere = _spheres[s];
                    for (int v = 0; v < count; ++v)
                    {
                        // CPhysicsColSphere::ProcessCollision (275-292)
                        Vector3 d = _position[v] - sphere.Center;
                        float length = d.Length();
                        if (length < 0.01f)
                        {
                            length = 0.01f;
                            d = new Vector3(length, 0f, 0f);
                        }
                        if (length < sphere.Radius)
                            AddOneTimeMove(v, d * ((sphere.Radius - length) / length));
                    }
                }

                for (int v = 0; v < count; ++v)
                    DoOneTimeMove(v);
            }

            for (int l = 0; l < _linkCount; ++l)
            {
                ref Link link = ref _links[l];
                if ((link.Style & LinkLooseDistance) == 0)
                    continue;

                // CPhysicsVertex::AddOneTimeMoveToKeepLength (177-186)
                Vector3 distance = _position[link.A] - _position[link.B];
                float length = MathF.Max(0.001f, distance.Length());
                distance *= (length - link.DistanceLarge) * 0.5f / length;
                _oneTimeMove[link.A] -= distance;
                _oneTimeMove[link.B] += distance;
                _oneTimeMoveCount[link.A]++;
                _oneTimeMoveCount[link.B]++;
            }
            for (int v = 0; v < count; ++v)
                DoOneTimeMove(v);

            int numHor = HorizontalCount;
            for (int l = 0; l < _linkCount; ++l)
            {
                ref Link link = ref _links[l];
                if (link.B < numHor || (link.Style & LinkStrictDistance) == 0)
                    continue;
                if (!KeepLength(link.B, link.A, link.DistanceSmall, link.DistanceLarge))
                    return false;
            }

            return true;
        }

        private void AddOneTimeMove(int v, Vector3 move)
        {
            _oneTimeMove[v] += move;
            _oneTimeMoveCount[v]++;
        }

        // CPhysicsVertex::DoOneTimeMove (188-206)
        private void DoOneTimeMove(int v)
        {
            if (_fixed[v])
            {
                _oneTimeMove[v] = Vector3.Zero;
                _oneTimeMoveCount[v] = 0;
                return;
            }

            if (_oneTimeMoveCount[v] > 0)
            {
                _position[v] += _oneTimeMove[v] / _oneTimeMoveCount[v];
                _oneTimeMove[v] = Vector3.Zero;
                _oneTimeMoveCount[v] = 0;
            }
        }

        // CPhysicsVertex::KeepLength (148-175): moves vertex v towards / away from other.
        private bool KeepLength(int v, int other, float small, float large)
        {
            if (_fixed[v])
                return true;

            Vector3 distance = _position[v] - _position[other];
            float length = MathF.Max(0.001f, distance.Length());
            if (length > large * 20f)
                return false;

            if (length > large)
                _position[v] -= distance * ((length - large) / length);
            else if (length < small)
                _position[v] -= distance * ((length - small) / length);

            return true;
        }
    }
}
