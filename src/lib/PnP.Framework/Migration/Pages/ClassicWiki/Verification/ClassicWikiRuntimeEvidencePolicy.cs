using AngleSharp;
using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PnP.Framework.Migration.Pages.ClassicWiki.Verification
{
    public sealed class ClassicWikiRuntimeEvidencePolicy : INativePageRuntimeEvidencePolicy
    {
        private const int MaximumScreenshotBytes = 32 * 1024 * 1024;
        private const int MaximumImageDimension = 16384;
        private const int MaximumDecodedRasterBytes = 64 * 1024 * 1024;

        public string ProfileId => NativePageRuntimeContract.ClassicWikiProfile;

        public string PolicyVersion => NativePageRuntimeContract.ClassicWikiPolicyVersion;

        public void ValidateBinding(NativePageRuntimeBinding binding, IMigrationArtifactStore artifactStore)
        {
            if (binding == null)
            {
                throw new InvalidDataException("A Classic Wiki native runtime binding is required.");
            }
            if (!string.Equals(binding.ProfileId, ProfileId, StringComparison.Ordinal)
                || !string.Equals(binding.PolicyVersion, PolicyVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The runtime binding profile/policy is foreign to Classic Wiki.");
            }
            if (artifactStore == null)
            {
                throw new InvalidDataException("The Classic Wiki runtime policy requires an exact artifact resolver.");
            }
        }

        public bool VerifyResult(
            NativePageRuntimeBinding binding,
            RuntimeVerificationResult result,
            IMigrationArtifactStore artifactStore)
        {
            ValidateBinding(binding, artifactStore);
            if (result == null || result.Http == null)
            {
                return false;
            }
            if (string.Equals(result.RequirementId, NativePageRuntimeContract.ScreenshotRequirementId, StringComparison.Ordinal))
            {
                return HasValidScreenshot(result, artifactStore);
            }

            var html = ReadText(artifactStore, result.EvidenceArtifactSha256);
            var dom = ReadText(artifactStore, result.DomProbeArtifactSha256);
            if (!IsSuccessfulHtml(result)
                || ContainsDenialOrErrorShell(html)
                || !TryReadDom(
                    dom,
                    out var surface,
                    out var readyState,
                    out var errorShell,
                    out var observedUrl,
                    out var authoredContent))
            {
                return false;
            }

            if (string.Equals(result.RequirementId, NativePageRuntimeContract.ErrorShellRequirementId, StringComparison.Ordinal))
            {
                return !errorShell;
            }
            if (string.Equals(result.RequirementId, NativePageRuntimeContract.RuntimeRequirementId, StringComparison.Ordinal))
            {
                return !errorShell
                    && string.Equals(surface, "classic-wiki", StringComparison.Ordinal)
                    && string.Equals(readyState, "complete", StringComparison.Ordinal)
                    && string.Equals(observedUrl, result.Http.FinalUrl, StringComparison.Ordinal)
                    && string.Equals(
                        MigrationDigest.ComputeSha256(authoredContent),
                        binding.ExpectedAuthoredContentSha256,
                        StringComparison.OrdinalIgnoreCase)
                    && ContainsAuthoredSurface(html, authoredContent);
            }
            return false;
        }

        private static bool HasValidScreenshot(RuntimeVerificationResult result, IMigrationArtifactStore store)
        {
            if (string.IsNullOrWhiteSpace(result.ScreenshotArtifactSha256)
                || !result.ScreenshotArtifactLength.HasValue
                || result.ScreenshotArtifactLength.Value <= 0
                || !store.Contains(result.ScreenshotArtifactSha256))
            {
                return false;
            }
            var bytes = ReadBytes(store, result.ScreenshotArtifactSha256);
            return bytes != null
                && bytes.LongLength == result.ScreenshotArtifactLength.Value
                && string.Equals(
                    MigrationDigest.ComputeSha256(bytes),
                    result.ScreenshotArtifactSha256,
                    StringComparison.OrdinalIgnoreCase)
                && IsSupportedImage(bytes);
        }

        private static bool IsSuccessfulHtml(RuntimeVerificationResult result)
        {
            return result.Http.StatusCode >= 200
                && result.Http.StatusCode < 300
                && result.Http.ContentType?.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsDenialOrErrorShell(string value)
        {
            var text = ExtractRenderedText(value);
            return text.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("sign in", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("unauthorized", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("forbidden", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("errorshell", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("error-shell", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryReadDom(
            string json,
            out string surface,
            out string readyState,
            out bool errorShell,
            out string observedUrl,
            out string authoredContent)
        {
            surface = null;
            readyState = null;
            errorShell = true;
            observedUrl = null;
            authoredContent = null;
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object
                        || !TryGetString(root, "schemaVersion", out var schemaVersion)
                        || !string.Equals(schemaVersion, "pnp-classic-wiki-runtime-dom/v1", StringComparison.Ordinal)
                        || !TryGetString(root, "surface", out surface)
                        || !TryGetString(root, "readyState", out readyState)
                        || !TryGetString(root, "observedUrl", out observedUrl)
                        || !TryGetString(root, "authoredContent", out authoredContent)
                        || !root.TryGetProperty("errorShell", out var errorValue)
                        || errorValue.ValueKind != JsonValueKind.False && errorValue.ValueKind != JsonValueKind.True)
                    {
                        return false;
                    }
                    errorShell = errorValue.GetBoolean();
                    return root.EnumerateObject().Count() == 6;
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryGetString(JsonElement root, string name, out string value)
        {
            value = null;
            return root.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value = property.GetString());
        }

        private static string DecodeHtml(string value)
        {
            var current = value ?? string.Empty;
            for (var index = 0; index < 3; index++)
            {
                var decoded = WebUtility.HtmlDecode(current);
                if (string.Equals(decoded, current, StringComparison.Ordinal))
                {
                    break;
                }
                current = decoded;
            }
            return current;
        }

        private static bool ContainsAuthoredSurface(string html, string authoredContent)
        {
            var expected = ExtractRenderedText(authoredContent);
            var observed = ExtractRenderedText(html);
            return !string.IsNullOrWhiteSpace(expected)
                && observed.IndexOf(expected, StringComparison.Ordinal) >= 0;
        }

        private static bool IsSupportedImage(byte[] bytes)
        {
            return CanDecodeBoundedPngRaster(bytes) || HasBoundedJpegRaster(bytes);
        }

        private static string ExtractRenderedText(string html)
        {
            try
            {
                var configuration = Configuration.Default.WithCss();
                var context = BrowsingContext.New(configuration);
                var document = new HtmlParser(new HtmlParserOptions { IsEmbedded = true }, context)
                    .ParseDocument(DecodeHtml(html));
                var rendered = new StringBuilder();
                AppendVisibleRenderedText(document.Body ?? document.DocumentElement, rendered);
                return NormalizeRenderedText(rendered.ToString());
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AppendVisibleRenderedText(INode node, StringBuilder result)
        {
            if (node == null)
            {
                return;
            }
            if (node is IText text)
            {
                result.Append(text.Data);
                return;
            }
            if (node is IElement element)
            {
                if (!IsPotentiallyVisible(element))
                {
                    return;
                }
                var boundary = IsRenderedTextBoundary(element.LocalName);
                if (boundary)
                {
                    AppendTextBoundary(result);
                }
                if (string.Equals(element.LocalName, "br", StringComparison.OrdinalIgnoreCase))
                {
                    AppendTextBoundary(result);
                    return;
                }
                foreach (var child in element.ChildNodes)
                {
                    AppendVisibleRenderedText(child, result);
                }
                if (boundary)
                {
                    AppendTextBoundary(result);
                }
                return;
            }
            foreach (var child in node.ChildNodes)
            {
                AppendVisibleRenderedText(child, result);
            }
        }

        private static bool IsPotentiallyVisible(IElement element)
        {
            var name = element.LocalName;
            if (string.Equals(name, "script", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "style", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "noscript", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "template", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "head", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "meta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "link", StringComparison.OrdinalIgnoreCase)
                || element.HasAttribute("hidden")
                || string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(name, "input", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var style = element.ComputeCurrentStyle();
            if (style == null)
            {
                return false;
            }
            var display = style.GetPropertyValue("display")?.Trim();
            var visibility = style.GetPropertyValue("visibility")?.Trim();
            var contentVisibility = style.GetPropertyValue("content-visibility")?.Trim();
            var opacity = style.GetPropertyValue("opacity")?.Trim();
            if (string.Equals(display, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(visibility, "hidden", StringComparison.OrdinalIgnoreCase)
                || string.Equals(visibility, "collapse", StringComparison.OrdinalIgnoreCase)
                || string.Equals(contentVisibility, "hidden", StringComparison.OrdinalIgnoreCase)
                || double.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacityValue)
                    && opacityValue <= 0)
            {
                return false;
            }
            return true;
        }

        private static bool IsRenderedTextBoundary(string localName)
        {
            switch ((localName ?? string.Empty).ToLowerInvariant())
            {
                case "address":
                case "article":
                case "aside":
                case "blockquote":
                case "caption":
                case "dd":
                case "div":
                case "dl":
                case "dt":
                case "fieldset":
                case "figcaption":
                case "figure":
                case "footer":
                case "form":
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                case "header":
                case "hr":
                case "li":
                case "main":
                case "nav":
                case "ol":
                case "p":
                case "pre":
                case "section":
                case "table":
                case "tbody":
                case "td":
                case "tfoot":
                case "th":
                case "thead":
                case "tr":
                case "ul":
                    return true;
                default:
                    return false;
            }
        }

        private static void AppendTextBoundary(StringBuilder result)
        {
            if (result.Length > 0 && !char.IsWhiteSpace(result[result.Length - 1]))
            {
                result.Append(' ');
            }
        }

        private static string NormalizeRenderedText(string value)
        {
            var result = new StringBuilder();
            var pendingSpace = false;
            foreach (var character in value ?? string.Empty)
            {
                if (char.IsWhiteSpace(character) || character == '\u00a0')
                {
                    pendingSpace = result.Length > 0;
                }
                else
                {
                    if (pendingSpace)
                    {
                        result.Append(' ');
                        pendingSpace = false;
                    }
                    result.Append(character);
                }
            }
            return result.ToString().Trim();
        }

        private static bool CanDecodeBoundedPngRaster(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 45 || bytes.Length > MaximumScreenshotBytes
                || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4e || bytes[3] != 0x47
                || bytes[4] != 0x0d || bytes[5] != 0x0a || bytes[6] != 0x1a || bytes[7] != 0x0a)
            {
                return false;
            }
            var offset = 8;
            var sawHeader = false;
            var sawImageData = false;
            var finishedImageData = false;
            var sawPalette = false;
            var width = 0;
            var height = 0;
            byte bitDepth = 0;
            byte colorType = 0;
            using (var imageData = new MemoryStream())
            while (offset + 12 <= bytes.Length)
            {
                var length = ReadBigEndianInt32(bytes, offset);
                if (length < 0 || offset + 12L + length > bytes.Length)
                {
                    return false;
                }
                var typeOffset = offset + 4;
                var dataOffset = offset + 8;
                var expectedCrc = ReadBigEndianUInt32(bytes, dataOffset + length);
                if (ComputePngCrc(bytes, typeOffset, length + 4) != expectedCrc)
                {
                    return false;
                }
                for (var typeIndex = 0; typeIndex < 4; typeIndex++)
                {
                    var typeByte = bytes[typeOffset + typeIndex];
                    if (!((typeByte >= 'A' && typeByte <= 'Z') || (typeByte >= 'a' && typeByte <= 'z')))
                    {
                        return false;
                    }
                }
                if (bytes[typeOffset + 2] >= 'a' && bytes[typeOffset + 2] <= 'z')
                {
                    return false;
                }
                var type = Encoding.ASCII.GetString(bytes, typeOffset, 4);
                if (!sawHeader)
                {
                    if (!string.Equals(type, "IHDR", StringComparison.Ordinal) || length != 13
                        || (width = ReadBigEndianInt32(bytes, dataOffset)) <= 0
                        || (height = ReadBigEndianInt32(bytes, dataOffset + 4)) <= 0
                        || width > MaximumImageDimension || height > MaximumImageDimension
                        || !IsSupportedPngColorMode(
                            bitDepth = bytes[dataOffset + 8],
                            colorType = bytes[dataOffset + 9])
                        || bytes[dataOffset + 10] != 0
                        || bytes[dataOffset + 11] != 0
                        || bytes[dataOffset + 12] != 0)
                    {
                        return false;
                    }
                    sawHeader = true;
                }
                else if (string.Equals(type, "IHDR", StringComparison.Ordinal))
                {
                    return false;
                }
                else if (string.Equals(type, "PLTE", StringComparison.Ordinal))
                {
                    if (sawImageData || sawPalette || colorType == 0 || colorType == 4
                        || length == 0 || length > 768 || length % 3 != 0)
                    {
                        return false;
                    }
                    sawPalette = true;
                }
                else if (string.Equals(type, "IDAT", StringComparison.Ordinal))
                {
                    if (finishedImageData || length == 0)
                    {
                        return false;
                    }
                    sawImageData = true;
                    imageData.Write(bytes, dataOffset, length);
                }
                else if (string.Equals(type, "IEND", StringComparison.Ordinal))
                {
                    if (length != 0 || !sawImageData || offset + 12 != bytes.Length
                        || colorType == 3 && !sawPalette)
                    {
                        return false;
                    }
                    return TryDecodePngRaster(
                        imageData.ToArray(),
                        width,
                        height,
                        GetPngBitsPerPixel(bitDepth, colorType));
                }
                else
                {
                    if (sawImageData)
                    {
                        finishedImageData = true;
                    }
                    if (char.IsUpper(type[0]))
                    {
                        return false;
                    }
                }
                offset += 12 + length;
            }
            return false;
        }

        private static bool IsSupportedPngColorMode(byte bitDepth, byte colorType)
        {
            switch (colorType)
            {
                case 0: return bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8 || bitDepth == 16;
                case 2: return bitDepth == 8 || bitDepth == 16;
                case 3: return bitDepth == 1 || bitDepth == 2 || bitDepth == 4 || bitDepth == 8;
                case 4: return bitDepth == 8 || bitDepth == 16;
                case 6: return bitDepth == 8 || bitDepth == 16;
                default: return false;
            }
        }

        private static int GetPngBitsPerPixel(byte bitDepth, byte colorType)
        {
            switch (colorType)
            {
                case 0:
                case 3:
                    return bitDepth;
                case 2:
                    return bitDepth * 3;
                case 4:
                    return bitDepth * 2;
                case 6:
                    return bitDepth * 4;
                default:
                    return 0;
            }
        }

        private static bool TryDecodePngRaster(byte[] compressed, int width, int height, int bitsPerPixel)
        {
            if (compressed == null || compressed.Length < 6 || bitsPerPixel <= 0)
            {
                return false;
            }
            var cmf = compressed[0];
            var flags = compressed[1];
            if ((cmf & 0x0f) != 8 || (cmf >> 4) > 7
                || ((cmf << 8) + flags) % 31 != 0 || (flags & 0x20) != 0)
            {
                return false;
            }
            var rowBytes = ((long)width * bitsPerPixel + 7) / 8;
            var expectedLength = (rowBytes + 1) * height;
            if (rowBytes <= 0 || expectedLength <= 0 || expectedLength > MaximumDecodedRasterBytes)
            {
                return false;
            }
            if (!HasCompleteDeflateStream(compressed, 2, compressed.Length - 6, expectedLength))
            {
                return false;
            }
            try
            {
                byte[] decoded;
                using (var input = new MemoryStream(compressed, 2, compressed.Length - 6, false))
                using (var inflater = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream((int)expectedLength))
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = inflater.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > expectedLength)
                        {
                            return false;
                        }
                        output.Write(buffer, 0, read);
                    }
                    if (output.Length != expectedLength)
                    {
                        return false;
                    }
                    decoded = output.ToArray();
                }
                for (long row = 0; row < height; row++)
                {
                    if (decoded[row * (rowBytes + 1)] > 4)
                    {
                        return false;
                    }
                }
                return ReadBigEndianUInt32(compressed, compressed.Length - 4) == ComputeAdler32(decoded);
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static bool HasCompleteDeflateStream(byte[] bytes, int offset, int length, long expectedLength)
        {
            var reader = new DeflateBitReader(bytes, offset, length);
            long produced = 0;
            while (reader.TryReadBits(1, out var finalValue)
                && reader.TryReadBits(2, out var blockType))
            {
                if (blockType == 0)
                {
                    if (!reader.TryAlignToByte()
                        || !reader.TryReadBits(16, out var storedLength)
                        || !reader.TryReadBits(16, out var inverseLength)
                        || (storedLength ^ 0xffff) != inverseLength
                        || !reader.TrySkipBytes(storedLength))
                    {
                        return false;
                    }
                    produced += storedLength;
                }
                else if (blockType == 1 || blockType == 2)
                {
                    if (!TryReadDeflateTrees(reader, blockType, out var literalTree, out var distanceTree)
                        || !TryReadDeflateCompressedBlock(
                            reader,
                            literalTree,
                            distanceTree,
                            ref produced,
                            expectedLength))
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                if (produced > expectedLength)
                {
                    return false;
                }
                if (finalValue != 0)
                {
                    return produced == expectedLength && reader.BytesConsumed == length;
                }
            }
            return false;
        }

        private static bool TryReadDeflateTrees(
            DeflateBitReader reader,
            int blockType,
            out DeflateHuffmanTree literalTree,
            out DeflateHuffmanTree distanceTree)
        {
            literalTree = null;
            distanceTree = null;
            if (blockType == 1)
            {
                var literalLengths = new int[288];
                for (var symbol = 0; symbol <= 143; symbol++) literalLengths[symbol] = 8;
                for (var symbol = 144; symbol <= 255; symbol++) literalLengths[symbol] = 9;
                for (var symbol = 256; symbol <= 279; symbol++) literalLengths[symbol] = 7;
                for (var symbol = 280; symbol <= 287; symbol++) literalLengths[symbol] = 8;
                var distanceLengths = Enumerable.Repeat(5, 32).ToArray();
                return DeflateHuffmanTree.TryCreate(literalLengths, out literalTree)
                    && DeflateHuffmanTree.TryCreate(distanceLengths, out distanceTree);
            }

            if (!reader.TryReadBits(5, out var literalCountValue)
                || !reader.TryReadBits(5, out var distanceCountValue)
                || !reader.TryReadBits(4, out var codeLengthCountValue))
            {
                return false;
            }
            var literalCount = literalCountValue + 257;
            var distanceCount = distanceCountValue + 1;
            var codeLengthCount = codeLengthCountValue + 4;
            var codeLengthOrder = new[] { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };
            var codeLengths = new int[19];
            for (var index = 0; index < codeLengthCount; index++)
            {
                if (!reader.TryReadBits(3, out codeLengths[codeLengthOrder[index]]))
                {
                    return false;
                }
            }
            if (!DeflateHuffmanTree.TryCreate(codeLengths, out var codeLengthTree))
            {
                return false;
            }

            var allLengths = new List<int>(literalCount + distanceCount);
            while (allLengths.Count < literalCount + distanceCount)
            {
                if (!codeLengthTree.TryReadSymbol(reader, out var symbol))
                {
                    return false;
                }
                if (symbol <= 15)
                {
                    allLengths.Add(symbol);
                    continue;
                }
                int repeat;
                int value;
                if (symbol == 16)
                {
                    if (allLengths.Count == 0 || !reader.TryReadBits(2, out var extra)) return false;
                    repeat = extra + 3;
                    value = allLengths[allLengths.Count - 1];
                }
                else if (symbol == 17)
                {
                    if (!reader.TryReadBits(3, out var extra)) return false;
                    repeat = extra + 3;
                    value = 0;
                }
                else if (symbol == 18)
                {
                    if (!reader.TryReadBits(7, out var extra)) return false;
                    repeat = extra + 11;
                    value = 0;
                }
                else
                {
                    return false;
                }
                if (allLengths.Count + repeat > literalCount + distanceCount)
                {
                    return false;
                }
                for (var index = 0; index < repeat; index++) allLengths.Add(value);
            }

            var literalLengthsResult = allLengths.Take(literalCount).ToArray();
            var distanceLengthsResult = allLengths.Skip(literalCount).Take(distanceCount).ToArray();
            return literalLengthsResult.Length > 256
                && literalLengthsResult[256] != 0
                && DeflateHuffmanTree.TryCreate(literalLengthsResult, out literalTree)
                && DeflateHuffmanTree.TryCreate(distanceLengthsResult, out distanceTree);
        }

        private static bool TryReadDeflateCompressedBlock(
            DeflateBitReader reader,
            DeflateHuffmanTree literalTree,
            DeflateHuffmanTree distanceTree,
            ref long produced,
            long expectedLength)
        {
            var lengthBases = new[]
            {
                3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27,
                31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
            };
            var lengthExtras = new[]
            {
                0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2,
                2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
            };
            var distanceBases = new[]
            {
                1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129,
                193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
                8193, 12289, 16385, 24577
            };
            var distanceExtras = new[]
            {
                0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6,
                6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
            };

            while (literalTree.TryReadSymbol(reader, out var symbol))
            {
                if (symbol < 256)
                {
                    produced++;
                }
                else if (symbol == 256)
                {
                    return true;
                }
                else
                {
                    var lengthIndex = symbol - 257;
                    if (lengthIndex < 0 || lengthIndex >= lengthBases.Length
                        || !reader.TryReadBits(lengthExtras[lengthIndex], out var lengthExtra)
                        || !distanceTree.TryReadSymbol(reader, out var distanceSymbol)
                        || distanceSymbol < 0 || distanceSymbol >= distanceBases.Length
                        || !reader.TryReadBits(distanceExtras[distanceSymbol], out var distanceExtra))
                    {
                        return false;
                    }
                    var matchLength = lengthBases[lengthIndex] + lengthExtra;
                    var matchDistance = distanceBases[distanceSymbol] + distanceExtra;
                    if (matchDistance > produced)
                    {
                        return false;
                    }
                    produced += matchLength;
                }
                if (produced > expectedLength)
                {
                    return false;
                }
            }
            return false;
        }

        private sealed class DeflateBitReader
        {
            private readonly byte[] bytes;
            private readonly int offset;
            private readonly int bitLength;
            private int bitOffset;

            public DeflateBitReader(byte[] bytes, int offset, int length)
            {
                this.bytes = bytes;
                this.offset = offset;
                bitLength = length * 8;
            }

            public int BytesConsumed => (bitOffset + 7) / 8;

            public bool TryReadBits(int count, out int value)
            {
                value = 0;
                if (count < 0 || count > 16 || bitOffset + count > bitLength)
                {
                    return false;
                }
                for (var bit = 0; bit < count; bit++)
                {
                    value |= (bytes[offset + (bitOffset >> 3)] >> (bitOffset & 7) & 1) << bit;
                    bitOffset++;
                }
                return true;
            }

            public bool TryAlignToByte()
            {
                bitOffset = (bitOffset + 7) & ~7;
                return bitOffset <= bitLength;
            }

            public bool TrySkipBytes(int count)
            {
                if (count < 0 || (long)bitOffset + count * 8L > bitLength)
                {
                    return false;
                }
                bitOffset += count * 8;
                return true;
            }
        }

        private sealed class DeflateHuffmanTree
        {
            private readonly Dictionary<int, int> symbols;
            private readonly int maximumBits;

            private DeflateHuffmanTree(Dictionary<int, int> symbols, int maximumBits)
            {
                this.symbols = symbols;
                this.maximumBits = maximumBits;
            }

            public static bool TryCreate(int[] lengths, out DeflateHuffmanTree tree)
            {
                tree = null;
                if (lengths == null || lengths.Length == 0)
                {
                    return false;
                }
                var counts = new int[16];
                foreach (var length in lengths)
                {
                    if (length < 0 || length > 15) return false;
                    if (length > 0) counts[length]++;
                }
                var total = counts.Sum();
                if (total == 0)
                {
                    return false;
                }
                var remaining = 1;
                for (var bits = 1; bits <= 15; bits++)
                {
                    remaining = (remaining << 1) - counts[bits];
                    if (remaining < 0) return false;
                }
                var nextCode = new int[16];
                var code = 0;
                for (var bits = 1; bits <= 15; bits++)
                {
                    code = (code + counts[bits - 1]) << 1;
                    nextCode[bits] = code;
                }
                var table = new Dictionary<int, int>();
                var maximumBits = 0;
                for (var symbol = 0; symbol < lengths.Length; symbol++)
                {
                    var length = lengths[symbol];
                    if (length == 0) continue;
                    var reversedCode = ReverseBits(nextCode[length]++, length);
                    table.Add(length << 16 | reversedCode, symbol);
                    maximumBits = Math.Max(maximumBits, length);
                }
                tree = new DeflateHuffmanTree(table, maximumBits);
                return true;
            }

            public bool TryReadSymbol(DeflateBitReader reader, out int symbol)
            {
                symbol = -1;
                var code = 0;
                for (var length = 1; length <= maximumBits; length++)
                {
                    if (!reader.TryReadBits(1, out var bit)) return false;
                    code |= bit << (length - 1);
                    if (symbols.TryGetValue(length << 16 | code, out symbol)) return true;
                }
                return false;
            }

            private static int ReverseBits(int value, int count)
            {
                var result = 0;
                for (var bit = 0; bit < count; bit++)
                {
                    result = result << 1 | value >> bit & 1;
                }
                return result;
            }
        }

        private static uint ComputeAdler32(byte[] bytes)
        {
            const uint modulus = 65521;
            uint a = 1;
            uint b = 0;
            foreach (var value in bytes)
            {
                a = (a + value) % modulus;
                b = (b + a) % modulus;
            }
            return b << 16 | a;
        }

        private static bool HasBoundedJpegRaster(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12 || bytes.Length > MaximumScreenshotBytes
                || bytes[0] != 0xff || bytes[1] != 0xd8
                || bytes[bytes.Length - 2] != 0xff || bytes[bytes.Length - 1] != 0xd9)
            {
                return false;
            }
            var offset = 2;
            var sawFrame = false;
            var sawQuantizationTable = false;
            var sawHuffmanTable = false;
            var frame = new JpegFrame();
            var quantizationTables = new HashSet<byte>();
            var dcHuffmanTables = new Dictionary<byte, JpegHuffmanTable>();
            var acHuffmanTables = new Dictionary<byte, JpegHuffmanTable>();
            while (offset < bytes.Length)
            {
                if (offset + 1 >= bytes.Length || bytes[offset++] != 0xff)
                {
                    return false;
                }
                while (offset < bytes.Length && bytes[offset] == 0xff)
                {
                    offset++;
                }
                if (offset >= bytes.Length)
                {
                    return false;
                }
                var marker = bytes[offset++];
                if (marker == 0xd9)
                {
                    return false;
                }
                if (marker == 0x00 || marker == 0x01 || marker >= 0xd0 && marker <= 0xd8)
                {
                    return false;
                }
                if (offset + 2 > bytes.Length)
                {
                    return false;
                }
                var length = bytes[offset] << 8 | bytes[offset + 1];
                if (length < 2 || offset + length > bytes.Length)
                {
                    return false;
                }
                var segmentStart = offset + 2;
                var segmentEnd = offset + length;
                if (marker == 0xdb)
                {
                    if (!ValidateJpegQuantizationTables(bytes, segmentStart, segmentEnd, quantizationTables))
                    {
                        return false;
                    }
                    sawQuantizationTable = true;
                }
                else if (marker == 0xc4)
                {
                    if (!ValidateJpegHuffmanTables(
                        bytes,
                        segmentStart,
                        segmentEnd,
                        dcHuffmanTables,
                        acHuffmanTables))
                    {
                        return false;
                    }
                    sawHuffmanTable = true;
                }
                else if (IsJpegStartOfFrame(marker))
                {
                    if (sawFrame || !ValidateJpegFrame(bytes, segmentStart, segmentEnd, frame))
                    {
                        return false;
                    }
                    sawFrame = true;
                }
                else if (marker == 0xda)
                {
                    if (!sawFrame || !sawQuantizationTable || !sawHuffmanTable
                        || frame.Components.Values.Any(component =>
                            !quantizationTables.Contains(component.QuantizationTableId))
                        || !ValidateJpegScanHeader(
                            bytes,
                            segmentStart,
                            segmentEnd,
                            frame.Components,
                            dcHuffmanTables,
                            acHuffmanTables,
                            out var scanComponents))
                    {
                        return false;
                    }
                    return TryDecodeJpegBaselineScan(
                            bytes,
                            segmentEnd,
                            frame,
                            scanComponents,
                            dcHuffmanTables,
                            acHuffmanTables,
                            out var scanEnd)
                        && scanEnd + 2 == bytes.Length
                        && bytes[scanEnd] == 0xff
                        && bytes[scanEnd + 1] == 0xd9;
                }
                offset = segmentEnd;
            }
            return false;
        }

        private static bool IsJpegStartOfFrame(byte marker)
        {
            return marker == 0xc0 || marker == 0xc1;
        }

        private static bool ValidateJpegQuantizationTables(
            byte[] bytes,
            int offset,
            int end,
            ISet<byte> tables)
        {
            var sawTable = false;
            while (offset < end)
            {
                var descriptor = bytes[offset++];
                var precision = descriptor >> 4;
                var tableId = (byte)(descriptor & 0x0f);
                if (precision > 1 || tableId > 3)
                {
                    return false;
                }
                var tableBytes = precision == 0 ? 64 : 128;
                if (offset + tableBytes > end)
                {
                    return false;
                }
                offset += tableBytes;
                tables.Add(tableId);
                sawTable = true;
            }
            return sawTable && offset == end;
        }

        private static bool ValidateJpegHuffmanTables(
            byte[] bytes,
            int offset,
            int end,
            IDictionary<byte, JpegHuffmanTable> dcTables,
            IDictionary<byte, JpegHuffmanTable> acTables)
        {
            var sawTable = false;
            while (offset < end)
            {
                var descriptor = bytes[offset++];
                var tableClass = descriptor >> 4;
                var tableId = (byte)(descriptor & 0x0f);
                if (tableClass > 1 || tableId > 3 || offset + 16 > end)
                {
                    return false;
                }
                var counts = new byte[16];
                var symbols = 0;
                var nextCode = 0;
                for (var index = 0; index < 16; index++)
                {
                    counts[index] = bytes[offset + index];
                    symbols += counts[index];
                    if (nextCode + counts[index] > (1 << (index + 1)))
                    {
                        return false;
                    }
                    nextCode = (nextCode + counts[index]) << 1;
                }
                offset += 16;
                if (symbols == 0 || symbols > 256 || offset + symbols > end)
                {
                    return false;
                }
                var target = tableClass == 0 ? dcTables : acTables;
                if (target.ContainsKey(tableId))
                {
                    return false;
                }
                var table = JpegHuffmanTable.Create(counts, bytes, offset, symbols);
                if (table == null)
                {
                    return false;
                }
                offset += symbols;
                target.Add(tableId, table);
                sawTable = true;
            }
            return sawTable && offset == end;
        }

        private static bool ValidateJpegFrame(byte[] bytes, int offset, int end, JpegFrame frame)
        {
            if (end - offset < 6 || bytes[offset] != 8)
            {
                return false;
            }
            var height = bytes[offset + 1] << 8 | bytes[offset + 2];
            var width = bytes[offset + 3] << 8 | bytes[offset + 4];
            var count = bytes[offset + 5];
            if (height <= 0 || width <= 0 || height > MaximumImageDimension || width > MaximumImageDimension
                || count < 1 || count > 4 || end - offset != 6 + count * 3)
            {
                return false;
            }
            frame.Width = width;
            frame.Height = height;
            frame.Components.Clear();
            offset += 6;
            for (var index = 0; index < count; index++)
            {
                var id = bytes[offset];
                var sampling = bytes[offset + 1];
                var quantizationTableId = bytes[offset + 2];
                if (frame.Components.ContainsKey(id) || (sampling >> 4) == 0 || (sampling >> 4) > 4
                    || (sampling & 0x0f) == 0 || (sampling & 0x0f) > 4 || quantizationTableId > 3)
                {
                    return false;
                }
                frame.Components.Add(id, new JpegFrameComponent
                {
                    HorizontalSampling = sampling >> 4,
                    VerticalSampling = sampling & 0x0f,
                    QuantizationTableId = quantizationTableId
                });
                offset += 3;
            }
            return true;
        }

        private static bool ValidateJpegScanHeader(
            byte[] bytes,
            int offset,
            int end,
            IDictionary<byte, JpegFrameComponent> frameComponents,
            IDictionary<byte, JpegHuffmanTable> dcTables,
            IDictionary<byte, JpegHuffmanTable> acTables,
            out IList<JpegScanComponent> scanComponents)
        {
            scanComponents = null;
            if (offset >= end)
            {
                return false;
            }
            var count = bytes[offset++];
            if (count < 1 || count > frameComponents.Count || end - offset != count * 2 + 3)
            {
                return false;
            }
            var componentIds = new HashSet<byte>();
            var values = new List<JpegScanComponent>();
            for (var index = 0; index < count; index++)
            {
                var id = bytes[offset++];
                var tables = bytes[offset++];
                if (!frameComponents.ContainsKey(id) || !componentIds.Add(id)
                    || (tables >> 4) > 3 || (tables & 0x0f) > 3)
                {
                    return false;
                }
                values.Add(new JpegScanComponent
                {
                    ComponentId = id,
                    DcTableId = (byte)(tables >> 4),
                    AcTableId = (byte)(tables & 0x0f)
                });
            }
            var spectralStart = bytes[offset++];
            var spectralEnd = bytes[offset++];
            var approximation = bytes[offset];
            if (spectralStart != 0 || spectralEnd != 63 || approximation != 0
                || count != frameComponents.Count)
            {
                return false;
            }
            if (!values.All(value => dcTables.ContainsKey(value.DcTableId)
                && acTables.ContainsKey(value.AcTableId)))
            {
                return false;
            }
            scanComponents = values;
            return true;
        }

        private static bool TryDecodeJpegBaselineScan(
            byte[] bytes,
            int offset,
            JpegFrame frame,
            IList<JpegScanComponent> scanComponents,
            IDictionary<byte, JpegHuffmanTable> dcTables,
            IDictionary<byte, JpegHuffmanTable> acTables,
            out int scanEnd)
        {
            scanEnd = offset;
            if (frame == null || frame.Width <= 0 || frame.Height <= 0
                || frame.Components.Count == 0 || scanComponents == null
                || scanComponents.Count != frame.Components.Count)
            {
                return false;
            }
            var maximumHorizontalSampling = frame.Components.Values.Max(value => value.HorizontalSampling);
            var maximumVerticalSampling = frame.Components.Values.Max(value => value.VerticalSampling);
            var mcuColumns = (frame.Width + 8 * maximumHorizontalSampling - 1) / (8 * maximumHorizontalSampling);
            var mcuRows = (frame.Height + 8 * maximumVerticalSampling - 1) / (8 * maximumVerticalSampling);
            var reader = new JpegEntropyBitReader(bytes, offset);
            for (var row = 0; row < mcuRows; row++)
            {
                for (var column = 0; column < mcuColumns; column++)
                {
                    foreach (var scanComponent in scanComponents)
                    {
                        var frameComponent = frame.Components[scanComponent.ComponentId];
                        var blockCount = frameComponent.HorizontalSampling * frameComponent.VerticalSampling;
                        for (var block = 0; block < blockCount; block++)
                        {
                            if (!TryDecodeJpegBlock(
                                reader,
                                dcTables[scanComponent.DcTableId],
                                acTables[scanComponent.AcTableId]))
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            return reader.TryFinishScan(out scanEnd);
        }

        private static bool TryDecodeJpegBlock(
            JpegEntropyBitReader reader,
            JpegHuffmanTable dcTable,
            JpegHuffmanTable acTable)
        {
            if (!dcTable.TryReadSymbol(reader, out var dcSize) || dcSize > 11
                || !reader.TrySkipBits(dcSize))
            {
                return false;
            }
            var coefficient = 1;
            while (coefficient < 64)
            {
                if (!acTable.TryReadSymbol(reader, out var symbol))
                {
                    return false;
                }
                if (symbol == 0)
                {
                    return true;
                }
                if (symbol == 0xf0)
                {
                    coefficient += 16;
                    if (coefficient > 64)
                    {
                        return false;
                    }
                    continue;
                }
                var zeroRun = symbol >> 4;
                var size = symbol & 0x0f;
                if (size == 0 || size > 10)
                {
                    return false;
                }
                coefficient += zeroRun;
                if (coefficient >= 64 || !reader.TrySkipBits(size))
                {
                    return false;
                }
                coefficient++;
            }
            return true;
        }

        private sealed class JpegFrame
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public IDictionary<byte, JpegFrameComponent> Components { get; }
                = new Dictionary<byte, JpegFrameComponent>();
        }

        private sealed class JpegFrameComponent
        {
            public int HorizontalSampling { get; set; }
            public int VerticalSampling { get; set; }
            public byte QuantizationTableId { get; set; }
        }

        private sealed class JpegScanComponent
        {
            public byte ComponentId { get; set; }
            public byte DcTableId { get; set; }
            public byte AcTableId { get; set; }
        }

        private sealed class JpegHuffmanTable
        {
            private readonly IDictionary<int, byte> symbols;
            private readonly int maximumCodeLength;

            private JpegHuffmanTable(IDictionary<int, byte> symbols, int maximumCodeLength)
            {
                this.symbols = symbols;
                this.maximumCodeLength = maximumCodeLength;
            }

            public static JpegHuffmanTable Create(byte[] counts, byte[] bytes, int offset, int symbolCount)
            {
                var symbols = new Dictionary<int, byte>();
                var code = 0;
                var symbolOffset = 0;
                var maximumCodeLength = 0;
                for (var length = 1; length <= 16; length++)
                {
                    var count = counts[length - 1];
                    if (code + count > 1 << length)
                    {
                        return null;
                    }
                    for (var index = 0; index < count; index++)
                    {
                        symbols.Add(length << 16 | code, bytes[offset + symbolOffset++]);
                        code++;
                        maximumCodeLength = length;
                    }
                    code <<= 1;
                }
                return symbolOffset == symbolCount
                    ? new JpegHuffmanTable(symbols, maximumCodeLength)
                    : null;
            }

            public bool TryReadSymbol(JpegEntropyBitReader reader, out byte symbol)
            {
                symbol = 0;
                var code = 0;
                for (var length = 1; length <= maximumCodeLength; length++)
                {
                    if (!reader.TryReadBit(out var bit))
                    {
                        return false;
                    }
                    code = code << 1 | bit;
                    if (symbols.TryGetValue(length << 16 | code, out symbol))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        private sealed class JpegEntropyBitReader
        {
            private readonly byte[] bytes;
            private int offset;
            private int currentByte;
            private int bitsRemaining;

            public JpegEntropyBitReader(byte[] bytes, int offset)
            {
                this.bytes = bytes;
                this.offset = offset;
            }

            public bool TryReadBit(out int bit)
            {
                bit = 0;
                if (bitsRemaining == 0 && !TryLoadByte())
                {
                    return false;
                }
                bitsRemaining--;
                bit = currentByte >> bitsRemaining & 1;
                return true;
            }

            public bool TrySkipBits(int count)
            {
                for (var index = 0; index < count; index++)
                {
                    if (!TryReadBit(out _))
                    {
                        return false;
                    }
                }
                return true;
            }

            public bool TryFinishScan(out int scanEnd)
            {
                scanEnd = offset;
                while (bitsRemaining > 0)
                {
                    if (!TryReadBit(out var bit) || bit != 1)
                    {
                        return false;
                    }
                }
                while (offset + 1 < bytes.Length && bytes[offset] == 0xff && bytes[offset + 1] == 0xff)
                {
                    offset++;
                }
                scanEnd = offset;
                return scanEnd + 1 < bytes.Length && bytes[scanEnd] == 0xff;
            }

            private bool TryLoadByte()
            {
                if (offset >= bytes.Length)
                {
                    return false;
                }
                var value = bytes[offset++];
                if (value == 0xff)
                {
                    if (offset >= bytes.Length || bytes[offset] != 0x00)
                    {
                        offset--;
                        return false;
                    }
                    offset++;
                }
                currentByte = value;
                bitsRemaining = 8;
                return true;
            }
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static uint ReadBigEndianUInt32(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        private static uint ComputePngCrc(byte[] bytes, int offset, int count)
        {
            var crc = 0xffffffffu;
            for (var index = 0; index < count; index++)
            {
                crc ^= bytes[offset + index];
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
                }
            }
            return crc ^ 0xffffffffu;
        }

        private static byte[] ReadBytes(IMigrationArtifactStore store, string digest)
        {
            if (store == null || string.IsNullOrWhiteSpace(digest) || !store.Contains(digest))
            {
                return null;
            }
            using (var input = store.OpenRead(digest))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                return copy.ToArray();
            }
        }

        private static string ReadText(IMigrationArtifactStore store, string digest)
        {
            if (store == null || string.IsNullOrWhiteSpace(digest) || !store.Contains(digest))
            {
                return null;
            }
            using (var input = store.OpenRead(digest))
            using (var copy = new MemoryStream())
            {
                input.CopyTo(copy);
                return Encoding.UTF8.GetString(copy.ToArray());
            }
        }
    }
}
