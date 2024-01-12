using BpsNet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace BspNetTest
{
    [TestClass]
    public class BpsPatchTest
    {
        [TestMethod]
        public void ChecksumTest()
        {
            byte[] data = File.ReadAllBytes(Path.Combine(new string[] { ProjectSourcePath.Value, "data", "gradient.bps" }));
            BpsPatch p = new BpsPatch(data); ;
        }

        [TestMethod]
        public void InvalidMagicTest()
        {
            byte[] data = { 0, 1, 2, 3, 4, 5, 6, 7 };
            Assert.ThrowsException<InvalidDataException>(() => new BpsPatch(data));
        }

        [TestMethod]
        public void OutOfRangeNumberTest()
        {
            byte[] data = { (byte)'B', (byte)'P', (byte)'S', (byte)'1', 4, 5, 6, 7, 8, 9 };
            Assert.ThrowsException<InvalidDataException>(() => new BpsPatch(data));
        }

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "WriteNumber")]
        extern static void BpsPatchWriteNumber(BpsPatch @this, uint value, Stream stream);
        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ReadNumber")]
        extern static uint BpsPatchReadNumber(BpsPatch @this, byte[] data, ref int readIndex);

        [TestMethod]
        public void MaxNumberTest()
        {
            var memoryStream = new MemoryStream(16);
            BpsPatchWriteNumber(null!, (uint)Int32.MaxValue, memoryStream);
            var data = memoryStream.ToArray();
            int readIndex = 0;
            uint number = BpsPatchReadNumber(null!, data, ref readIndex);
            Assert.AreEqual((uint)Int32.MaxValue, number);
        }

        [TestMethod]
        public void TestRoundTripString()
        {
            byte[] original = UTF8Encoding.UTF8.GetBytes("123456700000000000012345678");
            byte[] target = UTF8Encoding.UTF8.GetBytes("123456700000001111123456999999abc0000000000");

            AssertRoundTrip(original, target, "metadata with UTF-8: Hur mår du? Ça va très bien !");
        }

        [TestMethod]
        public void TestRoundTripStringGradient()
        {
            byte[] original = File.ReadAllBytes(Path.Combine(new string[] { ProjectSourcePath.Value, "data", "gradient1.png" }));
            byte[] target = File.ReadAllBytes(Path.Combine(new string[] { ProjectSourcePath.Value, "data", "gradient2.png" }));

            AssertRoundTrip(original, target, "metadata with UTF-8: Hur mår du? Ça va très bien !");
        }

        [TestMethod]
        public void TestExistingGradientPatch()
        {
            byte[] original = File.ReadAllBytes(Path.Combine(new string[] { ProjectSourcePath.Value, "data", "gradient1.png" }));
            byte[] target = File.ReadAllBytes(Path.Combine(new string[] { ProjectSourcePath.Value, "data", "gradient2.png" }));
            byte[] patchBytes = File.ReadAllBytes(Path.Combine(new string[] { ProjectSourcePath.Value, "data", "gradient.bps" }));

            var patch = new BpsPatch(patchBytes);
            byte[] patchedBytes = patch.Apply(original);
            CollectionAssert.AreEqual(target, patchedBytes);
        }


        public void AssertRoundTrip(byte[] original, byte[] target, string metadata)
        {
            AssertRoundTripLinear(original, target, metadata);
            AssertRoundTripDelta(original, target, metadata);
        }

        public void AssertRoundTripLinear(byte[] original, byte[] target, string metadata)
        {
            BpsPatch generatedPatch = BpsPatch.Create(original, target, metadata, false);
            byte[] generatedPatchBytes = generatedPatch.GetBytes();
            System.Console.WriteLine($"Generated a linear patch of {generatedPatchBytes.Length} bytes");

            var generatedPatchRoundTrip = new BpsPatch(generatedPatchBytes);
            var patchedRoundTrip = generatedPatchRoundTrip.Apply(original);
            CollectionAssert.AreEqual(target, patchedRoundTrip);
            Assert.AreEqual(generatedPatchRoundTrip.Metadata, metadata);
        }

        public void AssertRoundTripDelta(byte[] original, byte[] target, string metadata)
        {
            BpsPatch generatedPatch = BpsPatch.Create(original, target, metadata, true);
            byte[] generatedPatchBytes = generatedPatch.GetBytes();
            System.Console.WriteLine($"Generated a delta patch of {generatedPatchBytes.Length} bytes");

            var generatedPatchRoundTrip = new BpsPatch(generatedPatchBytes);
            var patchedRoundTrip = generatedPatchRoundTrip.Apply(original);
            CollectionAssert.AreEqual(target, patchedRoundTrip);
            Assert.AreEqual(generatedPatchRoundTrip.Metadata, metadata);
        }
    }

    internal static class ProjectSourcePath
    {
        private static string? lazyValue;
        public static string Value => lazyValue ??= calculatePath();

        private static string calculatePath()
        {
            string pathName = GetSourceFilePathName();
            return Path.GetDirectoryName(pathName) ?? "";
        }
        public static string GetSourceFilePathName([CallerFilePath] string? callerFilePath = null) => callerFilePath ?? "";
    }
}