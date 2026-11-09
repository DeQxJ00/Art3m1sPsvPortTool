using Art3m1s.PsvTool.Core;
using Xunit;
namespace Art3m1s.PsvTool.Core.Tests;
public sealed class PsbBc7Tests
{
    [Fact]
    public void Bc7RejectsTruncatedPayloadAndPreservesChannelsOnRoundTrip()
    {
        Assert.Throws<InvalidDataException>(() => Bc7TextureCodec.Decode(new byte[15],4,4));
        Assert.Throws<InvalidDataException>(() => Bc7TextureCodec.Decode(new byte[17],4,4));
        byte[] pixels=Enumerable.Range(0,16).SelectMany(_=>new byte[]{180,90,40,128}).ToArray();
        byte[] decoded=Bc7TextureCodec.Decode(Bc7TextureCodec.Encode(pixels,4,4),4,4);
        for(int i=0;i<decoded.Length;i++) Assert.InRange(Math.Abs(decoded[i]-pixels[i]),0,3);
        Assert.Equal(96,PsbTextureSizeEstimator.PayloadBytes("BC7",9,5));
    }

    [Theory]
    [InlineData(1.0, PsbDxt5Layout.Linear)]
    [InlineData(0.5, PsbDxt5Layout.Swizzled)]
    public async Task Bc7ConvertsWithColorAndAlpha(double ratio, PsbDxt5Layout layout)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".psb");
        byte[] block = new byte[16]; int bit = 0;
        void Bits(int value, int count) { for(int i=0;i<count;i++,bit++) block[bit/8] |= (byte)(((value>>i)&1)<<(bit%8)); }
        Bits(64,7); foreach(int channel in new[]{90,45,20,64}) { Bits(channel,7); Bits(channel,7); }
        Bits(0,2); Bits(0,63);
        byte[] payload = Enumerable.Range(0,6).SelectMany(_=>block).ToArray();
        try
        {
            await File.WriteAllBytesAsync(path, PsbFixture.CreateWithEncodedResources(4, true, (_,_,_) => ("BC7",payload),(9,5)));
            await new PsbProcessor().ProcessWithFormatAsync(path, ratio, ratio, PsbTextureFormat.Dxt5, default, layout);
            var atlas = Assert.Single((await PsbTextureScanner.InspectFileAsync(path)).Atlases);
            Assert.Equal(PsbTextureFormats.TypeName(PsbTextureFormat.Dxt5,layout),atlas.Format);
            Assert.Equal((int)(9*ratio),atlas.Width); Assert.Equal((int)(5*ratio),atlas.Height);
            byte[] pixels=PsbProcessor.InspectTextureResource(await File.ReadAllBytesAsync(path),"atlas0");
            if(layout==PsbDxt5Layout.Swizzled) pixels=PsbDxt5Storage.Unswizzle(pixels,atlas.Width,atlas.Height);
            byte[] rgba=PsbProcessor.DecodeDxt5ForTest(pixels,atlas.Width,atlas.Height);
            for(int i=0;i<rgba.Length;i+=4) { Assert.InRange(rgba[i],(byte)170,(byte)190); Assert.InRange(rgba[i+1],(byte)80,(byte)100); Assert.InRange(rgba[i+2],(byte)30,(byte)50); Assert.InRange(rgba[i+3],(byte)125,(byte)131); }
        }
        finally { File.Delete(path); }
    }
}
