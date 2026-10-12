param([string]$Src, [string]$OutDir)
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing, System.Drawing.Primitives, System.Drawing.Common, System.Private.Windows.Core, System.Private.Windows.GdiPlus, System.Collections, System.Runtime.InteropServices, System.Runtime, System.Runtime.Extensions, System.Private.CoreLib -TypeDefinition @'
using System; using System.IO; using System.Drawing; using System.Drawing.Imaging;
using System.Drawing.Drawing2D; using System.Collections.Generic;
public static class IconMaker {
  static bool Bg(byte[] p, int i) { return p[i] > 238 && p[i+1] > 238 && p[i+2] > 238; }
  public static void Run(string src, string outDir, int l, int t, int size) {
    using var full = new Bitmap(src);
    var crop = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(crop)) g.DrawImage(full, new Rectangle(0,0,size,size), new Rectangle(l,t,size,size), GraphicsUnit.Pixel);
    var d = crop.LockBits(new Rectangle(0,0,size,size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[d.Stride*size]; System.Runtime.InteropServices.Marshal.Copy(d.Scan0, px, 0, px.Length);
    var outside = new bool[size*size];
    // shape is convex: scan in from each side along rows and columns
    for (int y=0;y<size;y++){ for(int x=0;x<size && Bg(px,y*d.Stride+x*4);x++) outside[y*size+x]=true;
                              for(int x=size-1;x>=0 && Bg(px,y*d.Stride+x*4);x--) outside[y*size+x]=true; }
    for (int x=0;x<size;x++){ for(int y=0;y<size && Bg(px,y*d.Stride+x*4);y++) outside[y*size+x]=true;
                              for(int y=size-1;y>=0 && Bg(px,y*d.Stride+x*4);y--) outside[y*size+x]=true; }
    const double bgAvg = 255, fgAvg = (94+122+157)/3.0;
    for (int y=0;y<size;y++) for (int x=0;x<size;x++) {
      int i=y*d.Stride+x*4; if (outside[y*size+x]) { px[i+3]=0; continue; }
      bool edge=false;
      for (int dy=-2;dy<=2&&!edge;dy++) for(int dx=-2;dx<=2;dx++){ int xx=x+dx, yy=y+dy;
        if (xx<0||yy<0||xx>=size||yy>=size||outside[yy*size+xx]) { edge=true; break; } }
      if (!edge) continue;
      double avg=(px[i]+px[i+1]+px[i+2])/3.0;
      double a=Math.Clamp((bgAvg-avg)/(bgAvg-fgAvg),0,1);
      if (a<=0.01){ px[i+3]=0; continue; }
      for (int c=0;c<3;c++) px[i+c]=(byte)Math.Clamp((px[i+c]-255*(1-a))/a,0,255);
      px[i+3]=(byte)(a*255);
    }
    System.Runtime.InteropServices.Marshal.Copy(px,0,d.Scan0,px.Length); crop.UnlockBits(d);
    crop.Save(Path.Combine(outDir,"icon.png"), ImageFormat.Png);
    var sizes = new[]{16,20,24,32,40,48,64,96,128,256}; var frames = new List<byte[]>();
    foreach (var s in sizes) {
      using var bmp = new Bitmap(s,s,PixelFormat.Format32bppArgb);
      using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode=PixelOffsetMode.HighQuality; g.CompositingQuality=CompositingQuality.HighQuality;
        using var ia = new ImageAttributes(); ia.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(crop,new Rectangle(0,0,s,s),0,0,size,size,GraphicsUnit.Pixel,ia); }
      using var ms = new MemoryStream(); bmp.Save(ms, ImageFormat.Png); frames.Add(ms.ToArray());
    }
    using var fs = File.Create(Path.Combine(outDir,"icon.ico")); using var w = new BinaryWriter(fs);
    w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
    int offset = 6 + 16*sizes.Length;
    for (int k=0;k<sizes.Length;k++){ int s=sizes[k];
      w.Write((byte)(s>=256?0:s)); w.Write((byte)(s>=256?0:s)); w.Write((byte)0); w.Write((byte)0);
      w.Write((short)1); w.Write((short)32); w.Write(frames[k].Length); w.Write(offset); offset+=frames[k].Length; }
    foreach (var f in frames) w.Write(f);
  }
}
'@
New-Item -ItemType Directory -Force $OutDir | Out-Null
[IconMaker]::Run($Src, $OutDir, 151, 152, 951)
Get-ChildItem $OutDir | Select-Object Name, Length


