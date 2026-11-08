using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
public static class NativeExport {
  static Color Sample(Bitmap b,double x,double y) {
    int cx=(int)Math.Round(x),cy=(int)Math.Round(y);var rr=new List<int>();var gg=new List<int>();var bb=new List<int>();
    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){Color c=b.GetPixel(cx+dx,cy+dy);rr.Add(c.R);gg.Add(c.G);bb.Add(c.B);}
    rr.Sort();gg.Sort();bb.Sort();return Color.FromArgb(255,rr[4],gg[4],bb[4]);
  }
  static int Dist(Color c,int r,int g,int b){int dr=c.R-r,dg=c.G-g,db=c.B-b;return dr*dr+dg*dg+db*db;}
  static int Components(bool[] mask) {
    var seen=new bool[1024];int count=0;for(int p=0;p<1024;p++){
      if(!mask[p]||seen[p])continue;count++;var q=new Queue<int>();q.Enqueue(p);seen[p]=true;
      while(q.Count>0){int at=q.Dequeue(),x=at%32,y=at/32;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<0||yy<0||xx>=32||yy>=32)continue;int n=yy*32+xx;if(mask[n]&&!seen[n]){seen[n]=true;q.Enqueue(n);}}}
    }return count;
  }
  public static void Run(string source,string output,string previews,string pack) {
    Directory.CreateDirectory(output);Directory.CreateDirectory(previews);var records=new List<string>();
    using(var sheet=new Bitmap(source)) {
      if(sheet.Width!=1800||sheet.Height!=1200)throw new Exception("Unexpected reference size: "+pack);
      for(int row=0;row<6;row++)for(int col=0;col<8;col++) {
        int id=row*8+col+1;string name=pack+"-"+id.ToString("00")+".png";
        using(var icon=new Bitmap(32,32,PixelFormat.Format32bppArgb)) {
          // Pixel lattice measured from repeated color transitions in the public reference.
          // The source sprite is already 32x32; do not crop its bounds or resize its silhouette.
          for(int y=0;y<32;y++)for(int x=0;x<32;x++)icon.SetPixel(x,y,Sample(sheet,89.2+208*col+(x+.5)*5.2,44.8+187.2*row+(y+.5)*5.2));
          var candidate=new bool[1024];var background=new bool[1024];var q=new Queue<int>();
          for(int y=0;y<32;y++)for(int x=0;x<32;x++) {
            int p=y*32+x;Color c=icon.GetPixel(x,y);int d=Dist(c,71,52,47);candidate[p]=d<=225;
            // Near-exact flat tile color also identifies enclosed background holes.
            if((candidate[p]&&(x==0||y==0||x==31||y==31))||d<=49){background[p]=true;q.Enqueue(p);}
          }
          int[] dxs={-1,1,0,0},dys={0,0,-1,1};
          while(q.Count>0){int p=q.Dequeue(),x=p%32,y=p/32;for(int k=0;k<4;k++){int xx=x+dxs[k],yy=y+dys[k];if(xx<0||yy<0||xx>=32||yy>=32)continue;int n=yy*32+xx;if(candidate[n]&&!background[n]){background[n]=true;q.Enqueue(n);}}}
          int opaque=0;bool[] mask=new bool[1024];var colors=new HashSet<int>();
          for(int y=0;y<32;y++)for(int x=0;x<32;x++) {
            int p=y*32+x;if(background[p])icon.SetPixel(x,y,Color.FromArgb(0,0,0,0));
            else{opaque++;mask[p]=true;colors.Add(icon.GetPixel(x,y).ToArgb());}
          }
          if(opaque<150||opaque>1020)throw new Exception("Unexpected alpha mask "+name+": "+opaque);
          icon.Save(Path.Combine(output,name),ImageFormat.Png);
          records.Add(String.Format("{{\"file\":\"{0}\",\"row\":{1},\"column\":{2},\"width\":32,\"height\":32,\"opaquePixels\":{3},\"transparentPixels\":{4},\"colors\":{5},\"components\":{6},\"outlinePreserved\":true,\"resizedSilhouette\":false}}",name,row+1,col+1,opaque,1024-opaque,colors.Count,Components(mask)));
        }
      }
    }
    File.WriteAllText(Path.Combine(output,"manifest.json"),"[\n"+String.Join(",\n",records)+"\n]");
    foreach(string theme in new[]{"light","dark","checker"})using(var board=new Bitmap(1088,864)){
      for(int y=0;y<board.Height;y++)for(int x=0;x<board.Width;x++){int v=theme=="light"?255:theme=="dark"?16:((x/16+y/16)%2==0?190:230);board.SetPixel(x,y,Color.FromArgb(v,v,v));}
      for(int id=1;id<=48;id++)using(var icon=new Bitmap(Path.Combine(output,pack+"-"+id.ToString("00")+".png"))){
        int bx=(id-1)%8*136+4,by=(id-1)/8*144+4;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){Color c=icon.GetPixel(x,y);if(c.A==0)continue;for(int yy=0;yy<4;yy++)for(int xx=0;xx<4;xx++)board.SetPixel(bx+x*4+xx,by+y*4+yy,c);}
        using(var g=Graphics.FromImage(board))using(var f=new Font("Arial",7))g.DrawString(id.ToString("00"),f,theme=="dark"?Brushes.White:Brushes.Black,bx,by+128);
      }
      board.Save(Path.Combine(previews,pack+"-"+theme+".png"),ImageFormat.Png);
    }
  }
}
