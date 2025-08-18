using Client.Data.ATT;
using Client.Data.MAP;
using Client.Data.OBJS;

namespace Client.Data.UnitTests;

[TestFixture]
public class World_Should_Deserialize
{
    private readonly int worldIndex = 1;

    private string WorldFolder => Path.Combine(Constants.DataPath, $"World{worldIndex}");
    private ATTReader attReader;
    private MapReader mapReader;
    private OBJReader objReader;

    [SetUp]
    public void SetUp()
    {
        attReader = new();
        mapReader = new();
        objReader = new();
    }

    [Test]
    public void WorldFolderExists()
    {
        var result = Directory.Exists(
            WorldFolder
        );
        Assert.That(result, Is.True, $"World {worldIndex} folder should exists");
    }

    [Test]
    public async Task World1_TerrainObjectFileExists()
    {
        var filePath = Path.Combine(WorldFolder, $"EncTerrain{worldIndex}.obj");
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "Terrain file should exists");
        OBJ? data = await objReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Ozb Data should be deserialized!");
        Assert.That(data.MapNumber, Is.EqualTo(worldIndex), "Map Number should equal to world index");
        Assert.That(data.Version, Is.EqualTo(0), "This map should use Version 0");
    }

    [Test]
    public async Task World1_TerrainAttObjectFileExists()
    {
        var filePath = Path.Combine(WorldFolder, $"EncTerrain{worldIndex}.att");
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "Terrain Att file should exists");
        TerrainAttribute? data = await attReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Att Data should be deserialized!");
        Assert.That(data.Index, Is.EqualTo(worldIndex), "Map Number should equal to world index");
        Assert.That(data.Version, Is.EqualTo(0), "This map should use Version 0");
    }
    [Test]
    public async Task World1_TerrainMapFileExists()
    {
        var filePath = Path.Combine(WorldFolder, $"EncTerrain{worldIndex}.map");
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "Terrain Map file should exists");
        TerrainMapping? data = await mapReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Map Data should be deserialized!");
        Assert.That(data?.MapNumber, Is.EqualTo(worldIndex), "Map Number should equal to world index");
        Assert.That(data?.Version, Is.EqualTo(0), "This map should use Version 0");
    }
}
