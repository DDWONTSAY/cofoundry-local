using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace Cofoundry {
public static class Ui {
#if ENGLISH
  public static bool English=true;
#else
  public static bool English=false;
#endif
  static readonly Dictionary<string,string> translations=Load();
  static Dictionary<string,string> Load(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Cofoundry.English.json")){if(stream==null)return new Dictionary<string,string>();using(var reader=new StreamReader(stream))return new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(reader.ReadToEnd());}}
  public static string T(string text){string translated;return English&&translations.TryGetValue(text,out translated)?translated:text;}
}
public static class SoftUi {
  public static GraphicsPath Shape(Rectangle bounds,int radius){var p=new GraphicsPath();int d=Math.Max(1,Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height)));p.AddArc(bounds.Left,bounds.Top,d,d,180,90);p.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);p.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);p.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  public static Color Background(Control c){while(c!=null){if(c.BackColor.A==255)return c.BackColor;c=c.Parent;}return SystemColors.Control;}
  // Paint the corners against an opaque parent background; window regions leave
  // uncovered pixels during native child-window repainting and resizing.
  public static void Round(Control c,int radius){
    c.Paint+=(s,e)=>{if(c.Width<2||c.Height<2)return;using(var corners=new GraphicsPath(FillMode.Alternate))using(var shape=Shape(c.ClientRectangle,radius))using(var brush=new SolidBrush(Background(c.Parent))){corners.AddRectangle(c.ClientRectangle);corners.AddPath(shape,false);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.FillPath(brush,corners);}};
    c.SizeChanged+=(s,e)=>c.Invalidate();c.ParentChanged+=(s,e)=>c.Invalidate();
  }
}
public class SoftButton : Button {
  bool hover;
  public SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
  protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
  protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
  protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(SoftUi.Background(Parent));}
  protected override void OnPaint(PaintEventArgs e){
    if(Width<2||Height<2)return;
    e.Graphics.Clear(SoftUi.Background(Parent));
    e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
    var bounds=new Rectangle(1,1,Width-3,Height-3);
    using(var shape=SoftUi.Shape(bounds,(int)(12*e.Graphics.DpiX/96F))){
      var fill=!Enabled?Color.FromArgb(235,238,242):hover&&!FlatAppearance.MouseOverBackColor.IsEmpty?FlatAppearance.MouseOverBackColor:BackColor;
      using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
      if(FlatAppearance.BorderSize>0)using(var pen=new Pen(Focused?Color.FromArgb(99,102,241):FlatAppearance.BorderColor,Focused?2:1))e.Graphics.DrawPath(pen,shape);
    }
    var textBounds=new Rectangle(Padding.Left+8,Padding.Top+3,Math.Max(1,Width-Padding.Horizontal-16),Math.Max(1,Height-Padding.Vertical-6));
    var flags=TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix;
    flags|=TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter;
    TextRenderer.DrawText(e.Graphics,Text,Font,textBounds,Enabled?ForeColor:Color.FromArgb(112,120,133),flags);
    if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(bounds,-5,-5));
  }
}
}
