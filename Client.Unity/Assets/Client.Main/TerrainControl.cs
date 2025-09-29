using System;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Client.Data.ATT;
using Client.Data.MAP;
using Client.Data.OBJS;
using Client.Data.OZB;
using Client.Main.Content;
using Client.Main;
using static Client.Main.Utils;
using UnityEngine;
using UnityEngine.Networking;
using JetBrains.Annotations;
using TMPro;
using UnityEngine.Rendering;
using System.Threading;
using UnityEngine.Rendering.Universal;
using static UnityEditor.Searcher.SearcherWindow.Alignment;
using Org.BouncyCastle.Security;
using UnityEditor;


public class TerrainControl : MonoBehaviour
{
    //private Camera cam;
    //private CameraController camcon;

    private const float SpecialHeight = 1200f;
    private const int BlockSize = 4;
    private const int MAX_LOD_LEVELS = 20;
    private const float LOD_DISTANCE_MULTIPLIER = 3000f;
    private const float WindScale = 10f;
    private const int UPDATE_INTERVAL_MS = 32;
    private const float CAMERA_MOVE_THRESHOLD = 32f;

    private TerrainAttribute _terrain;
    private TerrainMapping _mapping;
    private Texture2D[] _textures;
    private float[] _terrainGrassWind;
    private Color[] _backTerrainLight;
    private Vector3[] _terrainNormal;
    private Color[] _backTerrainHeight;
    private Color[] _terrainLightData;

    public short WorldIndex;
    public short worldIndex{ get => WorldIndex; set => WorldIndex = value; }


    public Vector3 Light { get; set; } = new Vector3(0.5f, -0.5f, 0.5f);
    public Dictionary<int, string> TextureMappingFiles { get; set; } = new Dictionary<int, string>
        {
            { 0, "TileGrass01.ozj" },
            { 1, "TileGrass02.ozj" },
            { 2, "TileGround01.ozj" },
            { 3, "TileGround02.ozj" },
            { 4, "TileGround03.ozj" },
            { 5, "TileWater01.ozj" },
            { 6, "TileWood01.ozj" },
            { 7, "TileRock01.ozj" },
            { 8, "TileRock02.ozj" },
            { 9, "TileRock03.ozj" },
            { 10, "TileRock04.ozj" },
            { 11, "TileRock05.ozj" },
            { 12, "TileRock06.ozj" },
            { 13, "TileRock07.ozj" },
            { 30, "TileGrass01.ozt" },
            { 31, "TileGrass02.ozt" },
            { 32, "TileGrass03.ozt" },
            { 100, "leaf01.ozt" },
            { 101, "leaf02.ozj" },
            { 102, "rain01.ozt" },
            { 103,  "rain02.ozt" },
            { 104,  "rain03.ozt" }
        };

    
    private readonly Vector2[] _terrainTextureCoord;
    private readonly Vector3[] _tempTerrainVertex;
    private readonly Color[] _tempTerrainLights;

    private double _lastUpdateTime;
    private readonly int[] LOD_STEPS = { 1, 4 };
    private Vector2 _lastCameraPosition;

    private readonly TerrainBlockCache _blockCache;
    private readonly Queue<TerrainBlock> _visibleBlocks = new Queue<TerrainBlock>(64);

    private HashSet<(int, int)> _renderedBlocks = new HashSet<(int, int)>();
    private byte[] _mappingAlpha;
    private Color32[] colors;

    GameObject tileContainer;

    public TerrainControl()
    {
        _blockCache = new TerrainBlockCache(BlockSize, Constants.TERRAIN_SIZE);
        _terrainTextureCoord = new Vector2[4];
        _tempTerrainVertex = new Vector3[4];
        _tempTerrainLights = new Color[4];
    }

    public IEnumerator Load()
    {
        Debug.Log("Load Starts...");

        //Camera.main.aspect = 1.4f;

        var terrainReader = new ATTReader();
        var ozbReader = new OZBReader();
        var objReader = new OBJReader();
        var mappingReader = new MapReader();

        var worldFolder = $"World{WorldIndex}";

        var fullPathWorldFolder = Path.Combine(Constants.DataPath, worldFolder);

        if (!Directory.Exists(fullPathWorldFolder))
        {
            Debug.Log("Error on FullPathWorldFolder.");
            yield break;
        }
        else
        {
            Debug.Log("FullPathWorldFolder path ok.");
        }

        var coroutines = new List<IEnumerator>();

        coroutines.Add(LoadTerrain(terrainReader, Path.Combine(fullPathWorldFolder, $"EncTerrain{WorldIndex}.att")));
        coroutines.Add(LoadTerrainHeight(ozbReader, Path.Combine(fullPathWorldFolder, $"TerrainHeight.OZB")));
        coroutines.Add(LoadMapping(mappingReader, Path.Combine(fullPathWorldFolder, $"EncTerrain{WorldIndex}.map")));

        var textureMapFiles = new string[256];

        foreach (var kvp in TextureMappingFiles)
        {
            textureMapFiles[kvp.Key] = Path.Combine(fullPathWorldFolder, kvp.Value);
        }

        for (int i = 1; i <= 36; i++)
        {
            textureMapFiles[13 + i] = Path.Combine(fullPathWorldFolder, $"ExtTile{i:00}.ozj");
        }

        _textures = new Texture2D[textureMapFiles.Length];

        for (int t = 0; t < textureMapFiles.Length; t++)
        {
            var path = textureMapFiles[t];
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

            coroutines.Add(LoadTexture(path, t));
        }

        var textureLightPath = Path.Combine(fullPathWorldFolder, "TerrainLight.OZB");

        if (File.Exists(textureLightPath))
        {
            coroutines.Add(LoadTerrainLight(ozbReader, textureLightPath));
        }
        else
        {
            _terrainLightData = Enumerable.Repeat(Color.white, Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE).ToArray();
        }

        foreach (var coroutine in coroutines)
        {
            yield return StartCoroutine(coroutine);
        }

        _renderedBlocks.Clear();

        Debug.Log("✔ All terrain data loaded!");

        if (_backTerrainHeight != null && _backTerrainHeight.Length > 0)
        {
            CreateTerrainNormal();
        }
        else
        {
            Debug.LogError("❌ _backTerrainHeight is null or empty!");
        }

        if (_terrainLightData != null && _terrainLightData.Length > 0)
        {
            CreateTerrainLight();
        }
        else
        {
            Debug.LogError("❌ _terrainLightData is null or empty!");
        }

        OnLoadTerrainLightLoaded += () =>
        {
            // Force terrain render on load completion
            _lastCameraPosition = new Vector2(float.MinValue, float.MinValue);
            RenderTerrain();
        };

        Debug.Log("Load Complete!");
    }


    public event Action OnLoadTerrainLoaded;
    public event Action OnLoadTerrainHeightLoaded;
    public event Action OnLoadMappingLoaded;
    public event Action OnLoadTextureLoaded;
    public event Action OnLoadTerrainLightLoaded;

    private IEnumerator LoadTerrain(ATTReader reader, string path)
    {
        var task = reader.Load(path);
        while (!task.IsCompleted) yield return null;
        _terrain = task.Result;

        OnLoadTerrainLoaded?.Invoke();
    }

    private IEnumerator LoadTerrainHeight(OZBReader reader, string path)
    {
        var task = reader.Load(path);
        while (!task.IsCompleted) yield return null;
        _backTerrainHeight = task.Result.Data.Select(x => new Color(x.R, x.G, x.B)).ToArray();

        OnLoadTerrainHeightLoaded?.Invoke();
    }

    private IEnumerator LoadMapping(MapReader reader, string path)
    {
        var task = reader.Load(path);
        while (!task.IsCompleted) yield return null;
        _mapping = task.Result;

        OnLoadMappingLoaded?.Invoke();
    }

    private IEnumerator LoadTexture(string path, int index)
    {
        var task = TextureLoader.Instance.Prepare(path);
        while (!task.IsCompleted) yield return null;
        _textures[index] = TextureLoader.Instance.GetTexture2D(path);

        OnLoadTextureLoaded?.Invoke();
    }

    private IEnumerator LoadTerrainLight(OZBReader reader, string path)
    {
        var task = reader.Load(path);
        while (!task.IsCompleted) yield return null;
        _terrainLightData = task.Result.Data.Select(x => new Color(x.R, x.G, x.B)).ToArray();

        OnLoadTerrainLightLoaded?.Invoke();
    }

    private IEnumerator WaitForTerrainLight(int idx1, int idx2, int idx3, int idx4)
    {
        while (_backTerrainLight == null || _backTerrainLight.Length == 0)
        {
            yield return null; // Wait one frame
        }

        Debug.Log("✅ _backTerrainLight is ready. Calling PrepareTileLights again.");
        PrepareTileLights(idx1, idx2, idx3, idx4);
    }

    private void CreateTerrainNormal()
    {
        Debug.Log("CreateTerrainNormal");
        _terrainNormal = new Vector3[Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE];

        for (int z = 0; z < Constants.TERRAIN_SIZE; z++)
        {
            for (int x = 0; x < Constants.TERRAIN_SIZE; x++)
            {
                int index = GetTerrainIndex(x, z);

                Vector3 v1 = new Vector3((x + 1) * Constants.TERRAIN_SCALE, _backTerrainHeight[GetTerrainIndexRepeat(x + 1, z)].b, z * Constants.TERRAIN_SCALE);
                Vector3 v2 = new Vector3((x + 1) * Constants.TERRAIN_SCALE, _backTerrainHeight[GetTerrainIndexRepeat(x + 1, z + 1)].b, (z + 1) * Constants.TERRAIN_SCALE);
                Vector3 v3 = new Vector3(x * Constants.TERRAIN_SCALE, _backTerrainHeight[GetTerrainIndexRepeat(x, z + 1)].b, (z + 1) * Constants.TERRAIN_SCALE);
                Vector3 v4 = new Vector3(x * Constants.TERRAIN_SCALE, _backTerrainHeight[GetTerrainIndexRepeat(x, z)].b, z * Constants.TERRAIN_SCALE);

                Vector3 faceNormal1 = Vector3.Cross(v2 - v1, v3 - v1).normalized;
                Vector3 faceNormal2 = Vector3.Cross(v4 - v3, v1 - v3).normalized;

                _terrainNormal[index] += faceNormal1 + faceNormal2;
            }
        }

        for (int i = 0; i < _terrainNormal.Length; i++)
        {
            _terrainNormal[i].Normalize();
        }
        Debug.Log("✅ CreateTerrainNormal has been initialized!");
    }

    private void CreateTerrainLight()
    {
        Debug.Log("CreateTerrainLight");
        _backTerrainLight = new Color[Constants.TERRAIN_SIZE * Constants.TERRAIN_SIZE];

        for (int z = 0; z < Constants.TERRAIN_SIZE; z++)
        {
            for (int x = 0; x < Constants.TERRAIN_SIZE; x++)
            {
                int index = GetTerrainIndex(x, z);
                float luminosity = Mathf.Clamp(Vector3.Dot(_terrainNormal[index], Light) + 0.5f, 0f, 1f);
                _backTerrainLight[index] = _terrainLightData[index] * luminosity;
            }
        }
        Debug.Log("✅ CreateTerrainLight has been initialized!");

    }

    private void Start()
    {
        StartCoroutine(Load());

        OnLoadTerrainLightLoaded += () =>
        {

        };
    }

    private void Awake()
    {
        tileContainer = new GameObject("TerrainTiles");
    }

    private void Update()
    {
        if (_backTerrainLight != null && _backTerrainLight.Length > 0)
        RenderTerrain();
    }

    private void RenderTerrain()
    {
        if (_backTerrainHeight == null)
            return;

        var cameraPosition = new Vector3(Camera.main.transform.position.x, 0f, Camera.main.transform.position.z);
        UpdateVisibleBlocks(cameraPosition);

        foreach (var block in _visibleBlocks)
        {
            if (block.IsVisible && !AlreadyRendered(block))
            {
                float xStart = block.Xi * Constants.TERRAIN_SCALE;
                RenderTerrainBlock(
                    xStart / Constants.TERRAIN_SCALE,
                    block.Zi * Constants.TERRAIN_SCALE / Constants.TERRAIN_SCALE,
                    block.Xi,
                    block.Zi,
                    LOD_STEPS[block.LODLevel]);
            }
        }
    }

    private class TerrainBlock
    {
        public Bounds Bounds;
        public float MinY;
        public float MaxY;
        public int LODLevel;
        public Vector3 Center;
        public bool IsVisible;
        public int Xi;
        public int Zi;
    }

    private class TerrainBlockCache
    {
        private readonly TerrainBlock[,] _blocks;
        private readonly int _blockSize;
        private readonly int _gridSize;
        public TerrainBlockCache(int blockSize, int terrainSize)
        {
            _blockSize = blockSize;
            _gridSize = terrainSize / blockSize;
            _blocks = new TerrainBlock[_gridSize, _gridSize];

            for (int z = 0; z < _gridSize; z++)
            {
                for (int x = 0; x < _gridSize; x++)
                {
                    _blocks[z, x] = new TerrainBlock
                    {
                        Xi = x * blockSize,
                        Zi = z * blockSize
                    };
                }
            }
        }
        public TerrainBlock GetBlock(int x, int z) => _blocks[z, x];

        public void PrintBlocks()
        {
            for (int z = 0; z < _gridSize; z++)
            {
                for (int x = 0; x < _gridSize; x++)
                {
                    TerrainBlock block = _blocks[z, x];
                    Debug.Log($"Block ({x}, {z}): " +
                              $"Center = {block.Center}, " +
                              $"Bounds = {block.Bounds}, " +
                              $"MinZ = {block.MinY}, " +
                              $"MaxZ = {block.MaxY}, " +
                              $"LOD = {block.LODLevel}, " +
                              $"IsVisible = {block.IsVisible}, " +
                              $"Xi = {block.Xi}, " +
                              $"Yi = {block.Zi}"
                              );
                }
            }
        }
    }

    private void UpdateVisibleBlocks(Vector3 cameraPosition)
    {
        if (Vector3.Distance(_lastCameraPosition, cameraPosition) < CAMERA_MOVE_THRESHOLD)
            return;

        _lastCameraPosition = cameraPosition;
        _visibleBlocks.Clear();

        float renderDistance = Camera.main.farClipPlane * 1.5f;
        const int EXTRA_BLOCKS_MARGIN = 2;

        int startX = Mathf.Max(0, Mathf.FloorToInt((cameraPosition.x - renderDistance) / (Constants.TERRAIN_SCALE * BlockSize)) - EXTRA_BLOCKS_MARGIN);
        int startY = Mathf.Max(0, Mathf.FloorToInt((cameraPosition.z - renderDistance) / (Constants.TERRAIN_SCALE * BlockSize)) - EXTRA_BLOCKS_MARGIN);
        int endX = Mathf.Min(Constants.TERRAIN_SIZE / BlockSize - 1, Mathf.FloorToInt((cameraPosition.x + renderDistance) / (Constants.TERRAIN_SCALE * BlockSize)) + EXTRA_BLOCKS_MARGIN);
        int endY = Mathf.Min(Constants.TERRAIN_SIZE / BlockSize - 1, Mathf.FloorToInt((cameraPosition.z + renderDistance) / (Constants.TERRAIN_SCALE * BlockSize)) + EXTRA_BLOCKS_MARGIN);

        int vectorSize = System.Numerics.Vector<float>.Count;
        float[] heightBuffer = new float[BlockSize * BlockSize];
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);

        for (int gridY = startY; gridY <= endY; gridY++)
        {
            for (int gridX = startX; gridX <= endX; gridX++)
            {
                var block = _blockCache.GetBlock(gridX, gridY);

                float xStart = block.Xi * Constants.TERRAIN_SCALE;
                float yStart = block.Zi * Constants.TERRAIN_SCALE;
                float xEnd = (block.Xi + BlockSize) * Constants.TERRAIN_SCALE;
                float yEnd = (block.Zi + BlockSize) * Constants.TERRAIN_SCALE;

                block.Center = new Vector3((xStart + xEnd) * 0.5f, 0, (yStart + yEnd) * 0.5f);
                float distanceToCamera = Vector3.Distance(block.Center, cameraPosition);

                //block.LODLevel = GetLODLevel(distanceToCamera);                               // Fix some tiles not rendered after camera zoom out
                int lodStep = LOD_STEPS[Mathf.Clamp(block.LODLevel, 0, LOD_STEPS.Length - 1)];  //

                if (distanceToCamera <= renderDistance * 1.2f)
                {
                    int idx = 0;
                    for (int y = 0; y < BlockSize; y++)
                    {
                        for (int x = 0; x < BlockSize; x++)
                        {
                            int terrainIndex = GetTerrainIndexRepeat(block.Xi + x, block.Zi + y);
                            heightBuffer[idx++] = _backTerrainHeight[terrainIndex].b * 1.5f;
                        }
                    }

                    var minVector = new System.Numerics.Vector<float>(float.MaxValue);
                    var maxVector = new System.Numerics.Vector<float>(float.MinValue);

                    for (int i = 0; i < heightBuffer.Length; i += vectorSize)
                    {
                        var heightVector = new System.Numerics.Vector<float>(heightBuffer, i);
                        minVector = System.Numerics.Vector.Min(minVector, heightVector);
                        maxVector = System.Numerics.Vector.Max(maxVector, heightVector);
                    }

                    block.MinY = minVector[0];
                    block.MaxY = maxVector[0];
                    for (int i = 1; i < vectorSize; i++)
                    {
                        block.MinY = Mathf.Min(block.MinY, minVector[i]);
                        block.MaxY = Mathf.Max(block.MaxY, maxVector[i]);
                    }
                    
                    block.Bounds = new Bounds(
                        new Vector3((xStart + xEnd) * 0.5f, (block.MinY + block.MaxY) * 0.5f, (yStart + yEnd) * 0.5f),
                        new Vector3(xEnd - xStart, block.MaxY - block.MinY, yEnd - yStart)
                    );

                    block.IsVisible = GeometryUtility.TestPlanesAABB(frustumPlanes, block.Bounds);
                    if (block.IsVisible)
                    {
                        _visibleBlocks.Enqueue(block);
                    }
                }
                else
                {
                    block.IsVisible = false;
                }
            }
        }
    }


    private bool AlreadyRendered(TerrainBlock block)
    {
        var key = (block.Xi, block.Zi);
        if (_renderedBlocks.Contains(key))
        return true;

        _renderedBlocks.Add(key);
        return false;
    }

    private void RenderTerrainBlock(float xf, float zf, int xi, int zi, int lodStep)
    {
        //Debug.Log("lodStep before calc:" + lodStep);
        if(BlockSize % lodStep != 0)
        {
        Debug.Log("BlockSize % lodStep after calc:" + BlockSize % lodStep);
        lodStep = 1;
        Debug.Log("lodStep after calc:" + lodStep);
        }

        for (int i = 0; i < BlockSize; i += lodStep)
        {
            for (int j = 0; j < BlockSize; j += lodStep)
            {
                RenderTerrainTile(xf + j, zf + i, xi + j, zi + i, (float)lodStep, lodStep);
            }
        }
    }

    private void RenderTerrainTile(float xf, float zf, int xi, int zi, float lodf, int lodi)
    {
        int idx1 = GetTerrainIndex(xi, zi);

        if (_terrain.TerrainWall[idx1].HasFlag(TWFlags.NoGround))
            return;

        int idx2 = GetTerrainIndex(xi + lodi, zi);
        int idx3 = GetTerrainIndex(xi + lodi, zi + lodi);
        int idx4 = GetTerrainIndex(xi, zi + lodi);

        PrepareTileVertices(xi, zi, xf, zf, idx1, idx2, idx3, idx4, lodf);
        PrepareTileLights(idx1, idx2, idx3, idx4);

        float lodScale = lodf;


        //if (_mapping.Alpha == null || _mapping.Alpha.Length == 0)
        //{
        //    Debug.LogWarning("⚠️ _mapping is not ready. Retrying next frame...");
        //    StartCoroutine(Load());
        //    return;
        //}

        byte alpha1 = idx1 >= _mapping.Alpha.Length ? (byte)0 : _mapping.Alpha[idx1];
        byte alpha2 = idx2 >= _mapping.Alpha.Length ? (byte)0 : _mapping.Alpha[idx2];
        byte alpha3 = idx3 >= _mapping.Alpha.Length ? (byte)0 : _mapping.Alpha[idx3];
        byte alpha4 = idx4 >= _mapping.Alpha.Length ? (byte)0 : _mapping.Alpha[idx4];

        //Debug.Log("alpha1: " + alpha1);
        //Debug.Log("alpha2: " + alpha2);
        //Debug.Log("alpha3: " + alpha3);
        //Debug.Log("alpha4: " + alpha4);

        bool isOpaque = alpha1 >= 255 && alpha2 >= 255 && alpha3 >= 255 && alpha4 >= 255;
        bool hasAlpha = alpha1 > 0 || alpha2 > 0 || alpha3 > 0 || alpha4 > 0;

        var (_tempTerrainVertices, triangles) = PrepareTileVertices(xi, zi, xf, zf, idx1, idx2, idx3, idx4, lodf);

        if (isOpaque)
        {
            int number = 1;
            RenderTexture(_mapping.Layer2[idx1], _tempTerrainVertices, triangles, xf, zf, lodScale, number);
        }
        else
        {
            int number = 2;
            RenderTexture(_mapping.Layer1[idx1], _tempTerrainVertices, triangles, xf, zf, lodScale, number);
        }


        if (hasAlpha && !isOpaque)
        {
            int number = 3;
            ApplyAlphaToLights(alpha1, alpha2, alpha3, alpha4);
            RenderTexture(_mapping.Layer2[idx1], _tempTerrainVertices, triangles, xf, zf, lodScale, number);
        }
    }

    private (Vector3[], int[]) PrepareTileVertices(int xi, int zi, float xf, float zf, int idx1, int idx2, int idx3, int idx4, float lodf)
    {
        float terrainHeight1 = idx1 >= _backTerrainHeight.Length ? 0f : _backTerrainHeight[idx1].b * 1.5f;
        float terrainHeight2 = idx2 >= _backTerrainHeight.Length ? 0f : _backTerrainHeight[idx2].b * 1.5f;
        float terrainHeight3 = idx3 >= _backTerrainHeight.Length ? 0f : _backTerrainHeight[idx3].b * 1.5f;
        float terrainHeight4 = idx4 >= _backTerrainHeight.Length ? 0f : _backTerrainHeight[idx4].b * 1.5f;

        float sx = xf * Constants.TERRAIN_SCALE;
        float sz = zf * Constants.TERRAIN_SCALE;
        float scaledSize = Constants.TERRAIN_SCALE * lodf;

        Vector3[] _tempTerrainVertices = new Vector3[4]
        {
            new Vector3(sx, terrainHeight1, sz),
            new Vector3(sx + scaledSize, terrainHeight2, sz),
            new Vector3(sx + scaledSize, terrainHeight3, sz + scaledSize),
            new Vector3(sx, terrainHeight4, sz + scaledSize)
        };

        int[] _tempTerrainTriangles = new int[6]
        {
            0, 2, 1,  // First Triangle
            0, 3, 2   // Second Triangle
        };

        if (idx1 < _terrain.TerrainWall.Length && _terrain.TerrainWall[idx1].HasFlag(TWFlags.Height))
            _tempTerrainVertices[0].y += SpecialHeight;
        if (idx2 < _terrain.TerrainWall.Length && _terrain.TerrainWall[idx2].HasFlag(TWFlags.Height))
            _tempTerrainVertices[1].y += SpecialHeight;
        if (idx3 < _terrain.TerrainWall.Length && _terrain.TerrainWall[idx3].HasFlag(TWFlags.Height))
            _tempTerrainVertices[2].y += SpecialHeight;
        if (idx4 < _terrain.TerrainWall.Length && _terrain.TerrainWall[idx4].HasFlag(TWFlags.Height))
            _tempTerrainVertices[3].y += SpecialHeight;

        return (_tempTerrainVertices, _tempTerrainTriangles);

    }

    private void RenderTexture(int textureIndex, Vector3[] _tempTerrainVertices, int[] triangles, float xf, float zf, float lodScale = 1.0f, int number = 0)
    {
        if (textureIndex == 255 || textureIndex < 0 || textureIndex >= _textures.Length || _textures[textureIndex] == null)
            return;

        var texture = _textures[textureIndex];

        if (texture == null)
        {
            Debug.Log("null texture");
        }


        float baseWidth = 64f / texture.width;
        float baseHeight = 64f / texture.height;
        float suf = xf * baseWidth;
        float svf = zf * baseHeight;
        float uvWidth = baseWidth * lodScale;
        float uvHeight = baseHeight * lodScale;

        var _terrainTextureCoord = new Vector2[4];
        _terrainTextureCoord[0] = new Vector2(suf, svf);
        _terrainTextureCoord[1] = new Vector2(suf + uvWidth, svf);
        _terrainTextureCoord[2] = new Vector2(suf + uvWidth, svf + uvHeight);
        _terrainTextureCoord[3] = new Vector2(suf, svf + uvHeight);

        Mesh mesh = new Mesh();
        mesh.vertices = _tempTerrainVertices;
        mesh.triangles = triangles;
        mesh.uv = _terrainTextureCoord;
        mesh.colors = _tempTerrainLights;

        if (number == 2)
        {
            Material material;

            if (textureIndex == 5)
            {
                material = new Material(Shader.Find("Custom/WaterShader"));
                material.mainTexture = _textures[textureIndex];
                material.SetTexture("_MainTex", texture);
                material.SetFloat("_DistortionFrequency", 1.5f);
                material.SetFloat("_DistortionAmplitude", 0.02f);
                material.SetFloat("_WaterSpeed", 0.9f);
            }
            else
            {
                material = new Material(Shader.Find("Custom/SimpleTextureShader"));
                material.mainTexture = _textures[textureIndex];
            }

            GameObject terrainTile = new GameObject($"TerrainTile_{xf}_{zf}" + "<->" + number);
            terrainTile.transform.position = Vector3.zero;
            MeshFilter meshFilter = terrainTile.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;
            MeshRenderer meshRenderer = terrainTile.AddComponent<MeshRenderer>();
            meshRenderer.material = material;

            terrainTile.transform.SetParent(tileContainer.transform);
        }

        if (number == 3 || number == 1)
        {
            Material material;

            if (textureIndex == 5)
            {
                material = new Material(Shader.Find("Custom/WaterShader"));
                material.mainTexture = _textures[textureIndex];
                material.SetTexture("_MainTex", texture);
                material.SetFloat("_DistortionFrequency", 1.5f);
                material.SetFloat("_DistortionAmplitude", 0.02f);
                material.SetFloat("_WaterSpeed", 0.9f);
            }
            else
            {
                material = new Material(Shader.Find("Custom/SimpleTextureAlphaShader"));
                material.mainTexture = texture;
            }

            GameObject terrainTile = new GameObject($"TerrainTile_{xf}_{zf}" + "<->" + number);
            terrainTile.transform.position = Vector3.zero;
            MeshFilter meshFilter = terrainTile.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;
            MeshRenderer meshRenderer = terrainTile.AddComponent<MeshRenderer>();
            meshRenderer.material = material;
            
            terrainTile.transform.SetParent(tileContainer.transform);
        }
    }

    private void PrepareTileLights(int idx1, int idx2, int idx3, int idx4)
    {
        if (_backTerrainLight == null || _backTerrainLight.Length == 0)
        {
           Debug.LogWarning("⚠️ _backTerrainLight is not ready. Retrying next frame...");
           StartCoroutine(WaitForTerrainLight(idx1, idx2, idx3, idx4));
           return;
        }
        

        _tempTerrainLights[0] = idx1 < _backTerrainLight.Length ? new Color(
        _backTerrainLight[idx1].r / 255f,
        _backTerrainLight[idx1].g / 255f,
        _backTerrainLight[idx1].b / 255f,
        _backTerrainLight[idx1].a)
            : Color.black;

        _tempTerrainLights[1] = idx2 < _backTerrainLight.Length
            ? new Color(
                _backTerrainLight[idx2].r / 255f,
                _backTerrainLight[idx2].g / 255f,
                _backTerrainLight[idx2].b / 255f,
                _backTerrainLight[idx2].a)
            : Color.black;

        _tempTerrainLights[2] = idx3 < _backTerrainLight.Length
            ? new Color(
                _backTerrainLight[idx3].r / 255f,
                _backTerrainLight[idx3].g / 255f,
                _backTerrainLight[idx3].b / 255f,
                _backTerrainLight[idx3].a)
            : Color.black;

        _tempTerrainLights[3] = idx4 < _backTerrainLight.Length
            ? new Color(
                _backTerrainLight[idx4].r / 255f,
                _backTerrainLight[idx4].g / 255f,
                _backTerrainLight[idx4].b / 255f,
                _backTerrainLight[idx4].a)
            : Color.black;
    }

    private void ApplyAlphaToLights(byte alpha1, byte alpha2, byte alpha3, byte alpha4)
    {
        _tempTerrainLights[0] *= alpha1 / 255f;
        _tempTerrainLights[1] *= alpha2 / 255f;
        _tempTerrainLights[2] *= alpha3 / 255f;
        _tempTerrainLights[3] *= alpha4 / 255f;

        _tempTerrainLights[0].a = alpha1 / 255f;
        _tempTerrainLights[1].a = alpha2 / 255f;
        _tempTerrainLights[2].a = alpha3 / 255f;
        _tempTerrainLights[3].a = alpha4 / 255f;

    }

    private int GetLODLevel(float distance)
    {
        float levelF = distance / LOD_DISTANCE_MULTIPLIER;
        int level = Mathf.FloorToInt(levelF);

        float blend = levelF - level;
        level = Mathf.RoundToInt(Mathf.Lerp(level, level + 1, blend));

        return Mathf.Min(level, MAX_LOD_LEVELS - 1);
    }

    private static int GetTerrainIndex(int x, int y) => y * Constants.TERRAIN_SIZE + x;

    private static int GetTerrainIndexRepeat(int x, int y) =>
        ((y & Constants.TERRAIN_SIZE_MASK) * Constants.TERRAIN_SIZE) + (x & Constants.TERRAIN_SIZE_MASK);

    public TWFlags RequestTerraingFlag(int x, int y) => _terrain.TerrainWall[GetTerrainIndex(x, y)];

}
