using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using PnP.Framework.Migration.Packaging;
using PnP.Framework.Migration.Verification;
using PnP.Framework.Migration.Verification.NativePageRuntime;
using System;
using System.Collections.Generic;
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
                var document = new HtmlParser().ParseDocument(DecodeHtml(html));
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
                || element.HasAttribute("inert")
                || string.Equals(element.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(element.GetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(name, "input", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var style = element.GetAttribute("style");
            if (string.IsNullOrWhiteSpace(style))
            {
                return true;
            }
            foreach (var declaration in style.Split(';'))
            {
                var separator = declaration.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }
                var property = declaration.Substring(0, separator).Trim();
                var value = declaration.Substring(separator + 1).Trim();
                var important = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
                if (important >= 0)
                {
                    value = value.Substring(0, important).Trim();
                }
                if (string.Equals(property, "display", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property, "visibility", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(value, "collapse", StringComparison.OrdinalIgnoreCase))
                    || string.Equals(property, "content-visibility", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(value, "hidden", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
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
            var sawScan = false;
            var frameComponents = new HashSet<byte>();
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
                    return sawFrame && sawQuantizationTable && sawHuffmanTable && sawScan && offset == bytes.Length;
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
                    if (!ValidateJpegQuantizationTables(bytes, segmentStart, segmentEnd))
                    {
                        return false;
                    }
                    sawQuantizationTable = true;
                }
                else if (marker == 0xc4)
                {
                    if (!ValidateJpegHuffmanTables(bytes, segmentStart, segmentEnd))
                    {
                        return false;
                    }
                    sawHuffmanTable = true;
                }
                else if (IsJpegStartOfFrame(marker))
                {
                    if (sawFrame || !ValidateJpegFrame(bytes, segmentStart, segmentEnd, frameComponents))
                    {
                        return false;
                    }
                    sawFrame = true;
                }
                else if (marker == 0xda)
                {
                    if (!sawFrame || !sawQuantizationTable || !sawHuffmanTable
                        || !ValidateJpegScanHeader(bytes, segmentStart, segmentEnd, frameComponents))
                    {
                        return false;
                    }
                    var scanOffset = segmentEnd;
                    var entropyBytes = 0;
                    while (scanOffset < bytes.Length)
                    {
                        if (bytes[scanOffset] != 0xff)
                        {
                            entropyBytes++;
                            scanOffset++;
                            continue;
                        }
                        if (scanOffset + 1 >= bytes.Length)
                        {
                            return false;
                        }
                        var next = bytes[scanOffset + 1];
                        if (next == 0x00)
                        {
                            entropyBytes++;
                            scanOffset += 2;
                            continue;
                        }
                        if (next == 0xff)
                        {
                            scanOffset++;
                            continue;
                        }
                        if (next >= 0xd0 && next <= 0xd7)
                        {
                            scanOffset += 2;
                            continue;
                        }
                        break;
                    }
                    if (entropyBytes == 0)
                    {
                        return false;
                    }
                    sawScan = true;
                    offset = scanOffset;
                    continue;
                }
                offset = segmentEnd;
            }
            return false;
        }

        private static bool IsJpegStartOfFrame(byte marker)
        {
            return marker == 0xc0 || marker == 0xc1 || marker == 0xc2;
        }

        private static bool ValidateJpegQuantizationTables(byte[] bytes, int offset, int end)
        {
            while (offset < end)
            {
                var descriptor = bytes[offset++];
                var precision = descriptor >> 4;
                if (precision > 1 || (descriptor & 0x0f) > 3)
                {
                    return false;
                }
                var tableBytes = precision == 0 ? 64 : 128;
                if (offset + tableBytes > end)
                {
                    return false;
                }
                offset += tableBytes;
            }
            return offset == end;
        }

        private static bool ValidateJpegHuffmanTables(byte[] bytes, int offset, int end)
        {
            while (offset < end)
            {
                var descriptor = bytes[offset++];
                if ((descriptor >> 4) > 1 || (descriptor & 0x0f) > 3 || offset + 16 > end)
                {
                    return false;
                }
                var symbols = 0;
                for (var index = 0; index < 16; index++)
                {
                    symbols += bytes[offset + index];
                }
                offset += 16;
                if (symbols == 0 || symbols > 256 || offset + symbols > end)
                {
                    return false;
                }
                offset += symbols;
            }
            return offset == end;
        }

        private static bool ValidateJpegFrame(byte[] bytes, int offset, int end, ISet<byte> components)
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
            components.Clear();
            offset += 6;
            for (var index = 0; index < count; index++)
            {
                var id = bytes[offset];
                var sampling = bytes[offset + 1];
                if (!components.Add(id) || (sampling >> 4) == 0 || (sampling >> 4) > 4
                    || (sampling & 0x0f) == 0 || (sampling & 0x0f) > 4 || bytes[offset + 2] > 3)
                {
                    return false;
                }
                offset += 3;
            }
            return true;
        }

        private static bool ValidateJpegScanHeader(byte[] bytes, int offset, int end, ISet<byte> frameComponents)
        {
            if (offset >= end)
            {
                return false;
            }
            var count = bytes[offset++];
            if (count < 1 || count > frameComponents.Count || end - offset != count * 2 + 3)
            {
                return false;
            }
            var scanComponents = new HashSet<byte>();
            for (var index = 0; index < count; index++)
            {
                var id = bytes[offset++];
                var tables = bytes[offset++];
                if (!frameComponents.Contains(id) || !scanComponents.Add(id)
                    || (tables >> 4) > 3 || (tables & 0x0f) > 3)
                {
                    return false;
                }
            }
            var spectralStart = bytes[offset++];
            var spectralEnd = bytes[offset++];
            var approximation = bytes[offset];
            return spectralStart <= spectralEnd && spectralEnd <= 63
                && (approximation >> 4) <= 13 && (approximation & 0x0f) <= 13;
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
