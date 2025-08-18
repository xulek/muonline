using System;
using System.IO;
using System.Collections;
using Client.Data.Texture;
using Client.Data.OZB;


namespace Client.Data.UnitTests;

[TestFixture]
public class Texture_Should_Deserialize
{
    private OZBReader ozbReader;
    private OZDReader ozdReader;

    private OZJReader ozjReader;
    private OZPReader ozpReader;
    private OZTReader oztReader;

    [SetUp]
    public void SetUp()
    {
        ozbReader = new();
        ozdReader = new();
        ozjReader = new();
        ozpReader = new();
        oztReader = new();
    }

    [Test]
    public void DataFolderExists()
    {
        var result = Directory.Exists(Constants.DataPath);
        Assert.That(result, Is.True, "Data folder should exists");
    }

    [Test]
    public void FifthSkillSlotFileExists()
    {
        var filePath = $"{Constants.DataPath}/Item/5thskillslot.bmd";
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "/Item/5thskillslot.bmd file should exists");

    }

    [Test]
    public async Task World1_TerrainLightFileExists()
    {
        var filePath = $"{Constants.DataPath}/World1/TerrainLight.OZB";
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "/World1/TerrainLight.OZB file should exists");
        OZB.OZB? data = await ozbReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Ozb Data should be deserialized!");
        Assert.That(data.Width, Is.EqualTo(256), "Map width should be 256");
    }

    [Test]
    public async Task Gfx_WinFileExists()
    {
        var filePath = $"{Constants.DataPath}/Interface/GFx/win.ozd";
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "/Interface/GFx/win.ozd file should exists");
        TextureData? data = await ozdReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Ozd Data should be deserialized!");
        Assert.That(data.Width, Is.EqualTo(256), "Image Width should be 256");
        Assert.That(data.Height, Is.EqualTo(64), "Image Height should be 64");
    }

    [Test]
    public async Task World_TileGrassFileExists()
    {
        var filePath = $"{Constants.DataPath}/World1/TileGrass01.OZJ";
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "/World1/TileGrass01.OZJ file should exists");
        TextureData? data = await ozjReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Jpg Data should be deserialized!");
        Assert.Multiple(() =>
        {
            Assert.That(data.Width, Is.EqualTo(256), "Image Width should be 256");
            Assert.That(data.Height, Is.EqualTo(256), "Image Height should be 256");
        });
    }
    [Test]
    public async Task Item_TextureFileExists()
    {
        var filePath = $"{Constants.DataPath}/Item/texture/hhj.ozj";
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "/Item/texture/hhj.ozj file should exists");
        TextureData? data = await ozjReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Jpg Data should be deserialized!");
        Assert.Multiple(() =>
        {
            Assert.That(data.Width, Is.EqualTo(16), "Image Width should be 16");
            Assert.That(data.Height, Is.EqualTo(16), "Image Height should be 16");
        });
    }
    [Test]
    public async Task MinimapFileExists()
    {
        var filePath = $"{Constants.DataPath}/Interface/GFx/NaviMap/Navimap106.OZP";
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "/Interface/GFx/NaviMap/Navimap106.OZP file should exists");
        TextureData? data = await ozpReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Png Data should be deserialized!");
        Assert.Multiple(() =>
        {
            Assert.That(data.Width, Is.EqualTo(512), "Image Width should be 512");
            Assert.That(data.Height, Is.EqualTo(512), "Image Height should be 512");
        });
    }
    [Test]
    public async Task MonsterTgaTextureFileExists()
    {
        var filePath = $"{Constants.DataPath}/World1/TileGrass01.OZT";
        var result = File.Exists(filePath);
        Assert.That(result, Is.True, "/World1/TileGrass01.OZT file should exists");
        TextureData? data = await oztReader.Load(filePath);
        Assert.That(data, Is.Not.Null, "Tga Data should be deserialized!");
        Assert.Multiple(() =>
        {
            Assert.That(data.Width, Is.EqualTo(256), "Image Width should be 256");
            Assert.That(data.Height, Is.EqualTo(64), "Image Height should be 64");
        });
    }
}
