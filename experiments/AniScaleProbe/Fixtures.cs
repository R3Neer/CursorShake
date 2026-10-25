using System.Text;
static class Fixtures {
 public static readonly int[] Rates={18,30,42,54};
 public static readonly int[][] Colors={new[]{255,0,0},new[]{0,255,0},new[]{0,0,255},new[]{255,255,0}};
 static byte[] Chunk(string name,byte[] content) {using var m=new MemoryStream();using var w=new BinaryWriter(m);w.Write(Encoding.ASCII.GetBytes(name));w.Write(content.Length);w.Write(content);if((content.Length&1)!=0)w.Write((byte)0);return m.ToArray();}
 public static byte[] Cur(int width,int height,int hotX,int hotY,byte[] pixels,byte[] mask) {
  using var m=new MemoryStream();using var w=new BinaryWriter(m);
  w.Write((ushort)0);w.Write((ushort)2);w.Write((ushort)1);
  w.Write((byte)(width>=256?0:width));w.Write((byte)(height>=256?0:height));w.Write((byte)0);w.Write((byte)0);
  w.Write((ushort)hotX);w.Write((ushort)hotY);w.Write(40+pixels.Length+mask.Length);w.Write(22);
  w.Write(40);w.Write(width);w.Write(height*2);w.Write((ushort)1);w.Write((ushort)32);w.Write(0);w.Write(pixels.Length+mask.Length);
  w.Write(0);w.Write(0);w.Write(0);w.Write(0);w.Write(pixels);w.Write(mask);return m.ToArray();
 }
 public static byte[] Ani(int size) {
  using var main=new MemoryStream();using var mw=new BinaryWriter(main);
  mw.Write(Encoding.ASCII.GetBytes("ACON"));
  using(var a=new MemoryStream()){using(var w=new BinaryWriter(a,Encoding.UTF8,true)){w.Write(36);w.Write(4);w.Write(4);w.Write(size);w.Write(size);w.Write(32);w.Write(1);w.Write(18);w.Write(3);}mw.Write(Chunk("anih",a.ToArray()));}
  using(var a=new MemoryStream()){using(var w=new BinaryWriter(a,Encoding.UTF8,true))foreach(int r in Rates)w.Write(r);mw.Write(Chunk("rate",a.ToArray()));}
  using(var a=new MemoryStream()){using(var w=new BinaryWriter(a,Encoding.UTF8,true))for(int k=0;k<4;k++)w.Write(k);mw.Write(Chunk("seq ",a.ToArray()));}
  using(var f=new MemoryStream()){using(var fw=new BinaryWriter(f,Encoding.UTF8,true)){
   fw.Write(Encoding.ASCII.GetBytes("fram"));
   for(int k=0;k<4;k++){
    byte[] pix=new byte[size*size*4],mask=new byte[((size+31)/32)*4*size];
    for(int i=0;i<size*size;i++){pix[4*i]=(byte)Colors[k][2];pix[4*i+1]=(byte)Colors[k][1];pix[4*i+2]=(byte)Colors[k][0];pix[4*i+3]=255;}
    fw.Write(Chunk("icon",Cur(size,size,size/8,size/8,pix,mask)));
   }
  }mw.Write(Chunk("LIST",f.ToArray()));}
  return Chunk("RIFF",main.ToArray());
 }
}
