using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Skylarping.Characters;

namespace Skylarping.Tests
{
    [TestFixture]
    public class CardFileTests
    {
        // 1x1 PNG
        private static readonly byte[] Picture = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        private const string CardJson =
            @"{""spec"":""chara_card_v3"",""spec_version"":""3.0"",""data"":{""name"":""Vee"",""description"":""d"",""first_mes"":""Hi""," +
            @"""assets"":[{""type"":""emotion"",""uri"":""embeded://assets/happy.png"",""name"":""happy"",""ext"":""png""}," +
            @"{""type"":""icon"",""uri"":""embeded://assets/icon/main.png"",""name"":""main"",""ext"":""png""}]}}";

        /// <summary>Builds a zip by hand, since .NET 4.0 can't. Files are (name, contents, deflate?).</summary>
        private static byte[] Zip(IEnumerable<Tuple<string, byte[], bool>> files, byte[] prefix = null, int flags = 0, int forcedMethod = -1)
        {
            var body = new MemoryStream();
            var directory = new MemoryStream();
            var bodyWriter = new BinaryWriter(body);
            var directoryWriter = new BinaryWriter(directory);
            int count = 0;
            foreach (Tuple<string, byte[], bool> file in files)
            {
                byte[] name = Encoding.UTF8.GetBytes(file.Item1);
                byte[] data = file.Item3 ? Deflate(file.Item2) : file.Item2;
                ushort method = (ushort)(forcedMethod >= 0 ? forcedMethod : file.Item3 ? 8 : 0);
                uint offset = (uint)body.Position;

                bodyWriter.Write(0x04034B50u);
                bodyWriter.Write((ushort)20);
                bodyWriter.Write((ushort)flags);
                bodyWriter.Write(method);
                bodyWriter.Write(0u);  // time, date
                bodyWriter.Write(0u);  // crc: not checked
                bodyWriter.Write((uint)data.Length);
                bodyWriter.Write((uint)file.Item2.Length);
                bodyWriter.Write((ushort)name.Length);
                bodyWriter.Write((ushort)0);
                bodyWriter.Write(name);
                bodyWriter.Write(data);

                directoryWriter.Write(0x02014B50u);
                directoryWriter.Write((ushort)20);
                directoryWriter.Write((ushort)20);
                directoryWriter.Write((ushort)flags);
                directoryWriter.Write(method);
                directoryWriter.Write(0u);
                directoryWriter.Write(0u);
                directoryWriter.Write((uint)data.Length);
                directoryWriter.Write((uint)file.Item2.Length);
                directoryWriter.Write((ushort)name.Length);
                directoryWriter.Write((ushort)0);
                directoryWriter.Write((ushort)0);
                directoryWriter.Write((ushort)0);
                directoryWriter.Write((ushort)0);
                directoryWriter.Write(0u);
                directoryWriter.Write(offset);
                directoryWriter.Write(name);
                count++;
            }

            var result = new MemoryStream();
            var writer = new BinaryWriter(result);
            if (prefix != null)
                writer.Write(prefix);
            writer.Write(body.ToArray());
            writer.Write(directory.ToArray());
            writer.Write(0x06054B50u);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)count);
            writer.Write((ushort)count);
            writer.Write((uint)directory.Length);
            writer.Write((uint)body.Length); // measured from the zip's own start, as appended zips often are
            writer.Write((ushort)0);
            return result.ToArray();
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                    deflate.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }

        private static Tuple<string, byte[], bool> File_(string name, byte[] data, bool deflate = true)
        {
            return Tuple.Create(name, data, deflate);
        }

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        [Test]
        public void ZipReader_ReadsStoredAndDeflatedFiles()
        {
            byte[] long_text = Bytes(new string('a', 100000));
            var zip = new ZipReader(Zip(new[] { File_("stored.txt", Bytes("plain"), false), File_("dir/deflated.txt", long_text) }));

            CollectionAssert.AreEquivalent(new[] { "stored.txt", "dir/deflated.txt" }, zip.Names);
            Assert.AreEqual(Bytes("plain"), zip.Read("stored.txt"));
            Assert.AreEqual(long_text, zip.Read("DIR/Deflated.txt"), "names ignore case");
            Assert.AreEqual(Bytes("plain"), zip.Read("./stored.txt"));
            Assert.Throws<FileNotFoundException>(() => zip.Read("nope.txt"));
        }

        [Test]
        public void ZipReader_ReadsAZipAppendedToSomethingElse()
        {
            var zip = new ZipReader(Zip(new[] { File_("a.txt", Bytes("hello")) }, prefix: new byte[] { 0xFF, 0xD8, 1, 2, 3, 4, 5 }));

            Assert.AreEqual(Bytes("hello"), zip.Read("a.txt"));
        }

        [TestCase(new byte[0])]
        [TestCase(new byte[] { 1, 2, 3 })]
        [TestCase(new byte[] { (byte)'P', (byte)'K', 3, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]
        public void ZipReader_RejectsWhatIsNotAZip(byte[] bytes)
        {
            Assert.Throws<InvalidDataException>(() => new ZipReader(bytes));
        }

        [Test]
        public void ZipReader_RejectsEncryptedFilesAndUnknownCompression()
        {
            var encrypted = new ZipReader(Zip(new[] { File_("a.txt", Bytes("x"), false) }, flags: 1));
            Assert.Throws<InvalidDataException>(() => encrypted.Read("a.txt"));

            var unknown = new ZipReader(Zip(new[] { File_("a.txt", Bytes("x"), false) }, forcedMethod: 12));
            Assert.Throws<InvalidDataException>(() => unknown.Read("a.txt"));
        }

        [Test]
        public void ZipReader_SurvivesEveryTruncation()
        {
            byte[] zip = Zip(new[] { File_("a.txt", Bytes("hello there")) });
            for (int length = 0; length < zip.Length; length++)
            {
                byte[] part = zip.Take(length).ToArray();
                try
                {
                    new ZipReader(part).Read("a.txt");
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is FileNotFoundException)
                {
                    // Expected.
                }
            }
        }

        [Test]
        public void Charx_ImportsTheCardAndItsMainIcon()
        {
            byte[] charx = Zip(new[]
            {
                File_("card.json", Bytes(CardJson)),
                File_("assets/happy.png", new byte[] { 1 }),
                File_("assets/icon/main.png", Picture),
            });

            LoadedCard loaded = CharacterCardFile.Load(charx, ".charx");

            Assert.AreEqual("Vee", loaded.Card.Name);
            Assert.AreEqual(CardSpec.V3, loaded.Card.SourceSpec);
            Assert.AreEqual(Picture, loaded.Image);
        }

        [Test]
        public void Charx_IsRecognizedWithoutItsExtension()
        {
            byte[] charx = Zip(new[] { File_("card.json", Bytes(CardJson)) });

            Assert.AreEqual("Vee", CharacterCardFile.Load(charx).Card.Name);
        }

        [TestCase("embedded://assets/icon/main.png")]
        [TestCase("EMBEDED://assets/icon/main.png")]
        public void Charx_AcceptsBothSpellingsOfEmbedded(string uri)
        {
            string json = CardJson.Replace("embeded://assets/icon/main.png", uri);
            byte[] charx = Zip(new[] { File_("card.json", Bytes(json)), File_("assets/icon/main.png", Picture) });

            Assert.AreEqual(Picture, CharacterCardFile.Load(charx, ".charx").Image);
        }

        [Test]
        public void Charx_ReadsAnIconInADataUrl()
        {
            string json = CardJson.Replace("embeded://assets/icon/main.png", "data:image/png;base64," + Convert.ToBase64String(Picture));
            byte[] charx = Zip(new[] { File_("card.json", Bytes(json)) });

            Assert.AreEqual(Picture, CharacterCardFile.Load(charx, ".charx").Image);
        }

        [TestCase("ccdefault:")]
        [TestCase("https://example.com/icon.png")]
        [TestCase("embeded://assets/missing.png")]
        [TestCase("data:image/png;base64,not base64!")]
        public void Charx_WithoutAReadableIcon_StillImports(string uri)
        {
            string json = CardJson.Replace("embeded://assets/icon/main.png", uri);
            byte[] charx = Zip(new[] { File_("card.json", Bytes(json)) });

            LoadedCard loaded = CharacterCardFile.Load(charx, ".charx");

            Assert.AreEqual("Vee", loaded.Card.Name);
            Assert.IsNull(loaded.Image);
        }

        [Test]
        public void Charx_WithoutACard_IsReported()
        {
            byte[] charx = Zip(new[] { File_("readme.txt", Bytes("hi")) });

            var ex = Assert.Throws<CharacterCardException>(() => CharacterCardFile.Load(charx, ".charx"));
            StringAssert.Contains("doesn't contain a character card", ex.Message);
        }

        [Test]
        public void Charx_ThatIsDamaged_IsReported()
        {
            Assert.Throws<CharacterCardException>(() => CharacterCardFile.Load(new byte[] { (byte)'P', (byte)'K', 1, 2 }, ".charx"));
        }

        [Test]
        public void WebpIsStillDeclined()
        {
            var ex = Assert.Throws<CharacterCardException>(() => CharacterCardFile.Load(new byte[] { 1, 2, 3 }, ".webp"));
            StringAssert.Contains("WebP", ex.Message);
        }

        [Test]
        public void Crc32_MatchesThePngSpec()
        {
            // The checksum every PNG's IEND chunk ends with.
            Assert.AreEqual(0xAE426082u, CharacterCardFile.Crc32(Encoding.ASCII.GetBytes("IEND")));
        }

        private static CharacterCard MadeHere()
        {
            return new CharacterCard { Name = "Vee", Description = "Café owner \U0001F600", Lorebook = new Lorebook { Name = "Book" } };
        }

        [Test]
        public void PngExport_RoundTrips()
        {
            byte[] png = CharacterCardFile.ToPng(MadeHere(), Picture);

            LoadedCard loaded = CharacterCardFile.Load(png, ".png");

            Assert.AreEqual("Vee", loaded.Card.Name);
            Assert.AreEqual("Café owner \U0001F600", loaded.Card.Description);
            Assert.AreEqual("Book", loaded.Card.Lorebook.Name);
            Assert.AreEqual(png, loaded.Image);
        }

        [Test]
        public void PngExport_HoldsBothSpecs_AndV3Wins()
        {
            Dictionary<string, string> chunks = CharacterCardFile.ReadPngText(CharacterCardFile.ToPng(MadeHere(), Picture));

            Assert.AreEqual("chara_card_v3", (string)Chunk(chunks, "ccv3")["spec"]);
            Assert.AreEqual("3.0", (string)Chunk(chunks, "ccv3")["spec_version"]);
            Assert.IsNotNull(Chunk(chunks, "ccv3")["data"]["group_only_greetings"], "required by V3");
            Assert.AreEqual("chara_card_v2", (string)Chunk(chunks, "chara")["spec"]);
            Assert.AreEqual("Vee", (string)Chunk(chunks, "chara")["data"]["name"]);
        }

        private static JObject Chunk(Dictionary<string, string> chunks, string keyword)
        {
            return JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(chunks[keyword])));
        }

        [Test]
        public void PngExport_IsStillAPictureWithValidChunks()
        {
            byte[] png = CharacterCardFile.ToPng(MadeHere(), Picture);

            var decoder = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.AreEqual(1, decoder.Frames[0].PixelWidth);

            int pos = 8;
            var types = new List<string>();
            while (pos < png.Length)
            {
                int length = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
                types.Add(Encoding.ASCII.GetString(png, pos + 4, 4));
                byte[] typed = new byte[length + 4];
                Buffer.BlockCopy(png, pos + 4, typed, 0, typed.Length);
                int at = pos + 8 + length;
                uint stored = ((uint)png[at] << 24) | ((uint)png[at + 1] << 16) | ((uint)png[at + 2] << 8) | png[at + 3];
                Assert.AreEqual(CharacterCardFile.Crc32(typed), stored, "checksum of " + types.Last());
                pos = at + 4;
            }
            Assert.AreEqual("IEND", types.Last());
            Assert.AreEqual(pos, png.Length);
        }

        [Test]
        public void PngExport_ReplacesACardAlreadyInThePicture()
        {
            byte[] first = CharacterCardFile.ToPng(new CharacterCard { Name = "Old" }, Picture);
            byte[] second = CharacterCardFile.ToPng(new CharacterCard { Name = "New" }, first);

            Assert.AreEqual("New", CharacterCardFile.Load(second, ".png").Card.Name);
            Assert.Less(second.Length, first.Length + 100, "the old chunks are gone");
        }

        [Test]
        public void PngExport_NeedsAPng()
        {
            Assert.Throws<CharacterCardException>(() => CharacterCardFile.ToPng(MadeHere(), new byte[] { 1, 2, 3 }));
            Assert.Throws<CharacterCardException>(() => CharacterCardFile.ToPng(MadeHere(), Picture.Take(20).ToArray()));
        }

        [Test]
        public void JsonExport_RoundTrips()
        {
            CharacterCard loaded = CharacterCardFile.Load(Bytes(CharacterCardFile.ToJson(MadeHere())), ".json").Card;

            Assert.AreEqual("Vee", loaded.Name);
            Assert.AreEqual("Book", loaded.Lorebook.Name);
        }

        [TestCase(".png")]
        [TestCase(".json")]
        public void ExportedCards_KeepTheirSkylarpingSettings(string extension)
        {
            CharacterCard card = MadeHere();
            card.Tools = new List<string> { "save_memory" };
            card.ProactiveMessages = true;
            card.ProactiveMaxUnanswered = 6;
            card.SuppressActions = true;
            card.UseScenario = true;
            card.MoodMinHours = 2;
            card.MoodMaxHours = 3;
            byte[] file = extension == ".png" ? CharacterCardFile.ToPng(card, Picture) : Bytes(CharacterCardFile.ToJson(card));

            CharacterCard loaded = CharacterCardFile.Load(file, extension).Card;

            CollectionAssert.AreEqual(new[] { "save_memory" }, loaded.Tools);
            Assert.IsTrue(loaded.ProactiveMessages);
            Assert.AreEqual(6, loaded.ProactiveMaxUnanswered);
            Assert.IsTrue(loaded.SuppressActions);
            Assert.IsTrue(loaded.UseScenario);
            Assert.AreEqual(2, loaded.MoodMinHours);
            Assert.AreEqual(3, loaded.MoodMaxHours);
        }

        [Test]
        public void ExportedCards_DontPointAtEmbeddedAssets()
        {
            CharacterCard imported = CharacterCardFile.Load(
                Zip(new[] { File_("card.json", Bytes(CardJson)), File_("assets/icon/main.png", Picture) }), ".charx").Card;

            var assets = (JArray)JObject.Parse(CharacterCardFile.ToJson(imported))["data"]["assets"];

            var only = (JObject)assets.Single();
            Assert.AreEqual("icon", (string)only["type"]);
            Assert.AreEqual("main", (string)only["name"]);
            Assert.AreEqual("ccdefault:", (string)only["uri"]);
        }

        [Test]
        public void ExportedCards_KeepOnlineAssets()
        {
            string json = CardJson.Replace("embeded://assets/happy.png", "https://example.com/happy.png");
            CharacterCard card = CharacterCard.FromJson(json);

            var assets = (JArray)JObject.Parse(CharacterCardFile.ToJson(card))["data"]["assets"];

            Assert.IsTrue(assets.Any(a => (string)a["uri"] == "https://example.com/happy.png"));
        }

        [TestCase("Vee", "Vee")]
        [TestCase("Who? / Me: \"Vee\"", "Who  Me Vee")]
        [TestCase("???", "Character")]
        public void ExportFileName_IsSafe(string name, string expected)
        {
            Assert.AreEqual(expected, CharacterCardFile.FileNameFor(new CharacterCard { Name = name }));
        }

        [TestCase(".png")]
        [TestCase(".json")]
        public void Save_WritesTheFormatOfTheExtension(string extension)
        {
            string path = Path.Combine(Path.GetTempPath(), "skylarping-test-" + Guid.NewGuid().ToString("N") + extension);
            try
            {
                CharacterCardFile.Save(MadeHere(), Picture, path);

                Assert.AreEqual("Vee", CharacterCardFile.Load(path).Card.Name);
                Assert.AreEqual(extension == ".png", CharacterCardFile.IsPng(File.ReadAllBytes(path)));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
