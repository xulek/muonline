using Client.Data.CAP;
using Client.Data.CWS;
using Client.Data.OBJS;

namespace Client.Data.UnitTests;

[TestFixture]
public class Control_Should_Deserialize
{

    private CAPReader capReader;
    private CWSReader cwsReader;

    [SetUp]
    public void SetUp()
    {
        capReader = new();
        cwsReader = new();
    }

    [Test]
    public async Task GenericCameraAnglePositionFileExists()
    {
        var path = Path.Combine(Constants.DataPath, "G_Camera_Angle_Position.bmd");
        var genericCameraAnglePositionFile = File.Exists(
            path
        );
        Assert.That(genericCameraAnglePositionFile, Is.True, $"Generic camera angle position file should exists");

        CameraAnglePosition data = await capReader.Load(path);
        Assert.That(data, Is.Not.Null, "CAP should be deserialized");
    }
    [Test]
    public async Task CustomCameraAnglePositionFileExists()
    {
        var path = Path.Combine(Constants.DataPath, "World111", "Camera_Angle_Position.bmd");
        var customCameraAnglePositionFile = File.Exists(
            path
        );
        Assert.That(customCameraAnglePositionFile, Is.True, $"World111 camera angle position file should exists");
        CameraAnglePosition data = await capReader.Load(path);

        Assert.That(data, Is.Not.Null, "CAP should be deserialized");

    }

    [Test]
    public async Task CameraWalkScriptFileExists()
    {

        var path = Path.Combine(Constants.DataPath, "World74", "CWScript74.cws");

        var exists = File.Exists(
            path
        );
        Assert.That(exists, Is.True, $"Camera walk script file should exists");

        CameraWalkScript data = await cwsReader.Load(path);
        Assert.That(data, Is.Not.Null, "CWS should be deserialized");
    }

}
