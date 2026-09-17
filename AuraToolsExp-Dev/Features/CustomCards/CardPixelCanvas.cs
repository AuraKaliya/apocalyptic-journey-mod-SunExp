using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;
using AuraToolsExp.Dll.Features.PixelEmoji;

namespace AuraToolsExp.Dll.Features.CustomCards;

public static class CardPixelCanvas
{
    public static readonly int[] Sizes = { 32, 64, 128 };
    public static readonly string[] Templates = { "空白", "火焰", "冰晶", "叶片", "星光", "盾牌" };
    public static bool IsValid(int size, string? encoded)
    {
        if (!Sizes.Contains(size) || encoded==null || encoded.Length!=(size*size+2)/3*4) return false;
        try { var pixels=Convert.FromBase64String(encoded); return pixels.Length==size*size && pixels.All(x=>x<PixelEmojiCodec.PaletteRgba.Length); }
        catch (FormatException) { return false; }
    }
    public static byte[] Template(int size,int template)
    {
        if (!Sizes.Contains(size) || template<0 || template>=Templates.Length) throw new ArgumentOutOfRangeException();
        var pixels=new byte[size*size];
        for (int y=0;y<size;y++) for (int x=0;x<size;x++)
        {
            double u=(x+0.5)/size*2-1, v=(y+0.5)/size*2-1, a=Math.Abs(u), b=Math.Abs(v);
            byte color=0;
            if (template==1 && v>-0.75 && v<0.8 && a<(0.7-v*0.55)*(0.65+0.15*Math.Sin(v*12))) color=(byte)(a<0.17 && v<0.15 ? 10 : a<0.32 ? 9 : 7);
            if (template==2 && a+b<0.82) color=(byte)(a<0.08 || b<0.08 ? 2 : u*v>0 ? 24 : 22);
            if (template==3 && u*u*2+v*v*0.9<0.65 && a+b<1.03) color=(byte)(Math.Abs(u-v*0.2)<0.045 ? 17 : u>v*0.2 ? 19 : 16);
            if (template==4 && a+b<0.83 && (a<0.16 || b<0.16 || a+b<0.35)) color=(byte)(a+b<0.22 ? 2 : 10);
            if (template==5 && v<0.72 && v>-0.8 && a<0.58 && (v>0 || a<0.58+v*0.55)) color=(byte)(a>0.47 || v>0.6 || (v<0 && a>0.45+v*0.55) ? 9 : Math.Abs(u)<0.07 || Math.Abs(v-0.15)<0.07 ? 24 : 22);
            pixels[y*size+x]=color;
        }
        return pixels;
    }

    /// <summary>PNG generation is pure managed code, safe during native background deserialization.</summary>
    public static byte[] Png(CustomCardArtwork art)
    {
        if(!IsValid(art.Size,art.Pixels))throw new ArgumentException("Invalid card artwork.");
        var pixels=Convert.FromBase64String(art.Pixels);
        using var raw=new MemoryStream();
        // Editor coordinates use a bottom-left origin; PNG scanlines start at the top.
        const int outputSize=256;
        for(int y=outputSize-1;y>=0;y--)
        {
            raw.WriteByte(0);
            for(int x=0;x<outputSize;x++)
            {
                uint p=PixelEmojiCodec.PaletteRgba[pixels[(y*art.Size/outputSize)*art.Size+x*art.Size/outputSize]];
                raw.WriteByte((byte)(p>>24));raw.WriteByte((byte)(p>>16));raw.WriteByte((byte)(p>>8));raw.WriteByte((byte)p);
            }
        }
        var bytes=raw.ToArray();
        using var compressed=new MemoryStream();compressed.WriteByte(0x78);compressed.WriteByte(0x9c);
        using(var deflate=new DeflateStream(compressed,CompressionLevel.Optimal,true))deflate.Write(bytes,0,bytes.Length);
        uint a=1,b=0;foreach(byte v in bytes){a=(a+v)%65521;b=(b+a)%65521;}WriteUInt(compressed,(b<<16)|a);
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10},0,8);
        using var header=new MemoryStream();WriteUInt(header,outputSize);WriteUInt(header,outputSize);header.Write(new byte[]{8,6,0,0,0},0,5);
        Chunk(output,"IHDR",header.ToArray());Chunk(output,"IDAT",compressed.ToArray());Chunk(output,"IEND",Array.Empty<byte>());return output.ToArray();
    }
    private static void WriteUInt(Stream stream,uint n){stream.WriteByte((byte)(n>>24));stream.WriteByte((byte)(n>>16));stream.WriteByte((byte)(n>>8));stream.WriteByte((byte)n);}
    private static void Chunk(Stream output,string name,byte[] data)
    {
        var type=Encoding.ASCII.GetBytes(name);WriteUInt(output,(uint)data.Length);output.Write(type,0,4);output.Write(data,0,data.Length);
        uint crc=0xffffffff;
        foreach(byte b in type.Concat(data)){crc^=b;for(int i=0;i<8;i++)crc=(crc&1)!=0?0xedb88320^(crc>>1):crc>>1;}
        WriteUInt(output,crc^0xffffffff);
    }
}
