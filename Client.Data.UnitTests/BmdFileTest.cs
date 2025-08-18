using Client.Data.BMD;

namespace Client.Data.UnitTests.Bmd
{
    [TestFixture]
    public class Control_Should_Deserialize
    {

        private BMDReader bmdReader;

        [SetUp]
        public void SetUp()
        {
            bmdReader = new();
        }

        [Test]
        public void Monster03BmdFileExists()
        {
            var monster03Exists = File.Exists(
                Path.Combine(Constants.DataPath, "Monster", "Monster03.bmd")
            );
            Assert.That(monster03Exists, Is.True, $"Monster03 file should exists");
        }

        [Test]
        public async Task Should_Deserialize_Monster03BmdFile()
        {
            var filePath = Path.Combine(Constants.DataPath, "Monster", "Monster03.bmd");

            var monster03Exists = File.Exists(
                filePath
            );

            Assert.That(monster03Exists, Is.True, $"Monster03 file should exists");

            BMD.BMD? bmd = await bmdReader.Load(filePath);

            Assert.That(bmd, Is.Not.Null, "Bmd file should be deserialized!");

            
        }
    }
}