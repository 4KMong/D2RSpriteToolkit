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
            string root = Path.Combine(Path.GetTempPath(), "D2RSpriteToolkit_v403_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                TestBppZeroAndRoundTrip(root);
                TestSemiTransparentBlackShadowRoundTrip(root);
                TestSemiTransparentColorRoundTrip(root);
                TestTransparentWhiteDoesNotBleedIntoBlackShadow(root);
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


        private static void TestSemiTransparentBlackShadowRoundTrip(string root)
        {
            string dir = CreateCaseDirectory(root, "black_shadow_roundtrip");
            string pngPath = Path.Combine(dir, "black_shadow.png");
            string spritePath = Path.Combine(dir, "black_shadow.sprite");
            string roundTripPngPath = Path.Combine(dir, "black_shadow.roundtrip.png");
            int[] alphas = new int[] { 0, 1, 2, 17, 64, 127, 128, 200, 254, 255 };

            using (Bitmap original = new Bitmap(alphas.Length, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                for (int x = 0; x < alphas.Length; x++)
                {
                    original.SetPixel(x, 0, Color.FromArgb(alphas[x], 0, 0, 0));
                }
                original.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
            }

            using (Bitmap loaded = InvokeMainFormBitmapMethod("LoadSpriteSourceBitmap", pngPath))
            {
                D2RSpriteCodec.SaveStaticRgbaSprite(loaded, spritePath);
            }

            Bitmap decoded;
            D2RSpriteInfo info;
            string error;
            if (!D2RSpritePreview.TryLoadSpriteBitmap(spritePath, out decoded, out info, out error))
            {
                throw new Exception("Black-shadow round-trip decode failed: " + error);
            }

            using (decoded)
            {
                AssertBlackShadowPixels(decoded, alphas, "Sprite decode");
                InvokeMainFormVoidMethod("SavePngSafely", decoded, roundTripPngPath);
            }

            using (Bitmap roundTripPng = ImageFileUtil.LoadBitmapNoLock(roundTripPngPath))
            {
                AssertBlackShadowPixels(roundTripPng, alphas, "PNG → Sprite → PNG");
            }
        }


        private static void TestSemiTransparentColorRoundTrip(string root)
        {
            string dir = CreateCaseDirectory(root, "semi_transparent_color_roundtrip");
            string pngPath = Path.Combine(dir, "semi_transparent_colors.png");
            string spritePath = Path.Combine(dir, "semi_transparent_colors.sprite");
            string roundTripPngPath = Path.Combine(dir, "semi_transparent_colors.roundtrip.png");
            Color[] colors = new Color[]
            {
                Color.FromArgb(1, 1, 127, 255),
                Color.FromArgb(2, 255, 1, 127),
                Color.FromArgb(17, 37, 149, 223),
                Color.FromArgb(64, 201, 77, 19),
                Color.FromArgb(127, 13, 241, 101),
                Color.FromArgb(128, 250, 129, 3),
                Color.FromArgb(200, 91, 17, 239),
                Color.FromArgb(254, 173, 211, 47)
            };

            // Build the fixture without GDI+'s PNG encoder. The fixture itself must contain
            // the exact straight RGBA bytes requested above or the test can hide the bug.
            using (Bitmap original = CreateBitmapFromColorsRaw(colors))
            {
                AssertBitmapMatchesColorsRaw(original, colors, "Fixture bitmap");
                StraightRgbaPngCodec.SaveRgba8(original, pngPath);
            }

            Bitmap fixtureDecoded;
            if (!StraightRgbaPngCodec.TryLoadRgba8(pngPath, out fixtureDecoded))
                throw new Exception("Exact PNG fixture could not be decoded by StraightRgbaPngCodec.");
            using (fixtureDecoded) AssertBitmapMatchesColorsRaw(fixtureDecoded, colors, "Exact PNG fixture");

            using (Bitmap loaded = InvokeMainFormBitmapMethod("LoadSpriteSourceBitmap", pngPath))
            {
                AssertBitmapMatchesColorsRaw(loaded, colors, "Production PNG load");
                D2RSpriteCodec.SaveStaticRgbaSprite(loaded, spritePath);
            }

            Bitmap decoded;
            D2RSpriteInfo info;
            string error;
            if (!D2RSpritePreview.TryLoadSpriteBitmap(spritePath, out decoded, out info, out error))
                throw new Exception("Semi-transparent color Sprite decode failed: " + error);

            using (decoded)
            {
                AssertBitmapMatchesColorsRaw(decoded, colors, "PNG -> Sprite decode");
                InvokeMainFormVoidMethod("SavePngSafely", decoded, roundTripPngPath);
            }

            Bitmap roundTrip;
            if (!StraightRgbaPngCodec.TryLoadRgba8(roundTripPngPath, out roundTrip))
                throw new Exception("Round-trip PNG is not a valid exact RGBA8 PNG.");
            using (roundTrip) AssertBitmapMatchesColorsRaw(roundTrip, colors, "PNG -> Sprite -> PNG");
        }


        private static void TestTransparentWhiteDoesNotBleedIntoBlackShadow(string root)
        {
            string fixturePath = Path.Combine("tests", "fixtures", "transparent-white-black-shadow.png");
            if (!File.Exists(fixturePath)) throw new Exception("Black-shadow PNG fixture was not found: " + fixturePath);

            // The fixture deliberately stores invisible white RGB (255,255,255,0) around
            // a semi-transparent black shadow. A straight-alpha resize can leak that hidden
            // white into the visible edge; the production PArgb path must not.
            using (Bitmap loaded = InvokeMainFormBitmapMethod("LoadSourceBitmap", fixturePath))
            using (Bitmap resized = InvokeMainFormBitmapMethod("ResizeTransparent", loaded, 23, 23))
            {
                bool sawVisibleShadow = false;
                for (int y = 0; y < resized.Height; y++)
                {
                    for (int x = 0; x < resized.Width; x++)
                    {
                        Color actual = resized.GetPixel(x, y);
                        if (actual.A == 0) continue;
                        sawVisibleShadow = true;
                        if (actual.R != 0 || actual.G != 0 || actual.B != 0)
                        {
                            throw new Exception(
                                "Transparent-white RGB bled into black shadow at " + x + "," + y +
                                ": ARGB=" + actual.A + "," + actual.R + "," + actual.G + "," + actual.B + ".");
                        }
                    }
                }
                if (!sawVisibleShadow) throw new Exception("Black-shadow fringe test produced no visible shadow pixels.");
            }
        }

        private static Bitmap CreateBitmapFromColorsRaw(Color[] colors)
        {
            Bitmap bitmap = new Bitmap(colors.Length, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, 1);
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[colors.Length * 4];
                for (int x = 0; x < colors.Length; x++)
                {
                    int i = x * 4; Color c = colors[x];
                    row[i] = c.B; row[i + 1] = c.G; row[i + 2] = c.R; row[i + 3] = c.A;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0, row.Length);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        private static void AssertBitmapMatchesColorsRaw(Bitmap bitmap, Color[] expected, string stage)
        {
            AssertEqual(expected.Length, bitmap.Width, stage + " width changed.");
            AssertEqual(1, bitmap.Height, stage + " height changed.");
            if (bitmap.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb)
                throw new Exception(stage + " is not Format32bppArgb: " + bitmap.PixelFormat + ".");

            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            System.Drawing.Imaging.BitmapData data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] row = new byte[bitmap.Width * 4];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, row, 0, row.Length);
                for (int x = 0; x < expected.Length; x++)
                {
                    int i = x * 4;
                    Color e = expected[x];
                    int b = row[i], g = row[i + 1], r = row[i + 2], a = row[i + 3];
                    if (a != e.A || r != e.R || g != e.G || b != e.B)
                    {
                        throw new Exception(stage + " changed raw pixel at x=" + x +
                            ": expected ARGB=" + e.A + "," + e.R + "," + e.G + "," + e.B +
                            "; actual ARGB=" + a + "," + r + "," + g + "," + b + ".");
                    }
                }
            }
            finally { bitmap.UnlockBits(data); }
        }

        private static void AssertBitmapsEqualWithStage(Bitmap expected, Bitmap actual, string stage)
        {
            AssertEqual(expected.Width, actual.Width, stage + " width changed.");
            AssertEqual(expected.Height, actual.Height, stage + " height changed.");
            for (int y = 0; y < expected.Height; y++)
            {
                for (int x = 0; x < expected.Width; x++)
                {
                    Color e = expected.GetPixel(x, y);
                    Color a = actual.GetPixel(x, y);
                    if (e.ToArgb() != a.ToArgb())
                    {
                        throw new Exception(
                            stage + " changed pixel at " + x + "," + y +
                            ": expected ARGB=" + e.A + "," + e.R + "," + e.G + "," + e.B +
                            "; actual ARGB=" + a.A + "," + a.R + "," + a.G + "," + a.B + ".");
                    }
                }
            }
        }

        private static void AssertBlackShadowPixels(Bitmap bitmap, int[] alphas, string stage)
        {
            AssertEqual(alphas.Length, bitmap.Width, stage + " width changed.");
            AssertEqual(1, bitmap.Height, stage + " height changed.");
            for (int x = 0; x < alphas.Length; x++)
            {
                Color actual = bitmap.GetPixel(x, 0);
                AssertEqual(alphas[x], actual.A, stage + " alpha changed at x=" + x + ".");
                AssertEqual(0, actual.R, stage + " red channel became non-black at x=" + x + ".");
                AssertEqual(0, actual.G, stage + " green channel became non-black at x=" + x + ".");
                AssertEqual(0, actual.B, stage + " blue channel became non-black at x=" + x + ".");
            }
        }

        private static void InvokeMainFormVoidMethod(string methodName, params object[] args)
        {
            MethodInfo method = typeof(MainForm).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) throw new Exception(methodName + " was not found.");
            method.Invoke(null, args);
        }

        private static Bitmap InvokeMainFormBitmapMethod(string methodName, params object[] args)
        {
            MethodInfo method = typeof(MainForm).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) throw new Exception(methodName + " was not found.");
            object result = method.Invoke(null, args);
            Bitmap bitmap = result as Bitmap;
            if (bitmap == null) throw new Exception(methodName + " did not return a Bitmap.");
            return bitmap;
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
