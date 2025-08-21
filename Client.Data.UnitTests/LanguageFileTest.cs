


using Client.Data.BMD;
using Client.Data.LANG;
using ICSharpCode.SharpZipLib.Zip;

namespace Client.Data.UnitTests;

[TestFixture]
public class LangFiles_Should_Deserialize
{

    private LangGateReader gateReader;
    private LangMPRReader mprReader;
    private LangSkillReader skillReader;

    private ItemBMDReader itemBmdReader;

    [SetUp]
    public void SetUp()
    {
        gateReader = new();
        mprReader = new();
        skillReader = new();
        itemBmdReader = new();
    }

    [Test]
    public async Task Should_Deserialize_Lang_mpr_File()
    {
        var path = Path.Combine(Constants.DataPath, "Lang.mpr");

        var exists = File.Exists(
           path
        );
        Assert.That(exists, Is.True, $"Lang.mpr file should exists");

        var data = await mprReader.Load(path);
        Assert.That(data, Is.Not.Null, "Mpr zipfile should loaded");

        var gateData = await gateReader.Load(data, "/Gate.txt");
        Assert.That(gateData, Is.Not.Null, "Gate data is not null");

        var skillData = await skillReader.Load(data, "/Skill(kor).txt");
        Assert.That(skillData, Is.Not.Null, "Skill data is not null");
    }

    [Test]
    public async Task Should_Deserialize_Item_Bmd_File()
    {
        var path = Path.Combine(Constants.DataPath, "Local", "item.bmd");

        var exists = File.Exists(
           path
        );
        Assert.That(exists, Is.True, $"item.bmd file should exists");

        var data = await itemBmdReader.Load(path);
        Assert.That(data, Is.Not.Null, "Mpr zipfile should loaded");
    }
}