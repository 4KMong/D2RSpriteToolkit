using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace D2RSpriteToolkit
{
    internal static class CodecSmokeTests
    {
        private const int HeaderSize = 0x28;

        public static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "D2RSpriteToolkit_v402_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                TestBppZeroAndRoundTrip(root);
                TestMalformedTemplateWidth(root);
                TestDxt5TemplateBecomesRgba(root);
                TestTemplateLookupWithoutFileList(root);
                TestInvalidSameNameSpriteIsRejected(root);
                TestNoTemplateCreatesStaticSprite(root);
                TestNonDivisibleWidthPreservesOriginal(root);
                Console.WriteLine("All PNG-to-Sprite regression tests passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void TestBppZeroAndRoundTrip(string root)
        {
            string dir = CreateCaseDirectory(root, "bpp_zero_roundtrip");
            string pngPath = Path.Combine(dir, "frontend_charactertile_black.lowend.png");
            string spritePath = Path.Combine(dir, "frontend_charactertile_black.lowend.sprite");

            using (Bitmap source = CreatePatternBitmap(921, 61))
            {
                source.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                WriteTemplate(spritePath, new byte[] { (byte)'S', (byte)'P', (byte)'a', (byte)'1' }, 31, 307, 921, 61, 3, 0, 0);

                AssertLookupStatus(pngPath, "Valid");
                D2RSpriteCodec.SaveRgbaSpriteUsingTemplate(source, spritePath, spritePath);

                byte[] header = ReadHeader(spritePath);
                AssertEqual("SPa1", System.Text.Encoding.ASCII.GetString(header, 0, 4), "Template magic variant was not preserved.");
                AssertEqual(31, BitConverter.ToUInt16(header, 0x04), "Output version must be v31.");
                AssertEqual(307, BitConverter.ToUInt16(header, 0x06), "Frame width mismatch.");
                AssertEqual(921, BitConverter.ToInt32(header, 0x08), "Total width mismatch.");
                AssertEqual(61, BitConverter.ToInt32(header, 0x0C), "Height mismatch.");
                AssertEqual(3, BitConverter.ToInt32(header, 0x14), "Frame count mismatch.");
                AssertEqual(0, BitConverter.ToInt32(header, 0x20), "Canonical v31 reserved field 0x20 must be zero.");
                AssertEqual(0, BitConverter.ToInt32(header, 0x24), "Canonical v31 reserved field 0x24 must be zero.");

                Bitmap decoded;
                D2RSpriteInfo info;
                string error;
                if (!D2RSpritePreview.TryLoadSpriteBitmap(spritePath, out decoded, out info, out error))
                {
                    throw new Exception("Round-trip decode failed: " + error);
                }
                using (decoded)
                {
                    AssertBitmapsEqual(source, decoded);
                }
            }
        }

        private static void TestMalformedTemplateWidth(string root)
        {
            string dir = CreateCaseDirectory(root, "malformed_width");
            string spritePath = Path.Combine(dir, "wide.sprite");
            WriteTemplate(spritePath, Magic("SpA1"), 31, 130, 7919, 1, 60, 0, 0);

            using (Bitmap source = CreatePatternBitmap(7920, 1))
            {
                D2RSpriteCodec.SaveRgbaSpriteUsingTemplate(source, spritePath, spritePath);
            }

            byte[] header = ReadHeader(spritePath);
            AssertEqual(132, BitConverter.ToUInt16(header, 0x06), "7920 / 60 must produce a 132px frame width.");
            AssertEqual(7920, BitConverter.ToInt32(header, 0x08), "Output width must come from the PNG.");
            AssertEqual(60, BitConverter.ToInt32(header, 0x14), "Frame count must come from the template.");
        }

        private static void TestDxt5TemplateBecomesRgba(string root)
        {
            string dir = CreateCaseDirectory(root, "dxt5_template");
            string spritePath = Path.Combine(dir, "dxt.sprite");
            WriteTemplate(spritePath, Magic("SpA1"), 61, 10, 30, 4, 3, 0, 0);

            using (Bitmap source = CreatePatternBitmap(36, 4))
            {
                D2RSpriteCodec.SaveRgbaSpriteUsingTemplate(source, spritePath, spritePath);
            }

            byte[] header = ReadHeader(spritePath);
            AssertEqual(31, BitConverter.ToUInt16(header, 0x04), "v61 templates must produce v31 RGBA output.");
            AssertEqual(12, BitConverter.ToUInt16(header, 0x06), "Frame width must be rebuilt from the PNG.");
            AssertEqual(3, BitConverter.ToInt32(header, 0x14), "Frame count mismatch.");
        }

        private static void TestTemplateLookupWithoutFileList(string root)
        {
            string dir = CreateCaseDirectory(root, "disk_lookup");
            string pngPath = Path.Combine(dir, "not_loaded.png");
            string spritePath = Path.Combine(dir, "not_loaded.sprite");

            using (Bitmap source = CreatePatternBitmap(20, 2))
            {
                source.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            WriteTemplate(spritePath, Magic("SpA1"), 31, 10, 20, 2, 2, 0, 0);
            AssertLookupStatus(pngPath, "Valid");
        }

        private static void TestInvalidSameNameSpriteIsRejected(string root)
        {
            string dir = CreateCaseDirectory(root, "invalid_template");
            string pngPath = Path.Combine(dir, "invalid.png");
            string spritePath = Path.Combine(dir, "invalid.sprite");
            byte[] original = new byte[] { 1, 2, 3, 4, 5, 6, 7 };

            using (Bitmap source = CreatePatternBitmap(8, 2))
            {
                source.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            File.WriteAllBytes(spritePath, original);

            AssertLookupStatus(pngPath, "Invalid");
            AssertBytesEqual(original, File.ReadAllBytes(spritePath), "Invalid same-name Sprite was modified.");
        }

        private static void TestNoTemplateCreatesStaticSprite(string root)
        {
            string dir = CreateCaseDirectory(root, "static");
            string pngPath = Path.Combine(dir, "static.png");
            string spritePath = Path.Combine(dir, "static.sprite");

            using (Bitmap source = CreatePatternBitmap(17, 5))
            {
                source.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                AssertLookupStatus(pngPath, "None");
                D2RSpriteCodec.SaveStaticRgbaSprite(source, spritePath);
            }

            byte[] header = ReadHeader(spritePath);
            AssertEqual("SPa1", System.Text.Encoding.ASCII.GetString(header, 0, 4), "A Sprite created without a template must use the vanilla-compatible default magic.");
            AssertEqual(1, BitConverter.ToInt32(header, 0x14), "A PNG without a template must become a one-frame Sprite.");
            AssertEqual(17, BitConverter.ToUInt16(header, 0x06), "Static frame width mismatch.");
        }

        private static void TestNonDivisibleWidthPreservesOriginal(string root)
        {
            string dir = CreateCaseDirectory(root, "non_divisible");
            string spritePath = Path.Combine(dir, "non_divisible.sprite");
            WriteTemplate(spritePath, Magic("SpA1"), 31, 3, 9, 2, 3, 0, 0);
            byte[] original = File.ReadAllBytes(spritePath);

            bool failed = false;
            using (Bitmap source = CreatePatternBitmap(10, 2))
            {
                try
                {
                    D2RSpriteCodec.SaveRgbaSpriteUsingTemplate(source, spritePath, spritePath);
                }
                catch (InvalidOperationException)
                {
                    failed = true;
                }
            }

            if (!failed) throw new Exception("A non-divisible PNG width was accepted unexpectedly.");
            AssertBytesEqual(original, File.ReadAllBytes(spritePath), "The existing Sprite changed after a rejected conversion.");
        }

        private static void AssertLookupStatus(string pngPath, string expected)
        {
            MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            MethodInfo method = typeof(MainForm).GetMethod("FindFrameTemplateForPng", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new Exception("FindFrameTemplateForPng was not found.");

            object[] arguments = new object[] { pngPath, null, null, null };
            object result = method.Invoke(form, arguments);
            string actual = result == null ? string.Empty : result.ToString();
            AssertEqual(expected, actual, "Unexpected frame-template lookup status for " + Path.GetFileName(pngPath) + ".");
        }

        private static Bitmap CreatePatternBitmap(int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int alpha = ((x + y) % 11 == 0) ? 0 : 255;
                    int red = alpha == 0 ? 0 : ((x * 3 + y * 19) & 255);
                    int green = alpha == 0 ? 0 : ((x * 29 + y * 5) & 255);
                    int blue = alpha == 0 ? 0 : ((x * 7 + y * 13) & 255);
                    bitmap.SetPixel(x, y, Color.FromArgb(alpha, red, green, blue));
                }
            }
            return bitmap;
        }

        private static void AssertBitmapsEqual(Bitmap expected, Bitmap actual)
        {
            AssertEqual(expected.Width, actual.Width, "Round-trip width mismatch.");
            AssertEqual(expected.Height, actual.Height, "Round-trip height mismatch.");

            for (int y = 0; y < expected.Height; y++)
            {
                for (int x = 0; x < expected.Width; x++)
                {
                    if (expected.GetPixel(x, y).ToArgb() != actual.GetPixel(x, y).ToArgb())
                    {
                        throw new Exception("Round-trip pixel mismatch at " + x + "," + y + ".");
                    }
                }
            }
        }

        private static void WriteTemplate(string path, byte[] magic, ushort version, ushort frameWidth, int width, int height, int frameCount, int field20, int field24)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                writer.Write(magic);
                writer.Write(version);
                writer.Write(frameWidth);
                writer.Write(width);
                writer.Write(height);
                writer.Write(0);
                writer.Write(frameCount);
                writer.Write(0);
                writer.Write(0);
                writer.Write(field20);
                writer.Write(field24);
            }
        }

        private static byte[] ReadHeader(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < HeaderSize) throw new Exception("Sprite header is too short: " + path);
            byte[] header = new byte[HeaderSize];
            Buffer.BlockCopy(bytes, 0, header, 0, header.Length);
            return header;
        }

        private static byte[] Magic(string text)
        {
            return System.Text.Encoding.ASCII.GetBytes(text);
        }

        private static string CreateCaseDirectory(string root, string name)
        {
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void AssertBytesEqual(byte[] expected, byte[] actual, string message)
        {
            if (expected.Length != actual.Length) throw new Exception(message);
            for (int i = 0; i < expected.Length; i++)
            {
                if (expected[i] != actual[i]) throw new Exception(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new Exception(message + " Expected: " + expected + ", actual: " + actual);
            }
        }
    }
}
