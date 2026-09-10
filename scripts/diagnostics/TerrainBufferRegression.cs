// Run: dotnet run --project scripts/diagnostics/TerrainBufferRegression.csproj -p:MonoGameFramework=MonoGame.Framework.DesktopGL
// Exercise the real CPU batching path without a graphics device or game assets.
using System;
using System.Reflection;
using Client.Main.Controls.Terrain;
using Client.Main.Graphics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
foreach (bool alpha in new[] { false, true })
{
    using var renderer = new TerrainRenderer(null,
        new TerrainData { Textures = Array.Empty<Texture2D>() }, null, null, null);
    var addVertices = typeof(TerrainRenderer).GetMethod("AddTileToBatch", PrivateInstance)
        .CreateDelegate<Action<int, TerrainVertexPositionColorNormalTexture[], bool>>(renderer);
    var tile = new TerrainVertexPositionColorNormalTexture[6];
    for (int i = 0; i < tile.Length; i++)
        tile[i] = new(new Vector3(i + 1, 20, 30), Color.White, Vector3.UnitZ, Vector2.One);

    // Camera movement can require more tiles than the current allocation holds.
    // Every tile queued BEFORE each growth must still be submitted in that frame.
    for (int frame = 0; frame < 2; frame++)
    {
        typeof(TerrainRenderer).GetMethod("ResetBatchTracking", PrivateInstance).Invoke(renderer, null);
        for (int i = 0; i < 1500; i++)
            addVertices(0, tile, alpha);
        var vertices = ((TerrainVertexPositionColorNormalTexture[][])typeof(TerrainRenderer)
            .GetField(alpha ? "_tileAlphaBatches" : "_tileBatches", PrivateInstance).GetValue(renderer))[0];
        for (int i = 0; i < 1500 * 6; i++)
            if (!vertices[i].Equals(tile[i % 6]))
                throw new Exception($"Queued terrain vertex lost: alpha={alpha}, frame={frame}, vertex={i}");
    }

    typeof(TerrainRenderer).GetField("_buildingPersistentTerrainIndexCache", PrivateInstance).SetValue(renderer, true);
    var addIndices = typeof(TerrainRenderer).GetMethod("AddTileToIndexBatch", PrivateInstance)
        .CreateDelegate<Action<int, ushort, ushort, ushort, ushort, bool>>(renderer);
    // A growing resident index cache must retain the existing triangles as well.
    for (int i = 0; i < 17000; i++)
        addIndices(0, 1, 2, 3, 4, alpha);
    var indices = ((ushort[][])typeof(TerrainRenderer)
        .GetField(alpha ? "_tileAlphaIndexBatches" : "_tileIndexBatches", PrivateInstance).GetValue(renderer))[0];
    ushort[] expected = { 1, 2, 3, 3, 4, 1 };
    for (int i = 0; i < 17000 * 6; i++)
        if (indices[i] != expected[i % 6])
            throw new Exception($"Queued terrain index lost: alpha={alpha}, index={i}");
    Console.WriteLine($"PASS: alpha={alpha}, vertex growth and following frame, persistent index growth");
}
